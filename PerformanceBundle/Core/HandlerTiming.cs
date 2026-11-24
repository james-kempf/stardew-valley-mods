using System;
using System.Collections.Generic;
using System.Linq;

namespace PerformanceBundle.Core;

/// <summary>One entry of an event's handler array as seen by the profiler: one of our markers, or a mod's handler.</summary>
internal readonly record struct TimelineEntry(int MarkerId, string? Mod)
{
    public bool IsMarker => this.MarkerId >= 0;

    public static TimelineEntry Marker(int id) => new(id, null);

    public static TimelineEntry Handler(string mod) => new(-1, mod);
}

/// <summary>Receives handler time samples from <see cref="EventTimeline"/>.</summary>
internal interface IHandlerTimingSink
{
    void Add(string mod, string eventName, long ticks, int calls);
}

/// <summary>Handler time per mod/event within the current frame, for spike attribution. Main thread only; cleared every
/// frame by the spike logger.</summary>
internal sealed class FrameAttribution : IHandlerTimingSink
{
    private readonly Dictionary<(string Mod, string Event), long> Ticks = new();

    public bool HasData => this.Ticks.Count > 0;

    public void Add(string mod, string eventName, long ticks, int calls)
    {
        if (ticks <= 0)
            return;
        this.Ticks[(mod, eventName)] = this.Ticks.TryGetValue((mod, eventName), out long t) ? t + ticks : ticks;
    }

    /// <summary>The <paramref name="count"/> mod/event pairs with the most time, highest first.</summary>
    public IReadOnlyList<FrameCulprit> Top(int count, long ticksPerSecond) =>
        this.Ticks.OrderByDescending(p => p.Value).ThenBy(p => p.Key.Mod, StringComparer.OrdinalIgnoreCase).Take(count)
            .Select(p => new FrameCulprit(p.Key.Mod, p.Key.Event, p.Value * 1000.0 / ticksPerSecond)).ToList();

    public void Clear()
    {
        if (this.Ticks.Count > 0)
            this.Ticks.Clear();
    }
}

/// <summary>Turns marker hits for one event into per-mod handler times. Markers sit between handlers; the time from
/// one marker to the next marker later in the same handler array is charged to the handlers between them, if they
/// all belong to one mod (otherwise the sample is dropped). The layout is taken from the actual handler array, so a
/// re-sort by SMAPI (handlers added during the run) can't attribute time to the wrong mod.</summary>
internal sealed class EventTimeline
{
    private readonly Dictionary<int, int> PositionOf = new();
    private TimelineEntry[] Layout = Array.Empty<TimelineEntry>();
    private int LastPosition = -1;
    private int EndMarkerPosition = -1;
    private long LastTimestamp;

    public EventTimeline(string eventName) => this.EventName = eventName;

    public string EventName { get; }

    /// <summary>Samples dropped: the handlers between two markers belonged to more than one mod, or a pass was cut
    /// short by a nested raise (or an exception) before its end marker.</summary>
    public long Dropped { get; private set; }

    public void SetLayout(IReadOnlyList<TimelineEntry> layout)
    {
        this.Layout = layout.ToArray();
        this.PositionOf.Clear();
        for (int i = 0; i < this.Layout.Length; i++)
            if (this.Layout[i].IsMarker)
                this.PositionOf[this.Layout[i].MarkerId] = this.EndMarkerPosition = i;
        this.ResetSequence();
    }

    /// <summary>Forget the previous marker (e.g. after the layout changed), so the next hit starts a new sequence.</summary>
    public void ResetSequence() => this.LastPosition = -1;

