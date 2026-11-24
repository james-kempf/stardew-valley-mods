using System.Collections.Generic;

namespace PerformanceBundle.Modules;

/// <summary>A switchable feature. ModEntry enables or disables each module from its config switch and re-applies
/// the config after Generic Mod Config Menu saves. Add a module by implementing this (usually through
/// <see cref="PerfModule"/>) and listing it in ModEntry.CreateModules.</summary>
internal interface IPerfModule
{
    /// <summary>Short name used in logs and perf_report.</summary>
    string Name { get; }

    bool IsEnabled { get; }

    /// <summary>True after a handler threw; the module stays off for the rest of the session.</summary>
    bool Faulted { get; }

    /// <summary>The config switch for this module.</summary>
    bool IsConfiguredOn(ModConfig config);

    /// <summary>Subscribe to events and apply any global state. Idempotent; never throws.</summary>
    void Enable();

    /// <summary>Unsubscribe, cancel background work and restore any global state. Idempotent; never throws.</summary>
    void Disable();

    /// <summary>Add this module's section to perf_report.</summary>
    void WriteReport(PerfReport report);

    /// <summary>Clear counters (perf_reset). Never touches global state.</summary>
    void ResetCounters();
}

/// <summary>perf_report content: one section per module, each a list of "label: value" lines.</summary>
internal sealed class PerfReport
{
    private readonly List<string> Lines = new();

    public void Section(string title, string status) => this.Lines.Add($"== {title} ({status}) ==");

    public void Add(string label, object? value) => this.Lines.Add($"  {label}: {value}");

    public void Note(string text) => this.Lines.Add($"  {text}");

    public IReadOnlyList<string> GetLines() => this.Lines;
}
