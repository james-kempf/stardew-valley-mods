using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using GenericModConfigMenu;
using PerformanceBundle.Core;
using PerformanceBundle.Modules;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace PerformanceBundle;

internal sealed class ModEntry : Mod
{
    private const string GmcmId = "spacechase0.GenericModConfigMenu";
    private const double DiagnosticsBudgetUs = 50;

    private ModConfig Config = null!;
    private ModServices Services = null!;
    private List<IPerfModule> Modules = new();
    private EnvironmentReportModule EnvReport = null!;
    private HandlerProfilerModule Profiler = null!;
    private InvalidationReportModule Invalidation = null!;
    private AutoReportModule AutoReport = null!;
    private readonly DateTime SessionStart = DateTime.Now;

    /// <summary>Asset name → CP packs targeting it, for the culprit summary; rebuilt when a new pack scan arrives.</summary>
    private readonly Dictionary<string, string[]> PackMatchCache = new();
    private object? PackMatchSource;

    /// <summary>Screen 0 update ticks since the last perf_reset, for the per-tick diagnostics overhead.</summary>
    private long Ticks;
    private long TicksSinceMs;

    public override void Entry(IModHelper helper)
    {
        // Read before any module can change it: GcLatency restores to the mode it finds, and Batch here means
        // concurrent GC is off.
        GCLatencyMode startupMode = GCSettings.LatencyMode;
        this.Config = helper.ReadConfig<ModConfig>();
        this.Services = new ModServices(helper, this.Monitor, () => this.Config, startupMode);
        this.Modules = this.CreateModules();
        this.ApplyConfig();

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.GameLoop.SaveLoaded += (_, _) =>
        {
            // Don't count title-screen time in the first played day's frame counters.
            if (Context.ScreenId == 0)
                this.Services.Session.StartPlay(ModServices.NowMs);
        };
        AppDomain.CurrentDomain.ProcessExit += this.OnProcessExit;

        helper.ConsoleCommands.Add("perf_report",
            "Shows Performance Bundle counters: likely culprits, per-day summary, frame spikes, GC, warps, invalidations, handler times, pre-compile progress and the mod's own overhead. "
                + "With AutoReport on, the full report is also written to Mods/PerformanceBundle/reports without any command.",
            (_, _) => this.Monitor.Log(this.BuildReport("perf_report", full: false), LogLevel.Info));
        helper.ConsoleCommands.Add("perf_reset", "Clears Performance Bundle counters (settings are unchanged).", (_, _) => this.ResetCounters());
        helper.ConsoleCommands.Add("perf_env",
            "Checks SMAPI and runtime settings and Content Patcher packs that update on every warp or clock change, with a recommendation for each. Read-only.",
            (_, _) => this.EnvReport.Run(fromCommand: true));
        helper.ConsoleCommands.Add("perf_profile",
            "Times every mod's event handlers for a while and logs a ranked table. Needs HandlerProfiler on in the config "
                + "(with ProfileMode Continuous, handlers are already timed for the session report).\n\nUsage: perf_profile [seconds, default 10]\nperf_profile stop",
            (_, args) => this.Profiler.Command(args));
    }

    /// <summary>All modules, in report order.</summary>
    private List<IPerfModule> CreateModules()
    {
        this.EnvReport = new EnvironmentReportModule(this.Services);
        this.Profiler = new HandlerProfilerModule(this.Services);
        this.Invalidation = new InvalidationReportModule(this.Services);
        this.AutoReport = new AutoReportModule(this.Services, trigger => this.BuildReport(trigger, full: true));
        return new List<IPerfModule>
        {
            new GcLatencyModule(this.Services),
            new GcTransitionCollectModule(this.Services),
            new PreJitModule(this.Services),
            new AdjacentPrewarmModule(this.Services),
            new SpikeDiagnosticsModule(this.Services),
            this.Invalidation,
            this.Profiler,
            this.EnvReport,
            this.AutoReport
        };
    }

