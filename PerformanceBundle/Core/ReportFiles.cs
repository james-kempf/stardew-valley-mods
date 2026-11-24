using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PerformanceBundle.Core;

/// <summary>Session report file names and pruning. Only files named like <c>session-YYYYMMDD-HHMMSS.txt</c> are ever
/// deleted.</summary>
internal static class ReportFiles
{
    public const string FolderName = "reports";
    private static readonly Regex SessionFile = new(@"^session-\d{8}-\d{6}\.txt$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string SessionFileName(DateTime start) => $"session-{start:yyyyMMdd-HHmmss}.txt";

    public static bool IsSessionFile(string fileName) => SessionFile.IsMatch(fileName);

    /// <summary>Session files to delete so that <paramref name="keep"/> remain, counting <paramref name="current"/>
    /// (which is never selected). Names sort by their timestamp, so the oldest go first.</summary>
    public static List<string> SelectForPruning(IEnumerable<string> fileNames, int keep, string current) =>
        fileNames.Where(IsSessionFile)
            .Where(n => !string.Equals(n, current, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(n => n, StringComparer.OrdinalIgnoreCase)
            .Skip(Math.Max(0, keep - 1))
            .ToList();

    /// <summary>Leftover <c>session-*.txt.tmp</c> files from interrupted writes, except this session's own.</summary>
    public static List<string> SelectStaleTemps(IEnumerable<string> fileNames, string current) =>
        fileNames.Where(n => n.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) && IsSessionFile(n[..^4]))
            .Where(n => !string.Equals(n, current + ".tmp", StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>Whether <paramref name="path"/> is inside <paramref name="root"/> (after resolving "..").</summary>
    public static bool IsInside(string root, string path)
    {
        string r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(r, StringComparison.Ordinal);
    }
}

/// <summary>Writes one session report file under <c>&lt;mod folder&gt;/reports</c>, replacing it on each write. The
/// text is always built by the caller (on the main thread); this only writes it, optionally on a worker task. Writes are
/// ordered: a write that finishes after a newer one never replaces it. Never throws.</summary>
internal sealed class ReportFileWriter
{
    private readonly object Sync = new();
    private readonly string ModDirectory;
    private readonly int Keep;
    private long Submitted;
    private long Written;
    private bool Pruned;

    public ReportFileWriter(string modDirectory, DateTime sessionStart, int keep = 10)
    {
        this.ModDirectory = modDirectory;
        this.Keep = keep;
        this.Directory = Path.Combine(modDirectory, ReportFiles.FolderName);
        this.FileName = ReportFiles.SessionFileName(sessionStart);
        this.FilePath = Path.Combine(this.Directory, this.FileName);
    }

    public string Directory { get; }
    public string FileName { get; }
    public string FilePath { get; }

    /// <summary>The last write error, cleared by the next successful write.</summary>
    public string? LastError { get; private set; }

    public DateTime? LastWrite { get; private set; }

    public void WriteInBackground(string text)
    {
        long seq = Interlocked.Increment(ref this.Submitted);
        Task.Run(() => this.Write(seq, text));
    }

    public bool WriteNow(string text) => this.Write(Interlocked.Increment(ref this.Submitted), text);

    internal bool Write(long seq, string text)
    {
        lock (this.Sync)
        {
            if (seq <= this.Written)
                return false;
            try
            {
                if (!ReportFiles.IsInside(this.ModDirectory, this.FilePath))
                    throw new InvalidOperationException($"report path {this.FilePath} is outside the mod folder");
                System.IO.Directory.CreateDirectory(this.Directory);
                if (!this.Pruned)
                {
                    this.Pruned = true;
                    this.Prune();
                }
                string temp = this.FilePath + ".tmp";
                File.WriteAllText(temp, text);
                File.Move(temp, this.FilePath, overwrite: true);
                this.Written = seq;
                this.LastWrite = DateTime.Now;
                this.LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                this.LastError = ex.Message;
                return false;
            }
        }
    }

    private void Prune()
    {
        var names = new DirectoryInfo(this.Directory).GetFiles().Select(f => f.Name).ToList();
        foreach (string name in ReportFiles.SelectForPruning(names, this.Keep, this.FileName).Concat(ReportFiles.SelectStaleTemps(names, this.FileName)))
        {
            try
            {
                File.Delete(Path.Combine(this.Directory, name));
            }
            catch (Exception)
            {
                // A file we can't delete is left for the next session.
            }
        }
    }
}
