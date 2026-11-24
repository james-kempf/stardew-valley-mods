using System.Collections.Generic;
using System.Linq;

namespace PerformanceBundle.Core;

internal sealed record NameCount(string Name, int Count);

/// <summary>Raised the first time a clock minute's invalidation count reaches the storm threshold.</summary>
internal sealed record StormNotice(long Minute, int Count, IReadOnlyList<NameCount> Top);

/// <summary>A copy of one warp's counts, taken under the aggregator's lock.</summary>
internal sealed record WarpSnapshot(string Location, long AtMs, int Invalidated, IReadOnlyList<NameCount> Top);

internal sealed class WarpInvalidations
{
    public WarpInvalidations(string location, long atMs)
    {
        this.Location = location;
        this.AtMs = atMs;
    }

    public string Location { get; }
    public long AtMs { get; }
    public int Invalidated { get; set; }
    public Dictionary<string, int> Names { get; } = new();
}

/// <summary>Aggregates invalidated asset names per session, per clock minute and per warp. Thread-safe:
/// SMAPI raises AssetsInvalidated wherever the invalidation happens.</summary>
internal sealed class InvalidationAggregator
{
    public const int WarpHistory = 10;
    private const int MinuteMs = 60_000;

    private readonly object Sync = new();
    private readonly long WarpWindowMs;
    private readonly Dictionary<string, int> Totals = new();
    private readonly Dictionary<string, int> MinuteNames = new();
    private readonly List<WarpInvalidations> Warps = new();
    /// <summary>Names invalidated within the window after any warp, for the whole session (not just recent warps).</summary>
    private readonly Dictionary<string, int> WarpNames = new();
    private int WarpTotal;
    private long CurrentMinute = -1;
    private int CurrentMinuteCount;
    private bool CurrentMinuteNotified;

    /// <param name="stormThresholdPerMinute">Invalidated names in one clock minute that count as a storm.</param>
    /// <param name="warpWindowMs">Invalidations within this time after a warp are charged to it. CP reacts to the
    /// warp in its own Warped handler, which may run after ours.</param>
    public InvalidationAggregator(int stormThresholdPerMinute, long warpWindowMs = 2000)
    {
        this.StormThreshold = stormThresholdPerMinute;
        this.WarpWindowMs = warpWindowMs;
    }

    public int StormThreshold { get; set; }
    public int TotalEvents { get; private set; }
    public int TotalNames { get; private set; }
    public int WorstMinuteCount { get; private set; }
    public int StormMinutes { get; private set; }

    /// <summary>Recent warps with their 3 most invalidated names, copied inside the lock (the live entries keep
    /// changing on loader threads).</summary>
    public IReadOnlyList<WarpSnapshot> RecentWarps
    {
        get
        {
            lock (this.Sync)
                return this.Warps.Select(w => new WarpSnapshot(w.Location, w.AtMs, w.Invalidated, Top(w.Names, 3))).ToList();
        }
    }

    /// <summary>Record one AssetsInvalidated event. Returns a notice when this minute first reaches the threshold.</summary>
    public StormNotice? Record(IEnumerable<string> names, long nowMs)
    {
        lock (this.Sync)
        {
            long minute = nowMs / MinuteMs;
            if (minute != this.CurrentMinute)
            {
                this.CurrentMinute = minute;
                this.CurrentMinuteCount = 0;
                this.CurrentMinuteNotified = false;
                this.MinuteNames.Clear();
            }

            WarpInvalidations? warp = this.Warps.Count > 0 ? this.Warps[^1] : null;
            if (warp != null && nowMs - warp.AtMs > this.WarpWindowMs)
                warp = null;

            this.TotalEvents++;
            foreach (string name in names)
            {
                this.TotalNames++;
                this.CurrentMinuteCount++;
                Increment(this.Totals, name);
                Increment(this.MinuteNames, name);
                if (warp != null)
                {
                    warp.Invalidated++;
                    Increment(warp.Names, name);
                    Increment(this.WarpNames, name);
                }
            }

            if (this.CurrentMinuteCount > this.WorstMinuteCount)
                this.WorstMinuteCount = this.CurrentMinuteCount;
            if (this.CurrentMinuteNotified || this.StormThreshold <= 0 || this.CurrentMinuteCount < this.StormThreshold)
                return null;
            this.CurrentMinuteNotified = true;
            this.StormMinutes++;
            return new StormNotice(minute, this.CurrentMinuteCount, Top(this.MinuteNames, 5));
        }
    }

    public void MarkWarp(string location, long nowMs)
    {
        lock (this.Sync)
        {
            this.Warps.Add(new WarpInvalidations(location, nowMs));
            this.WarpTotal++;
            if (this.Warps.Count > WarpHistory)
                this.Warps.RemoveAt(0);
        }
    }

    /// <summary>Session totals of names invalidated after warps, and the number of warps.</summary>
    public (Dictionary<string, int> Names, int Warps) WarpTotals()
    {
        lock (this.Sync) return (new Dictionary<string, int>(this.WarpNames), this.WarpTotal);
    }

    public IReadOnlyList<NameCount> TopAssets(int count)
    {
        lock (this.Sync) return Top(this.Totals, count);
    }

    public void Reset()
    {
        lock (this.Sync)
        {
            this.Totals.Clear();
            this.MinuteNames.Clear();
            this.Warps.Clear();
            this.WarpNames.Clear();
            this.WarpTotal = 0;
            this.CurrentMinute = -1;
            this.CurrentMinuteCount = 0;
            this.CurrentMinuteNotified = false;
            this.TotalEvents = this.TotalNames = this.WorstMinuteCount = this.StormMinutes = 0;
        }
    }

    public static IReadOnlyList<NameCount> Top(Dictionary<string, int> counts, int count) =>
        counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key).Take(count).Select(p => new NameCount(p.Key, p.Value)).ToList();

    private static void Increment(Dictionary<string, int> counts, string name) =>
        counts[name] = counts.TryGetValue(name, out int n) ? n + 1 : 1;
}
