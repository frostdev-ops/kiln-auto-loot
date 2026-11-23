// SPDX-License-Identifier: MIT
// A fake world for the loot loop (src/Server/IGame.cs): entities with a template, components, an offer, slots; players;
// and a record of every accept, loot-all and Items.Move the loop sent.
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Server;

namespace KilnAutoLoot.Offline.LootServerTests;

internal sealed class FakeEntity
{
    public required EntityId Id { get; init; }
    public WorldPosition Position { get; set; }
    public Guid Template { get; set; }
    public ulong Serial { get; set; } = 1;
    public HashSet<string> Components { get; } = [];
    public uint? PickupItem { get; set; }
    public Offer Offer { get; set; } = Offer.None;
    public bool Dead { get; set; }
    public List<ContainerSlot>? Slots { get; set; }
    public Sight Sight { get; set; } = Sight.Visible;
    public int PickupAmount { get; set; } = 1;
    /// <summary>The game's SpawnTime of the entity (0 = none, as for what the save loaded).</summary>
    public long SpawnStamp { get; set; }
    public int Rarity { get; set; } = -1;
}

internal sealed class FakeGame : IGame
{
    public readonly Dictionary<EntityId, FakeEntity> Entities = new();
    public readonly List<GamePlayer> PlayerList = [];
    public readonly List<(EntityId Guest, EntityId Host, uint Offer)> Interacts = [];
    public readonly List<(EntityId Guest, EntityId Host)> Loots = [];
    public readonly List<(EntityId From, ulong Slot, EntityId To, Action<MoveOutcome> Done, Action<IReadOnlyList<EntityId>>? Moved)> Moves = [];
    /// <summary>The count of each move (0 = the whole slot), in the order of <see cref="Moves"/>.</summary>
    public readonly List<int> MoveCounts = [];
    /// <summary>The players' inventories (null = cannot be read); the free slots are <see cref="FreeSlotCount"/>.</summary>
    public List<BagSlot>? BagSlots = [];
    public int BagReads;
    public int StackWish;
    public bool SightOn = true;
    public int SightCalls;
    /// <summary>What one line-of-sight call costs on the work clock (microseconds).</summary>
    public long SightCostMicros;
    public readonly Dictionary<string, int> FindCalls = new();
    public readonly Dictionary<Guid, string> TemplateNames = new();
    public int FreeSlotCount = 10;
    /// <summary>The work clock the loop's step budget reads (microseconds): each Spawn.Inspect costs
    /// <see cref="InspectCostMicros"/>, or the entity's own entry in <see cref="InspectCost"/>.</summary>
    public long Micros;
    public long InspectCostMicros;
    public readonly Dictionary<EntityId, long> InspectCost = new();
    public readonly HashSet<EntityId> Inspected = [];
    public int InspectCalls;
    private uint _next = 100;
    private long _stamp;

    public EntityId AddPlayer(double x = 0, double y = 0, double z = 0, bool uiOpen = false, bool nearBase = false)
    {
        var id = new EntityId((uint)(PlayerList.Count + 1));
        PlayerList.Add(new GamePlayer(id, new WorldPosition(x, y, z), uiOpen, nearBase));
        return id;
    }

    public FakeEntity Add(Guid template, double x, string[] components, uint? pickupItem = null, double y = 0) =>
        Add(new EntityId(_next++), template, x, components, pickupItem, y);

    /// <summary>A new entity under the id of one that is gone: the game hands ids out again.</summary>
    public FakeEntity Reuse(EntityId id, Guid template, double x, string[] components, uint? pickupItem = null, double y = 0)
    {
        Entities.Remove(id);
        return Add(id, template, x, components, pickupItem, y);
    }

    private FakeEntity Add(EntityId id, Guid template, double x, string[] components, uint? pickupItem, double y)
    {
        var e = new FakeEntity
        {
            Id = id, Position = new WorldPosition(x, y, 0), Template = template, PickupItem = pickupItem, SpawnStamp = ++_stamp * 16_666_667,
        };
        foreach (var c in components) e.Components.Add(c);
        if (pickupItem is not null) e.Components.Add(LootLoop.PickupComponent);
        Entities[e.Id] = e;
        return e;
    }

