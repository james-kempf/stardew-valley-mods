using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PerformanceBundle.Core;

internal enum Severity { Ok, Info, Warn }

internal sealed record Recommendation(string Item, string Value, Severity Severity, string Advice);

/// <summary>A CP pack's OnLocationChange/OnTimeChange patch counts. <paramref name="Targets"/> are the assets those
/// patches can change during a day (from <see cref="CpConditionalTargets"/>), when scanned.</summary>
internal sealed record CpPackRate(string Name, string UniqueId, int OnLocationChange, int OnTimeChange, AssetNameSet? Targets = null)
{
    public int Total => this.OnLocationChange + this.OnTimeChange;
}

internal sealed class SmapiFlags
{
    public IReadOnlyList<string> VerboseLogging { get; set; } = Array.Empty<string>();
    public bool? DeveloperMode { get; set; }
    public bool? ParanoidWarnings { get; set; }
    public bool? CheckForUpdates { get; set; }
    public bool? LogNetworkTraffic { get; set; }
    public List<string> SourceFiles { get; } = new();
}

internal sealed class RuntimeGcConfig
{
    public string Path { get; set; } = "";
    public bool Found { get; set; }
    public bool? Concurrent { get; set; }
    public bool? TieredCompilation { get; set; }
    public bool? Server { get; set; }
}

internal sealed class EnvSnapshot
{
    public SmapiFlags Smapi { get; set; } = new();
    public RuntimeGcConfig Runtime { get; set; } = new();
    public GCLatencyMode StartupLatencyMode { get; set; } = GCLatencyMode.Interactive;
    public bool IsServerGc { get; set; }
    /// <summary>Relevant DOTNET_/COMPlus_ variables that are set (name to value).</summary>
    public Dictionary<string, string> EnvVars { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<CpPackRate> CpPacks { get; } = new();
    public int CsharpMods { get; set; }
    public int ContentPacks { get; set; }
    public int Screens { get; set; } = 1;
}

/// <summary>Read-only environment checks for perf_env. File I/O only; no game or SMAPI APIs.</summary>
internal static class EnvironmentScan
{
    public const string ContentPatcherId = "Pathoschild.ContentPatcher";

    public static readonly string[] WatchedEnvVars =
    {
        "DOTNET_gcConcurrent", "COMPlus_gcConcurrent", "DOTNET_gcServer", "COMPlus_gcServer",
        "DOTNET_TieredCompilation", "COMPlus_TieredCompilation", "DOTNET_GCgen0size", "COMPlus_GCgen0size"
    };

    private static readonly JsonDocumentOptions Loose = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static readonly Regex UpdateField = new("\"Update\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Merge SMAPI config files in SMAPI's order (config.json, config.user.json, Mods/SMAPI-config.json);
    /// later files override earlier ones. Missing or unreadable files are skipped.</summary>
    public static SmapiFlags ReadSmapiFlags(IEnumerable<string> pathsInMergeOrder)
    {
        var flags = new SmapiFlags();
        foreach (string path in pathsInMergeOrder)
        {
            using JsonDocument? doc = TryParseFile(path);
            if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object)
                continue;
            flags.SourceFiles.Add(path);
            foreach (JsonProperty p in doc.RootElement.EnumerateObject())
            {
                switch (p.Name.ToLowerInvariant())
                {
                    case "verboselogging":
                        flags.VerboseLogging = p.Value.ValueKind == JsonValueKind.Array
                            ? p.Value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToArray()
                            : Array.Empty<string>();
                        break;
                    case "developermode": flags.DeveloperMode = ReadBool(p.Value); break;
                    case "paranoidwarnings": flags.ParanoidWarnings = ReadBool(p.Value); break;
                    case "checkforupdates": flags.CheckForUpdates = ReadBool(p.Value); break;
                    case "lognetworktraffic": flags.LogNetworkTraffic = ReadBool(p.Value); break;
                }
            }
        }
        return flags;
    }