    private void ApplyConfig()
    {
        foreach (IPerfModule module in this.Modules)
        {
            if (module.IsConfiguredOn(this.Config))
                module.Enable();
            else
                module.Disable();
        }
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        this.Services.GameLaunched = true;
        try
        {
            this.Services.LaunchUptime = DateTime.Now - Process.GetCurrentProcess().StartTime;
        }
        catch (Exception)
        {
            // Process start time isn't available on every platform; the report just omits it.
        }

        // The session report includes the perf_env results, so scan quietly when the launch log is off.
        if (this.Config.AutoReport && !this.EnvReport.IsEnabled)
        {
            try
            {
                this.EnvReport.Run(fromCommand: false, quiet: true);
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't start the settings scan for the session report: {ex}", LogLevel.Trace);
            }
        }

        try
        {
            this.RegisterConfigMenu();
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Couldn't add the Generic Mod Config Menu page; edit config.json instead.\n{ex}", LogLevel.Warn);
        }
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (Context.ScreenId == 0)
            this.Ticks++;
        this.EnvReport.FlushPendingLogs();
    }

    private void RegisterConfigMenu()
    {
        var gmcm = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>(GmcmId);
        if (gmcm == null)
            return;

        var m = this.ModManifest;
        var t = this.Helper.Translation;
        gmcm.Register(m, reset: () => this.Config = new ModConfig(), save: () =>
        {
            this.Helper.WriteConfig(this.Config);
            this.ApplyConfig();
        });

        void Bool(string key, Func<bool> get, Action<bool> set) =>
            gmcm.AddBoolOption(m, get, set, () => t.Get($"config.{key}.name"), () => t.Get($"config.{key}.tooltip"));

        gmcm.AddSectionTitle(m, () => t.Get("section.optimizations"));
        Bool(nameof(ModConfig.GcLatency), () => this.Config.GcLatency, v => this.Config.GcLatency = v);
        Bool(nameof(ModConfig.GcTransitionCollect), () => this.Config.GcTransitionCollect, v => this.Config.GcTransitionCollect = v);
        Bool(nameof(ModConfig.PreJit), () => this.Config.PreJit, v => this.Config.PreJit = v);
        Bool(nameof(ModConfig.AdjacentPrewarm), () => this.Config.AdjacentPrewarm, v => this.Config.AdjacentPrewarm = v);

        gmcm.AddSectionTitle(m, () => t.Get("section.diagnostics"));
        Bool(nameof(ModConfig.SpikeDiagnostics), () => this.Config.SpikeDiagnostics, v => this.Config.SpikeDiagnostics = v);
        Bool(nameof(ModConfig.InvalidationReport), () => this.Config.InvalidationReport, v => this.Config.InvalidationReport = v);
        Bool(nameof(ModConfig.HandlerProfiler), () => this.Config.HandlerProfiler, v => this.Config.HandlerProfiler = v);
        gmcm.AddTextOption(m, () => this.Config.ProfileMode.ToString(),
            v => this.Config.ProfileMode = Enum.TryParse(v, out ProfileMode mode) ? mode : ProfileMode.Continuous,
            () => t.Get("config.ProfileMode.name"), () => t.Get("config.ProfileMode.tooltip"),
            allowedValues: Enum.GetNames(typeof(ProfileMode)), formatAllowedValue: v => t.Get($"config.ProfileMode.{v}"));
        Bool(nameof(ModConfig.AutoReport), () => this.Config.AutoReport, v => this.Config.AutoReport = v);

        gmcm.AddSectionTitle(m, () => t.Get("section.recommendations"));
        Bool(nameof(ModConfig.EnvironmentReport), () => this.Config.EnvironmentReport, v => this.Config.EnvironmentReport = v);

        gmcm.AddSectionTitle(m, () => t.Get("section.thresholds"));
        gmcm.AddNumberOption(m, () => this.Config.SpikeThresholdMs, v => this.Config.SpikeThresholdMs = v,
            () => t.Get("config.SpikeThresholdMs.name"), () => t.Get("config.SpikeThresholdMs.tooltip"), min: 20, max: 1000, interval: 5);
        gmcm.AddNumberOption(m, () => this.Config.InvalidationStormPerMinute, v => this.Config.InvalidationStormPerMinute = v,
            () => t.Get("config.InvalidationStormPerMinute.name"), () => t.Get("config.InvalidationStormPerMinute.tooltip"), min: 10, max: 5000, interval: 10);
        gmcm.AddNumberOption(m, () => this.Config.ProfilerMaxOverheadUsPerTick, v => this.Config.ProfilerMaxOverheadUsPerTick = v,
            () => t.Get("config.ProfilerMaxOverheadUsPerTick.name"), () => t.Get("config.ProfilerMaxOverheadUsPerTick.tooltip"), min: 20, max: 5000, interval: 10);
        gmcm.AddNumberOption(m, () => this.Config.AdjacentPrewarmBudgetMs, v => this.Config.AdjacentPrewarmBudgetMs = v,
            () => t.Get("config.AdjacentPrewarmBudgetMs.name"), () => t.Get("config.AdjacentPrewarmBudgetMs.tooltip"), min: 1, max: 8, interval: 1);
    }

