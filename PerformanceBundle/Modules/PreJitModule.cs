using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using PerformanceBundle.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace PerformanceBundle.Modules;

/// <summary>SMAPI turns tiered compilation off, so each method is fully JIT-compiled on first call (the first warp to a
/// location type stutters). This compiles location, NPC and rendering methods of the game assembly on a background
/// BelowNormal thread with RuntimeHelpers.PrepareMethod.
///
/// The thread only uses reflection and PrepareMethod: no Game1, content or SMAPI API. It starts on the first
/// UpdateTicked after GameLaunched (so mods that patch at GameLaunched are done), stops on ReturnedToTitle, Disable
/// and process exit, and resumes where it stopped after the next save loads.</summary>
internal sealed class PreJitModule : PerfModule
{
    private readonly object Sync = new();
    private Thread? Worker;
    private CancellationTokenSource? Cancel;
    private List<MethodBase>? Work;
    private volatile int NextIndex;
    private volatile int Prepared;
    private volatile int Failed;
    private long ElapsedTicks;
    private volatile bool Completed;
    private volatile Exception? ThreadError;
    private volatile string State = "waiting for launch";
    private bool PausedForTitle;
    private bool CompletionLogged;

    public PreJitModule(ModServices services) : base(services) { }

    public override string Name => "PreJit";

    public override bool IsConfiguredOn(ModConfig config) => config.PreJit;

    protected override void OnEnable()
    {
        var ev = this.Helper.Events.GameLoop;
        this.On<UpdateTickedEventArgs>(h => ev.UpdateTicked += h, h => ev.UpdateTicked -= h, _ => this.OnUpdateTicked());
        this.On<ReturnedToTitleEventArgs>(h => ev.ReturnedToTitle += h, h => ev.ReturnedToTitle -= h, _ =>
        {
            if (Context.ScreenId != 0)
                return;
            this.PausedForTitle = true;
            this.Stop("paused (returned to title)");
        });
        this.On<SaveLoadedEventArgs>(h => ev.SaveLoaded += h, h => ev.SaveLoaded -= h, _ =>
        {
            if (Context.ScreenId == 0)
                this.PausedForTitle = false;
        });

        EventHandler onExit = (_, _) => this.Stop("stopped (process exit)");
        AppDomain.CurrentDomain.ProcessExit += onExit;
        this.OnDisableDo(() => AppDomain.CurrentDomain.ProcessExit -= onExit);
    }

    protected override void OnDisable() => this.Stop("stopped (turned off)");

    protected override void WriteDetails(PerfReport report)
    {
        report.Add("state", this.State);
        int total = this.Work?.Count ?? 0;
        report.Add("methods compiled", $"{this.Prepared} of {total}" + (this.Failed > 0 ? $" ({this.Failed} failed)" : ""));
        report.Add("compile time (background)", $"{Interlocked.Read(ref this.ElapsedTicks) * 1000.0 / Stopwatch.Frequency:0} ms");
    }

    private void OnUpdateTicked()
    {
        if (this.ThreadError is Exception error)
        {
            this.ThreadError = null;
            this.Fail("background thread", error);
            return;
        }
        if (this.Completed)
        {
            if (!this.CompletionLogged)
            {
                this.CompletionLogged = true;
                this.Monitor.Log($"PreJit compiled {this.Prepared} methods ({this.Failed} failed) in {Interlocked.Read(ref this.ElapsedTicks) * 1000.0 / Stopwatch.Frequency:0} ms of background time.", LogLevel.Trace);
            }
            return;
        }
        if (this.PausedForTitle || !this.S.GameLaunched || Context.ScreenId != 0)
            return;

        lock (this.Sync)
        {
            // A stopped thread may still be finishing its current method; wait for it before starting another.
            if (this.Worker?.IsAlive == true)
                return;

            Assembly gameAssembly = typeof(StardewValley.Game1).Assembly; // typeof doesn't run Game1's static constructor
            var cancel = new CancellationTokenSource();
            this.Cancel = cancel;
            this.State = "running";
            this.Worker = new Thread(() => this.Run(gameAssembly, cancel.Token))
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "PerformanceBundle.PreJit"
            };
            this.Worker.Start();
        }
    }

    private void Stop(string state)
    {
        lock (this.Sync)
        {
            if (this.Cancel == null)
                return;
            this.Cancel.Cancel();
            this.Cancel = null;
            if (!this.Completed)
                this.State = state;
        }
    }

    /// <summary>Background thread. Reflection and PrepareMethod only.</summary>
    private void Run(Assembly gameAssembly, CancellationToken token)
    {
        long start = Stopwatch.GetTimestamp();
        bool finished = false;
        try
        {
            this.Work ??= PreJitScope.Collect(gameAssembly, PreJitScope.DefaultNamespaces, PreJitScope.DefaultTypes);
            List<MethodBase> work = this.Work;
            while (this.NextIndex < work.Count)
            {
                if (token.IsCancellationRequested)
                    return;
                try
                {
                    RuntimeHelpers.PrepareMethod(work[this.NextIndex].MethodHandle);
                    this.Prepared++;
                }
                catch (Exception)
                {
                    this.Failed++;
                }
                this.NextIndex++;
            }

            finished = true;
        }
        catch (Exception ex)
        {
            // Reported to the main thread, which turns the module off.
            this.State = "failed";
            this.ThreadError = ex;
        }
        finally
        {
            Interlocked.Add(ref this.ElapsedTicks, Stopwatch.GetTimestamp() - start);
            if (finished)
            {
                this.State = "done";
                this.Completed = true; // set last: the main thread logs the totals once it sees this
            }
        }
    }
}
