namespace PerformanceBundle.Core;

/// <summary>Lets an action run at most once per in-game day (keyed on the day counter, e.g. <c>Game1.Date.TotalDays</c>).</summary>
internal sealed class DayTransitionGuard
{
    private int? LastDay;

    /// <summary>True the first time it's called for a given day; false for repeats until the day changes or <see cref="Reset"/>.</summary>
    public bool TryClaim(int totalDays)
    {
        if (this.LastDay == totalDays)
            return false;
        this.LastDay = totalDays;
        return true;
    }

    public void Reset() => this.LastDay = null;
}