    /// <summary>Main thread. The report: likely culprits, per-day summary, every module's section and the diagnostics'
    /// own cost; with <paramref name="full"/>, also each day's details and the perf_env results.</summary>
    private string BuildReport(string trigger, bool full)
    {
        DateTime now = DateTime.Now;
        var lines = new List<string>
        {
            $"{this.ModManifest.Name} {this.ModManifest.Version} report ({trigger}), {now:yyyy-MM-dd HH:mm:ss}",
            $"Session started {this.SessionStart:yyyy-MM-dd HH:mm:ss} ({(now - this.SessionStart).TotalMinutes:0} min). "
                + $"SMAPI {Constants.ApiVersion}, Stardew Valley {StardewValley.Game1.version}, {this.Helper.ModRegistry.GetAll().Count()} mods loaded.",
            "Times are real time. Frame times are measured on the first screen and cover all split-screen screens.",
            "",
            "== Likely culprits =="
        };
        foreach (string line in CulpritRanking.Format(this.GetCulpritInputs(), cache: this.PackMatchCache))
            lines.Add("  " + line);

        IReadOnlyList<DayRecord> days = this.Services.Session.GetDays();
        string label = Context.IsWorldReady ? AutoReportModule.CurrentDayLabel() : this.AutoReport.DayLabel;
        DayRecord? today = this.Services.Session.PeekDay($"{label} (so far)", ModServices.NowMs);
        lines.Add("");
        lines.Add("== Days (one line per in-game day; counters reset at each day end) ==");
        if (days.Count == 0 && today == null)
            lines.Add("  no data yet");
        foreach (DayRecord day in days)
            lines.Add("  " + day.Summary);
        if (today != null)
            lines.Add("  " + today.Summary);

        lines.Add("");
        lines.AddRange(this.BuildModuleReport().GetLines());

        if (full)
        {
            lines.Add("");
            lines.Add("== Day details ==");
            foreach (DayRecord day in days)
                lines.AddRange(day.Details);
            if (today != null)
                lines.AddRange(today.Details);

            lines.Add("");
            lines.Add("== Environment (perf_env) ==");
            lines.Add(this.EnvReport.LastReportText ?? "not run yet (the scan starts at launch and takes a few seconds)");
        }
        return string.Join("\n", lines);
    }

