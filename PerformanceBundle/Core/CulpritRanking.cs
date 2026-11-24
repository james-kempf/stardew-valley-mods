using System;
using System.Collections.Generic;
using System.Linq;

namespace PerformanceBundle.Core;

/// <summary>Everything the "Likely culprits" summary is ranked from. All values are snapshots.</summary>
internal sealed class CulpritInputs
{
    /// <summary>Total handler time per mod (Stopwatch ticks) and the seconds the profiler was attached.</summary>
    public IReadOnlyList<(string Mod, long Ticks, long Calls)> HandlerMods { get; set; } = Array.Empty<(string, long, long)>();
    public double ProfiledSeconds { get; set; }
    public long TicksPerSecond { get; set; } = 1;

    /// <summary>How often each mod had the most handler time in a spike frame.</summary>
    public IReadOnlyDictionary<string, int> SpikeTopMods { get; set; } = new Dictionary<string, int>();
    public int Spikes { get; set; }
    public int SpikesWithCulprits { get; set; }

    /// <summary>Asset names invalidated within the window after a warp, and the warp count.</summary>
    public IReadOnlyDictionary<string, int> WarpInvalidatedNames { get; set; } = new Dictionary<string, int>();
    public int Warps { get; set; }

    /// <summary>CP packs with location/time-dependent patches, or null if the pack scan hasn't finished.</summary>
    public IReadOnlyList<CpPackRate>? CpPacks { get; set; }

    public IReadOnlyDictionary<string, WarpStat> WarpsByLocation { get; set; } = new Dictionary<string, WarpStat>();
}

internal sealed record PackInvalidations(string Pack, double PerWarp, int Total);

/// <summary>Ranks the session's data into a short list of likely causes, shown at the top of each report.</summary>
internal static class CulpritRanking
{
    /// <summary>Mods by handler time per second profiled, highest first.</summary>
    public static List<(string Mod, double MsPerSecond)> TopHandlerMods(IEnumerable<(string Mod, long Ticks, long Calls)> mods, double seconds, long ticksPerSecond, int max)
    {
        if (seconds <= 0)
            return new();
        return mods.Where(m => m.Ticks > 0)
            .Select(m => (m.Mod, MsPerSecond: m.Ticks * 1000.0 / ticksPerSecond / seconds))
            .OrderByDescending(m => m.MsPerSecond).ThenBy(m => m.Mod, StringComparer.OrdinalIgnoreCase)
            .Take(max).ToList();
    }

    public static List<(string Mod, int Count)> TopSpikeMods(IReadOnlyDictionary<string, int> counts, int max) =>
        counts.Select(p => (Mod: p.Key, Count: p.Value))
            .OrderByDescending(p => p.Count).ThenBy(p => p.Mod, StringComparer.OrdinalIgnoreCase)
            .Take(max).ToList();

    /// <summary>Charge each asset name invalidated after a warp to every CP pack whose location/time-dependent patches
    /// target it, then rank packs by names per warp. <paramref name="cache"/> (name → matching packs) avoids repeating
    /// pattern matches across reports.</summary>
    public static List<PackInvalidations> CpPacksPerWarp(IReadOnlyDictionary<string, int> warpNames, int warps, IReadOnlyList<CpPackRate> packs, int max,
        Dictionary<string, string[]>? cache = null)
    {
        if (warps <= 0 || warpNames.Count == 0)
            return new();
        cache ??= new();
        var totals = new Dictionary<string, int>();
        foreach (var (name, count) in warpNames)
        {
            if (!cache.TryGetValue(name, out string[]? matches))
                cache[name] = matches = packs.Where(p => p.Targets?.Contains(name) == true).Select(p => p.Name).ToArray();
            foreach (string pack in matches)
                totals[pack] = totals.TryGetValue(pack, out int n) ? n + count : count;
        }
        return totals.Select(p => new PackInvalidations(p.Key, (double)p.Value / warps, p.Value))
            .OrderByDescending(p => p.Total).ThenBy(p => p.Pack, StringComparer.OrdinalIgnoreCase)
            .Take(max).ToList();
    }

    /// <summary>Locations by their slowest warp, highest first.</summary>
    public static List<(string Location, WarpStat Stat)> SlowestWarps(IReadOnlyDictionary<string, WarpStat> warps, int max) =>
        warps.Select(p => (Location: p.Key, Stat: p.Value))
            .OrderByDescending(p => p.Stat.MaxMs).ThenBy(p => p.Location, StringComparer.OrdinalIgnoreCase)
            .Take(max).ToList();

    public static List<string> Format(CulpritInputs input, int max = 5, Dictionary<string, string[]>? cache = null)
    {
        var lines = new List<string>();

        var handlers = TopHandlerMods(input.HandlerMods, input.ProfiledSeconds, input.TicksPerSecond, max);
        lines.Add($"Slowest mods by event handler time (ms per second, over {input.ProfiledSeconds / 60:0.0} min profiled):");
        if (handlers.Count == 0)
            lines.Add("  no handler profiler data (HandlerProfiler off, not attached yet, or no save loaded)");
        for (int i = 0; i < handlers.Count; i++)
            lines.Add($"  {i + 1}. {handlers[i].Mod}: {handlers[i].MsPerSecond:0.00} ms/s");

        var spikeMods = TopSpikeMods(input.SpikeTopMods, max);
        lines.Add($"Mods most often slowest in spike frames ({input.SpikesWithCulprits} of {input.Spikes} spikes had handler data):");
        if (spikeMods.Count == 0)
            lines.Add("  none");
        for (int i = 0; i < spikeMods.Count; i++)
            lines.Add($"  {i + 1}. {spikeMods[i].Mod}: top in {spikeMods[i].Count} spike frames");

        lines.Add($"Content Patcher packs whose OnLocationChange/OnTimeChange patches target assets reloaded after warps ({input.Warps} warps):");
        if (input.CpPacks == null)
            lines.Add("  pack scan not finished");
        else
        {
            var packs = CpPacksPerWarp(input.WarpInvalidatedNames, input.Warps, input.CpPacks, max, cache);
            if (packs.Count == 0)
                lines.Add("  none");
            for (int i = 0; i < packs.Count; i++)
                lines.Add($"  {i + 1}. {packs[i].Pack}: {packs[i].PerWarp:0.0} assets per warp ({packs[i].Total} total)");
        }

        var warps = SlowestWarps(input.WarpsByLocation, max);
        lines.Add("Slowest warps by location (max / avg frame ms around the warp, count):");
        if (warps.Count == 0)
            lines.Add("  none");
        for (int i = 0; i < warps.Count; i++)
            lines.Add($"  {i + 1}. {warps[i].Location}: {warps[i].Stat.MaxMs:0} / {warps[i].Stat.AvgMs:0} ms, {warps[i].Stat.Count}x");
        return lines;
    }
}