    public void Remove(FakeEntity e) => Entities.Remove(e.Id);

    public void Clear()
    {
        Interacts.Clear();
        Loots.Clear();
        Moves.Clear();
        MoveCounts.Clear();
    }

    public IReadOnlyList<GamePlayer> Players() => PlayerList;

    public IReadOnlyList<EntityId> Find(string component)
    {
        FindCalls[component] = FindCalls.GetValueOrDefault(component) + 1;
        return Entities.Values.Where(e => e.Components.Contains(component)).Select(e => e.Id).ToList();
    }

    public IReadOnlyList<EntityId> Within(IReadOnlyList<EntityId> candidates, WorldPosition at, double radius) =>
        candidates.Where(c => PositionOf(c) is { } p && p.DistanceTo(at) <= radius)
            .OrderBy(c => PositionOf(c)!.Value.DistanceTo(at)).ToList();

    public WorldPosition? PositionOf(EntityId entity) =>
        Entities.TryGetValue(entity, out var e) ? e.Position
        : PlayerList.FirstOrDefault(p => p.Entity == entity) is { Entity.IsNone: false } gp ? gp.Position : null;

    public string? TemplateName(Guid template) => TemplateNames.TryGetValue(template, out var n) ? n : null;

    public long SpawnStamp(EntityId entity) => Entities.TryGetValue(entity, out var e) ? e.SpawnStamp : 0;

    public Inspected? Inspect(EntityId entity)
    {
        InspectCalls++;
        Inspected.Add(entity);
        Micros += InspectCost.TryGetValue(entity, out var cost) ? cost : InspectCostMicros;
        return Entities.TryGetValue(entity, out var e) ? new Inspected(e.Template, e.Serial) : null;
    }

    public bool TryPickupItem(EntityId entity, out uint itemId)
    {
        itemId = Entities.TryGetValue(entity, out var e) && e.PickupItem is { } i ? i : 0;
        return itemId != 0;
    }

    public GroundItem? PickupDetails(EntityId entity) =>
        Entities.TryGetValue(entity, out var e) && e.PickupItem is not null ? new GroundItem(e.PickupAmount, e.Rarity) : null;

    public BagRead? Bag(EntityId player)
    {
        BagReads++;
        return BagSlots is null ? null : new BagRead(BagSlots.ToList(), FreeSlotCount);
    }

    public int StackSizeWish => StackWish;

    public bool SightAvailable => SightOn;

    public bool HasInventory(EntityId entity) =>
        Entities.TryGetValue(entity, out var e) && e.Components.Contains(LootLoop.InventoryComponent);

    public Offer OfferOf(EntityId entity) => Entities.TryGetValue(entity, out var e) ? e.Offer : Offer.None;

    public bool IsDead(EntityId entity) => Entities.TryGetValue(entity, out var e) && e.Dead;

    public IReadOnlyList<ContainerSlot>? Slots(EntityId container) =>
        Entities.TryGetValue(container, out var e) ? e.Slots : null;

    public int FreeSlots(EntityId player) => FreeSlotCount;

    public Sight LineOfSight(EntityId player, EntityId target)
    {
        SightCalls++;
        Micros += SightCostMicros;
        return Entities.TryGetValue(target, out var e) ? e.Sight : Sight.Visible;
    }

    public void Interact(EntityId guest, EntityId host, uint offerId) => Interacts.Add((guest, host, offerId));

    public void Loot(EntityId guest, EntityId host) => Loots.Add((guest, host));

    public void Move(EntityId from, ulong slot, EntityId to, int count, Action<MoveOutcome> done,
        Action<IReadOnlyList<EntityId>>? moved = null)
    {
        Moves.Add((from, slot, to, done, moved));
        MoveCounts.Add(count);
    }
}

