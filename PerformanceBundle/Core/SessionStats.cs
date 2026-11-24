using System;
using System.Collections.Generic;
using System.Linq;

namespace PerformanceBundle.Core;

/// <summary>Warp frame times for one location.</summary>
internal sealed class WarpStat
{
    public int Count { get; private set; }
    public double TotalMs { get; private set; }
    public double MaxMs { get; private set; }
    public double AvgMs => this.Count == 0 ? 0 : this.TotalMs / this.Count;

    public void Add(double ms)
    {
        this.Count++;
        this.TotalMs += ms;
        this.MaxMs = Math.Max(this.MaxMs, ms);
    }

    public WarpStat Copy()
    {
        var copy = new WarpStat();
        copy.Count = this.Count;
        copy.TotalMs = this.TotalMs;
        copy.MaxMs = this.MaxMs;
        return copy;
    }
}

/// <summary>Counters for one in-game day (or the part of it that was played).</summary>
internal sealed class PeriodStats
{
    public const int SpikesKept = 5;

    public long Frames;
    public double TotalFrameMs;
    public double MaxFrameMs;
    public readonly SpikeLog Spikes = new(SpikesKept);
    public readonly Dictionary<string, WarpStat> Warps = new();
    public double? SaveLoadMs;
    public int Gen0, Gen1, Gen2;
    public double GcPauseMs;
    public long Invalidated;
    public int StormMinutes;
    public readonly Dictionary<string, int> InvalidatedNames = new();

    public bool IsEmpty => this.Frames == 0 && this.Invalidated == 0 && this.SaveLoadMs == null;
}

/// <summary>A finished day: a one-line summary and the detail block.</summary>
internal sealed record DayRecord(string Label, string Summary, IReadOnlyList<string> Details);

/// <summary>Per-day counters plus the session-wide inputs for the "Likely culprits" summary. Thread-safe: asset
/// invalidations can be reported from loader threads.</summary>
internal sealed class SessionStats
{
    public const int MaxDays = 120;

    private readonly object Sync = new();
    private readonly List<DayRecord> Days = new();
    private readonly Dictionary<string, WarpStat> SessionWarps = new();
    private readonly Dictionary<string, int> SpikeTopMods = new();
    private PeriodStats Day = new();
    private long DayStartMs;

    public SessionStats(long nowMs) => this.DayStartMs = nowMs;

    /// <summary>Spikes this session, and how many of them had handler profiler data.</summary>
    public int Spikes { get; private set; }
    public int SpikesWithCulprits { get; private set; }

    public void RecordFrame(double ms, int gen0, int gen1, int gen2, double gcPauseMs)
    {
        lock (this.Sync)
        {
            PeriodStats d = this.Day;
            d.Frames++;
            d.TotalFrameMs += ms;
            d.MaxFrameMs = Math.Max(d.MaxFrameMs, ms);
            d.Gen0 += gen0;
            d.Gen1 += gen1;
            d.Gen2 += gen2;
            d.GcPauseMs += gcPauseMs;
        }
    }

    public void RecordSpike(SpikeRecord spike)
    {
        lock (this.Sync)
        {
            this.Day.Spikes.Add(spike);
            this.Spikes++;
            if (spike.Culprits is { Count: > 0 } culprits)
            {
                this.SpikesWithCulprits++;
                string mod = culprits[0].Mod;
                this.SpikeTopMods[mod] = this.SpikeTopMods.TryGetValue(mod, out int n) ? n + 1 : 1;
            }
        }
    }

    public void RecordWarp(string location, double ms)
    {
        lock (this.Sync)
        {
            Add(this.Day.Warps, location, ms);
            Add(this.SessionWarps, location, ms);
        }
    }

    public void RecordSaveLoad(double ms)
    {
        lock (this.Sync)
            this.Day.SaveLoadMs = ms;
    }

    public void RecordInvalidated(IReadOnlyCollection<string> names)
    {
        lock (this.Sync)
        {
            this.Day.Invalidated += names.Count;
            foreach (string name in names)
                this.Day.InvalidatedNames[name] = this.Day.InvalidatedNames.TryGetValue(name, out int n) ? n + 1 : 1;
        }
    }

    public void RecordStorm()
    {
        lock (this.Sync)
            this.Day.StormMinutes++;
    }

    /// <summary>A save finished loading: restart the day's clock and frame/GC counters so title-screen time isn't
    /// counted. The save load time, spikes (incl. the load itself), warps and invalidations are kept.</summary>
    public void StartPlay(long nowMs)
    {
        lock (this.Sync)
        {
            PeriodStats d = this.Day;
            this.DayStartMs = nowMs;
            d.Frames = 0;
            d.TotalFrameMs = d.MaxFrameMs = d.GcPauseMs = 0;
            d.Gen0 = d.Gen1 = d.Gen2 = 0;
        }
    }

