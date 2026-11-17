// SPDX-License-Identifier: MIT
// The work the F8 window hands to the tick thread while no world runs (the title screen). Kiln runs a mod's posted work
// (Scheduler.Post) only at a world's step, and on the title screen no world steps: 0.1.6's World tab stayed read-only
// there and a change made there waited for the next world (a player's report, Nexus, 17 Nov 2026). The game's UI frame
// runs this queue there instead (ClientPart: Kiln's tick thread on the title screen); a world that starts first runs
// what is left at its first step (the drain posted with each piece of work). Pure: the tests give the post function.
using System.Collections.Concurrent;

namespace KilnAutoLoot.Client;

internal sealed class MenuWork(Action<Action> postToTick, Action<Exception> failed)
{
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly object _running = new();

    /// <summary>There is work waiting.</summary>
    public bool Pending => !_queue.IsEmpty;

    /// <summary>Queues <paramref name="work"/> for the next <see cref="Run"/>: the next UI frame on the title screen,
    /// or the next world's first step when one starts first. Any thread.</summary>
    public void Post(Action work)
    {
        _queue.Enqueue(work);
        postToTick(Run);
    }

    /// <summary>Runs the queued work in order, each piece once; one that throws is reported and the rest still run.
    /// Tick thread.</summary>
    public void Run()
    {
        lock (_running)
        {
            while (_queue.TryDequeue(out var work))
            {
                try
                {
                    work();
                }
                catch (Exception e)
                {
                    failed(e);
                }
            }
        }
    }
}
