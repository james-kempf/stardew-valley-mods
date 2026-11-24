namespace PerformanceBundle.Core;

/// <summary>Decides when continuous handler profiling costs too much. Overhead is averaged per tick over 1-second
/// windows; the guard trips when every window for <c>sustainMs</c> was over the cap. A single window under the cap
/// starts the count again, so short bursts (a warp, a day start) never trip it.</summary>
internal sealed class OverheadGuard
{
    private readonly long SustainMs;
    private readonly long WindowMs;
    private long WindowStartMs = -1;
    private double WindowUs;
    private long WindowTicks;
    private long? OverSinceMs;

    public OverheadGuard(long sustainMs = 60_000, long windowMs = 1_000)
    {
        this.SustainMs = sustainMs;
        this.WindowMs = windowMs;
    }

    /// <summary>Average overhead per tick in the last complete window.</summary>
    public double LastWindowUsPerTick { get; private set; }

    /// <summary>Add one tick's overhead. Returns true once the cap has been exceeded for the sustain time.
    /// A cap of 0 or less turns the guard off.</summary>
    public bool Sample(long nowMs, double overheadUs, double capUsPerTick)
    {
        if (this.WindowStartMs < 0)
            this.WindowStartMs = nowMs;
        this.WindowUs += overheadUs;
        this.WindowTicks++;
        if (nowMs - this.WindowStartMs < this.WindowMs)
            return false;

        this.LastWindowUsPerTick = this.WindowUs / this.WindowTicks;
        if (capUsPerTick > 0 && this.LastWindowUsPerTick > capUsPerTick)
            this.OverSinceMs ??= this.WindowStartMs;
        else
            this.OverSinceMs = null;
        this.WindowStartMs = nowMs;
        this.WindowUs = 0;
        this.WindowTicks = 0;
        return this.OverSinceMs is long since && nowMs - since >= this.SustainMs;
    }

    public void Reset()
    {
        this.WindowStartMs = -1;
        this.WindowUs = 0;
        this.WindowTicks = 0;
        this.OverSinceMs = null;
        this.LastWindowUsPerTick = 0;
    }
}
