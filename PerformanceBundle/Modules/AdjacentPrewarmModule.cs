using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using PerformanceBundle.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Locations;

namespace PerformanceBundle.Modules;

/// <summary>After a warp, loads the tilesheet textures of the new location's warp and door targets into the game's
/// shared content manager (<see cref="Game1.content"/>, the one the map display device loads tilesheets from), a few
/// per tick on the main thread within a time budget. Maps themselves aren't prewarmed: the game loads every location's
/// map at save load (research.md 3.3), so only tilesheet textures that aren't cached yet (e.g. after an invalidation)
/// are left, and the expected benefit is small. Assets targeted by Content Patcher patches that update on location or
/// time change, and any asset invalidated this session, are never prewarmed. It only calls Load, so other mods' edits
/// apply exactly as they would on the game's own load. Targets are resolved only from locations that already exist
/// (never via getLocationFromName, which creates mine/volcano levels).</summary>
internal sealed class AdjacentPrewarmModule : PerfModule
{
    private readonly PrewarmTracker Tracker = new();
    private readonly PerScreen<PrewarmQueue> Queues;

    /// <summary>Every screen's queue, for AssetsInvalidated, which may be raised off the main thread where PerScreen
    /// isn't safe to enumerate. Queues of closed screens stay listed but are empty.</summary>
    private readonly List<PrewarmQueue> AllQueues = new();
    private Func<string, bool>? IsLoaded;
    private bool IsLoadedResolved;
    private bool ScanStarted;
    private volatile string? Unavailable;

    public AdjacentPrewarmModule(ModServices services) : base(services) =>
        this.Queues = new PerScreen<PrewarmQueue>(() =>
        {
            var queue = new PrewarmQueue();
            lock (this.AllQueues)
                this.AllQueues.Add(queue);
            return queue;
        });

    public override string Name => "AdjacentPrewarm";

    public override bool IsConfiguredOn(ModConfig config) => config.AdjacentPrewarm;

    protected override void OnEnable()
    {
        var player = this.Helper.Events.Player;
        var loop = this.Helper.Events.GameLoop;
        var content = this.Helper.Events.Content;
        this.On<WarpedEventArgs>(h => player.Warped += h, h => player.Warped -= h, this.OnWarped);
        this.On<UpdateTickedEventArgs>(h => loop.UpdateTicked += h, h => loop.UpdateTicked -= h, _ => this.Drain());
        this.On<ReturnedToTitleEventArgs>(h => loop.ReturnedToTitle += h, h => loop.ReturnedToTitle -= h, _ => this.Tracker.Clear(this.Queues.Value));
        this.On<AssetsInvalidatedEventArgs>(h => content.AssetsInvalidated += h, h => content.AssetsInvalidated -= h,
            e =>
            {
                List<PrewarmQueue> queues;
                lock (this.AllQueues)
                    queues = this.AllQueues.ToList();
                this.Tracker.OnInvalidated(e.NamesWithoutLocale.Select(n => n.BaseName), queues);
            });
    }

    protected override void OnDisable()
    {
        lock (this.AllQueues)
            foreach (PrewarmQueue queue in this.AllQueues)
                this.Tracker.Clear(queue);
    }

    protected override void WriteDetails(PerfReport report)
    {
        var t = this.Tracker;
        if (this.Unavailable != null)
            report.Note($"unavailable: {this.Unavailable}");
        report.Note("Pre-loads tilesheet textures of neighboring locations; maps are already loaded at save load, so gains are usually small.");
        report.Add("exclusion list", t.IsReady ? $"{t.ConditionalTargetCount} conditional CP targets, {t.InvalidatedCount} invalidated assets" : "not ready");
        report.Add("prewarmed / used / unused", $"{t.Prewarmed} / {t.Used} / {t.Unused}");
        report.Add("invalidated before use", t.InvalidatedBeforeUse);
        report.Add("already cached / excluded / failed", $"{t.AlreadyLoaded} / {t.Excluded} / {t.Failed}");
    }

    protected override void OnResetCounters() => this.Tracker.ResetCounters();

    private void OnWarped(WarpedEventArgs e)
    {
        if (!e.IsLocalPlayer)
            return;
        this.StartScan();
        PrewarmQueue queue = this.Queues.Value;
        this.Tracker.OnWarped(queue, TileSheetAssets(e.NewLocation));
        if (!this.Tracker.IsReady || e.NewLocation == null)
            return;

        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Warp warp in e.NewLocation.warps)
            if (!string.IsNullOrEmpty(warp.TargetName))
                targets.Add(warp.TargetName);
        foreach (string door in e.NewLocation.doors.Values)
            if (!string.IsNullOrEmpty(door))
                targets.Add(door);
        targets.Remove(e.NewLocation.NameOrUniqueName);
        // Mine and volcano levels are created on lookup; never touch them.
        targets.RemoveWhere(name => MineShaft.IsGeneratedLevel(name, out _) || VolcanoDungeon.IsGeneratedLevel(name, out _));
        if (targets.Count == 0)
            return;

