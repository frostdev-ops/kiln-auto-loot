// SPDX-License-Identifier: MIT
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Offline;

internal sealed class FakeClock : IClock
{
    public long NowMs { get; set; }
}

internal static class Fixtures
{
    // id, internal, English, German, lootable, category, menu group, guid, stack
    public const string ItemsText =
        "# id\tinternal\ten\tde\tlootable\tcategory\tgroup\tguid\tstack\n" +
        "1\tMaterial_T1_Twigs\tTwigs\tÄste\t1\tMaterials\tMaterials\tb28bc647-c6b2-4415-9bef-82acfbf97353\t50\n" +
        "2\tMaterial_T1_Stone\tStone\tStein\t1\tMaterials\tMaterials\t\t50\n" +
        "3\tMaterial_PlantFibre\tPlant Fiber\tPflanzenfaser\t1\tMaterials\tMaterials\n" +
        "4\tQuest_Key\tOld Key\tAlter Schlüssel\t1\tKeys\tKeys\n" +
        "5\tHidden_Thing\tHidden\t\t0\tMisc\n" +
        "6\tFood_Apple\tApple\tApfel\t1\tFood\t\n" +
        "7\tFlint\tFlint\tFeuerstein\t1\tTerrain\tTerrain\n" +
        "8\tNo_Names\t\t\t1\tMaterials\tMaterials\n";

    public static ItemTable Items() => ItemTable.Parse(new StringReader(ItemsText));

    /// <summary>The same items as a dedicated server knows them: internal names only (english: the English names too,
    /// as Kiln's item catalog gives them).</summary>
    public static ItemTable ServerItems(bool english = false) =>
        ItemTable.FromRows(Items().All.Select(r => r with { English = english ? r.English : "", German = "" }));

    public static QuestItems Quest(params uint[] ids) =>
        QuestItems.Parse(new StringReader(string.Join("\n", ids.Select(i => $"{i}\tq"))));

    public static IgnoreList Ignore(string raw) => new(raw, Items());

    public static TemplateRule Rule(Group g, TemplateFlags f = TemplateFlags.None, params uint[] yield) =>
        new(Guid.NewGuid(), g, f, "T_" + g, yield);
}
