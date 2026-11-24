using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using PerformanceBundle.Core;
using StardewModdingAPI;
using StardewModdingAPI.Enums;
using StardewModdingAPI.Events;
using StardewValley;

namespace PerformanceBundle.Modules;

/// <summary>Passive frame-spike logger. On screen 0's UpdateTicked it measures the time since the previous tick
/// (which covers every screen's update and draw) and, for slow frames, records GC collections and pause time,
/// asset invalidations/loads (AssetsInvalidated/AssetReady, never AssetRequested), any warp in that window and, while
/// HandlerProfiler is attached, the three mod/event pairs with the most handler time in it. Also times warps and save
/// loading, and feeds every frame, spike, warp and load into the per-day session stats. Self-measured handler time is in perf_report (budget under 50 us/tick).</summary>
internal sealed class SpikeDiagnosticsModule : PerfModule
{
    private const int WorstKept = 10;
    private const int WarpHistory = 10;
    private const long SpikeLogGapMs = 1000;

    /// <summary>GC.GetTotalPauseDuration exists in the game's 6.0.32 runtime but not in the net6.0 reference assemblies.</summary>
    private static readonly Func<TimeSpan>? TotalPauseDuration = CreatePauseClock();

    private readonly SpikeLog Spikes = new(WorstKept);
    private readonly List<(string Location, double Ms)> Warps = new();
    private long LastTickTimestamp;
    private double PreviousFrameMs;
    private long Frames;
    private double TotalFrameMs;
    private double MaxFrameMs;
    private int LastGen0, LastGen1, LastGen2;
    private int BaseGen0, BaseGen1, BaseGen2;
    private TimeSpan LastPause;
    private double TotalPauseMs;
    private int InvalidatedSinceTick;
    private int LoadedSinceTick;
    private long TotalInvalidated;
    private long TotalLoaded;
    /// <summary>Warps since the last screen-0 tick; a list so warps on several screens in one window are all kept.</summary>
    private readonly List<string> PendingWarps = new();
    private int WarpCount;
    private double WarpTotalMs;
    private double WarpMaxMs;
    private long LastSpikeLogMs = -SpikeLogGapMs;
    private long? LoadStartMs;
    private double? LastSaveLoadMs;

    public SpikeDiagnosticsModule(ModServices services) : base(services) { }

    public override string Name => "SpikeDiagnostics";

    public override bool IsPassiveDiagnostic => true;

    public override bool IsConfiguredOn(ModConfig config) => config.SpikeDiagnostics;

    protected override void OnEnable()
    {
        var loop = this.Helper.Events.GameLoop;
        var content = this.Helper.Events.Content;
        this.LastTickTimestamp = 0;
        this.SnapshotGc();
        this.On<UpdateTickedEventArgs>(h => loop.UpdateTicked += h, h => loop.UpdateTicked -= h, _ => this.OnUpdateTicked());
        this.On<AssetsInvalidatedEventArgs>(h => content.AssetsInvalidated += h, h => content.AssetsInvalidated -= h, e =>
        {
            Interlocked.Add(ref this.InvalidatedSinceTick, e.NamesWithoutLocale.Count);
            Interlocked.Add(ref this.TotalInvalidated, e.NamesWithoutLocale.Count);
        });
        this.On<AssetReadyEventArgs>(h => content.AssetReady += h, h => content.AssetReady -= h, _ =>
        {
            Interlocked.Increment(ref this.LoadedSinceTick);
            Interlocked.Increment(ref this.TotalLoaded);
        });
        this.On<WarpedEventArgs>(h => this.Helper.Events.Player.Warped += h, h => this.Helper.Events.Player.Warped -= h, e =>
        {
            string name = e.NewLocation?.NameOrUniqueName ?? "?";
            this.PendingWarps.Add(Context.ScreenId == 0 ? name : $"{name} (screen {Context.ScreenId})");
        });
        this.On<LoadStageChangedEventArgs>(h => this.Helper.Events.Specialized.LoadStageChanged += h, h => this.Helper.Events.Specialized.LoadStageChanged -= h, e =>
        {
            if (Context.ScreenId != 0)
                return;
            if (e.OldStage == LoadStage.None && e.NewStage != LoadStage.None)
                this.LoadStartMs = ModServices.NowMs;
            else if (e.NewStage == LoadStage.Ready && this.LoadStartMs is long start)
            {
                this.LastSaveLoadMs = ModServices.NowMs - start;
                this.LoadStartMs = null;
                this.S.Session.RecordSaveLoad(this.LastSaveLoadMs.Value);
                this.Monitor.Log($"Save load took {this.LastSaveLoadMs / 1000:0.00} s (SMAPI load stages to Ready).", LogLevel.Trace);
            }
        });
    }

