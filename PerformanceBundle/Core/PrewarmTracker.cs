using System;
using System.Collections.Generic;

namespace PerformanceBundle.Core;

/// <summary>One screen's prewarm state: assets waiting to load and assets loaded but not yet visited.</summary>
internal sealed class PrewarmQueue
{
    internal readonly Queue<string> Pending = new();
    internal readonly HashSet<string> Queued = new(StringComparer.OrdinalIgnoreCase);
    internal readonly HashSet<string> AwaitingUse = new(StringComparer.OrdinalIgnoreCase);

    public int Count => this.Pending.Count;
}

/// <summary>Session-wide prewarm bookkeeping shared by all screens (the content cache is shared): the exclusion
/// rules, invalidation history and counters. Thread-safe because AssetsInvalidated can be raised off the main thread.
/// An asset is never prewarmed if a conditional CP patch targets it or if it has been invalidated this session.</summary>
internal sealed class PrewarmTracker
{
    private readonly object Sync = new();
    private readonly HashSet<string> Invalidated = new(StringComparer.OrdinalIgnoreCase);
    private AssetNameSet? Conditional;

    public long Prewarmed { get; private set; }
    public long AlreadyLoaded { get; private set; }
    public long Used { get; private set; }
    public long Unused { get; private set; }
    public long InvalidatedBeforeUse { get; private set; }
    public long Excluded { get; private set; }
    public long Failed { get; private set; }

    /// <summary>False until the CP scan has finished; nothing is queued before that.</summary>
    public bool IsReady
    {
        get { lock (this.Sync) return this.Conditional != null; }
    }

    public int ConditionalTargetCount
    {
        get { lock (this.Sync) return this.Conditional?.Count ?? 0; }
    }

    public int InvalidatedCount
    {
        get { lock (this.Sync) return this.Invalidated.Count; }
    }

    public void SetConditionalTargets(AssetNameSet set)
    {
        lock (this.Sync)
            this.Conditional = set;
    }

    /// <summary>Queue assets for the next prewarm pass; excluded, queued or awaiting-use ones are skipped. Returns the number queued.</summary>
    public int Enqueue(PrewarmQueue queue, IEnumerable<string> names)
    {
        lock (this.Sync)
        {
            if (this.Conditional == null)
                return 0;
            int added = 0;
            foreach (string raw in names)
            {
                string name = AssetNameSet.Normalize(raw);
                if (name.Length == 0 || queue.Queued.Contains(name) || queue.AwaitingUse.Contains(name))
                    continue;
                if (this.IsExcludedLocked(name))
                {
                    this.Excluded++;
                    continue;
                }
                queue.Queued.Add(name);
                queue.Pending.Enqueue(name);
                added++;
            }
            return added;
        }
    }

    /// <summary>Next asset to load, skipping any that became excluded since they were queued.</summary>
    public bool TryDequeue(PrewarmQueue queue, out string name)
    {
        lock (this.Sync)
        {
            while (queue.Pending.Count > 0)
            {
                name = queue.Pending.Dequeue();
                queue.Queued.Remove(name);
                if (!this.IsExcludedLocked(name))
                    return true;
            }
            name = "";
            return false;
        }
    }

    public void MarkPrewarmed(PrewarmQueue queue, string name)
    {
        lock (this.Sync)
        {
            this.Prewarmed++;
            queue.AwaitingUse.Add(name);
        }
    }

    public void MarkAlreadyLoaded()
    {
        lock (this.Sync)
            this.AlreadyLoaded++;
    }

    /// <summary>A load threw: count it and never try that asset again this session.</summary>
    public void MarkFailed(string name)
    {
        lock (this.Sync)
        {
            this.Failed++;
            this.Invalidated.Add(AssetNameSet.Normalize(name));
        }
    }

    /// <summary>The player arrived somewhere using these assets: prewarmed ones count as used. Anything else still
    /// awaiting use was for a different neighbor and counts as unused; the queue starts over.</summary>
    public void OnWarped(PrewarmQueue queue, IEnumerable<string> arrivedAssets)
    {
        lock (this.Sync)
        {
            foreach (string raw in arrivedAssets)
                if (queue.AwaitingUse.Remove(AssetNameSet.Normalize(raw)))
                    this.Used++;
            this.Unused += queue.AwaitingUse.Count;
            ClearLocked(queue);
        }
    }

    /// <summary>Returned to title: drop the queue without counting.</summary>
    public void Clear(PrewarmQueue queue)
    {
        lock (this.Sync)
            ClearLocked(queue);
    }

    /// <summary>Record invalidated assets: a prewarmed asset not yet used counts as invalidated-before-use, and every
    /// invalidated asset is excluded from prewarming for the rest of the session.</summary>
    public void OnInvalidated(IEnumerable<string> names, IEnumerable<PrewarmQueue> queues)
    {
        lock (this.Sync)
        {
            var list = new List<string>();
            foreach (string raw in names)
            {
                string name = AssetNameSet.Normalize(raw);
                this.Invalidated.Add(name);
                list.Add(name);
            }
            foreach (PrewarmQueue queue in queues)
                foreach (string name in list)
                    if (queue.AwaitingUse.Remove(name))
                        this.InvalidatedBeforeUse++;
        }
    }

    public bool IsExcluded(string name)
    {
        lock (this.Sync)
            return this.IsExcludedLocked(AssetNameSet.Normalize(name));
    }

    public void ResetCounters()
    {
        lock (this.Sync)
            this.Prewarmed = this.AlreadyLoaded = this.Used = this.Unused = this.InvalidatedBeforeUse = this.Excluded = this.Failed = 0;
    }

    private bool IsExcludedLocked(string name) => this.Invalidated.Contains(name) || this.Conditional?.Contains(name) == true;

    private static void ClearLocked(PrewarmQueue queue)
    {
        queue.Pending.Clear();
        queue.Queued.Clear();
        queue.AwaitingUse.Clear();
    }
}