/// <summary>A loop over a fake world with a small template table.</summary>
internal sealed class LoopRig
{
    public static readonly Guid Corpse = new("11111111-0000-0000-0000-000000000001");
    public static readonly Guid Chest = new("11111111-0000-0000-0000-000000000002");
    public static readonly Guid Bush = new("11111111-0000-0000-0000-000000000003");
    public static readonly Guid Twig = new("11111111-0000-0000-0000-000000000004");   // ground pickup, item 1
    public static readonly Guid Enemy = new("11111111-0000-0000-0000-000000000005");
    public static readonly Guid Mushroom = new("11111111-0000-0000-0000-000000000006"); // ground pickup with inventory
    public static readonly Guid Boss = new("11111111-0000-0000-0000-000000000007");
    public static readonly Guid Wolf = new("11111111-0000-0000-0000-000000000008");    // enemy, animal
    public static readonly Guid FogNest = new("11111111-0000-0000-0000-000000000009"); // ground, container path, item 2
    public static readonly Guid Bones = new("11111111-0000-0000-0000-00000000000a");   // corpse drop pile, yield apple + flint
    public static readonly Guid Parts = new("11111111-0000-0000-0000-00000000000b");   // corpse drop pile, yield unknown
    public static readonly Guid Generic = new("11111111-0000-0000-0000-00000000000c"); // generic drop, its item set at the spawn
    public static readonly Guid Book = new("11111111-0000-0000-0000-00000000000d");    // world book players also build, item 1
    public static readonly Guid Frogs = new("11111111-0000-0000-0000-00000000000e");   // critter (bait swarm), yield item 40
    public static readonly Guid Crop = new("11111111-0000-0000-0000-00000000000f");    // ripe crop in a farm bed, yield plant fibre
    public static readonly Guid Algae = new("11111111-0000-0000-0000-000000000010");   // a world plant planted algae grows into
    public static readonly Guid Goat = new("11111111-0000-0000-0000-000000000011");    // grown farm animal (livestock)
    public static readonly Guid Fish = new("11111111-0000-0000-0000-000000000012");    // fish swimming free, a pickup of item 50
    public static readonly Guid Vukah = new("11111111-0000-0000-0000-000000000013");   // corpse (no drop flag), yield flint + twigs
    public static readonly Guid EliteCorpse = new("11111111-0000-0000-0000-000000000014"); // an elite enemy's corpse
    public static readonly Guid Unknown = new("11111111-0000-0000-0000-0000000000ff");