    /// <summary>Every module's section (session totals since launch or perf_reset) and the diagnostics overhead.</summary>
    private PerfReport BuildModuleReport()
    {
        var report = new PerfReport();
        double minutes = (ModServices.NowMs - this.TicksSinceMs) / 60_000.0;
        report.Section($"{this.ModManifest.Name} {this.ModManifest.Version} session totals", $"{minutes:0.0} min since start or perf_reset");
        foreach (IPerfModule module in this.Modules)
            module.WriteReport(report);

        // Passive diagnostics' own cost: their handler time (all screens and threads) per screen-0 update tick.
        var diagnostics = this.Modules.OfType<PerfModule>().Where(p => p.IsPassiveDiagnostic && p.IsEnabled).ToList();
        report.Section("Diagnostics overhead", $"target under {DiagnosticsBudgetUs:0} us/tick");
        if (this.Ticks == 0 || diagnostics.Count == 0)
            report.Note("no data yet");
        else
        {
            double perTick = diagnostics.Sum(p => p.HandlerMicroseconds) / this.Ticks;
            report.Add("average per tick", $"{perTick:0.0} us over {this.Ticks} ticks ({(perTick < DiagnosticsBudgetUs ? "within" : "over")} budget)");
            foreach (PerfModule p in diagnostics)
                report.Add($"  {p.Name}", $"{p.HandlerMicroseconds / this.Ticks:0.0} us/tick");
        }
        report.Note("HandlerProfiler's marker overhead is listed in its own section.");
        return report;
    }

    private CulpritInputs GetCulpritInputs()
    {
        IReadOnlyList<CpPackRate>? packs = this.EnvReport.CpPacks;
        if (!ReferenceEquals(packs, this.PackMatchSource))
        {
            this.PackMatchCache.Clear();
            this.PackMatchSource = packs;
        }
        var (warpNames, warps) = this.Invalidation.Aggregator.WarpTotals();
        SessionStats session = this.Services.Session;
        return new CulpritInputs
        {
            HandlerMods = this.Profiler.SessionTable.RankedMods(int.MaxValue),
            ProfiledSeconds = this.Profiler.SessionSeconds,
            TicksPerSecond = Stopwatch.Frequency,
            SpikeTopMods = session.GetSpikeTopMods(),
            Spikes = session.Spikes,
            SpikesWithCulprits = session.SpikesWithCulprits,
            WarpInvalidatedNames = warpNames,
            Warps = warps,
            CpPacks = packs,
            WarpsByLocation = session.GetSessionWarps()
        };
    }

    /// <summary>Game closing. SMAPI 4.5.2 doesn't raise MonoGame's Exiting (SGameRunner.OnExiting doesn't call base);
    /// instead SCore.Dispose disposes every mod on the main thread before Environment.Exit. So the final report is built
    /// and written synchronously here (a worker task could be killed by the exit), then the profiler's markers are
    /// removed. Each step is guarded; never throws.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                if (this.AutoReport?.IsEnabled == true && this.AutoReport.WriteFinal("game closing") is string text)
                    this.Monitor.Log(text, LogLevel.Info);
            }
            catch (Exception ex)
            {
                try
                {
                    this.Monitor.Log($"Couldn't write the session report while closing: {ex}", LogLevel.Trace);
                }
                catch (Exception)
                {
                    // Logging may already be shut down.
                }
            }
            try
            {
                this.Profiler?.DetachForExit();
            }
            catch (Exception)
            {
                // The process is ending; the handler lists go with it.
            }
        }
        base.Dispose(disposing);
    }

    /// <summary>Any thread, best effort: write the last built report if the game closed without disposing mods.</summary>
    private void OnProcessExit(object? sender, EventArgs e)
    {
        try
        {
            if (this.AutoReport.IsEnabled)
                this.AutoReport.WriteOnProcessExit();
        }
        catch (Exception)
        {
            // Nothing can be reported this late.
        }
    }

    private void ResetCounters()
    {
        foreach (IPerfModule module in this.Modules)
            module.ResetCounters();
        this.Ticks = 0;
        this.TicksSinceMs = ModServices.NowMs;
        this.Monitor.Log("Performance Bundle counters cleared.", LogLevel.Info);
    }
}
