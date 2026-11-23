// SPDX-License-Identifier: MIT
// "/autoloot near" (0.2.8; the owner's Vukah corpse on 0.2.8-test.1 was never looted and left no kiln.log line): every
// entity within the player's reach, what it is now (Spawn.Inspect) and what the loop has it as, the candidate lists it
// is in, alive or dead, its inventory and stacks, its pickup and offer, and what the next whole scan decided for it with
// the reason: the loop's own trace notes (ProbeTrace.Note), and the player's (settings, a window, F9, an enemy). Entities
// the mod's table knows that are in no candidate list (World.Find did not name them) come from an origin overlap when
// Kiln has one. Tick thread.
using System.Globalization;
using System.Text;
using Kiln;
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Server;

internal sealed class NearReport
{
    public const int MaxEntities = 80;
    public const int MaxChatLines = 12;

    /// <summary>The lists the scan takes its targets from, and the enemy list (letters in the report).</summary>
    public static readonly (string Component, char Letter)[] Lists =
    [
        (LootLoop.PickupComponent, 'P'), (LootLoop.OfferComponent, 'O'), (LootLoop.InventoryComponent, 'I'),
        (LootLoop.LootMarkerComponent, 'M'), (LootLoop.EnemyComponent, 'E'),
    ];

    private readonly EntityId _player;
    private readonly WorldPosition _at;
    private readonly double _reach;
    private readonly Dictionary<EntityId, (double D, double Dy, string Lists)> _entities;
    private readonly Action<IReadOnlyList<string> /*kiln.log*/, IReadOnlyList<string> /*chat*/> _done;
    private readonly Dictionary<EntityId, List<string>> _notes = new();
    private Func<EntityId, bool>? _prevWatching;
    private Action<EntityId, string>? _prevSink;

    /// <summary>The scan the report is on (0 = the next one to start).</summary>
    public long Scan { get; set; }

    private NearReport(EntityId player, WorldPosition at, double reach, Dictionary<EntityId, (double, double, string)> entities,
        Action<IReadOnlyList<string>, IReadOnlyList<string>> done)
    {
        _player = player;
        _at = at;
        _reach = reach;
        _entities = entities;
        _done = done;
    }

    /// <summary>The entities around <paramref name="at"/> within <paramref name="reach"/>: those of the candidate lists,
    /// and <paramref name="others"/> (an origin overlap) that are in none.</summary>
    public static NearReport Around(IGame game, EntityId player, WorldPosition at, double reach, IEnumerable<EntityId> others,
        Action<IReadOnlyList<string>, IReadOnlyList<string>> done)
    {
        var found = new Dictionary<EntityId, (double D, double Dy, string Lists)>();
        foreach (var (component, letter) in Lists)
            foreach (var e in game.Within(game.Find(component), at, reach))
            {
                if (e == player) continue;
                if (found.TryGetValue(e, out var f)) found[e] = f with { Lists = f.Lists + letter };
                else if (game.PositionOf(e) is { } p) found[e] = (p.DistanceTo(at), p.Y - at.Y, letter.ToString());
            }
        foreach (var e in others)
            if (e != player && !found.ContainsKey(e) && game.PositionOf(e) is { } p && p.DistanceTo(at) <= reach)
                found[e] = (p.DistanceTo(at), p.Y - at.Y, "");
        return new NearReport(player, at, reach, found, done);
    }

    public bool Watches(EntityId e) => (Scan != 0 && (e == _player || _entities.ContainsKey(e))) || _prevWatching?.Invoke(e) == true;

    public void Note(EntityId e, string text)
    {
        if (Scan != 0 && (e == _player || _entities.ContainsKey(e)))
        {
            if (!_notes.TryGetValue(e, out var list)) _notes[e] = list = [];
            if (list.Count < 24 && (list.Count == 0 || list[^1] != text)) list.Add(text);
        }
        _prevSink?.Invoke(e, text);
    }

    public void Arm(Func<EntityId, bool>? watching, Action<EntityId, string>? sink)
    {
        _prevWatching = watching;
        _prevSink = sink;
    }

