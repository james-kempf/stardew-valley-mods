using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime;
using System.Threading;
using PerformanceBundle.Core;
using StardewModdingAPI;

namespace PerformanceBundle.Modules;

/// <summary>Shared services and session state passed to every module.</summary>
internal sealed class ModServices
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    public ModServices(IModHelper helper, IMonitor monitor, Func<ModConfig> getConfig, GCLatencyMode startupLatencyMode)
    {
        this.Helper = helper;
        this.Monitor = monitor;
        this.GetConfig = getConfig;
        this.StartupLatencyMode = startupLatencyMode;
    }

    public IModHelper Helper { get; }
    public IMonitor Monitor { get; }
    public Func<ModConfig> GetConfig { get; }

    /// <summary>GCSettings.LatencyMode when the mod was constructed, before any module changed it.</summary>
    public GCLatencyMode StartupLatencyMode { get; }

    /// <summary>Set by ModEntry when GameLoop.GameLaunched (raised once, not per screen) has been handled.</summary>
    public bool GameLaunched { get; set; }

    /// <summary>Process uptime when GameLaunched was raised, if known.</summary>
    public TimeSpan? LaunchUptime { get; set; }

    /// <summary>Per-day counters and session totals for the automatic report.</summary>
    public SessionStats Session { get; } = new(NowMs);

    /// <summary>Handler time in the current frame, filled by HandlerProfiler and taken by SpikeDiagnostics.</summary>
    public FrameAttribution Frame { get; } = new();

    /// <summary>Monotonic milliseconds shared by all screens (Game1 tick counters are per screen).</summary>
    public static long NowMs => Clock.ElapsedMilliseconds;
}

/// <summary>Base for modules: tracked event subscriptions, try/catch around every handler (a failure logs once,
/// turns off only this module and restores its global state through <see cref="OnDisable"/>) and self-measured
/// handler time for perf_report.</summary>
internal abstract class PerfModule : IPerfModule
{
    private readonly List<Action> Unsubscribers = new();
    private long HandlerTicks;
    private long HandlerCalls;

    protected PerfModule(ModServices services) => this.S = services;

    protected ModServices S { get; }
    protected IMonitor Monitor => this.S.Monitor;
    protected IModHelper Helper => this.S.Helper;

    public abstract string Name { get; }
    public bool IsEnabled { get; private set; }
    public bool Faulted { get; private set; }

    /// <summary>Whether this module's handler time counts toward the passive-diagnostics budget (under 50 us/tick).</summary>
    public virtual bool IsPassiveDiagnostic => false;

    /// <summary>Total time spent in this module's handlers since the last reset, in microseconds.</summary>
    public double HandlerMicroseconds => Interlocked.Read(ref this.HandlerTicks) * 1_000_000.0 / Stopwatch.Frequency;

    public long HandlerCallCount => Interlocked.Read(ref this.HandlerCalls);

    public abstract bool IsConfiguredOn(ModConfig config);

    public void Enable()
    {
        if (this.IsEnabled || this.Faulted)
            return;
        this.IsEnabled = true;
        try
        {
            this.OnEnable();
        }
        catch (Exception ex)
        {
            this.Fail("enable", ex);
        }
    }

    public void Disable()
    {
        if (!this.IsEnabled)
            return;
        this.IsEnabled = false;
        foreach (Action unsubscribe in this.Unsubscribers)
        {
            try
            {
                unsubscribe();
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"{this.Name}: couldn't unsubscribe a handler: {ex}", LogLevel.Trace);
            }
        }
        this.Unsubscribers.Clear();
        try
        {
            this.OnDisable();
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"{this.Name} failed while turning off: {ex}", LogLevel.Error);
        }
    }

    public void WriteReport(PerfReport report)
    {
        string status = this.Faulted ? "turned off after an error" : this.IsEnabled ? "on" : "off";
        report.Section(this.Name, status);
        try
        {
            this.WriteDetails(report);
        }
        catch (Exception ex)
        {
            report.Note($"report failed: {ex.Message}");
        }
        long calls = this.HandlerCallCount;
        if (calls > 0)
            report.Add("handler time", $"{this.HandlerMicroseconds / 1000:0.###} ms over {calls} calls ({this.HandlerMicroseconds / calls:0.#} us avg)");
    }

    public void ResetCounters()
    {
        Interlocked.Exchange(ref this.HandlerTicks, 0);
        Interlocked.Exchange(ref this.HandlerCalls, 0);
        this.OnResetCounters();
    }

    /// <summary>Subscribe a guarded handler that's removed again on Disable. Example:
    /// <c>this.On&lt;DayStartedEventArgs&gt;(h =&gt; ev.DayStarted += h, h =&gt; ev.DayStarted -= h, this.OnDayStarted);</c></summary>
    protected void On<TArgs>(Action<EventHandler<TArgs>> add, Action<EventHandler<TArgs>> remove, Action<TArgs> handler)
    {
        EventHandler<TArgs> wrapped = (_, e) => this.Invoke(handler, e);
        add(wrapped);
        this.Unsubscribers.Add(() => remove(wrapped));
    }

    /// <summary>Register any other cleanup to run on Disable (e.g. a non-SMAPI event).</summary>
    protected void OnDisableDo(Action cleanup) => this.Unsubscribers.Add(cleanup);

    /// <summary>Log once, mark the module faulted and turn it off (which restores global state).</summary>
    protected void Fail(string where, Exception ex)
    {
        if (!this.Faulted)
        {
            this.Faulted = true;
            this.Monitor.Log($"{this.Name} failed in {where} and was turned off for this session. Other features keep running.\n{ex}", LogLevel.Error);
        }
        this.Disable();
    }

    protected abstract void OnEnable();

    protected virtual void OnDisable() { }

    protected virtual void WriteDetails(PerfReport report) { }

    protected virtual void OnResetCounters() { }

    private void Invoke<TArgs>(Action<TArgs> handler, TArgs e)
    {
        if (!this.IsEnabled)
            return;
        long start = Stopwatch.GetTimestamp();
        try
        {
            handler(e);
        }
        catch (Exception ex)
        {
            this.Fail(handler.Method.Name, ex);
        }
        finally
        {
            Interlocked.Add(ref this.HandlerTicks, Stopwatch.GetTimestamp() - start);
            Interlocked.Increment(ref this.HandlerCalls);
        }
    }
}
