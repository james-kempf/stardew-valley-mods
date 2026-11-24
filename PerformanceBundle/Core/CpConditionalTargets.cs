using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PerformanceBundle.Core;

/// <summary>Asset names, case-insensitive with '/' and '\' treated alike. A name containing a Content Patcher token
/// (<c>{{...}}</c>) matches any text in the token's place, e.g. <c>Maps/{{season}}_outdoorsTileSheet</c>.</summary>
internal sealed class AssetNameSet
{
    private static readonly Regex Token = new(@"\{\{.*?\}\}", RegexOptions.Compiled);

    private readonly HashSet<string> Literals = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Regex> Patterns = new();
    private readonly HashSet<string> PatternSources = new(StringComparer.OrdinalIgnoreCase);

    public int Count => this.Literals.Count + this.Patterns.Count;

    public static string Normalize(string name) => name.Trim().Replace('\\', '/');

    public void Add(string name)
    {
        name = Normalize(name);
        if (name.Length == 0)
            return;
        if (!name.Contains("{{"))
        {
            this.Literals.Add(name);
            return;
        }
        if (!this.PatternSources.Add(name))
            return;
        var regex = new StringBuilder("^");
        int last = 0;
        foreach (Match m in Token.Matches(name))
        {
            regex.Append(Regex.Escape(name[last..m.Index])).Append(".*");
            last = m.Index + m.Length;
        }
        regex.Append(Regex.Escape(name[last..])).Append('$');
        this.Patterns.Add(new Regex(regex.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    public bool Contains(string name)
    {
        name = Normalize(name);
        return this.Literals.Contains(name) || this.Patterns.Any(p => p.IsMatch(name));
    }
}

/// <summary>Finds asset targets of Content Patcher patches that can change during a day (Update includes OnLocationChange
/// or OnTimeChange). Prewarming those would load a version that's replaced on the next warp or clock change.
/// Errs toward excluding: a conditional Include marks every target in the pack, and a file that can't be parsed
/// but mentions a conditional rate marks every target in that file. File I/O only; no game or SMAPI APIs.</summary>
internal static class CpConditionalTargets
{
    private static readonly JsonDocumentOptions Loose = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private static readonly Regex TargetField = new("\"Target\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Scan every loaded CP pack under the Mods folder.</summary>
    public static AssetNameSet ScanPacks(string modsPath, ISet<string> loadedIds)
    {
        var set = new AssetNameSet();
        EnvironmentScan.ForEachLoadedCpPack(modsPath, loadedIds, (dir, _) => AddPack(EnvironmentScan.ReadPackJsonFiles(dir), set));
        return set;
    }

    /// <summary>Add the conditional targets of one pack, given the text of its JSON files.</summary>
    public static void AddPack(IEnumerable<string> jsonFiles, AssetNameSet set)
    {
        var conditional = new List<string>();
        var all = new List<string>();
        bool packWide = false;
        foreach (string json in jsonFiles)
            packWide |= ReadFile(json, conditional, all);
        foreach (string target in packWide ? all : conditional)
            set.Add(target);
    }

    /// <summary>Collect targets from one file. Returns true if the file has a conditional Include.</summary>
    private static bool ReadFile(string json, List<string> conditional, List<string> all)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, Loose);
        }
        catch (JsonException)
        {
            // CP's parser accepts more than System.Text.Json; fall back to a text scan.
            var targets = TargetField.Matches(json).SelectMany(m => SplitTargets(m.Groups[1].Value)).ToList();
            all.AddRange(targets);
            if (IsConditionalRate(json))
                conditional.AddRange(targets);
            return false;
        }

        using (doc)
            return Walk(doc.RootElement, conditional, all);
    }

    private static bool Walk(JsonElement el, List<string> conditional, List<string> all)
    {
        bool conditionalInclude = false;
        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in el.EnumerateArray())
                conditionalInclude |= Walk(child, conditional, all);
            return conditionalInclude;
        }
        if (el.ValueKind != JsonValueKind.Object)
            return false;

        string? target = null, update = null, action = null;
        foreach (JsonProperty p in el.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.String)
            {
                if (p.Name.Equals("Target", StringComparison.OrdinalIgnoreCase)) target = p.Value.GetString();
                else if (p.Name.Equals("Update", StringComparison.OrdinalIgnoreCase)) update = p.Value.GetString();
                else if (p.Name.Equals("Action", StringComparison.OrdinalIgnoreCase)) action = p.Value.GetString();
            }
            else
                conditionalInclude |= Walk(p.Value, conditional, all);
        }

        bool isConditional = update != null && IsConditionalRate(update);
        if (isConditional && string.Equals(action, "Include", StringComparison.OrdinalIgnoreCase))
            conditionalInclude = true;
        if (target != null)
        {
            var targets = SplitTargets(target).ToList();
            all.AddRange(targets);
            if (isConditional)
                conditional.AddRange(targets);
        }
        return conditionalInclude;
    }

    private static bool IsConditionalRate(string text) =>
        text.IndexOf("OnLocationChange", StringComparison.OrdinalIgnoreCase) >= 0
        || text.IndexOf("OnTimeChange", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>CP allows several comma-separated targets; commas inside tokens (e.g. <c>{{Range:1,3}}</c>) don't split.</summary>
    internal static IEnumerable<string> SplitTargets(string target)
    {
        int depth = 0, start = 0;
        for (int i = 0; i < target.Length; i++)
        {
            if (i + 1 < target.Length && target[i] == '{' && target[i + 1] == '{') { depth++; i++; }
            else if (i + 1 < target.Length && target[i] == '}' && target[i + 1] == '}') { depth = Math.Max(0, depth - 1); i++; }
            else if (target[i] == ',' && depth == 0)
            {
                string part = target[start..i].Trim();
                if (part.Length > 0)
                    yield return part;
                start = i + 1;
            }
        }
        string tail = target[start..].Trim();
        if (tail.Length > 0)
            yield return tail;
    }
}
