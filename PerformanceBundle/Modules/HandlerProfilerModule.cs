using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using PerformanceBundle.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace PerformanceBundle.Modules;

/// <summary>Times every mod's SMAPI event handlers. SMAPI 4.5.2 has no timing facility, so this inserts marker handlers
/// (owned by this mod) between the existing entries of each event's internal handler list through reflection
/// (research.md 3.6). Mods' own handler entries and delegates are never replaced, so their Add/Remove keep working.
/// No Harmony.
/// <list type="bullet">
/// <item>ProfileMode Continuous: markers are inserted on the first screen-0 tick with a save loaded and stay until
/// returning to title, the module is turned off or fails, or the game closes. Results accumulate for the session report.
/// If the markers' own cost stays over ProfilerMaxOverheadUsPerTick for 60 s, they're removed for the rest of the session.</item>
/// <item>perf_profile &lt;seconds&gt;: a timed window logged as a ranked table. It uses the continuous markers if
/// attached, otherwise inserts markers for the window only.</item>
/// </list>
/// While attached, every sample also goes to <see cref="ModServices.Frame"/> so SpikeDiagnostics can name the slowest
/// handlers in a spike frame.</summary>
internal sealed class HandlerProfilerModule : PerfModule
{
    private const int DefaultSeconds = 10;
    private const int MaxSeconds = 600;
    private const int MaxRows = 25;
    private const int ReportRows = 15;

    private readonly OverheadGuard Guard = new();
    private SmapiEventAccess? Access;
    private string? UnavailableReason;
    private ProfileRun? Run;
    private ManualWindow? Window;
    private string? LastResult;

    /// <summary>Why continuous profiling stopped for the rest of the session (overhead cap, or markers couldn't be inserted).</summary>
    private string? ContinuousOffReason;

    // Session totals since launch or perf_reset, kept across attach/detach.
    private long AttachedMsTotal;
    private long? AttachedSinceMs;
    private long OverheadTicks;
    private long LastOverheadTicks;
    private long AttachedTicks;
    private long DroppedTotal;
    private long OffThreadTotal;
    private long ErrorsTotal;

    public HandlerProfilerModule(ModServices services) : base(services) { }

    public override string Name => "HandlerProfiler";

    /// <summary>Handler time per mod and event over everything profiled this session.</summary>
    public HandlerTimingTable SessionTable { get; } = new();

    /// <summary>Seconds the markers were attached since launch or perf_reset.</summary>
    public double SessionSeconds => (this.AttachedMsTotal + (this.AttachedSinceMs is long since ? ModServices.NowMs - since : 0)) / 1000.0;

    public override bool IsConfiguredOn(ModConfig config) => config.HandlerProfiler;

    private bool ContinuousWanted =>
        this.IsEnabled && this.S.GetConfig().ProfileMode == ProfileMode.Continuous && this.ContinuousOffReason == null && this.UnavailableReason == null;

    protected override void OnEnable()
    {
        var loop = this.Helper.Events.GameLoop;
        this.On<UpdateTickedEventArgs>(h => loop.UpdateTicked += h, h => loop.UpdateTicked -= h, _ => this.OnUpdateTicked());
        this.On<ReturnedToTitleEventArgs>(h => loop.ReturnedToTitle += h, h => loop.ReturnedToTitle -= h, _ =>
        {
            if (Context.ScreenId == 0)
                this.Detach("returned to title");
        });
    }

    protected override void OnDisable() => this.Detach("module turned off");

    protected override void OnResetCounters()
    {
        this.SessionTable.Clear();
        this.AttachedMsTotal = 0;
        if (this.AttachedSinceMs != null)
            this.AttachedSinceMs = ModServices.NowMs;
        this.OverheadTicks = this.LastOverheadTicks = this.AttachedTicks = 0;
        this.DroppedTotal = -(this.Run?.Dropped ?? 0);
        this.OffThreadTotal = this.ErrorsTotal = 0;
    }

    /// <summary>Remove the markers before the game closes (main thread).</summary>
    public void DetachForExit() => this.Detach("game closing");

