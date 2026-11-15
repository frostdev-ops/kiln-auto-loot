// SPDX-License-Identifier: MIT
namespace KilnAutoLoot.Core;

/// <summary>Per player: the "visit" memory of container targets. Not thread-safe: one player's
/// loop uses it from one thread.</summary>
public sealed class VisitTable
{
    /// <summary>When full, the entry seen longest ago is reused.</summary>
    public const int Capacity = 1024;
    /// <summary>Unseen longer than this = a new visit.</summary>
    public const long ReArmMs = 10000;
    /// <summary>An accept the game did not react to is sent again after this.</summary>
    public const long RetryMs = 3000;
    /// <summary>After this many, loot anyway.</summary>
    public const int MaxAccepts = 3;

    private sealed class Visit
    {
        public ulong Host;
        public uint OfferId;
        public int Accepts;
        public long LastSeen, AcceptTick, FirstSeen;
        public int Loots;
        public bool FirstAcceptGiven;
    }

    private readonly IClock _clock;
    private readonly Dictionary<ulong, Visit> _byHost = new();

    public VisitTable(IClock clock) => _clock = clock;

    /// <summary>Targets remembered now (at most <see cref="Capacity"/>).</summary>
    public int Count => _byHost.Count;

    /// <summary>What to do with a container target seen now:
    /// younger than openDelay = Wait; no usable offer = Loot; no accept yet = Accept; offer id changed since the accept =
    /// Loot; accept older than 3000 ms = Accept again below 3 accepts, else Loot; otherwise Wait.
    /// <paramref name="harvestOnce"/> (plant and farm templates): after the first <see cref="Looted"/> of a visit every
    /// further Loot answers <see cref="TargetStep.AlreadyHarvested"/>, also without an offer, so the server never sends a loot-all every scan.</summary>
    public TargetStep Next(ulong host, bool hasUsableOffer, uint offerId, float lootDelaySeconds, bool harvestOnce)
    {
        long now = _clock.NowMs;
        long delay = (long)(lootDelaySeconds * 1000.0f);
        var s = Touch(host, now);
        TargetStep step;
        if (now - s.FirstSeen < delay) step = TargetStep.Wait;
        else if (!hasUsableOffer) step = TargetStep.Loot;
        else if (s.Accepts == 0) step = TargetStep.Accept;
        else if (offerId != s.OfferId) step = TargetStep.Loot;
        else if (now - s.AcceptTick > RetryMs) step = s.Accepts < MaxAccepts ? TargetStep.Accept : TargetStep.Loot;
        else step = TargetStep.Wait;
        if (step == TargetStep.Loot && harvestOnce && s.Loots > 0) step = TargetStep.AlreadyHarvested;
        return step;
    }

    /// <summary>The pickup path: true once per visit, on its first sighting (no accept yet), no
    /// delay; a visit re-arms after 10000 ms unseen. Refreshes the visit like <see cref="Next"/>. Call
    /// <see cref="Accepted"/> after sending the accept as usual.</summary>
    public bool FirstAcceptDue(ulong host)
    {
        var s = Touch(host, _clock.NowMs);
        if (s.Accepts > 0 || s.FirstAcceptGiven) return false;
        s.FirstAcceptGiven = true;
        return true;
    }

    /// <summary>Call right after the accept event for this target was sent.</summary>
    public void Accepted(ulong host, uint offerId)
    {
        if (!_byHost.TryGetValue(host, out var s)) return;
        s.OfferId = offerId;
        s.Accepts++;
        s.AcceptTick = _clock.NowMs;
    }

    /// <summary>Call after a loot-all for this target was sent (counts the harvest of the visit).</summary>
    public void Looted(ulong host)
    {
        if (_byHost.TryGetValue(host, out var s)) s.Loots++;
    }

    /// <summary>Forget a target (it left the world).</summary>
    public void Forget(ulong host) => _byHost.Remove(host);

    private Visit Touch(ulong host, long now)
    {
        if (!_byHost.TryGetValue(host, out var s))
        {
            if (_byHost.Count >= Capacity)
            {
                Visit oldest = null!;
                foreach (var v in _byHost.Values) if (oldest is null || v.LastSeen < oldest.LastSeen) oldest = v;
                _byHost.Remove(oldest.Host);   // table full: the longest unseen is forgotten
            }
            s = new Visit { Host = host, FirstSeen = now, LastSeen = now };
            _byHost[host] = s;
        }
        else if (now - s.LastSeen > ReArmMs)
        {
            s.OfferId = 0; s.Accepts = 0; s.AcceptTick = 0; s.Loots = 0; s.FirstAcceptGiven = false; s.FirstSeen = now;   // a new visit
        }
        s.LastSeen = now;
        return s;
    }
}
