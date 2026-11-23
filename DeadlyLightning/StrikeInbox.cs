using System.Collections.Generic;
using System.Linq;

namespace DeadlyLightning;

/// <summary>Strike messages keyed by StrikeId, with dedupe and expiry. Game-independent so it can be tested
/// without the game; the caller passes the clock.</summary>
/// <remarks>Used as a static (not PerScreen) store so every split-screen screen sees it. An id is accepted once:
/// later copies (SMAPI delivering the same message to several screens, or a resend) are ignored, even after the
/// entry is removed or purged.</remarks>
internal sealed class StrikeInbox<T>
{
    private readonly Dictionary<string, (T Item, long AddedMs)> Entries = new();
    private readonly HashSet<string> KnownIds = new();

    public int Count => this.Entries.Count;

    /// <summary>Add an entry unless its id was seen before. Returns whether it was added.</summary>
    public bool TryAdd(string id, T item, long nowMs)
    {
        if (string.IsNullOrEmpty(id) || !this.KnownIds.Add(id))
            return false;
        this.Entries[id] = (item, nowMs);
        return true;
    }

    /// <summary>Snapshot of live entries with their age, so callers can remove while iterating.</summary>
    public List<(string Id, T Item, long AgeMs)> Snapshot(long nowMs)
        => this.Entries.Select(p => (p.Key, p.Value.Item, nowMs - p.Value.AddedMs)).ToList();

    /// <summary>Mark an entry consumed. Its id stays known, so a duplicate can't re-add it.</summary>
    public void Remove(string id) => this.Entries.Remove(id);

    public void Purge(long nowMs, long maxAgeMs)
    {
        foreach (var id in this.Entries.Where(p => nowMs - p.Value.AddedMs >= maxAgeMs).Select(p => p.Key).ToList())
            this.Entries.Remove(id);
    }

    public void Clear()
    {
        this.Entries.Clear();
        this.KnownIds.Clear();
    }
}
