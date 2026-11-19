// SPDX-License-Identifier: MIT
// Kiln Auto Loot: automatic looting, done on the server.
// The server world (a dedicated server, or the game while it hosts) loots for every player: Server/LootServer.
// The player's game shows the F8 window, takes the F9 key and shows the notices: Client/ClientPart.
using Kiln;

namespace KilnAutoLoot;

public sealed class AutoLootMod : Mod
{
    internal const string Version = "0.1.8";
    internal const string Id = "kiln-auto-loot";

    private Server.LootServer? _server;
    private Client.ClientPart? _client;

    /// <summary>The mod's one channel, opened in OnLoad before either part starts; both parts use it.</summary>
    internal static Net.ChannelHub Hub { get; private set; } = new();

    /// <summary>The mod's scheduler (Context.Scheduler), for parts that are not the Mod: posting to the tick thread.</summary>
    internal static Kiln.Scheduler? Scheduler { get; private set; }

    /// <summary>The mod's context (Tick, DataDirectory, Info), for parts that are not the Mod.</summary>
    internal static Kiln.ModContext? ModContext { get; private set; }

    public override void OnLoad()
    {
        ModContext = Context;
        Scheduler = Context.Scheduler;
        Hub = new Net.ChannelHub();
        Hub.Open();
        var data = new Data();
        // the server world reads the game data at its start, before the loot server's own start handler runs (handlers run
        // in the order they were added), so looting starts on a ready Data; a Data that is not ready takes nothing
        _subscriptions.Add(Events.On<ServerStartedEvent>(_ => FillServerData(data)));
        if (Lifecycle.IsServerRunning) FillServerData(data);
        _server = new Server.LootServer(data);
        _server.Start();
        // after the loot server's start: its settings are registered by then (the debug dump follows counterLog)
        _subscriptions.Add(Events.On<ServerStartedEvent>(_ => DumpData(data)));
        if (Ui.Available)
        {
            _client = new Client.ClientPart(data, _server);
            _client.Start();
            // the player's game: the window and the notices need the item names. A worker reads them now, or as soon as
            // the game serves its resources; this frame does not wait for it (on a hard disk it took seconds)
            if (!PollData(data))
            {
                _retryFill = (in TickInfo _) => RetryFill(data);
                Kiln.Client.Tick += _retryFill;
                // the title screen: no world steps, so Kiln.Client.Tick waits for one; the game's UI frame drives Kiln's
                // tick thread there and moves the load on (the F8 item list fills in the main menu)
                _menuFill = _ =>
                {
                    if (!Kiln.Client.InGame && Game.IsTickThread) RetryFill(data);
                };
                Ui.Screens.UiTick += _menuFill;
            }
        }
    }

    private readonly List<EventSubscription> _subscriptions = [];
    private TickHandler? _retryFill;
    private Action<GameScreen>? _menuFill;
    private long _nextFillTry;

    /// <summary>Each tick (or UI frame) while a worker runs; every 2 s until the game serves its resources.</summary>
    private void RetryFill(Data data)
    {
        if (!Server.GameData.GameDataLoader.Busy && Environment.TickCount64 < _nextFillTry) return;
        _nextFillTry = Environment.TickCount64 + 2000;
        if (PollData(data)) StopRetry();
    }

    private void StopRetry()
    {
        if (_retryFill is not null) Kiln.Client.Tick -= _retryFill;
        if (_menuFill is not null) Ui.Screens.UiTick -= _menuFill;
        _retryFill = null;
        _menuFill = null;
    }

    /// <summary>Moves the game data load on (GameDataLoader: once per process) and fills Data when it is in; never
    /// waits. Tick thread.</summary>
    private bool PollData(Data data)
    {
        if (data.Ready) return true;
        try
        {
            return Server.GameData.GameDataLoader.Poll(data, Context.DataDirectory, Log.Info, LoadError);
        }
        catch (Exception e)
        {
            LoadError(e);
            return false;
        }
    }

    /// <summary>A server world's start: its handlers expect the data, so this waits for the load (started at load in a
    /// player's game, so usually done by then).</summary>
    private void FillServerData(Data data)
    {
        if (data.Ready) return;
        try
        {
            if (Server.GameData.GameDataLoader.Finish(data, Context.DataDirectory, Log.Info, LoadError)) return;
        }
        catch (Exception e)
        {
            LoadError(e);
            return;
        }
        Log.Warn("Kiln Auto Loot: the game's data cannot be read in this world (kiln.assets or kiln.types is off): " +
                 "nothing is taken or smashed automatically");
    }

    private void LoadError(Exception e) =>
        Log.Error(e, "Kiln Auto Loot: the game data could not be read; nothing is taken automatically");

    private void DumpData(Data data)
    {
        if (data.Ready && _server?.ServerConfig?.Value.CounterLog == true)
            Server.GameData.GameDataLoader.WriteDump(data, Context.DataDirectory, Log.Info);
    }

    public override void OnUnload()
    {
        foreach (var sub in _subscriptions) sub.Dispose();
        _subscriptions.Clear();
        StopRetry();
        _client?.Stop();
        _server?.Stop();
        Hub.Dispose();
    }
}

/// <summary>The mod's data, built from the player's own game (Server/GameData reads it, src/Core decides it): which
/// templates are taken and how, the items with their names, the quest items and the smash targets. Empty and not
/// <see cref="Ready"/> until then: an unknown template is never taken, so nothing is looted or smashed meanwhile. The
/// tables are filled in place, so every part that holds them sees the data once it is there.</summary>
internal sealed record Data(Core.TemplateTable Templates, Core.ItemTable Items, Core.QuestItems Quest)
{
    public Data() : this(Core.TemplateTable.Empty(), Core.ItemTable.Empty(), Core.QuestItems.Empty()) { }

    public Server.SmashTable Smash { get; init; } = Server.SmashTable.Empty;

    /// <summary>The game data is in.</summary>
    public bool Ready { get; private set; }

    public void Fill(Core.Classification c)
    {
        Templates.Fill(c.Templates);
        Items.Fill(c.Items);
        Quest.Fill(c.QuestItems);
        Smash.Fill(c.Smash);
        Ready = true;
    }
}
