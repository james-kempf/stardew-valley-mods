using System;
using System.Collections.Generic;

namespace DeadlyLightning;

/// <summary>What the victim's client does with a strike order this tick.</summary>
internal enum OrderAction
{
    /// <summary>All guards pass: kill now.</summary>
    Execute,
    /// <summary>Only a transient guard failed (menu, tool, eating, can't move) and the deferral window is still open.</summary>
    Wait,
    /// <summary>Silent miss: a hard guard failed, or the deferral window ran out.</summary>
    Drop
}

/// <summary>How one screen shows a strike.</summary>
internal enum StrikeView
{
    /// <summary>The screen has no location yet; try again next tick.</summary>
    Later,
    /// <summary>Same location as the victim: bolt, full flash, thunder.</summary>
    Full,
    /// <summary>Outdoors in a storm elsewhere: small flash, distant thunder.</summary>
    Distant,
    /// <summary>Nothing to show.</summary>
    None
}

/// <summary>Victim-side state read from the local client. Kept as plain bools so the rules can be tested without the game.</summary>
internal readonly record struct VictimState(
    bool EventUp,
    bool Festival,
    bool KillScreen,
    bool HealthPositive,
    bool PassedOut,
    bool PassingOut,
    bool Warping,
    bool Fading,
    bool NewDay,
    bool LocationMatches,
    bool Outdoors,
    bool Storm,
    int RingCount,
    bool CanMove,
    bool UsingTool,
    bool Eating,
    bool MenuOpen,
    bool TimePasses);

/// <summary>Rules carried in a strike order so the victim re-checks exactly what the host required.</summary>
internal readonly record struct OrderRules(bool RequireOutdoors, bool RequireStorm, bool RequireRing);

/// <summary>Game-independent strike rules (no SMAPI/XNA types) so they can be tested without the game.</summary>
internal static class StrikeMath
{
    /// <summary>How long an order may wait on transient guards (menu, tool, eating) before it's dropped.</summary>
    public const long DeferralMs = 3000;

    /// <summary>Any inbox entry older than this is purged, e.g. an order for a split-screen player who left.</summary>
    public const long OrderExpiryMs = 10000;

    /// <summary>A strike visual older than this is no longer drawn (e.g. a screen that joined late).</summary>
    public const long VisualExpiryMs = 3000;

    /// <summary>Ring slots in use. A combined ring fills one slot, so it counts as one.</summary>
    public static int RingCount(bool hasLeft, bool hasRight)
        => (hasLeft ? 1 : 0) + (hasRight ? 1 : 0);

    /// <summary>Strike chance for one player per check. Exactly 0 without rings, whatever the config says.</summary>
    public static double ChanceFor(int ringCount, double baseChance, bool scaleWithRings)
    {
        if (ringCount <= 0 || double.IsNaN(baseChance))
            return 0;
        return Math.Clamp(baseChance * (scaleWithRings ? ringCount : 1), 0, 1);
    }

    /// <summary>Vanilla runs its lightning update on each 10-minute tick while time &lt; 2400, i.e. 6:10 to 23:50
    /// (107 checks). TimeChanged also fires for the 6:00 day reset and for console time changes going backwards.</summary>
    public static bool IsRollTime(int oldTime, int newTime)
        => newTime > 600 && newTime < 2400 && newTime > oldTime;

    /// <summary>A peer whose mod major.minor is older than the host's is skipped, since its message handling may differ.</summary>
    public static bool VersionCompatible(int peerMajor, int peerMinor, int hostMajor, int hostMinor)
        => peerMajor > hostMajor || (peerMajor == hostMajor && peerMinor >= hostMinor);

    /// <summary>Host-side coarse filter. The host can't read remote players' local state reliably (e.g. health is
    /// stale for remote farmers), so the victim's client re-checks everything before anything happens.</summary>
    public static bool HostEligible(bool hasLocation, bool isDedicated, bool hasMod, int ringCount, bool inStorm,
        bool outdoors, bool indoorsSafe, bool locationHasEvent, bool healthPositive)
        => hasLocation && !isDedicated && hasMod && ringCount > 0 && inStorm
           && (outdoors || !indoorsSafe) && !locationHasEvent && healthPositive;

    /// <summary>Independent roll per candidate; if several hit, one of the hits is picked uniformly.
    /// So there's at most one victim per check, and each player's odds stay close to their own chance.</summary>
    public static long? PickVictim(IReadOnlyList<(long Id, double Chance)> candidates, Func<double> next01)
    {
        List<long>? hits = null;
        foreach (var (id, chance) in candidates)
        {
            if (next01() < chance)
                (hits ??= new List<long>()).Add(id);
        }
        if (hits == null)
            return null;
        int i = Math.Min((int)(next01() * hits.Count), hits.Count - 1);
        return hits[i];
    }

    /// <summary>Reason the strike must be dropped at once, or null. Deaths during events, festivals, warps, fades,
    /// passing out or the day change break vanilla flows, so these never wait.</summary>
    public static string? HardBlockReason(in VictimState s, in OrderRules r)
    {
        if (s.EventUp) return "event running";
        if (s.Festival) return "festival";
        if (s.KillScreen) return "already dying";
        if (!s.HealthPositive) return "health already 0";
        if (s.PassedOut || s.PassingOut) return "passed out";
        if (s.Warping || s.Fading) return "warping or screen fading";
        if (s.NewDay) return "day ending";
        if (!s.LocationMatches) return "victim left the location";
        if (r.RequireOutdoors && !s.Outdoors) return "indoors";
        if (r.RequireStorm && !s.Storm) return "no storm here";
        if (r.RequireRing && s.RingCount <= 0) return "no ring";
        return null;
    }

    /// <summary>Reason to wait, or null. Vanilla only runs its death check while the farmer CanMove and the dying
    /// flow needs no open menu, so a kill here would be lost or stuck.</summary>
    public static string? TransientBlockReason(in VictimState s)
    {
        if (s.MenuOpen) return "menu open";
        if (!s.CanMove) return "can't move";
        if (s.UsingTool) return "using a tool";
        if (s.Eating) return "eating";
        if (!s.TimePasses) return "time paused";
        return null;
    }

    public static StrikeView ViewFor(bool hasLocation, bool sameLocation, bool outdoors, bool storm)
    {
        if (!hasLocation) return StrikeView.Later;
        if (sameLocation) return StrikeView.Full;
        return outdoors && storm ? StrikeView.Distant : StrikeView.None;
    }

    public static OrderAction Decide(string? hardReason, string? transientReason, long ageMs)
    {
        if (hardReason != null)
            return OrderAction.Drop;
        if (transientReason == null)
            return OrderAction.Execute;
        return ageMs < DeferralMs ? OrderAction.Wait : OrderAction.Drop;
    }
}
