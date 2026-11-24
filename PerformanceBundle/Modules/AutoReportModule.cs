using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using PerformanceBundle.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace PerformanceBundle.Modules;

/// <summary>Writes the whole report to <c>reports/session-YYYYMMDD-HHMMSS.txt</c> in this mod's folder (one file per
/// game session, replaced on each write; the newest 10 session files are kept) so nothing has to be typed during play.
/// Writes at each day end (screen 0, after closing that day's counters), every 5 minutes while a save is loaded, on
/// returning to title (also logged at Info) and when SMAPI disposes mods at a normal game close (ModEntry.Dispose; also
/// logged), with AppDomain.ProcessExit as a fallback that rewrites the last built text. The report text is always built
/// on the main thread; only the file write runs on a worker task.</summary>
internal sealed class AutoReportModule : PerfModule
{
    private const long CheckpointMs = 5 * 60_000;
    private const string NoSaveLabel = "before loading a save";

    private readonly Func<string, string> BuildReport;
    private readonly DayTransitionGuard DayGuard = new();
    private long LastWriteMs = ModServices.NowMs;
    private volatile string? LastBuilt;
    private int FinalWritten;
    private string? LastTrigger;
    private double LastBuildMs;
    private string? LoggedError;

    /// <param name="buildReport">Builds the full report text for a trigger description. Main thread only.</param>
    public AutoReportModule(ModServices services, Func<string, string> buildReport) : base(services)
    {
        this.BuildReport = buildReport;
        this.Writer = new ReportFileWriter(services.Helper.DirectoryPath, DateTime.Now);
    }

    public override string Name => "AutoReport";

    public ReportFileWriter Writer { get; }

    /// <summary>Label of the in-game day being recorded (screen 0).</summary>
    public string DayLabel { get; private set; } = NoSaveLabel;

    public override bool IsConfiguredOn(ModConfig config) => config.AutoReport;

    protected override void OnEnable()
    {
        var loop = this.Helper.Events.GameLoop;
        if (Context.IsWorldReady)
            this.DayLabel = CurrentDayLabel();
        this.On<SaveLoadedEventArgs>(h => loop.SaveLoaded += h, h => loop.SaveLoaded -= h, _ =>
        {
            if (Context.ScreenId != 0)
                return;
            this.DayLabel = CurrentDayLabel();
            this.DayGuard.Reset();
            this.LastWriteMs = ModServices.NowMs;
        });
        this.On<DayStartedEventArgs>(h => loop.DayStarted += h, h => loop.DayStarted -= h, _ =>
        {
            if (Context.ScreenId == 0)
                this.DayLabel = CurrentDayLabel();
        });
        this.On<DayEndingEventArgs>(h => loop.DayEnding += h, h => loop.DayEnding -= h, _ =>
        {
            if (Context.ScreenId != 0 || !this.DayGuard.TryClaim(Game1.Date.TotalDays))
                return;
            // The next day's counters start here, so they include the overnight save.
            this.S.Session.CloseDay(this.DayLabel, ModServices.NowMs);
            this.Write($"end of {this.DayLabel}", now: false);
        });
        this.On<ReturnedToTitleEventArgs>(h => loop.ReturnedToTitle += h, h => loop.ReturnedToTitle -= h, _ =>
        {
            if (Context.ScreenId != 0)
                return;
            this.S.Session.CloseDay($"{this.DayLabel} (until returning to title)", ModServices.NowMs);
            this.DayLabel = "title screen";
            this.DayGuard.Reset();
            string text = this.Write("returned to title", now: false);
            this.Monitor.Log(text, LogLevel.Info);
        });
        this.On<UpdateTickedEventArgs>(h => loop.UpdateTicked += h, h => loop.UpdateTicked -= h, _ =>
        {
            if (Context.ScreenId != 0)
                return;
            if (Context.IsWorldReady && ModServices.NowMs - this.LastWriteMs >= CheckpointMs)
                this.Write("checkpoint", now: false);
            string? error = this.Writer.LastError;
            if (error != null && error != this.LoggedError)
            {
                this.LoggedError = error;
                this.Monitor.Log($"Couldn't write the session report to {this.Writer.FilePath}: {error}", LogLevel.Warn);
            }
        });
    }

    protected override void WriteDetails(PerfReport report)
    {
        report.Add("file", Path.Combine(ReportFiles.FolderName, this.Writer.FileName));
        report.Add("last write", this.Writer.LastWrite is DateTime at
            ? $"{at:HH:mm:ss} ({this.LastTrigger}; built in {this.LastBuildMs:0.0} ms on the main thread)"
            : "none yet");
        if (this.Writer.LastError != null)
            report.Note($"last write failed: {this.Writer.LastError}");
    }

    /// <summary>Game closing (ModEntry.Dispose, main thread): build and write synchronously once. Returns the text, or null if the final
    /// report was already written.</summary>
    public string? WriteFinal(string trigger)
    {
        if (Interlocked.Exchange(ref this.FinalWritten, 1) == 1)
            return null;
        return this.Write(trigger, now: true);
    }

    /// <summary>Process exit (any thread): if the game didn't get to <see cref="WriteFinal"/>, write the last report
    /// built on the main thread. Builds nothing.</summary>
    public void WriteOnProcessExit()
    {
        if (Interlocked.Exchange(ref this.FinalWritten, 1) == 1)
            return;
        string? text = this.LastBuilt;
        if (text != null)
            this.Writer.WriteNow(text + $"\n\n(Written when the process exited, {DateTime.Now:HH:mm:ss}; the content is from the last write above.)");
    }

    /// <summary>Main thread: build the report, then write it (on a worker task unless <paramref name="now"/>).</summary>
    private string Write(string trigger, bool now)
    {
        long start = Stopwatch.GetTimestamp();
        string text = this.BuildReport(trigger);
        this.LastBuildMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        this.LastTrigger = trigger;
        this.LastWriteMs = ModServices.NowMs;
        this.LastBuilt = text;
        if (now)
            this.Writer.WriteNow(text);
        else
            this.Writer.WriteInBackground(text);
        return text;
    }

    public static string CurrentDayLabel() => $"Y{Game1.year} {Game1.season} {Game1.dayOfMonth}";
}
