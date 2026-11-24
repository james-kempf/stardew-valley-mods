using System.Runtime;

namespace PerformanceBundle.Core;

internal static class GcLatencyPolicy
{
    /// <summary>With concurrent GC off the runtime starts in Batch mode and silently ignores SustainedLowLatency
    /// (verified on the game's .NET 6.0.32, research.md section 2).</summary>
    public static bool CanUseSustainedLowLatency(GCLatencyMode startupMode) => startupMode != GCLatencyMode.Batch;
}
