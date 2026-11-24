using System;
using System.Runtime;
using PerformanceBundle.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace PerformanceBundle.Modules;

/// <summary>Sets GCSettings.LatencyMode = SustainedLowLatency while a save is loaded, so gen2 collections run in the
/// background instead of blocking a frame. GCSettings is process-wide, so only screen 0's handlers act (split-screen
/// raises these events per screen). The original mode is restored on Saving, ReturnedToTitle, Disable and on any error.</summary>
internal sealed class GcLatencyModule : PerfModule
{
    private GCLatencyMode? Original;
    private bool Unavailable;
    private int Applies;
    private bool PendingApply;

    public GcLatencyModule(ModServices services) : base(services) { }

    public override string Name => "GcLatency";

    public override bool IsConfiguredOn(ModConfig config) => config.GcLatency;

    protected override void OnEnable()
    {
        if (!GcLatencyPolicy.CanUseSustainedLowLatency(this.S.StartupLatencyMode))
        {
            this.Unavailable = true;
            this.Monitor.Log("GcLatency does nothing: concurrent (background) GC is off in this runtime (startup GC mode Batch). Run perf_env for details.", LogLevel.Info);
            return;
        }

        var ev = this.Helper.Events.GameLoop;
        this.On<SaveLoadedEventArgs>(h => ev.SaveLoaded += h, h => ev.SaveLoaded -= h, _ => this.Apply());
        this.On<DayStartedEventArgs>(h => ev.DayStarted += h, h => ev.DayStarted -= h, _ => this.Apply());
        this.On<SavedEventArgs>(h => ev.Saved += h, h => ev.Saved -= h, _ => this.Apply());
        this.On<SavingEventArgs>(h => ev.Saving += h, h => ev.Saving -= h, _ => this.RestoreFromScreenZero());
        this.On<ReturnedToTitleEventArgs>(h => ev.ReturnedToTitle += h, h => ev.ReturnedToTitle -= h, _ => this.RestoreFromScreenZero());

        // Turned on from the config menu mid-session. The menu may be open on another screen, where Apply does
        // nothing, so apply on screen 0's next tick instead (once; the handler is a no-op afterwards).
        if (Context.IsWorldReady)
        {
            this.PendingApply = true;
            this.On<UpdateTickedEventArgs>(h => ev.UpdateTicked += h, h => ev.UpdateTicked -= h, _ =>
            {
                if (this.PendingApply && Context.ScreenId == 0)
                {
                    this.PendingApply = false;
                    if (Context.IsWorldReady)
                        this.Apply();
                }
            });
        }
    }

    protected override void OnDisable() => this.Restore();

    protected override void WriteDetails(PerfReport report)
    {
        report.Add("startup GC mode", this.S.StartupLatencyMode);
        report.Add("current GC mode", GCSettings.LatencyMode);
        if (this.Unavailable)
            report.Note("no-op: concurrent GC is off (startup mode Batch); see perf_env");
        report.Add("active", this.Original != null);
        report.Add("times applied", this.Applies);
    }

    private void Apply()
    {
        if (Context.ScreenId != 0 || this.Original != null || this.Unavailable)
            return;

        this.Original = GCSettings.LatencyMode;
        try
        {
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        }
        catch
        {
            this.Restore();
            throw;
        }

        if (GCSettings.LatencyMode != GCLatencyMode.SustainedLowLatency)
        {
            this.Restore();
            this.Unavailable = true;
            this.Monitor.Log("GcLatency does nothing: the runtime ignored SustainedLowLatency (concurrent GC is probably off).", LogLevel.Info);
            return;
        }
        this.Applies++;
        this.Monitor.Log($"GC latency mode set to SustainedLowLatency (was {this.Original}).", LogLevel.Trace);
    }

    private void RestoreFromScreenZero()
    {
        if (Context.ScreenId == 0)
            this.Restore();
    }

    private void Restore()
    {
        if (this.Original is not GCLatencyMode original)
            return;
        this.Original = null;
        GCSettings.LatencyMode = original;
        this.Monitor.Log($"GC latency mode restored to {original}.", LogLevel.Trace);
    }
}