        foreach (GameLocation target in ExistingLocations(targets))
            this.Tracker.Enqueue(queue, TileSheetAssets(target));
    }

    /// <summary>Already-existing locations (Game1.locations and building interiors) whose name is in
    /// <paramref name="names"/>. Unlike Game1.getLocationFromName this can never create a location.</summary>
    private static IEnumerable<GameLocation> ExistingLocations(HashSet<string> names)
    {
        var found = new List<GameLocation>();
        foreach (GameLocation location in Game1.locations)
        {
            if (names.Contains(location.NameOrUniqueName))
                found.Add(location);
            foreach (Building building in location.buildings)
            {
                GameLocation? indoors = building.GetIndoors();
                if (indoors != null && names.Contains(indoors.NameOrUniqueName))
                    found.Add(indoors);
            }
        }
        return found;
    }

    /// <summary>Main thread, inside this screen's update, so <see cref="Game1.content"/> is the game's own manager.</summary>
    private void Drain()
    {
        PrewarmQueue queue = this.Queues.Value;
        if (queue.Count == 0 || !Context.IsWorldReady || Game1.eventUp || Game1.activeClickableMenu != null
            || Game1.fadeToBlack || Game1.globalFade || Game1.isWarping || Game1.CurrentEvent != null)
            return;
        if (!this.IsLoadedResolved)
        {
            this.IsLoadedResolved = true;
            this.IsLoaded = this.CreateIsLoaded();
        }
        if (this.IsLoaded == null)
            return;

        long budgetTicks = Math.Max(1, this.S.GetConfig().AdjacentPrewarmBudgetMs) * Stopwatch.Frequency / 1000;
        long start = Stopwatch.GetTimestamp();
        while (Stopwatch.GetTimestamp() - start < budgetTicks && this.Tracker.TryDequeue(queue, out string name))
        {
            if (this.IsLoaded!(name))
            {
                this.Tracker.MarkAlreadyLoaded();
                continue;
            }
            try
            {
                Game1.content.Load<Texture2D>(name);
                this.Tracker.MarkPrewarmed(queue, name);
            }
            catch (Exception ex)
            {
                this.Tracker.MarkFailed(name);
                this.Monitor.Log($"{this.Name}: couldn't pre-load {name}; it won't be tried again this session. {ex.Message}", LogLevel.Trace);
            }
        }
    }

    /// <summary>Build the conditional-target exclusion list once per session, after all mods are loaded. Main thread
    /// gathers SMAPI data; the worker only reads CP pack JSON files (no game or content calls).</summary>
    private void StartScan()
    {
        if (this.ScanStarted)
            return;
        this.ScanStarted = true;
        string modsPath = EnvironmentScan.FindModsRoot(this.Helper.DirectoryPath, Constants.GamePath);
        var loadedIds = new HashSet<string>(this.Helper.ModRegistry.GetAll().Select(m => m.Manifest.UniqueID), StringComparer.OrdinalIgnoreCase);
        Task.Run(() =>
        {
            try
            {
                this.Tracker.SetConditionalTargets(CpConditionalTargets.ScanPacks(modsPath, loadedIds));
            }
            catch (Exception ex)
            {
                // Without the exclusion list nothing is ever queued (IsReady stays false).
                this.Unavailable = $"Content Patcher scan failed: {ex.Message}";
            }
        });
    }

    /// <summary>Tilesheet asset names of a location's already-loaded map. Reads the map field, not the Map property,
    /// because the property can load the map.</summary>
    private static IEnumerable<string> TileSheetAssets(GameLocation? location)
    {
        if (location?.map == null)
            return Array.Empty<string>();
        return location.map.TileSheets.Select(t => t.ImageSource).Where(s => !string.IsNullOrEmpty(s)).ToList();
    }

    /// <summary>SMAPI's content manager (the runtime type of Game1.content) has a public IsLoaded(IAssetName); it isn't
    /// part of the mod API. Without it the module would load blindly, so it stays inactive with one warning.</summary>
    private Func<string, bool>? CreateIsLoaded()
    {
        MethodInfo? method = Game1.content?.GetType().GetMethod("IsLoaded", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(IAssetName) }, null);
        if (method == null || method.ReturnType != typeof(bool))
        {
            this.Unavailable = "SMAPI's content cache check (IsLoaded) wasn't found in this SMAPI version";
            this.Monitor.Log($"{this.Name} is inactive: {this.Unavailable}.", LogLevel.Warn);
            return null;
        }
        return name => (bool)method.Invoke(Game1.content, new object[] { this.Helper.GameContent.ParseAssetName(name) })!;
    }
}
