using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PerformanceBundle.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace PerformanceBundle.Modules;

/// <summary>perf_env: read-only checks of SMAPI flags, runtime GC/JIT settings and Content Patcher packs that
/// re-check patches on every warp or clock change, each with a recommendation. The config switch controls the
/// one-time log at GameLaunched; the command always works. Inputs that need SMAPI are gathered on the main thread,
/// file reading runs on a worker task so the game doesn't hitch, and the results are logged back on the main
/// thread by <see cref="FlushPendingLogs"/> (called from ModEntry's UpdateTicked, so it works while the module is off).</summary>
internal sealed class EnvironmentReportModule : PerfModule
{
    private readonly ConcurrentQueue<(string Text, LogLevel Level)> PendingLogs = new();
    private int Running;
    private DateTime? LastRun;
    private volatile string? ReportText;
    private volatile IReadOnlyList<CpPackRate>? Packs;

    public EnvironmentReportModule(ModServices services) : base(services) { }

    public override string Name => "EnvironmentReport";

    public override bool IsConfiguredOn(ModConfig config) => config.EnvironmentReport;

    protected override void OnEnable()
    {
        var loop = this.Helper.Events.GameLoop;
        this.On<GameLaunchedEventArgs>(h => loop.GameLaunched += h, h => loop.GameLaunched -= h, _ => this.Run(fromCommand: false));
    }

    protected override void WriteDetails(PerfReport report) =>
        report.Add("last perf_env run", this.LastRun?.ToString("HH:mm:ss") ?? "never");

    /// <summary>The full text of the last completed scan (built by the worker), or null.</summary>
    public string? LastReportText => this.ReportText;

    /// <summary>CP packs with OnLocationChange/OnTimeChange patches and their targets, from the last scan, or null.</summary>
    public IReadOnlyList<CpPackRate>? CpPacks => this.Packs;

    /// <summary>Start a report. From the command, every line is printed to the console; at launch, only the
    /// warnings and a summary go to the console and the full report goes to the log file. Quiet logs nothing
    /// (except errors) and only keeps the result for the session report.</summary>
    public void Run(bool fromCommand, bool quiet = false)
    {
        if (Interlocked.Exchange(ref this.Running, 1) == 1)
        {
            if (!quiet)
                this.Monitor.Log("perf_env is already running.", LogLevel.Info);
            return;
        }

        try
        {
            EnvInputs inputs = this.GatherInputs();
            if (fromCommand)
                this.Monitor.Log("perf_env: scanning settings and Content Patcher packs...", LogLevel.Info);
            Task.Run(() => this.Scan(inputs, fromCommand, quiet));
        }
        catch
        {
            Interlocked.Exchange(ref this.Running, 0);
            throw;
        }
    }

    /// <summary>Main thread: log what the worker produced.</summary>
    public void FlushPendingLogs()
    {
        while (this.PendingLogs.TryDequeue(out var entry))
            this.Monitor.Log(entry.Text, entry.Level);
    }