    protected override void WriteDetails(PerfReport report)
    {
        ModConfig config = this.S.GetConfig();
        report.Add("mode", config.ProfileMode == ProfileMode.Continuous
            ? "Continuous (times handlers while a save is loaded)"
            : "Manual (perf_profile only)");
        if (this.UnavailableReason != null)
            report.Note($"unavailable: {this.UnavailableReason}");
        if (this.ContinuousOffReason != null)
            report.Note($"continuous profiling stopped for this session: {this.ContinuousOffReason}");
        report.Add("markers", this.Run != null ? $"attached to {this.Run.EventCount} events ({this.Run.MarkerCount} markers)" : "not attached");
        report.Add("perf_profile", this.Window != null ? "running" : this.LastResult ?? "not run yet");
        double seconds = this.SessionSeconds;
        report.Add("time profiled", $"{seconds / 60:0.0} min");
        if (this.AttachedTicks > 0)
        {
            double perTick = this.OverheadTicks * 1_000_000.0 / Stopwatch.Frequency / this.AttachedTicks;
            report.Add("marker overhead", $"{perTick:0.0} us/tick average over {this.AttachedTicks} ticks, last second {this.Guard.LastWindowUsPerTick:0.0} us/tick "
                + $"(cap {config.ProfilerMaxOverheadUsPerTick} us/tick for 60 s)");
        }
        report.Add("samples dropped", $"{this.DroppedNow} (mixed-mod segments or nested raises), {Interlocked.Read(ref this.OffThreadTotal)} off the main thread, {this.ErrorsTotal} errors");
        if (seconds <= 0 || this.SessionTable.All.Count == 0)
            return;
        report.Note("Times are inclusive (a handler that raises another event includes those handlers). Only SMAPI event handlers are timed; "
            + "handlers a mod adds after the markers were inserted aren't timed until the next save load. If SMAPI re-sorts an event's handlers "
            + "(a mod subscribing with a priority during play), segments that end up holding two mods' handlers are dropped and counted above.");
        report.Note("The overhead figure is a lower bound: it measures time inside the markers, not SMAPI's own per-handler call cost for them.");
        foreach (string line in this.SessionTable.Format(seconds, Stopwatch.Frequency, ReportRows))
            report.Note(line);
    }

    /// <summary>Console command: <c>perf_profile [seconds]</c> or <c>perf_profile stop</c>.</summary>
    public void Command(string[] args)
    {
        if (!this.IsEnabled)
        {
            this.Monitor.Log("perf_profile is off. Turn on HandlerProfiler in config.json or Generic Mod Config Menu first.", LogLevel.Info);
            return;
        }
        if (args.Length > 0 && args[0].Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            if (this.Window == null)
                this.Monitor.Log("perf_profile isn't running.", LogLevel.Info);
            else
                this.EndWindow("stopped by command", detaching: false);
            return;
        }
        if (this.Window != null)
        {
            this.Monitor.Log("perf_profile is already running; use 'perf_profile stop' to end it early.", LogLevel.Info);
            return;
        }

        int seconds = DefaultSeconds;
        if (args.Length > 0 && (!int.TryParse(args[0], out seconds) || seconds < 1 || seconds > MaxSeconds))
        {
            this.Monitor.Log($"Usage: perf_profile [seconds 1-{MaxSeconds}, default {DefaultSeconds}] | perf_profile stop", LogLevel.Info);
            return;
        }

        if (!this.Attach(continuous: false))
        {
            this.Monitor.Log($"perf_profile couldn't start{(this.UnavailableReason != null ? $": {this.UnavailableReason}" : "")}.", LogLevel.Info);
            return;
        }
        long now = ModServices.NowMs;
        this.Window = new ManualWindow(now, now + seconds * 1000L, this.DroppedNow, Interlocked.Read(ref this.OffThreadTotal), this.ErrorsTotal);
        this.Monitor.Log($"perf_profile: timing {this.Run!.EventCount} events with handlers for {seconds} s. Play normally; the table is logged when it ends.", LogLevel.Info);
    }

    private long DroppedNow => this.DroppedTotal + (this.Run?.Dropped ?? 0);

