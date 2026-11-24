// SPDX-License-Identifier: MIT
using Kiln;
using KilnAutoLoot.Server;

namespace KilnAutoLoot.Client;

internal sealed class SightRaySettings
{
    [Comment("Show red debug rays for corpse visibility in a world this game hosts. Off by default.")]
    public bool ShowCorpseSightRays { get; set; }
}

/// <summary>Opt-in history of completed rays, local host only. The render thread reads copies, never game state.</summary>
internal sealed class SightRayOverlay
{
    internal const int Capacity = 48;
    internal const long VisibleMs = 2000;
    internal readonly record struct Segment(EntityId Target, SightSample Sample, WorldPosition From, WorldPosition To,
        WorldPosition? Hit, long Until);

    private static readonly VoxelSightOptions DefaultOptions = new();
    private static readonly Color BeyondHit = new(255, 50, 50, 95);
    private readonly object _gate = new();
    private readonly Segment[] _rays = new Segment[Capacity];
    private int _count;

    private ModConfig<SightRaySettings>? _settings;
    private EventSubscription? _frames;
    private bool _hosted; // server/client lifecycle and config callbacks are on the tick thread
    private volatile bool _enabled, _recording;

    internal bool Enabled => _enabled;
    internal bool Recording => _recording;
    internal volatile string? SaveError;

    internal void Start()
    {
        try
        {
            _settings = Config.Register<SightRaySettings>(ConfigKind.Client, "debug");
            _settings.Changed += OnSettingsChanged;
            Refresh();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            SaveError = $"Kiln Auto Loot: cannot enable ray diagnostics ({e.Message})";
            AutoLootMod.ModContext?.Log.Warn(SaveError);
            Stop();
        }
    }

    internal void Stop()
    {
        if (_settings is not null) _settings.Changed -= OnSettingsChanged;
        _settings = null;
        _hosted = _enabled = false;
        SyncRendering();
    }

    internal void Save(bool enabled)
    {
        if (_settings is null) return;
        SaveError = _settings.TrySet("showCorpseSightRays", enabled, out var error) ? null : error;
    }

    internal void SetHosted(bool hosted)
    {
        _hosted = hosted;
        SyncRendering();
    }

    private void OnSettingsChanged(ConfigChangedEvent _) => Refresh();

    private void Refresh()
    {
        _enabled = _settings?.Value.ShowCorpseSightRays == true;
        SyncRendering();
    }

    private void SyncRendering()
    {
        if (_enabled && _hosted)
        {
            _frames ??= Events.On<RenderFrameEvent>(Draw);
            _recording = true;
        }
        else
        {
            _recording = false;
            _frames?.Dispose();
            _frames = null;
            Clear();
        }
    }

    internal static bool CanDraw(SightAnswer answer) =>
        answer.State == Sight.Visible || (answer.State == Sight.Hidden && answer.Obstruction.HasValue);

    internal void Record(EntityId target, SightSample sample, SightAnswer answer, WorldPosition fromOrigin,
        WorldPosition toOrigin, VoxelSightOptions? options, long now)
    {
        if (!CanDraw(answer)) return;
        options ??= DefaultOptions;
        var from = Add(fromOrigin, options.FromOffset);
        var to = Add(toOrigin, options.ToOffset);
        var hit = answer.Obstruction?.Position;
        if (!Finite(from) || !Finite(to) || from == to || (hit is { } h && !Finite(h))) return;
        var next = new Segment(target, sample, from, to, hit, now + VisibleMs);
        lock (_gate)
        {
            var oldest = 0;
            for (var i = 0; i < _count; i++)
            {
                if (_rays[i].Target == target && _rays[i].Sample == sample)
                {
                    _rays[i] = next; // newest endpoints for this corpse/sample
                    return;
                }
                if (_rays[i].Until < _rays[oldest].Until) oldest = i;
            }
            _rays[_count < Capacity ? _count++ : oldest] = next;
        }
    }

    internal int CopyActive(Span<Segment> output, long now)
    {
        lock (_gate)
        {
            var n = 0;
            for (var i = 0; i < _count && n < output.Length; i++)
                if (_rays[i].Until > now) output[n++] = _rays[i];
            return n;
        }
    }

    internal void Clear()
    {
        lock (_gate) _count = 0;
    }

    internal void Draw(RenderFrameEvent frame)
    {
        if (!Recording) return;
        var world = frame.World;
        if (!world.Ready) return;
        Span<Segment> rays = stackalloc Segment[Capacity];
        var count = CopyActive(rays, Environment.TickCount64);
        for (var i = 0; i < count; i++)
        {
            var ray = rays[i];
            world.Line(ray.From, ray.Hit ?? ray.To, Color.Red, 3);
            if (ray.Hit is { } hit)
            {
                world.Line(hit, ray.To, BeyondHit, 2);
                world.Marker(hit, Color.Orange, 10, MarkerShape.Cross);
            }
            world.Marker(ray.To, Color.Red, 6, MarkerShape.Circle);
        }
    }

    private static WorldPosition Add(WorldPosition p, WorldPosition offset) => new(p.X + offset.X, p.Y + offset.Y, p.Z + offset.Z);
    private static bool Finite(WorldPosition p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);
}