    public (Func<EntityId, bool>?, Action<EntityId, string>?) Restore() => (_prevWatching, _prevSink);

    /// <summary>The scan is done: the lines go out.</summary>
    public void Finish(LootLoop loop, IGame game, Data data)
    {
        var log = new List<string>();
        var chat = new List<string>();
        var player = _notes.TryGetValue(_player, out var pn) ? string.Join(" / ", pn) : "the scan passed the player by";
        log.Add($"near: player {_player} at ({F(_at.X)}, {F(_at.Y)}, {F(_at.Z)}), reach {_reach:0} m, scan {Scan}: " +
                $"{_entities.Count} entities within reach (lists P pickups, O offers, I inventories, M loot markers, E enemies); {player}");
        chat.Add($"Auto Loot: {_entities.Count} things within {_reach:0} m; {player}");
        var shown = 0;
        foreach (var (e, (d, dy, lists)) in _entities.OrderBy(x => x.Value.D))
        {
            var inspected = game.Inspect(e);
            var template = inspected?.Template;
            TemplateRule? rule = template is { } g && data.Templates.TryGet(g, out var r) ? r : null;
            if (lists.Length == 0 && rule is null) continue;   // the overlap's props the mod has no rule for
            if (++shown > MaxEntities)
            {
                log.Add($"near: {_entities.Count - MaxEntities} more left out");
                break;
            }
            var name = Name(game, template);
            var stamp = game.SpawnStamp(e);
            var sb = new StringBuilder();
            sb.Append($"near: {e} {name} {d:0.0} m (dy {dy:+0.0;-0.0;0.0}): ");
            sb.Append(rule is null ? "not in the mod's table" : TemplateTable.WordOf(rule.Group) + (rule.Flags == TemplateFlags.None ? "" : " " + rule.Flags));
            sb.Append(lists.Length == 0 ? "; in NO candidate list (World.Find did not name it)" : "; lists " + lists);
            if (rule is null or { Group: Group.Enemy or Group.Boss }) sb.Append(game.IsDead(e) ? "; dead" : "; alive");
            sb.Append("; ").Append(Inventory(game, data, e));
            if (game.TryPickupItem(e, out var item)) sb.Append($"; pickup {ItemName(data, item)}");
            var offer = game.OfferOf(e);
            sb.Append(offer.Present ? $"; offer {offer.OfferId}{(offer.Disabled ? " disabled" : "")}" : "; no offer");
            sb.Append($"; SpawnTime {stamp}");
            if (loop.Cached(e) is { } c && (c.Template != template || c.Stamp != stamp))
                sb.Append($"; the loop had it as {Name(game, c.Template)} (SpawnTime {c.Stamp}): another entity under this id");
            var notes = _notes.TryGetValue(e, out var n) ? string.Join(" / ", n) : "not looked at in that scan";
            sb.Append("; loop: ").Append(notes);
            log.Add(sb.ToString());
            if (chat.Count < MaxChatLines)
                chat.Add($"{name} {d:0} m: {(n is { Count: > 0 } ? n[^1] : notes)}");
        }
        chat.Add("Auto Loot: the whole list is in kiln/logs/kiln.log (lines \"near:\").");
        _done(log, chat);
    }

    private static string Inventory(IGame game, Data data, EntityId e)
    {
        if (!game.HasInventory(e)) return "no inventory";
        if (game.Slots(e) is not { } slots) return "inventory cannot be read";
        if (slots.Count == 0) return "inventory empty";
        var stacks = string.Join(", ", slots.Take(6).Select(s => $"{ItemName(data, s.Item)} x{s.Count}"));
        return $"inventory {slots.Count} stack(s): {stacks}{(slots.Count > 6 ? ", ..." : "")}";
    }

    private static string Name(IGame game, Guid? template) =>
        template is not { } g ? "(no template)" : game.TemplateName(g) ?? g.ToString();

    private static string ItemName(Data data, uint item) =>
        data.Items.TryGet(item, out var row) && (row.English.Length > 0 ? row.English : row.Internal) is { Length: > 0 } name
            ? name
            : item.ToString(CultureInfo.InvariantCulture);

    private static string F(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
}