    /// <summary>Format the current day, store it and start a new one. Returns null when nothing was recorded.</summary>
    public DayRecord? CloseDay(string label, long nowMs)
    {
        lock (this.Sync)
        {
            PeriodStats day = this.Day;
            double minutes = (nowMs - this.DayStartMs) / 60_000.0;
            this.Day = new PeriodStats();
            this.DayStartMs = nowMs;
            if (day.IsEmpty)
                return null;
            DayRecord record = Format(label, minutes, day);
            this.Days.Add(record);
            if (this.Days.Count > MaxDays)
                this.Days.RemoveAt(0);
            return record;
        }
    }

    /// <summary>The current day so far, formatted but not closed.</summary>
    public DayRecord? PeekDay(string label, long nowMs)
    {
        lock (this.Sync)
            return this.Day.IsEmpty ? null : Format(label, (nowMs - this.DayStartMs) / 60_000.0, this.Day);
    }

    public IReadOnlyList<DayRecord> GetDays()
    {
        lock (this.Sync) return this.Days.ToList();
    }

    public Dictionary<string, WarpStat> GetSessionWarps()
    {
        lock (this.Sync) return this.SessionWarps.ToDictionary(p => p.Key, p => p.Value.Copy());
    }

    public Dictionary<string, int> GetSpikeTopMods()
    {
        lock (this.Sync) return new Dictionary<string, int>(this.SpikeTopMods);
    }

    public static DayRecord Format(string label, double minutes, PeriodStats d)
    {
        var summary = new List<string> { $"{label} ({minutes:0.0} min)" };
        if (d.Frames > 0)
            summary.Add($"frames avg {d.TotalFrameMs / d.Frames:0.0} / max {d.MaxFrameMs:0} ms");
        summary.Add($"spikes {d.Spikes.Count}" + (d.Spikes.Worst.Count > 0 ? $" (worst {d.Spikes.Worst[0].FrameMs:0} ms)" : ""));
        int warps = d.Warps.Values.Sum(w => w.Count);
        var slowest = d.Warps.OrderByDescending(p => p.Value.MaxMs).FirstOrDefault();
        summary.Add($"warps {warps}" + (warps > 0 ? $" (slowest {slowest.Key} {slowest.Value.MaxMs:0} ms)" : ""));
        if (d.SaveLoadMs is double load)
            summary.Add($"save load {load / 1000:0.0} s");
        summary.Add($"GC {d.Gen0}/{d.Gen1}/{d.Gen2}, pause {d.GcPauseMs:0} ms");
        summary.Add($"invalidated {d.Invalidated}" + (d.StormMinutes > 0 ? $", storm minutes {d.StormMinutes}" : ""));

        var details = new List<string> { $"-- {label} ({minutes:0.0} min real time) --" };
        if (d.SaveLoadMs is double ms)
            details.Add($"  save load: {ms / 1000:0.00} s");
        details.Add(d.Frames > 0
            ? $"  frames: {d.Frames}, avg / max {d.TotalFrameMs / d.Frames:0.0} / {d.MaxFrameMs:0.0} ms"
            : "  frames: none recorded (SpikeDiagnostics off?)");
        details.Add($"  GC collections gen0/gen1/gen2: {d.Gen0}/{d.Gen1}/{d.Gen2}, pause total {d.GcPauseMs:0} ms");
        if (d.Spikes.Count > 0)
        {
            details.Add($"  spikes: {d.Spikes.Count}; worst ({SpikeRecord.TableHeader}):");
            foreach (SpikeRecord s in d.Spikes.Worst)
                details.Add("    " + s.ToRow());
        }
        if (warps > 0)
        {
            details.Add("  warps by location (count, avg / max frame ms):");
            foreach (var (location, w) in d.Warps.OrderByDescending(p => p.Value.MaxMs).ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                details.Add($"    {location}: {w.Count}, {w.AvgMs:0} / {w.MaxMs:0}");
        }
        string top = string.Join(", ", InvalidationAggregator.Top(d.InvalidatedNames, 5).Select(t => $"{t.Name} x{t.Count}"));
        details.Add($"  assets invalidated: {d.Invalidated}, storm minutes: {d.StormMinutes}" + (top.Length > 0 ? $"; most: {top}" : ""));
        return new DayRecord(label, string.Join(" | ", summary), details);
    }

    private static void Add(Dictionary<string, WarpStat> warps, string location, double ms)
    {
        if (!warps.TryGetValue(location, out WarpStat? stat))
            warps[location] = stat = new WarpStat();
        stat.Add(ms);
    }
}