    private void OnUpdateTicked()
    {
        if (Context.ScreenId != 0)
            return;

        if (this.Run == null && this.ContinuousWanted && Context.IsWorldReady)
        {
            if (!this.Attach(continuous: true))
                this.ContinuousOffReason ??= this.UnavailableReason ?? "the markers couldn't be inserted (see the error above)";
        }
        else if (this.Run is { Continuous: true } && !this.ContinuousWanted)
        {
            // ProfileMode changed to Manual in the config menu.
            this.Run.Continuous = false;
            if (this.Window == null)
                this.Detach("ProfileMode set to Manual");
        }

        if (this.Run != null)
        {
            this.AttachedTicks++;
            double overheadUs = (this.OverheadTicks - this.LastOverheadTicks) * 1_000_000.0 / Stopwatch.Frequency;
            this.LastOverheadTicks = this.OverheadTicks;
            int cap = this.S.GetConfig().ProfilerMaxOverheadUsPerTick;
            if (this.Guard.Sample(ModServices.NowMs, overheadUs, cap) && this.Run.Continuous)
            {
                this.ContinuousOffReason = $"marker overhead stayed over {cap} us/tick for 60 s (last second {this.Guard.LastWindowUsPerTick:0} us/tick)";
                this.Monitor.Log($"HandlerProfiler: {this.ContinuousOffReason}, so continuous profiling is off for the rest of this session. "
                    + "Results so far stay in the report; perf_profile still works.", LogLevel.Info);
                this.Run.Continuous = false;
                if (this.Window == null)
                    this.Detach("overhead cap reached");
            }
        }

        if (this.Window != null && ModServices.NowMs >= this.Window.EndMs)
            this.EndWindow("finished", detaching: false);
    }

    /// <summary>Insert markers, or reuse the attached ones. Returns false if they couldn't be inserted.</summary>
    private bool Attach(bool continuous)
    {
        if (this.Run != null)
        {
            this.Run.Continuous |= continuous;
            return true;
        }

        if (this.Access == null && this.UnavailableReason == null)
        {
            this.Access = SmapiEventAccess.TryCreate(this.Helper.Events, out string? error);
            if (this.Access == null)
            {
                this.UnavailableReason = error;
                this.Monitor.Log($"HandlerProfiler is unavailable in this SMAPI version ({error}). Other features keep running.", LogLevel.Warn);
            }
        }
        if (this.Access == null)
            return false;

        var run = new ProfileRun(this, this.Access, continuous);
        try
        {
            run.Instrument();
        }
        catch (Exception ex)
        {
            int removed = run.Restore();
            this.Monitor.Log($"HandlerProfiler couldn't insert its markers; removed {removed} again. {ex}", LogLevel.Error);
            return false;
        }
        this.Run = run;
        this.AttachedSinceMs = ModServices.NowMs;
        this.LastOverheadTicks = this.OverheadTicks;
        this.Guard.Reset();
        this.S.Frame.Clear();
        if (continuous)
            this.Monitor.Log($"HandlerProfiler: timing {run.EventCount} events with handlers ({run.MarkerCount} markers) until returning to title. Results go into the session report.", LogLevel.Trace);
        return true;
    }

    /// <summary>End any perf_profile window (logging its table), then remove all markers. Safe when nothing is attached.</summary>
    private void Detach(string why)
    {
        if (this.Window != null)
            this.EndWindow(why, detaching: true);
        ProfileRun? run = this.Run;
        if (run == null)
            return;
        this.Run = null;

        int removed = run.Restore();
        this.DroppedTotal += run.Dropped;
        if (this.AttachedSinceMs is long since)
            this.AttachedMsTotal += ModServices.NowMs - since;
        this.AttachedSinceMs = null;
        this.S.Frame.Clear();

        if (removed == run.MarkerCount)
            this.Monitor.Log($"HandlerProfiler: {why}; removed all {removed} markers from {run.EventCount} events, handler lists restored.", LogLevel.Trace);
        else
            this.Monitor.Log($"HandlerProfiler: {why}; {run.MarkerCount - removed} of {run.MarkerCount} markers weren't found when restoring (a handler list was replaced by SMAPI).", LogLevel.Warn);
    }

