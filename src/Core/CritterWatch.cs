// SPDX-License-Identifier: MIT
// One kiln.log line per critter (fishing bait swarm) a player passes, saying whether the mod sent the take and, if not,
// why; one more when a take was sent but the swarm is still there a moment later (the game did not hand the bait over);
// and one per bait-like template the mod's table does not know. So a player's report shows what happened to the frogs
// and maggots they walked past. At most MaxLines per world, then one line saying the rest is left out. Pure; the loop
// writes the lines. A second watch ("fish") does the same for the fish a player with Fish on passes, and for the
// fishing spots in their reach; a third ("loot") says what a corpse, chest or creature pile kept, and why.
namespace KilnAutoLoot.Core;

public sealed class CritterWatch
{
    private readonly string _kind;

    /// <param name="kind">the lines' first word ("critter"; "fish" for the fish a player passes with Fish on).</param>
    public CritterWatch(string kind = "critter") => _kind = kind;

    public const int MaxLines = 200;
    /// <summary>A swarm still there this long after its take was sent counts as not handed over.</summary>
    public const long StillThereMs = 2000;
    private const int MaxRemembered = 4096;

    private sealed class Entry
    {
        public bool LeftLogged, SentLogged, StillLogged;
        public long? SentAt;
    }

    private readonly Dictionary<ulong, Entry> _byKey = new();
    private readonly HashSet<Guid> _templates = new();
    private int _lines;
    private bool _capSaid;

    /// <summary>Lines written so far (the cap counts these).</summary>
    public int Lines => _lines;

    /// <summary>The swarm is in a player's reach this scan; true the first time (a swarm not seen before).</summary>
    public bool Seen(ulong key)
    {
        if (_byKey.ContainsKey(key)) return false;
        Remember(key, new Entry());
        return true;
    }

    /// <summary>A critter left for a lasting reason (a switch, a list, the radius ...): a line the first time for this
    /// swarm, unless its take was sent already.</summary>
    public string? Left(ulong key, string text) => Left(key, () => text);

    /// <summary>As <see cref="Left(ulong, string)"/>, with the text made only when the line is written.</summary>
    public string? Left(ulong key, Func<string> text)
    {
        if (!_byKey.TryGetValue(key, out var e)) Remember(key, e = new Entry());
        if (e.LeftLogged || e.SentLogged) return null;
        e.LeftLogged = true;
        return Line(text());
    }

    /// <summary>A line was written for this key already ("left" or "sent"): <see cref="Left(ulong, string)"/> would
    /// write none.</summary>
    public bool Told(ulong key) => _byKey.TryGetValue(key, out var e) && (e.LeftLogged || e.SentLogged);

    /// <summary>The take was sent: a line the first time for this swarm (also after an earlier "left" line).
    /// <paramref name="first"/>: this is the swarm's first take.</summary>
    public string? Sent(ulong key, long now, string text, out bool first)
    {
        if (!_byKey.TryGetValue(key, out var e)) Remember(key, e = new Entry());
        first = e.SentAt is null;
        e.SentAt ??= now;
        if (e.SentLogged) return null;
        e.SentLogged = true;
        return Line(text);
    }

    /// <summary>The swarm was seen again: when its take went out at least <see cref="StillThereMs"/> ago, the line that
    /// the game did not hand it over (once per swarm), else null.</summary>
    public string? Again(ulong key, long now, Func<long, string> text)
    {
        if (!_byKey.TryGetValue(key, out var e) || e.SentAt is not { } at || e.StillLogged || now - at < StillThereMs) return null;
        e.StillLogged = true;
        return Line(text(now - at));
    }

    /// <summary>True once the swarm's take was sent and it is still around after <see cref="StillThereMs"/>.</summary>
    public bool Stuck(ulong key) => _byKey.TryGetValue(key, out var e) && e.StillLogged;

    /// <summary>A template the table does not know: a line once per template when its name looks like a bait swarm, with
    /// its GUID and what the game data the mod read showed of it (<paramref name="noteOf"/>: TemplateTable.BaitNote; null
    /// = the data had no such template).</summary>
    public string? Unknown(Guid template, Func<string?> nameOf, Func<Guid, string?>? noteOf = null)
    {
        if (!_templates.Add(template) || nameOf() is not { } name || !LooksLikeBait(name)) return null;
        var why = noteOf?.Invoke(template) is { } note
            ? $"the game data shows it, but not as fishing bait: {note}"
            : "the game data the mod read has no bait template with this GUID (a resource it could not read, or a " +
              "template the game added after the read)";
        return Line($"critter: template {name} ({template}) is not in the mod's table (never taken): {why}");
    }

    public static bool LooksLikeBait(string name) =>
        name.Contains("Bait", StringComparison.OrdinalIgnoreCase) && name.Contains("Scatter", StringComparison.OrdinalIgnoreCase);

    /// <summary>A new world: everything starts again.</summary>
    public void Reset()
    {
        _byKey.Clear();
        _templates.Clear();
        _lines = 0;
        _capSaid = false;
    }

    private void Remember(ulong key, Entry e)
    {
        if (_byKey.Count >= MaxRemembered) _byKey.Clear();
        _byKey[key] = e;
    }

    private string? Line(string text)
    {
        if (_lines < MaxLines)
        {
            _lines++;
            return text;
        }
        if (_capSaid) return null;
        _capSaid = true;
        return $"{_kind}: {MaxLines} {_kind} lines in this world; the rest are left out";
    }
}