    protected override void WriteDetails(PerfReport report)
    {
        if (this.S.LaunchUptime is TimeSpan launch)
            report.Add("process start to GameLaunched", $"{launch.TotalSeconds:0.0} s");
        if (this.LastSaveLoadMs is double load)
            report.Add("last save load", $"{load / 1000:0.00} s");
        report.Add("frames", this.Frames);
        if (this.Frames > 0)
            report.Add("frame time avg / max", $"{this.TotalFrameMs / this.Frames:0.0} / {this.MaxFrameMs:0.0} ms");
        int threshold = this.S.GetConfig().SpikeThresholdMs;
        report.Add($"spikes over {threshold} ms", this.Spikes.Count);
        report.Add("GC collections gen0/gen1/gen2", $"{this.LastGen0 - this.BaseGen0}/{this.LastGen1 - this.BaseGen1}/{this.LastGen2 - this.BaseGen2}");
        report.Add("GC pause total", $"{this.TotalPauseMs:0} ms" + (TotalPauseDuration == null ? " (approximate)" : ""));
        report.Add("assets invalidated / loaded", $"{Interlocked.Read(ref this.TotalInvalidated)} / {Interlocked.Read(ref this.TotalLoaded)}");
        if (this.WarpCount > 0)
        {
            report.Add("warps (frame time around warp) avg / max", $"{this.WarpCount} warps, {this.WarpTotalMs / this.WarpCount:0.0} / {this.WarpMaxMs:0.0} ms");
            report.Add("recent warps", string.Join(", ", this.Warps.AsEnumerable().Reverse().Take(5).Select(w => $"{w.Location} {w.Ms:0} ms")));
        }
        if (this.Spikes.Worst.Count > 0)
        {
            report.Note($"worst frames ({SpikeRecord.TableHeader}):");
            foreach (SpikeRecord s in this.Spikes.Worst)
                report.Note("  " + s.ToRow());
        }
    }

    protected override void OnResetCounters()
    {
        this.Spikes.Reset();
        this.Warps.Clear();
        this.Frames = 0;
        this.TotalFrameMs = this.MaxFrameMs = this.TotalPauseMs = 0;
        this.WarpCount = 0;
        this.WarpTotalMs = this.WarpMaxMs = 0;
        Interlocked.Exchange(ref this.TotalInvalidated, 0);
        Interlocked.Exchange(ref this.TotalLoaded, 0);
        this.BaseGen0 = this.LastGen0;
        this.BaseGen1 = this.LastGen1;
        this.BaseGen2 = this.LastGen2;
    }

