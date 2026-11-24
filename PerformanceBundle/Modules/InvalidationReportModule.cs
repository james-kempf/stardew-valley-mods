using System.Linq;
using PerformanceBundle.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace PerformanceBundle.Modules;

/// <summary>Counts invalidated asset names per session, per clock minute and per warp (AssetsInvalidated only; never
/// AssetRequested), and logs a line when a minute reaches the storm threshold. Report only: it never changes assets.</summary>
internal sealed class InvalidationReportModule : PerfModule
{
    public InvalidationAggregator Aggregator { get; }

    public InvalidationReportModule(ModServices services) : base(services) =>
        this.Aggregator = new InvalidationAggregator(services.GetConfig().InvalidationStormPerMinute);

    public override string Name => "InvalidationReport";

    public override bool IsPassiveDiagnostic => true;

    public override bool IsConfiguredOn(ModConfig config) => config.InvalidationReport;

    protected override void OnEnable()
    {
        var content = this.Helper.Events.Content;
        this.On<AssetsInvalidatedEventArgs>(h => content.AssetsInvalidated += h, h => content.AssetsInvalidated -= h, this.OnAssetsInvalidated);
        this.On<WarpedEventArgs>(h => this.Helper.Events.Player.Warped += h, h => this.Helper.Events.Player.Warped -= h, e =>
        {
            string name = e.NewLocation?.NameOrUniqueName ?? "?";
            this.Aggregator.MarkWarp(Context.ScreenId == 0 ? name : $"{name} (screen {Context.ScreenId})", ModServices.NowMs);
        });
    }

    protected override void WriteDetails(PerfReport report)
    {
        var a = this.Aggregator;
        report.Add("invalidation events / asset names", $"{a.TotalEvents} / {a.TotalNames}");
        report.Add("worst minute", $"{a.WorstMinuteCount} names");
        report.Add($"storm minutes (>= {a.StormThreshold} names)", a.StormMinutes);
        var top = a.TopAssets(10);
        if (top.Count > 0)
            report.Add("most invalidated", string.Join(", ", top.Select(t => $"{t.Name} x{t.Count}")));
        var warps = a.RecentWarps;
        if (warps.Count > 0)
        {
            report.Note("recent warps (invalidated within 2 s after the warp):");
            foreach (WarpSnapshot w in warps.Reverse().Take(5))
            {
                string names = string.Join(", ", w.Top.Select(t => t.Name));
                report.Note($"  {w.Location}: {w.Invalidated}" + (names.Length > 0 ? $" ({names})" : ""));
            }
        }
    }

    protected override void OnResetCounters() => this.Aggregator.Reset();

    private void OnAssetsInvalidated(AssetsInvalidatedEventArgs e)
    {
        this.Aggregator.StormThreshold = this.S.GetConfig().InvalidationStormPerMinute;
        var names = e.NamesWithoutLocale.Select(n => n.BaseName).ToList();
        StormNotice? storm = this.Aggregator.Record(names, ModServices.NowMs);
        this.S.Session.RecordInvalidated(names);
        if (storm != null)
        {
            this.S.Session.RecordStorm();
            this.Monitor.Log(
                $"Asset invalidation storm: {storm.Count} assets invalidated this minute (threshold {this.Aggregator.StormThreshold}). "
                + $"Most: {string.Join(", ", storm.Top.Select(t => $"{t.Name} x{t.Count}"))}. Details are in the session report (Mods/PerformanceBundle/reports) and perf_report.",
                LogLevel.Debug);
        }
    }
}
