// SPDX-License-Identifier: MIT
// The few mod types the loot loop names but the lab probe does not compile (the Mod itself, the server part): enough
// for the lab to run the mod's own LootLoop on the lab's world. Data is the mod's own record (src/AutoLootMod.cs).
namespace KilnAutoLoot
{
    internal static class AutoLootMod
    {
        internal const string Version = "labloot";
        internal static Kiln.Scheduler? Scheduler { get; set; }
        internal static Kiln.ModContext? ModContext { get; set; }
    }

    internal sealed record Data(Core.TemplateTable Templates, Core.ItemTable Items, Core.QuestItems Quest)
    {
        public Data() : this(Core.TemplateTable.Empty(), Core.ItemTable.Empty(), Core.QuestItems.Empty()) { }

        public Server.SmashTable Smash { get; init; } = Server.SmashTable.Empty;

        public bool Ready { get; private set; }

        public void Fill(Core.Classification c)
        {
            Templates.Fill(c.Templates, c.WorldLootOnly, c.BaitNotes);
            Items.Fill(c.Items);
            Quest.Fill(c.QuestItems);
            Smash.Fill(c.Smash);
            Ready = true;
        }
    }
}

namespace KilnAutoLoot.Server
{
    internal sealed class LootServer;
}