    public static RuntimeGcConfig ReadRuntimeConfig(string path)
    {
        var rc = new RuntimeGcConfig { Path = path };
        using JsonDocument? doc = TryParseFile(path);
        if (doc == null)
            return rc;
        rc.Found = true;
        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("runtimeOptions", out JsonElement opts) && opts.ValueKind == JsonValueKind.Object
            && opts.TryGetProperty("configProperties", out JsonElement props) && props.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty p in props.EnumerateObject())
            {
                if (p.Name.Equals("System.GC.Concurrent", StringComparison.OrdinalIgnoreCase)) rc.Concurrent = ReadBool(p.Value);
                else if (p.Name.Equals("System.Runtime.TieredCompilation", StringComparison.OrdinalIgnoreCase)) rc.TieredCompilation = ReadBool(p.Value);
                else if (p.Name.Equals("System.GC.Server", StringComparison.OrdinalIgnoreCase)) rc.Server = ReadBool(p.Value);
            }
        }
        return rc;
    }

    /// <summary>Count CP "Update" fields that include OnTimeChange / OnLocationChange (a field with both counts for both).
    /// Text scan, so files CP accepts but strict JSON rejects still count (same approach as research.md section 1).</summary>
    public static (int OnTimeChange, int OnLocationChange) CountUpdateRates(string json)
    {
        int time = 0, location = 0;
        foreach (Match m in UpdateField.Matches(json))
        {
            string value = m.Groups[1].Value;
            if (value.IndexOf("OnTimeChange", StringComparison.OrdinalIgnoreCase) >= 0) time++;
            if (value.IndexOf("OnLocationChange", StringComparison.OrdinalIgnoreCase) >= 0) location++;
        }
        return (time, location);
    }

    /// <summary>Find loaded Content Patcher packs under the Mods folder that use OnTimeChange/OnLocationChange, heaviest first.</summary>
    public static List<CpPackRate> ScanCpPacks(string modsPath, ISet<string> loadedIds)
    {
        var result = new List<CpPackRate>();
        ForEachLoadedCpPack(modsPath, loadedIds, (dir, id) =>
        {
            int time = 0, location = 0;
            var files = ReadPackJsonFiles(dir).ToList();
            foreach (string text in files)
            {
                var (t, l) = CountUpdateRates(text);
                time += t;
                location += l;
            }
            if (time + location > 0)
            {
                var targets = new AssetNameSet();
                CpConditionalTargets.AddPack(files, targets);
                result.Add(new CpPackRate(dir.Name, id, location, time, targets));
            }
        });
        return result.OrderByDescending(p => p.Total).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>SMAPI's mods folder isn't public API. It's the folder directly under the game folder that contains
    /// this mod (mods can be grouped in subfolders); if the mod isn't under the game folder, the mod's parent folder.</summary>
    public static string FindModsRoot(string modDirectory, string gamePath)
    {
        string game = Path.GetFullPath(gamePath).TrimEnd(Path.DirectorySeparatorChar);
        DirectoryInfo? dir = new DirectoryInfo(Path.GetFullPath(modDirectory));
        while (dir?.Parent != null)
        {
            if (string.Equals(dir.Parent.FullName.TrimEnd(Path.DirectorySeparatorChar), game, StringComparison.Ordinal))
                return dir.FullName;
            dir = dir.Parent;
        }
        return Path.GetDirectoryName(Path.GetFullPath(modDirectory).TrimEnd(Path.DirectorySeparatorChar)) ?? modDirectory;
    }

    public static List<Recommendation> Recommend(EnvSnapshot s)
    {
        var recs = new List<Recommendation>();
        SmapiFlags f = s.Smapi;

        if (f.VerboseLogging.Count == 0)
            recs.Add(new("VerboseLogging", "[]", Severity.Ok, "Keep it empty."));
        else
            recs.Add(new("VerboseLogging", "[" + string.Join(", ", f.VerboseLogging) + "]", Severity.Warn,
                f.VerboseLogging.Contains("*")
                    ? "Verbose logging for everything writes a log line on almost every operation. Set it back to [] in smapi-internal/config.user.json or Mods/SMAPI-config.json."
                    : "Remove these IDs when you're done troubleshooting; verbose logging adds log I/O."));

        recs.Add(f.DeveloperMode == true
            ? new("DeveloperMode", "true", Severity.Warn, "Set it to false for normal play; it adds extra logging and checks.")
            : new("DeveloperMode", Show(f.DeveloperMode), Severity.Ok, "Keep it false for play."));
        recs.Add(f.ParanoidWarnings == true
            ? new("ParanoidWarnings", "true", Severity.Warn, "Set it to false or null; it scans mods for extra warnings.")
            : new("ParanoidWarnings", Show(f.ParanoidWarnings), Severity.Ok, "Keep it null or false."));
        recs.Add(f.LogNetworkTraffic == true
            ? new("LogNetworkTraffic", "true", Severity.Warn, "Set it to false; it logs every multiplayer message.")
            : new("LogNetworkTraffic", Show(f.LogNetworkTraffic), Severity.Ok, "Keep it false."));
        recs.Add(new("CheckForUpdates", Show(f.CheckForUpdates), Severity.Info,
            "No change needed. SMAPI checks in the background, so it doesn't add to load time."));

        string? envConcurrent = EnvValue(s, "DOTNET_gcConcurrent", "COMPlus_gcConcurrent");
        bool concurrentOff = s.Runtime.Concurrent == false || envConcurrent == "0" || s.StartupLatencyMode == GCLatencyMode.Batch;
        string concurrentValue = $"runtimeconfig {(s.Runtime.Concurrent == null ? "unset (default on)" : Show(s.Runtime.Concurrent))}"
            + (envConcurrent != null ? $", env {envConcurrent}" : "") + $", startup GC mode {s.StartupLatencyMode}";
        recs.Add(concurrentOff
            ? new("System.GC.Concurrent", concurrentValue, Severity.Warn,
                "Background GC is off, so gen2 collections block the game and GcLatency can't help. Remove the System.GC.Concurrent=false setting or the gcConcurrent=0 environment variable.")
            : new("System.GC.Concurrent", concurrentValue, Severity.Ok, "Keep background GC on (never set it to false)."));

        string? envTiered = EnvValue(s, "DOTNET_TieredCompilation", "COMPlus_TieredCompilation");
        bool tieredOn = s.Runtime.TieredCompilation == true || envTiered == "1";
        string tieredValue = $"runtimeconfig {(s.Runtime.TieredCompilation == null ? "unset" : Show(s.Runtime.TieredCompilation))}"
            + (envTiered != null ? $", env {envTiered}" : "");
        recs.Add(tieredOn
            ? new("System.Runtime.TieredCompilation", tieredValue, Severity.Warn,
                "SMAPI turns tiered compilation off on purpose because it breaks Harmony patches. Restore SMAPI's default (false).")
            : new("System.Runtime.TieredCompilation", tieredValue, Severity.Info,
                "Leave it off; SMAPI needs that for Harmony. It's why each method is slow the first time it runs, which PreJit works around."));

        if (s.IsServerGc || s.Runtime.Server == true)
            recs.Add(new("System.GC.Server", "true", Severity.Info,
                "Server GC is meant for servers; the game normally uses workstation GC. Remove the setting unless you added it on purpose."));

        if (s.CpPacks.Count == 0)
            recs.Add(new("Content Patcher update rates", "no loaded packs use OnLocationChange/OnTimeChange", Severity.Ok, "Nothing to do."));
        else
        {
            int loc = s.CpPacks.Count(p => p.OnLocationChange > 0), time = s.CpPacks.Count(p => p.OnTimeChange > 0);
            string top = string.Join(", ", s.CpPacks.Take(8).Select(p => $"{p.Name} ({p.OnLocationChange} loc/{p.OnTimeChange} time)"));
            recs.Add(new("Content Patcher update rates",
                $"{s.CpPacks.Count} packs ({loc} OnLocationChange, {time} OnTimeChange); heaviest: {top}", Severity.Warn,
                "These packs re-check patches on every warp or clock change, and a changed map patch reloads the map then. Check each pack's config for options that turn off location- or time-based features you don't use, and run `patch summary <id>` to see which patches change. Don't edit other mods' files."));
        }

        recs.Add(new("Installed mods", $"{s.CsharpMods} C# mods, {s.ContentPacks} content packs", Severity.Info,
            "Most initial load time happens before any mod runs (SMAPI loading and rewriting mods, each mod's Entry, content pack loading). Removing mods you don't use is the main way to shorten it."));
        recs.Add(new("Logs", "", Severity.Info,
            "Play a normal session with your full mod set, then run perf_report and perf_env and share the log through smapi.io/log before tuning."));

        if (s.Screens > 1)
            recs.Add(new("Split-screen", $"{s.Screens} screens", Severity.Info,
                "Each screen reloads its own copy of a changed map, so warp stutter from location-based CP packs grows with the number of screens."));

        string? gen0 = EnvValue(s, "DOTNET_GCgen0size", "COMPlus_GCgen0size");
        if (gen0 != null)
            recs.Add(new("GCgen0size", gen0, Severity.Info,
                "Advanced and unverified on this runtime. If you added it to tune GC, compare perf_report with and without it."));

        return recs;
    }

    public static List<string> Format(IEnumerable<Recommendation> recs)
    {
        var lines = new List<string>();
        foreach (Recommendation r in recs)
        {
            string tag = r.Severity switch { Severity.Warn => "[!]", Severity.Info => "[i]", _ => "[ok]" };
            lines.Add(string.IsNullOrEmpty(r.Value) ? $"{tag} {r.Item}" : $"{tag} {r.Item}: {r.Value}");
            lines.Add($"      -> {r.Advice}");
        }
        return lines;
    }

    /// <summary>Call <paramref name="visit"/> with the folder and UniqueID of each loaded Content Patcher pack under
    /// the Mods folder. Folders starting with '.' are skipped (SMAPI ignores them). File I/O only.</summary>
    public static void ForEachLoadedCpPack(string modsPath, ISet<string> loadedIds, Action<DirectoryInfo, string> visit)
    {
        if (Directory.Exists(modsPath))
            ScanFolder(new DirectoryInfo(modsPath), loadedIds, visit);
    }

    /// <summary>Text of each .json file in a pack except manifest.json; unreadable files are skipped (results are a lower bound).</summary>
    public static IEnumerable<string> ReadPackJsonFiles(DirectoryInfo dir)
    {
        foreach (string file in Directory.EnumerateFiles(dir.FullName, "*.json", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFileName(file), "manifest.json", StringComparison.OrdinalIgnoreCase))
                continue;
            string? text = null;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception)
            {
                // unreadable file
            }
            if (text != null)
                yield return text;
        }
    }

    private static void ScanFolder(DirectoryInfo dir, ISet<string> loadedIds, Action<DirectoryInfo, string> visit)
    {
        IEnumerable<DirectoryInfo> children;
        try
        {
            string manifest = Path.Combine(dir.FullName, "manifest.json");
            if (File.Exists(manifest))
            {
                string? id = ReadCpPackId(manifest, loadedIds);
                if (id != null)
                    visit(dir, id);
                return;
            }
            children = dir.EnumerateDirectories().ToList();
        }
        catch (Exception)
        {
            return; // unreadable folder
        }

        foreach (DirectoryInfo child in children)
            if (!child.Name.StartsWith(".", StringComparison.Ordinal))
                ScanFolder(child, loadedIds, visit);
    }

    /// <summary>The pack's UniqueID if it's a loaded Content Patcher pack, else null.</summary>
    private static string? ReadCpPackId(string manifestPath, ISet<string> loadedIds)
    {
        using JsonDocument? doc = TryParseFile(manifestPath);
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object)
            return null;
        string? id = GetString(doc.RootElement, "UniqueID");
        if (id == null || !loadedIds.Contains(id))
            return null;
        if (!TryGetProperty(doc.RootElement, "ContentPackFor", out JsonElement forEl) || forEl.ValueKind != JsonValueKind.Object
            || !string.Equals(GetString(forEl, "UniqueID"), ContentPatcherId, StringComparison.OrdinalIgnoreCase))
            return null;
        return id;
    }

    private static JsonDocument? TryParseFile(string path)
    {
        try
        {
            return File.Exists(path) ? JsonDocument.Parse(File.ReadAllText(path), Loose) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value)
    {
        foreach (JsonProperty p in obj.EnumerateObject())
        {
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string? GetString(JsonElement obj, string name) =>
        TryGetProperty(obj, name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool? ReadBool(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String when bool.TryParse(v.GetString(), out bool b) => b,
        _ => null
    };

    private static string Show(bool? value) => value == null ? "null" : value.Value ? "true" : "false";

    private static string? EnvValue(EnvSnapshot s, params string[] names)
    {
        foreach (string n in names)
            if (s.EnvVars.TryGetValue(n, out string? v))
                return v;
        return null;
    }
}