    public const string TemplatesText =
        "# guid\tgroup\tflags\tname\tyield\n" +
        "11111111-0000-0000-0000-000000000001\tcorpse\t\tLootContainer_Enemy_Test\t\n" +
        "11111111-0000-0000-0000-000000000002\tchest\t\tLootChest_Random_Test\t\n" +
        "11111111-0000-0000-0000-000000000003\tplant\t\tScatter_Loot_PlantFibre_Test\t3\n" +
        "11111111-0000-0000-0000-000000000004\tground\t\tLootPickup_Twigs_Test\t1\n" +
        "11111111-0000-0000-0000-000000000005\tenemy\t\tEnemy_Test\t\n" +
        "11111111-0000-0000-0000-000000000006\tground\t\tScatter_Loot_Mushroom_Test\t6\n" +
        "11111111-0000-0000-0000-000000000007\tboss\t\tLootPickup_Enemy_Boss_Test\t7\n" +
        "11111111-0000-0000-0000-000000000008\tenemy\tanimal\tEnemy_Wolf_Test\t\n" +
        "11111111-0000-0000-0000-000000000009\tground\t\tFognest_Ressource_Destructible_Test\t2\n" +
        "11111111-0000-0000-0000-00000000000a\tcorpse\tdrop\tLootPickup_Material_Skeleton_Hound_Bones_Test\t6,7\n" +
        "11111111-0000-0000-0000-00000000000b\tcorpse\tdrop\tLootPickup_Material_Critter_Parts_Test\t\n" +
        "11111111-0000-0000-0000-00000000000c\tground\tgeneric\tBase_LootPickup_Generic_Gravity_Test\t\n" +
        "11111111-0000-0000-0000-00000000000d\tground\tdecor\tProp_Furniture_Book_Pile_Test_Lootpickup\t1\n" +
        "11111111-0000-0000-0000-00000000000e\tcritter\tswarm\tLootpickup_Scatter_VFX_Bait_Frogs_Test\t40\n" +
        "11111111-0000-0000-0000-00000000000f\tfarm\t\tT1_Plant_Growth_Test_2_harvest\t3\n" +
        "11111111-0000-0000-0000-000000000010\tplant\tgrown\tScatter_Loot_Wetland_Algae_01_d_AlgaeBuds_Test\t3\n" +
        "11111111-0000-0000-0000-000000000011\tlivestock\t\tPLACEMENT_HELPER_T1_Animal_Farm_Goat_Test\t\n" +
        "11111111-0000-0000-0000-000000000012\tfish\tanimal\tAnimal_wildlife_Fish_T1_common_01_Test\t50\n" +
        "11111111-0000-0000-0000-000000000013\tcorpse\t\tLootContainer_Enemy_Corpse_Vukah_Test\t7,1\n" +
        "11111111-0000-0000-0000-000000000014\tcorpse\telite\tLootContainer_Enemy_Skeleton_Elite_Test\t7\n";

    public static readonly string[] OfferInv = [LootLoop.OfferComponent, LootLoop.InventoryComponent];

    public FakeClock Clock { get; } = new() { NowMs = 1_000_000 };
    public FakeGame Game { get; } = new();
    public List<(EntityId Player, Net.NoticeMsg Msg)> Notices { get; } = [];
    public List<string> KeyLines { get; } = [];
    public LootLoop Loop { get; }
    public EntityId Player { get; }
    public Data Data { get; }

    /// <param name="items">the server's item table (default: the fixture with every name, as in a player's game).</param>
    /// <param name="hasMod">false: the player never sends settings, so the server's Defaults apply.</param>
    /// <param name="skipIds">the skip ids the player's game sends with its settings.</param>
    public LoopRig(Action<PersonalSettings>? settings = null, uint[]? quest = null, ServerSettings? server = null,
        ItemTable? items = null, bool hasMod = true, uint[]? skipIds = null, uint[]? onlyIds = null)
    {
        Data = new Data(TemplateTable.Parse(new StringReader(TemplatesText)), items ?? Fixtures.Items(), Fixtures.Quest(quest ?? []));
        Loop = new LootLoop(Game, Clock, Data, (p, m) => Notices.Add((p, m)), KeyLines.Add, () => Game.Micros);
        Loop.Configure(server ?? new ServerSettings());
        Player = Game.AddPlayer();
        var s = new PersonalSettings();
        settings?.Invoke(s);
        if (hasMod) Loop.SetPersonal(Player, s, skipIds, onlyIds);
        else
        {
            // a game without the mod sends nothing: the defaults apply once the wait for its settings is over
            Loop.Join(Player);
            Loop.Scan();
            Clock.NowMs += LootLoop.SettingsWaitMs;
            Loop.Scan();
        }
    }

    /// <summary>Advance the clock and run one scan.</summary>
    public void ScanAfter(long ms)
    {
        Clock.NowMs += ms;
        Loop.Scan();
    }

    /// <summary>One server step after <paramref name="ms"/> (the budgeted path the mod runs); returns the work
    /// microseconds the step spent on the fake work clock.</summary>
    public long StepAfter(long ms = 16)
    {
        Clock.NowMs += ms;
        var before = Game.Micros;
        Loop.Step();
        return Game.Micros - before;
    }

    public void ScanFor(long ms, long step = LootLoop.ScanEveryMs)
    {
        for (long t = 0; t < ms; t += step) ScanAfter(step);
    }
}