    /// <summary>Log the perf_profile window's table. Removes the markers too unless continuous profiling uses them.</summary>
    private void EndWindow(string why, bool detaching)
    {
        ManualWindow? w = this.Window;
        if (w == null)
            return;
        this.Window = null;

        double seconds = Math.Max(0.001, (ModServices.NowMs - w.StartMs) / 1000.0);
        var lines = new List<string>
        {
            $"perf_profile {why} after {seconds:0.0} s.",
            "Times are inclusive (a handler that triggers another event, e.g. an asset load, includes those handlers). "
                + $"Samples dropped: {this.DroppedNow - w.DroppedAtStart} (mixed-mod segments or nested raises), "
                + $"{Interlocked.Read(ref this.OffThreadTotal) - w.OffThreadAtStart} (off the main thread), {this.ErrorsTotal - w.ErrorsAtStart} (errors)."
        };
        lines.AddRange(w.Table.Format(seconds, Stopwatch.Frequency, MaxRows));
        this.Monitor.Log(string.Join("\n", lines), LogLevel.Info);
        this.LastResult = $"last run {DateTime.Now:HH:mm:ss}, {seconds:0} s, {w.Table.All.Count} mod/event rows";

        if (!detaching && this.Run is { Continuous: false })
            this.Detach("perf_profile run ended");
    }

    /// <summary>A perf_profile run: its own table plus the session counters when it started.</summary>
    private sealed class ManualWindow
    {
        public ManualWindow(long startMs, long endMs, long dropped, long offThread, long errors)
        {
            this.StartMs = startMs;
            this.EndMs = endMs;
            this.DroppedAtStart = dropped;
            this.OffThreadAtStart = offThread;
            this.ErrorsAtStart = errors;
        }

        public long StartMs { get; }
        public long EndMs { get; }
        public long DroppedAtStart { get; }
        public long OffThreadAtStart { get; }
        public long ErrorsAtStart { get; }
        public HandlerTimingTable Table { get; } = new();
    }

    /// <summary>One insertion of markers. Every sample goes to the session table, the perf_profile window (if any)
    /// and the current frame's attribution.</summary>
    private sealed class ProfileRun : IHandlerTimingSink
    {
        private readonly HandlerProfilerModule Module;
        private readonly SmapiEventAccess Access;
        private readonly List<EventProbe> Probes = new();
        private readonly int MainThreadId = Environment.CurrentManagedThreadId;

        public ProfileRun(HandlerProfilerModule module, SmapiEventAccess access, bool continuous)
        {
            this.Module = module;
            this.Access = access;
            this.Continuous = continuous;
        }

        /// <summary>Kept attached for the session report (otherwise removed when the perf_profile window ends).</summary>
        public bool Continuous { get; set; }

        public int EventCount => this.Probes.Count;
        public int MarkerCount => this.Probes.Sum(p => p.Markers.Count);
        public long Dropped => this.Probes.Sum(p => p.Timeline.Dropped);
        public bool Stopped { get; private set; }
        public bool IsMainThread => Environment.CurrentManagedThreadId == this.MainThreadId;

        public void Add(string mod, string eventName, long ticks, int calls)
        {
            this.Module.SessionTable.Add(mod, eventName, ticks, calls);
            this.Module.Window?.Table.Add(mod, eventName, ticks, calls);
            this.Module.S.Frame.Add(mod, eventName, ticks, calls);
        }

        public void CountOffThread() => Interlocked.Increment(ref this.Module.OffThreadTotal);

        public void CountError() => this.Module.ErrorsTotal++;

        /// <summary>Main thread only: time spent inside a marker.</summary>
        public void AddOverhead(long ticks) => this.Module.OverheadTicks += ticks;

        public void Instrument()
        {
            foreach (object managedEvent in this.Access.GetEvents())
            {
                var probe = new EventProbe(this, this.Access, managedEvent);
                // Added before inserting so a partial insert is still restored.
                this.Probes.Add(probe);
                if (!probe.Insert())
                    this.Probes.Remove(probe);
            }
        }

        public int Restore()
        {
            this.Stopped = true;
            int removed = 0;
            foreach (EventProbe probe in this.Probes)
            {
                try
                {
                    removed += probe.Remove();
                }
                catch (Exception)
                {
                    // Keep restoring the other events; the caller reports the shortfall.
                }
            }
            return removed;
        }
    }


