using System.Collections.Generic;
using System.Linq;

namespace PerformanceBundle.Core;

/// <summary>A mod's event handler time within one frame, from the handler profiler.</summary>
internal sealed record FrameCulprit(string Mod, string Event, double Ms);

/// <summary>One frame whose update-to-update interval exceeded the spike threshold. <paramref name="Culprits"/> are the
/// mod/event pairs with the most handler time in that frame (only while the handler profiler is attached).</summary>
internal sealed record SpikeRecord(
    double FrameMs, long AtMs, int Gen0, int Gen1, int Gen2, double GcPauseMs, int AssetsInvalidated, int AssetsLoaded, string? Warp,
    IReadOnlyList<FrameCulprit>? Culprits = null)
{
    public const string TableHeader = "ms | GC gen0/1/2 | GC pause ms | invalidated | loaded | warp | most handler time in the frame";

    public string ToRow() =>
        $"{this.FrameMs,7:0.0} | {this.Gen0}/{this.Gen1}/{this.Gen2} | {this.GcPauseMs:0.0} | {this.AssetsInvalidated} | {this.AssetsLoaded} | {this.Warp ?? "-"} | "
        + (this.Culprits is { Count: > 0 } c ? string.Join(", ", c.Select(x => $"{x.Mod} {x.Event} {x.Ms:0.0} ms")) : "-");
}

/// <summary>Keeps the N worst spikes (largest frame time first) plus a running count.</summary>
internal sealed class SpikeLog
{
    private readonly int Capacity;
    private readonly List<SpikeRecord> Items = new();

    public SpikeLog(int capacity) => this.Capacity = capacity;

    public int Count { get; private set; }

    /// <summary>Worst spikes, largest frame time first.</summary>
    public IReadOnlyList<SpikeRecord> Worst => this.Items;

    public void Add(SpikeRecord record)
    {
        this.Count++;
        if (this.Items.Count == this.Capacity && record.FrameMs <= this.Items[^1].FrameMs)
            return;
        int i = this.Items.FindIndex(s => s.FrameMs < record.FrameMs);
        this.Items.Insert(i < 0 ? this.Items.Count : i, record);
        if (this.Items.Count > this.Capacity)
            this.Items.RemoveAt(this.Items.Count - 1);
    }

    public void Reset()
    {
        this.Items.Clear();
        this.Count = 0;
    }
}