    private void OnUpdateTicked()
    {
        if (Context.ScreenId != 0)
            return;

        long now = Stopwatch.GetTimestamp();
        if (this.LastTickTimestamp == 0)
        {
            this.LastTickTimestamp = now;
            this.SnapshotGc();
            this.S.Frame.Clear();
            return;
        }
        double ms = (now - this.LastTickTimestamp) * 1000.0 / Stopwatch.Frequency;
        this.LastTickTimestamp = now;
        this.Frames++;
        this.TotalFrameMs += ms;
        if (ms > this.MaxFrameMs)
            this.MaxFrameMs = ms;

        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        int d0 = g0 - this.LastGen0, d1 = g1 - this.LastGen1, d2 = g2 - this.LastGen2;
        this.LastGen0 = g0;
        this.LastGen1 = g1;
        this.LastGen2 = g2;
        double pauseMs = 0;
        if (TotalPauseDuration != null)
        {
            TimeSpan pause = TotalPauseDuration();
            pauseMs = (pause - this.LastPause).TotalMilliseconds;
            this.LastPause = pause;
        }
        else if (d0 > 0)
            pauseMs = LastGcPauseMs();
        this.TotalPauseMs += pauseMs;
        this.S.Session.RecordFrame(ms, d0, d1, d2, pauseMs);

        int invalidated = Interlocked.Exchange(ref this.InvalidatedSinceTick, 0);
        int loaded = Interlocked.Exchange(ref this.LoadedSinceTick, 0);
        string? warp = this.PendingWarps.Count == 0 ? null : string.Join(", ", this.PendingWarps);
        this.PendingWarps.Clear();

        if (warp != null)
        {
            // SMAPI raises Warped on the tick after the game switched locations, so the load cost can be in
            // either the previous or the current frame interval.
            double warpMs = Math.Max(this.PreviousFrameMs, ms);
            this.WarpCount++;
            this.WarpTotalMs += warpMs;
            this.WarpMaxMs = Math.Max(this.WarpMaxMs, warpMs);
            this.Warps.Add((warp, warpMs));
            if (this.Warps.Count > WarpHistory)
                this.Warps.RemoveAt(0);
            this.S.Session.RecordWarp(warp, warpMs);
        }
        this.PreviousFrameMs = ms;

        // The frame's handler times cover this mod's previous UpdateTicked handler up to this one (handlers after this
        // one in the current tick count toward the next frame). Cleared every tick so nothing carries over.
        bool spike = ms >= this.S.GetConfig().SpikeThresholdMs && Game1.game1?.IsActive != false;
        IReadOnlyList<FrameCulprit>? culprits = spike && this.S.Frame.HasData ? this.S.Frame.Top(3, Stopwatch.Frequency) : null;
        this.S.Frame.Clear();
        if (!spike)
            return;
        var record = new SpikeRecord(ms, ModServices.NowMs, d0, d1, d2, pauseMs, invalidated, loaded, warp, culprits);
        this.Spikes.Add(record);
        this.S.Session.RecordSpike(record);
        if (record.AtMs - this.LastSpikeLogMs >= SpikeLogGapMs)
        {
            this.LastSpikeLogMs = record.AtMs;
            this.Monitor.Log($"Frame spike {ms:0.0} ms: GC {d0}/{d1}/{d2} (pause {pauseMs:0.0} ms), {invalidated} assets invalidated, {loaded} loaded{(warp != null ? $", warp to {warp}" : "")}"
                + (culprits is { Count: > 0 } ? $"; most handler time: {string.Join(", ", culprits.Select(c => $"{c.Mod} {c.Event} {c.Ms:0.0} ms"))}" : "") + ".", LogLevel.Trace);
        }
    }

    private void SnapshotGc()
    {
        this.LastGen0 = this.BaseGen0 = GC.CollectionCount(0);
        this.LastGen1 = this.BaseGen1 = GC.CollectionCount(1);
        this.LastGen2 = this.BaseGen2 = GC.CollectionCount(2);
        if (TotalPauseDuration != null)
            this.LastPause = TotalPauseDuration();
    }

    /// <summary>Fallback when GetTotalPauseDuration is missing: the pause of the most recent GC only.</summary>
    private static double LastGcPauseMs()
    {
        double total = 0;
        foreach (TimeSpan pause in GC.GetGCMemoryInfo(GCKind.Any).PauseDurations)
            total += pause.TotalMilliseconds;
        return total;
    }

    private static Func<TimeSpan>? CreatePauseClock()
    {
        try
        {
            MethodInfo? method = typeof(GC).GetMethod("GetTotalPauseDuration", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            return method?.ReturnType == typeof(TimeSpan) ? (Func<TimeSpan>)Delegate.CreateDelegate(typeof(Func<TimeSpan>), method) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