    /// <summary>Main thread: everything that needs SMAPI or the game.</summary>
    private EnvInputs GatherInputs()
    {
        var mods = this.Helper.ModRegistry.GetAll().ToList();
        var inputs = new EnvInputs
        {
            GamePath = Constants.GamePath,
            ModsPath = EnvironmentScan.FindModsRoot(this.Helper.DirectoryPath, Constants.GamePath),
            LoadedIds = new HashSet<string>(mods.Select(m => m.Manifest.UniqueID), StringComparer.OrdinalIgnoreCase),
            CsharpMods = mods.Count(m => !m.IsContentPack),
            ContentPacks = mods.Count(m => m.IsContentPack),
            Screens = Math.Max(1, StardewValley.GameRunner.instance?.gameInstances?.Count ?? 1),
            StartupLatencyMode = this.S.StartupLatencyMode,
            IsServerGc = System.Runtime.GCSettings.IsServerGC
        };
        foreach (string name in EnvironmentScan.WatchedEnvVars)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            if (value != null)
                inputs.EnvVars[name] = value;
        }
        return inputs;
    }

    /// <summary>Worker task: file reads only; output is queued for the main thread.</summary>
    private void Scan(EnvInputs inputs, bool fromCommand, bool quiet)
    {
        try
        {
            var snapshot = new EnvSnapshot
            {
                Smapi = EnvironmentScan.ReadSmapiFlags(new[]
                {
                    Path.Combine(inputs.GamePath, "smapi-internal", "config.json"),
                    Path.Combine(inputs.GamePath, "smapi-internal", "config.user.json"),
                    Path.Combine(inputs.ModsPath, "SMAPI-config.json")
                }),
                Runtime = EnvironmentScan.ReadRuntimeConfig(Path.Combine(inputs.GamePath, "StardewModdingAPI.runtimeconfig.json")),
                StartupLatencyMode = inputs.StartupLatencyMode,
                IsServerGc = inputs.IsServerGc,
                CsharpMods = inputs.CsharpMods,
                ContentPacks = inputs.ContentPacks,
                Screens = inputs.Screens
            };
            foreach (var pair in inputs.EnvVars)
                snapshot.EnvVars[pair.Key] = pair.Value;
            snapshot.CpPacks.AddRange(EnvironmentScan.ScanCpPacks(inputs.ModsPath, inputs.LoadedIds));

            List<Recommendation> recs = EnvironmentScan.Recommend(snapshot);
            List<string> lines = EnvironmentScan.Format(recs);
            int warnings = recs.Count(r => r.Severity == Severity.Warn);
            string header = $"Performance environment report ({warnings} warning{(warnings == 1 ? "" : "s")}). "
                + $"SMAPI config read from: {(snapshot.Smapi.SourceFiles.Count > 0 ? string.Join(", ", snapshot.Smapi.SourceFiles) : "none found")}; "
                + $"runtime config: {(snapshot.Runtime.Found ? snapshot.Runtime.Path : "not found")}.";

            string packList = snapshot.CpPacks.Count == 0
                ? ""
                : "Content Patcher packs with OnLocationChange/OnTimeChange (patches: location/time):\n"
                    + string.Join("\n", snapshot.CpPacks.Select(p => $"  {p.Name} [{p.UniqueId}]: {p.OnLocationChange}/{p.OnTimeChange}"));
            this.ReportText = header + "\n" + string.Join("\n", lines) + (packList.Length > 0 ? "\n" + packList : "");
            this.Packs = snapshot.CpPacks.ToList();

            if (quiet)
            {
                // Kept for the session report only.
            }
            else if (fromCommand)
            {
                this.Post(header + "\n" + string.Join("\n", lines), LogLevel.Info);
                if (packList.Length > 0)
                    this.Post(packList, LogLevel.Info);
            }
            else
            {
                this.Post(header + "\n" + string.Join("\n", lines), LogLevel.Trace);
                foreach (Recommendation r in recs.Where(r => r.Severity == Severity.Warn))
                    this.Post($"Performance tip: {r.Item}: {r.Value}. {r.Advice}", LogLevel.Info);
                this.Post($"Environment check done ({warnings} warning{(warnings == 1 ? "" : "s")}). Type perf_env in this console for the full report.", LogLevel.Info);
            }
            this.LastRun = DateTime.Now;
        }
        catch (Exception ex)
        {
            // Read-only report: a failure is logged but doesn't turn other features off.
            this.Post($"perf_env failed: {ex}", LogLevel.Error);
        }
        finally
        {
            Interlocked.Exchange(ref this.Running, 0);
        }
    }

    private void Post(string text, LogLevel level) => this.PendingLogs.Enqueue((text, level));

    private sealed class EnvInputs
    {
        public string GamePath { get; set; } = "";
        public string ModsPath { get; set; } = "";
        public HashSet<string> LoadedIds { get; set; } = new();
        public int CsharpMods { get; set; }
        public int ContentPacks { get; set; }
        public int Screens { get; set; } = 1;
        public System.Runtime.GCLatencyMode StartupLatencyMode { get; set; }
        public bool IsServerGc { get; set; }
        public Dictionary<string, string> EnvVars { get; } = new();
    }
}
