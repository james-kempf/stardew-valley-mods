using System;
using System.Diagnostics;
using System.Runtime;
using PerformanceBundle.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace PerformanceBundle.Modules;

/// <summary>One full compacting GC (LOH included) per day transition and once after a save loads, while the screen
/// is black, so less garbage is left to trigger gen2 pauses during play. Only screen 0 collects (GC is process-wide),
/// and a day-counter guard stops the SaveLoaded + DayStarted pair on load from collecting twice.</summary>
internal sealed class GcTransitionCollectModule : PerfModule
{
    private readonly DayTransitionGuard Guard = new();
    private int Collections;
    private double LastMs;
    private double MaxMs;
    private double TotalMs;
    private long LastFreedBytes;

    public GcTransitionCollectModule(ModServices services) : base(services) { }

    public override string Name => "GcTransitionCollect";

    public override bool IsConfiguredOn(ModConfig config) => config.GcTransitionCollect;

    protected override void OnEnable()
    {
        var ev = this.Helper.Events.GameLoop;
        this.On<SaveLoadedEventArgs>(h => ev.SaveLoaded += h, h => ev.SaveLoaded -= h, _ => this.TryCollect("save loaded"));
        // DayStarted comes after the save is written (SCore raises Saved then DayStarted), so the save's own garbage is included.
        this.On<DayStartedEventArgs>(h => ev.DayStarted += h, h => ev.DayStarted -= h, _ => this.TryCollect("day transition"));
        this.On<ReturnedToTitleEventArgs>(h => ev.ReturnedToTitle += h, h => ev.ReturnedToTitle -= h, _ =>
        {
            if (Context.ScreenId == 0)
                this.Guard.Reset();
        });
    }

    protected override void WriteDetails(PerfReport report)
    {
        report.Add("collections", this.Collections);
        if (this.Collections > 0)
        {
            report.Add("last / avg / max pause", $"{this.LastMs:0.0} / {this.TotalMs / this.Collections:0.0} / {this.MaxMs:0.0} ms");
            report.Add("last freed", $"{this.LastFreedBytes / (1024.0 * 1024.0):0.0} MB");
        }
    }

    protected override void OnResetCounters()
    {
        this.Collections = 0;
        this.LastMs = this.MaxMs = this.TotalMs = 0;
        this.LastFreedBytes = 0;
    }

    private void TryCollect(string reason)
    {
        if (Context.ScreenId != 0 || !this.Guard.TryClaim(Game1.Date.TotalDays))
            return;

        long before = GC.GetTotalMemory(false);
        var sw = Stopwatch.StartNew();
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
        catch
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.Default;
            throw;
        }
        sw.Stop();

        double ms = sw.Elapsed.TotalMilliseconds;
        this.Collections++;
        this.LastMs = ms;
        this.TotalMs += ms;
        this.MaxMs = Math.Max(this.MaxMs, ms);
        this.LastFreedBytes = before - GC.GetTotalMemory(false);
        this.Monitor.Log($"Full GC on {reason}: {ms:0.0} ms, freed {this.LastFreedBytes / (1024.0 * 1024.0):0.0} MB.", LogLevel.Trace);
    }
}