    /// <summary>Markers for one ManagedEvent and the timeline that turns their hits into handler times.</summary>
    private sealed class EventProbe
    {
        private readonly ProfileRun Run;
        private readonly SmapiEventAccess Access;
        private readonly object ManagedEvent;
        private readonly IList Handlers;
        private object? LastArray;

        public EventProbe(ProfileRun run, SmapiEventAccess access, object managedEvent)
        {
            this.Run = run;
            this.Access = access;
            this.ManagedEvent = managedEvent;
            this.Handlers = access.GetHandlerList(managedEvent);
            this.Timeline = new EventTimeline(access.GetEventName(managedEvent));
        }

        public EventTimeline Timeline { get; }

        /// <summary>Marker entries (SMAPI ManagedEventHandler objects) by reference, with their marker ids.</summary>
        public Dictionary<object, int> Markers { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>Marker delegates by reference, to recognize them in the handler array.</summary>
        private Dictionary<Delegate, int> MarkerDelegates { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>Under SMAPI's lock on the handler list (the same lock its Add/Remove/GetHandlers take), put a marker
        /// before every handler and one at the end, then clear the cached array so the next raise picks them up.</summary>
        public bool Insert()
        {
            lock (this.Handlers)
            {
                int count = this.Handlers.Count;
                if (count == 0)
                    return false;
                object last = this.Handlers[count - 1]!;
                this.Handlers.Add(this.CreateMarker(count, last));
                for (int i = count - 1; i >= 0; i--)
                    this.Handlers.Insert(i, this.CreateMarker(i, this.Handlers[i]!));
                this.Access.ClearCache(this.ManagedEvent);
            }
            return true;
        }

        /// <summary>Remove exactly the marker entries this probe added. Returns how many were found.</summary>
        public int Remove()
        {
            int removed = 0;
            lock (this.Handlers)
            {
                for (int i = this.Handlers.Count - 1; i >= 0; i--)
                {
                    if (this.Markers.ContainsKey(this.Handlers[i]!))
                    {
                        this.Handlers.RemoveAt(i);
                        removed++;
                    }
                }
                this.Access.ClearCache(this.ManagedEvent);
                // Every real handler was removed during the run: SMAPI's own Remove saw our markers and left
                // HasListeners true, which would keep SMAPI building args for an event nobody handles.
                if (this.Handlers.Count == 0)
                    this.Access.ClearHasListeners(this.ManagedEvent);
            }
            return removed;
        }

        /// <summary>Marker body. Runs inside SMAPI's raise loop.</summary>
        public void OnMarker(int id)
        {
            long enter = Stopwatch.GetTimestamp();
            if (this.Run.Stopped)
                return;
            if (!this.Run.IsMainThread)
            {
                // AssetRequested and similar can be raised on loader threads; the timeline is main-thread only.
                this.Run.CountOffThread();
                return;
            }
            try
            {
                object? array = this.Access.GetCachedArray(this.ManagedEvent);
                if (array != null && !ReferenceEquals(array, this.LastArray))
                {
                    this.LastArray = array;
                    this.Timeline.SetLayout(this.ReadLayout((Array)array));
                }
                this.Timeline.Hit(id, enter, this.Run);
                long exit = Stopwatch.GetTimestamp();
                this.Timeline.Arm(exit);
                this.Run.AddOverhead(exit - enter);
            }
            catch (Exception)
            {
                // Never throw into SMAPI's raise loop (it would log an error for this mod on every raise).
                this.Run.CountError();
                this.Timeline.ResetSequence();
            }
        }

        private object CreateMarker(int id, object neighbor)
        {
            var target = new MarkerTarget(this, id);
            Delegate handler = this.Access.CreateHandlerDelegate(this.ManagedEvent, target);
            object entry = this.Access.CreateEntry(this.ManagedEvent, handler, neighbor);
            this.Markers[entry] = id;
            this.MarkerDelegates[handler] = id;
            return entry;
        }

        private List<TimelineEntry> ReadLayout(Array array)
        {
            var layout = new List<TimelineEntry>(array.Length);
            foreach (object? entry in array)
            {
                if (entry == null)
                    continue;
                Delegate? handler = this.Access.GetEntryHandler(entry);
                layout.Add(handler != null && this.MarkerDelegates.TryGetValue(handler, out int id)
                    ? TimelineEntry.Marker(id)
                    : TimelineEntry.Handler(this.Access.GetModName(entry)));
            }
            return layout;
        }
    }

    /// <summary>Target of a marker delegate. Fire's (object?, object) signature binds to any EventHandler&lt;T&gt;
    /// whose T is a class, which all SMAPI event args are.</summary>
    private sealed class MarkerTarget
    {
        private readonly EventProbe Probe;
        private readonly int Id;

        public MarkerTarget(EventProbe probe, int id)
        {
            this.Probe = probe;
            this.Id = id;
        }

        public static readonly MethodInfo FireMethod = typeof(MarkerTarget).GetMethod(nameof(Fire))!;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Fire(object? sender, object e) => this.Probe.OnMarker(this.Id);
    }

    /// <summary>Reflection over SMAPI 4.5.2 internals (research.md 3.6): helper.Events.X → ModEventsBase.EventManager →
    /// public ManagedEvent&lt;T&gt; fields, each with private Handlers (List&lt;ManagedEventHandler&lt;T&gt;&gt;) and
    /// CachedHandlers (array). Every member is checked once in TryCreate; a missing one makes the profiler unavailable.</summary>
    private sealed class SmapiEventAccess
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly object EventManager;
        private readonly object OwnMod;
        private readonly Dictionary<Type, EventTypeInfo> Types = new();
        private readonly Dictionary<object, string> ModNames = new(ReferenceEqualityComparer.Instance);

        private SmapiEventAccess(object eventManager, object ownMod)
        {
            this.EventManager = eventManager;
            this.OwnMod = ownMod;
        }

        public static SmapiEventAccess? TryCreate(IModEvents events, out string? error)
        {
            try
            {
                object gameLoop = events.GameLoop;
                FieldInfo? managerField = FindField(gameLoop.GetType(), "EventManager");
                FieldInfo? modField = FindField(gameLoop.GetType(), "Mod");
                object? manager = managerField?.GetValue(gameLoop);
                object? mod = modField?.GetValue(gameLoop);
                if (manager == null || mod == null)
                {
                    error = "ModEventsBase.EventManager/Mod not found";
                    return null;
                }

                var access = new SmapiEventAccess(manager, mod);
                int events_ = 0;
                foreach (FieldInfo field in manager.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!IsManagedEvent(field.FieldType))
                        continue;
                    EventTypeInfo? info = EventTypeInfo.TryCreate(field.FieldType, out error);
                    if (info == null)
                        return null;
                    access.Types[field.FieldType] = info;
                    events_++;
                }
                if (events_ == 0)
                {
                    error = "no ManagedEvent fields on EventManager";
                    return null;
                }
                error = null;
                return access;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>Every ManagedEvent instance that currently has handlers.</summary>
        public IEnumerable<object> GetEvents()
        {
            foreach (FieldInfo field in this.EventManager.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!this.Types.ContainsKey(field.FieldType))
                    continue;
                object? ev = field.GetValue(this.EventManager);
                if (ev != null && this.GetHandlerList(ev).Count > 0)
                    yield return ev;
            }
        }

        public IList GetHandlerList(object ev) => (IList)this.Types[ev.GetType()].Handlers.GetValue(ev)!;

        public object? GetCachedArray(object ev) => this.Types[ev.GetType()].Cached.GetValue(ev);

        public void ClearCache(object ev) => this.Types[ev.GetType()].Cached.SetValue(ev, null);

        /// <summary>Set HasListeners to false, as SMAPI's Remove does when the list empties. Skipped if the member
        /// changed in a later SMAPI; the only effect then is SMAPI raising an event that has no handlers.</summary>
        public void ClearHasListeners(object ev) => this.Types[ev.GetType()].HasListenersSetter?.Invoke(ev, new object[] { false });

        public string GetEventName(object ev) => this.Types[ev.GetType()].EventName.GetValue(ev) as string ?? ev.GetType().Name;

        public Delegate CreateHandlerDelegate(object ev, MarkerTarget target) =>
            Delegate.CreateDelegate(this.Types[ev.GetType()].DelegateType, target, MarkerTarget.FireMethod);

        /// <summary>A ManagedEventHandler&lt;T&gt; for the marker, with this mod as source and the neighbor's priority
        /// and registration order so it sorts next to that handler.</summary>
        public object CreateEntry(object ev, Delegate handler, object neighbor)
        {
            EventTypeInfo info = this.Types[ev.GetType()];
            object order = info.RegistrationOrder.GetValue(neighbor)!;
            object priority = info.Priority.GetValue(neighbor)!;
            return info.EntryCtor.Invoke(new[] { handler, order, priority, this.OwnMod });
        }

        public Delegate? GetEntryHandler(object entry) =>
            entry.GetType().GetProperty("Handler", Any)?.GetValue(entry) as Delegate;

        public string GetModName(object entry)
        {
            object? mod = entry.GetType().GetProperty("SourceMod", Any)?.GetValue(entry);
            if (mod == null)
                return "(unknown)";
            if (!this.ModNames.TryGetValue(mod, out string? name))
            {
                name = mod.GetType().GetProperty("DisplayName", Any)?.GetValue(mod) as string
                    ?? (mod.GetType().GetProperty("Manifest", Any)?.GetValue(mod) as IManifest)?.UniqueID
                    ?? "(unknown)";
                this.ModNames[mod] = name;
            }
            return name;
        }

        private static bool IsManagedEvent(Type type) =>
            type.IsGenericType && type.GetGenericTypeDefinition().Name == "ManagedEvent`1";

        private static FieldInfo? FindField(Type? type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                FieldInfo? field = type.GetField(name, Any | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }
            return null;
        }

        /// <summary>Resolved members for one ManagedEvent&lt;T&gt;.</summary>
        private sealed class EventTypeInfo
        {
            public FieldInfo Handlers = null!;
            public FieldInfo Cached = null!;
            public PropertyInfo EventName = null!;
            public Type DelegateType = null!;
            public ConstructorInfo EntryCtor = null!;
            public PropertyInfo RegistrationOrder = null!;
            public PropertyInfo Priority = null!;
            public MethodInfo? HasListenersSetter;

            public static EventTypeInfo? TryCreate(Type eventType, out string? error)
            {
                Type args = eventType.GetGenericArguments()[0];
                FieldInfo? handlers = eventType.GetField("Handlers", Any);
                FieldInfo? cached = eventType.GetField("CachedHandlers", Any);
                PropertyInfo? name = eventType.GetProperty("EventName", Any);
                Type? listType = handlers?.FieldType;
                Type? entryType = listType is { IsGenericType: true } && typeof(IList).IsAssignableFrom(listType)
                    ? listType.GetGenericArguments()[0]
                    : null;
                Type delegateType = typeof(EventHandler<>).MakeGenericType(args);
                PropertyInfo? order = entryType?.GetProperty("RegistrationOrder", Any);
                PropertyInfo? priority = entryType?.GetProperty("Priority", Any);
                ConstructorInfo? ctor = entryType == null || order == null || priority == null
                    ? null
                    : entryType.GetConstructors(Any).FirstOrDefault(c =>
                    {
                        ParameterInfo[] p = c.GetParameters();
                        return p.Length == 4 && p[0].ParameterType == delegateType && p[1].ParameterType == order.PropertyType
                            && p[2].ParameterType == priority.PropertyType;
                    });

                if (handlers == null || cached == null || name == null || entryType == null || ctor == null || args.IsValueType
                    || cached.FieldType != entryType.MakeArrayType() || entryType.GetProperty("Handler", Any) == null
                    || entryType.GetProperty("SourceMod", Any) == null)
                {
                    error = $"{eventType.Name}<{args.Name}> internals changed";
                    return null;
                }
                error = null;
                return new EventTypeInfo
                {
                    Handlers = handlers,
                    Cached = cached,
                    EventName = name,
                    DelegateType = delegateType,
                    EntryCtor = ctor,
                    RegistrationOrder = order!,
                    Priority = priority!,
                    HasListenersSetter = eventType.GetProperty("HasListeners", Any) is { PropertyType: var t } hasListeners && t == typeof(bool)
                        ? hasListeners.GetSetMethod(nonPublic: true)
                        : null
                };
            }
        }
    }
}
