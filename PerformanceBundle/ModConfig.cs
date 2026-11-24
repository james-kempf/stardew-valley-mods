namespace PerformanceBundle;

/// <summary>When HandlerProfiler times handlers.</summary>
internal enum ProfileMode
{
    /// <summary>All the time while a save is loaded, for the session report (and perf_profile runs).</summary>
    Continuous,

    /// <summary>Only during perf_profile runs.</summary>
    Manual
}

/// <summary>One switch per module (defaults follow the research.md classification matrix) plus thresholds.</summary>
internal sealed class ModConfig
{
    // ---------- Real optimizations ----------

    /// <summary>Background (non-blocking) gen2 GC while a save is loaded. Process-wide, owned by the first screen.</summary>
    public bool GcLatency { get; set; } = true;

    /// <summary>One compacting GC per day transition and after loading a save, while the screen is black.</summary>
    public bool GcTransitionCollect { get; set; } = true;

    /// <summary>Compile location, NPC and rendering code on a low-priority background thread after launch.</summary>
    public bool PreJit { get; set; } = true;

    /// <summary>After a warp, pre-load tilesheet textures of neighboring locations into the shared content cache (off by default).</summary>
    public bool AdjacentPrewarm { get; set; }

    // ---------- Diagnostics ----------

    /// <summary>Passive frame-spike, GC and warp timing log, shown by perf_report.</summary>
    public bool SpikeDiagnostics { get; set; } = true;

    /// <summary>Counts invalidated assets per minute and per warp to find Content Patcher invalidation storms.</summary>
    public bool InvalidationReport { get; set; } = true;

    /// <summary>Per-mod event handler timing: continuously while a save is loaded (ProfileMode Continuous) and through the
    /// perf_profile command.</summary>
    public bool HandlerProfiler { get; set; } = true;

    /// <summary>Continuous: time handlers whenever a save is loaded. Manual: only during perf_profile runs.</summary>
    public ProfileMode ProfileMode { get; set; } = ProfileMode.Continuous;

    /// <summary>Write the full report to Mods/PerformanceBundle/reports at each day end, every 5 minutes, on returning
    /// to title and when the game closes normally.</summary>
    public bool AutoReport { get; set; } = true;

    // ---------- Recommendations ----------

    /// <summary>Log the perf_env environment report once at launch (the perf_env command always works).</summary>
    public bool EnvironmentReport { get; set; } = true;

    // ---------- Thresholds ----------

    /// <summary>A frame (update to update) longer than this many milliseconds is logged as a spike.</summary>
    public int SpikeThresholdMs { get; set; } = 50;

    /// <summary>Invalidated asset names in one minute that count as an invalidation storm (logged once per minute).</summary>
    public int InvalidationStormPerMinute { get; set; } = 100;

    /// <summary>Continuous profiling turns itself off for the session if its markers cost more than this many
    /// microseconds per tick for 60 seconds in a row.</summary>
    public int ProfilerMaxOverheadUsPerTick { get; set; } = 200;

    /// <summary>Most main-thread time AdjacentPrewarm spends loading per update tick, in milliseconds.</summary>
    public int AdjacentPrewarmBudgetMs { get; set; } = 2;
}