    /// <summary>A marker ran at <paramref name="enterTimestamp"/>. Records the time since the previous marker's
    /// <see cref="Arm"/> if that marker was earlier in the same array pass.</summary>
    public void Hit(int markerId, long enterTimestamp, IHandlerTimingSink table)
    {
        if (!this.PositionOf.TryGetValue(markerId, out int pos))
        {
            this.LastPosition = -1;
            return;
        }

        int prev = this.LastPosition;
        this.LastPosition = pos;
        if (prev < 0)
            return; // first marker of a raise
        if (pos <= prev)
        {
            // A new raise started before the previous pass reached its end marker (a nested raise from inside a
            // handler, or an exception that cut the pass short): the open segment can't be attributed.
            if (prev != this.EndMarkerPosition)
                this.Dropped++;
            return;
        }

        string? mod = null;
        int handlers = 0;
        for (int i = prev + 1; i < pos; i++)
        {
            TimelineEntry e = this.Layout[i];
            if (e.IsMarker)
                continue;
            if (mod != null && !string.Equals(mod, e.Mod, StringComparison.Ordinal))
            {
                this.Dropped++;
                return;
            }
            mod = e.Mod;
            handlers++;
        }
        if (mod != null)
            table.Add(mod, this.EventName, enterTimestamp - this.LastTimestamp, handlers);
    }

    /// <summary>Set the start time for the next segment (called as the marker returns, so marker overhead isn't charged).</summary>
    public void Arm(long exitTimestamp) => this.LastTimestamp = exitTimestamp;
}

internal sealed class HandlerTimingRow
{
    public HandlerTimingRow(string mod, string eventName)
    {
        this.Mod = mod;
        this.Event = eventName;
    }

    public string Mod { get; }
    public string Event { get; }
    public long TotalTicks { get; set; }
    public long MaxTicks { get; set; }
    public long Calls { get; set; }
}

/// <summary>Per mod and event totals for perf_profile, with ranked text output.</summary>
internal sealed class HandlerTimingTable : IHandlerTimingSink
{
    private readonly Dictionary<(string Mod, string Event), HandlerTimingRow> Rows = new();

    public IReadOnlyCollection<HandlerTimingRow> All => this.Rows.Values;

    public void Add(string mod, string eventName, long ticks, int calls)
    {
        if (ticks < 0)
            return;
        if (!this.Rows.TryGetValue((mod, eventName), out HandlerTimingRow? row))
            this.Rows[(mod, eventName)] = row = new HandlerTimingRow(mod, eventName);
        row.TotalTicks += ticks;
        row.Calls += calls;
        row.MaxTicks = Math.Max(row.MaxTicks, ticks);
    }

    public void Clear() => this.Rows.Clear();

    /// <summary>Rows sorted by total time, highest first.</summary>
    public List<HandlerTimingRow> Ranked(int max) =>
        this.Rows.Values.OrderByDescending(r => r.TotalTicks).ThenBy(r => r.Mod, StringComparer.OrdinalIgnoreCase).Take(max).ToList();

    /// <summary>Mods sorted by total time across all events, highest first.</summary>
    public List<(string Mod, long Ticks, long Calls)> RankedMods(int max) =>
        this.Rows.Values.GroupBy(r => r.Mod)
            .Select(g => (g.Key, g.Sum(r => r.TotalTicks), g.Sum(r => r.Calls)))
            .OrderByDescending(m => m.Item2).ThenBy(m => m.Key, StringComparer.OrdinalIgnoreCase)
            .Take(max).ToList();

    /// <summary>Text table. <paramref name="ticksPerSecond"/> is Stopwatch.Frequency.</summary>
    public List<string> Format(double seconds, long ticksPerSecond, int maxRows)
    {
        double Ms(long ticks) => ticks * 1000.0 / ticksPerSecond;
        double Us(long ticks) => ticks * 1_000_000.0 / ticksPerSecond;
        var lines = new List<string>();
        if (this.Rows.Count == 0)
        {
            lines.Add("No handler time recorded.");
            return lines;
        }

        lines.Add($"Per mod (total over {seconds:0.#} s):");
        foreach (var (mod, ticks, calls) in this.RankedMods(maxRows))
            lines.Add($"  {Ms(ticks),10:0.00} ms  {Ms(ticks) / seconds,8:0.000} ms/s  {calls,8} calls  {mod}");

        lines.Add("Per mod and event:");
        lines.Add($"  {"total ms",10}  {"calls",8}  {"avg us",9}  {"max us",9}  mod / event");
        foreach (HandlerTimingRow r in this.Ranked(maxRows))
            lines.Add($"  {Ms(r.TotalTicks),10:0.00}  {r.Calls,8}  {Us(r.TotalTicks) / Math.Max(1, r.Calls),9:0.0}  {Us(r.MaxTicks),9:0.0}  {r.Mod} / {r.Event}");
        return lines;
    }
}
