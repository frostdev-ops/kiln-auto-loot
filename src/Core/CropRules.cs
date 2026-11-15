// SPDX-License-Identifier: MIT
// Ripe crops. A crop in a farm bed is a chain of templates, one per growth stage ("T2_Plant_Growth_Beets_Seedling_0",
// "_1", "_2_harvest"). Several late stages may allow a harvest; only the last one gives the full yield, so the mod
// harvests only that one. The chain is the game's own growth data: the crop's plant template lists its stages in order
// (its Upgrade levels), so the last stage is the ripe one when it can be harvested. A crop the game adds later comes with
// such a list and needs no change here. Harvest stages that no list names fall back to their names: the crop is the
// name up to its stage part, the stage number is the number in it ("_Seedling_2_harvest" = 2, "_3_harvest" = 3,
// "_01_d_Harvest" = 1), and the highest one is ripe. Pure: no Kiln calls.
using System.Text.RegularExpressions;

namespace KilnAutoLoot.Core;

public static class CropRules
{
    private static readonly Regex StagePart = new(@"_(seedling|\d+|harvest)(_|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Number = new(@"(?<=_)\d+(?=_|$)", RegexOptions.CultureInvariant);

    /// <summary>A growth stage of a crop or a planted tree (it grows into the next stage).</summary>
    public static bool IsGrowthStage(TemplateFacts t) => t.Name.Contains("plant_growth", StringComparison.OrdinalIgnoreCase);

    /// <summary>A stage that can be harvested: its name says so and it has a stock to give.</summary>
    public static bool IsHarvestStage(TemplateFacts t) =>
        IsGrowthStage(t) && t.Name.Contains("harvest", StringComparison.OrdinalIgnoreCase) &&
        (t.Has("DefaultInventory") || t.Has("PickupItem")) && t.Has("InteractionOfferRuntime");

    /// <summary>The crop a stage belongs to (lower case), and the stage number (-1 when the name has none).</summary>
    public static (string Crop, int Stage) StageOf(string name)
    {
        var lower = name.ToLowerInvariant().TrimStart('_');
        var m = StagePart.Match(lower);
        var crop = m.Success ? lower[..m.Index] : lower;
        var rest = m.Success ? lower[m.Index..] : "";
        var stage = -1;
        foreach (Match n in Number.Matches(rest))
        {
            stage = int.Parse(n.Value, System.Globalization.CultureInfo.InvariantCulture);
            break;
        }
        return (crop, stage);
    }

    /// <summary>The stages a crop's plant template grows through, in order (its "Upgrade.stages" fact).</summary>
    public static IReadOnlyList<Guid> StagesOf(TemplateFacts plant) =>
        plant.Value("Upgrade.stages") is { Length: > 0 } s
            ? s.Split(',').Select(x => Guid.TryParse(x, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList()
            : [];

    /// <summary>For each crop, its last harvest stage: the last stage of its growth list when that can be harvested, else
    /// (for harvest stages no list names) the highest stage number of the name.</summary>
    public static HashSet<Guid> RipeStages(IReadOnlyList<TemplateFacts> templates, Func<TemplateFacts, bool> usable)
    {
        var byGuid = new Dictionary<Guid, TemplateFacts>();
        foreach (var t in templates) byGuid.TryAdd(t.Guid, t);
        var ripe = new HashSet<Guid>();
        var listed = new HashSet<Guid>();
        foreach (var plant in templates)
        {
            var stages = StagesOf(plant);
            if (stages.Count == 0 || !usable(plant)) continue;
            foreach (var g in stages) listed.Add(g);
            if (byGuid.TryGetValue(stages[^1], out var last) && IsHarvestStage(last) && usable(last)) ripe.Add(last.Guid);
        }
        var best = new Dictionary<string, (int Stage, List<Guid> Guids)>(StringComparer.Ordinal);
        foreach (var t in templates)
        {
            if (listed.Contains(t.Guid) || !IsHarvestStage(t) || !usable(t)) continue;
            var (crop, stage) = StageOf(t.Name);
            if (!best.TryGetValue(crop, out var b) || stage > b.Stage) best[crop] = (stage, [t.Guid]);
            else if (stage == b.Stage) b.Guids.Add(t.Guid);   // the same stage in two variants (animated, ...)
        }
        foreach (var b in best.Values) foreach (var g in b.Guids) ripe.Add(g);
        return ripe;
    }
}
