using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GenericModConfigMenu;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace DeadlyLightning;

/// <summary>Sent by the host to the victim's client. Plain properties so SMAPI can serialize it.</summary>
internal sealed class StrikeOrder
{
    public string StrikeId { get; set; } = "";
    public long VictimId { get; set; }
    public string LocationName { get; set; } = "";
    public bool RequireOutdoors { get; set; }
    public bool RequireStorm { get; set; }
    public bool RequireRing { get; set; }
}

/// <summary>Sent by the victim's client after the kill, so other players see the bolt.</summary>
internal sealed class StrikeVisual
{
    public string StrikeId { get; set; } = "";
    public long VictimId { get; set; }
    public string LocationName { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
}

internal sealed class ModEntry : Mod
{
    private const string GmcmId = "spacechase0.GenericModConfigMenu";
    private const string OrderType = "StrikeOrder";
    private const string VisualType = "StrikeVisual";

    private ModConfig Config = null!;

    // Static, not PerScreen: every split-screen screen must see the same messages, whichever screen context SMAPI
    // raised ModMessageReceived on and whether or not SMAPI also delivers to other local screens. StrikeInbox ignores
    // repeated StrikeIds, so a message delivered to several screens is stored once.
    private static readonly StrikeInbox<StrikeOrder> Orders = new();
    private static readonly StrikeInbox<StrikeVisual> Visuals = new();
    /// <summary>Monotonic clock shared by all screens (Game1 tick counters are per screen in split-screen).</summary>
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    /// <summary>Visuals this screen already showed. Each split-screen screen is its own client with its own copy of
    /// every GameLocation and its own Game1 statics, so bolt, flash and sound are all applied once per screen.</summary>
    private readonly PerScreen<HashSet<string>> SeenVisuals = new(() => new HashSet<string>());
    /// <summary>Each active screen's player id, refreshed every tick, so the host can tell local split-screen players
    /// apart from remote ones even if SMAPI doesn't list them as split-screen peers.</summary>
    private readonly PerScreen<long> LocalPlayerId = new();

    private static long NowMs => Clock.ElapsedMilliseconds;

    public override void Entry(IModHelper helper)
    {
        this.Config = helper.ReadConfig<ModConfig>();
        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.TimeChanged += this.OnTimeChanged;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.Multiplayer.ModMessageReceived += this.OnModMessageReceived;
        helper.Events.GameLoop.ReturnedToTitle += (_, _) =>
        {
            // The inboxes are shared by every screen, so only the main screen clears them (including known ids).
            if (Context.ScreenId == 0)
            {
                Orders.Clear();
                Visuals.Clear();
            }
            this.SeenVisuals.ResetAllScreens();
            this.LocalPlayerId.ResetAllScreens();
        };
        helper.ConsoleCommands.Add("deadlylightning_strike",
            "Host only. Strikes a player now, ignoring chance, rings, storm and indoors (safety checks still apply).\n"
            + "Usage: deadlylightning_strike [player name or id]. Default: you.",
            this.OnStrikeCommand);
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        var gmcm = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>(GmcmId);
        if (gmcm == null)
            return;

        var m = this.ModManifest;
        gmcm.Register(m, reset: () => this.Config = new ModConfig(), save: () => this.Helper.WriteConfig(this.Config));
        gmcm.AddBoolOption(m, () => this.Config.Enabled, v => this.Config.Enabled = v,
            () => "Enabled", () => "Turn lightning strikes on or off. In multiplayer only the host's settings are used.");
        gmcm.AddNumberOption(m, () => (float)this.Config.StrikeChancePerCheck, v => this.Config.StrikeChancePerCheck = Math.Round(Math.Clamp((double)v, 0, 1), 6),
            () => "Strike Chance per Check", () => "Chance per player each 10 in-game minutes (107 checks a day). 0.0005 is about 5.2% over a full storm day outdoors.",
            min: 0f, max: 0.05f, interval: 0.0001f, formatValue: v => $"{v * 100:0.##}%");
        gmcm.AddBoolOption(m, () => this.Config.ScaleWithRingCount, v => this.Config.ScaleWithRingCount = v,
            () => "Scale With Ring Count", () => "Two rings double the chance. A combined ring counts as one.");
        gmcm.AddBoolOption(m, () => this.Config.IndoorsSafe, v => this.Config.IndoorsSafe = v,
            () => "Indoors Safe", () => "Players inside buildings can't be struck.");
    }

    // ---------- Host: roll ----------

    private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
    {
        // Context.IsMainPlayer is only true on the host's first screen, so exactly one roll happens per check.
        if (!Context.IsMainPlayer || !this.Config.Enabled || Game1.newDay || !StrikeMath.IsRollTime(e.OldTime, e.NewTime))
            return;

        double baseChance = Math.Clamp(this.Config.StrikeChancePerCheck, 0, 1);
        var candidates = new List<(long Id, double Chance)>();
        foreach (Farmer f in Game1.getOnlineFarmers())
        {
            GameLocation? loc = f.currentLocation;
            int rings = StrikeMath.RingCount(f.leftRing.Value != null, f.rightRing.Value != null);
            // f.health is stale for remote farmers (the victim's client owns it); the victim re-checks it.
            bool eligible = StrikeMath.HostEligible(
                hasLocation: loc != null,
                isDedicated: f.IsDedicatedPlayer,
                hasMod: this.PeerHasCompatibleMod(f),
                ringCount: rings,
                inStorm: loc?.IsLightningHere() == true,
                outdoors: loc?.IsOutdoors == true,
                indoorsSafe: this.Config.IndoorsSafe,
                locationHasEvent: loc?.currentEvent != null,
                healthPositive: f.health > 0);
            if (eligible)
                candidates.Add((f.UniqueMultiplayerID, StrikeMath.ChanceFor(rings, baseChance, this.Config.ScaleWithRingCount)));
        }

        long? victimId = StrikeMath.PickVictim(candidates, Game1.random.NextDouble);
        if (victimId == null)
            return;
        Farmer victim = Game1.getOnlineFarmers().First(f => f.UniqueMultiplayerID == victimId.Value);
        this.Monitor.Log($"Lightning picked {victim.Name} at {e.NewTime} ({candidates.Count} eligible).", LogLevel.Trace);
        this.DispatchOrder(victim, requireConditions: true);
    }

    /// <summary>Local players (host, split-screen) always have the mod. A remote farmhand needs it with a
    /// major.minor no older than the host's, or it's never a candidate.</summary>
    private bool PeerHasCompatibleMod(Farmer f)
    {
        if (this.IsLocalFarmer(f))
            return true;
        IMultiplayerPeerMod? mod = this.Helper.Multiplayer.GetConnectedPlayer(f.UniqueMultiplayerID)?.GetMod(this.ModManifest.UniqueID);
        if (mod == null)
            return false;
        ISemanticVersion host = this.ModManifest.Version;
        if (StrikeMath.VersionCompatible(mod.Version.MajorVersion, mod.Version.MinorVersion, host.MajorVersion, host.MinorVersion))
            return true;
        this.Monitor.Log($"Skipping {f.Name}: their {this.ModManifest.Name} {mod.Version} is older than the host's {host}.", LogLevel.Trace);
        return false;
    }

    /// <summary>The host itself (GetConnectedPlayer returns null for it) or a split-screen player in this process.</summary>
    private bool IsLocalFarmer(Farmer f)
    {
        long id = f.UniqueMultiplayerID;
        if (id == Game1.player.UniqueMultiplayerID || this.LocalPlayerId.GetActiveValues().Any(p => p.Value == id))
            return true;
        return this.Helper.Multiplayer.GetConnectedPlayer(f.UniqueMultiplayerID)?.IsSplitScreen == true;
    }

    /// <summary>Send the order to the victim's own client. The host never writes a remote farmer's health: it's
    /// client-local and only the victim's screen can apply the death. If the victim disconnects before handling
    /// the order, nothing happens and there's no reroll.</summary>
    private void DispatchOrder(Farmer victim, bool requireConditions)
    {
        var order = new StrikeOrder
        {
            StrikeId = Guid.NewGuid().ToString("N"),
            VictimId = victim.UniqueMultiplayerID,
            LocationName = victim.currentLocation?.NameOrUniqueName ?? "",
            RequireOutdoors = requireConditions && this.Config.IndoorsSafe,
            RequireStorm = requireConditions,
            RequireRing = requireConditions
        };
        if (this.IsLocalFarmer(victim))
            Orders.TryAdd(order.StrikeId, order, NowMs);
        else
            this.Helper.Multiplayer.SendMessage(order, OrderType, new[] { this.ModManifest.UniqueID }, new[] { victim.UniqueMultiplayerID });
    }

    // ---------- Messages ----------

    private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
    {
        if (e.FromModID != this.ModManifest.UniqueID)
            return;
        if (e.Type == OrderType)
        {
            // Only the host rolls, so ignore orders from anyone else.
            if (e.FromPlayerID != Game1.MasterPlayer?.UniqueMultiplayerID)
                return;
            StrikeOrder order = e.ReadAs<StrikeOrder>();
            Orders.TryAdd(order.StrikeId, order, NowMs);
        }
        else if (e.Type == VisualType)
        {
            StrikeVisual visual = e.ReadAs<StrikeVisual>();
            Visuals.TryAdd(visual.StrikeId, visual, NowMs);
        }
    }

    // ---------- Every screen: execute and render ----------

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;
        this.LocalPlayerId.Value = Game1.player.UniqueMultiplayerID;
        long now = NowMs;
        Orders.Purge(now, StrikeMath.OrderExpiryMs);
        Visuals.Purge(now, StrikeMath.VisualExpiryMs);
        this.ProcessOrders(now);
        this.RenderVisuals(now);
    }

    private void ProcessOrders(long now)
    {
        Farmer player = Game1.player;
        foreach (var (id, order, age) in Orders.Snapshot(now))
        {
            // Only the screen whose own player is the victim acts; other screens leave the order alone.
            if (order.VictimId != player.UniqueMultiplayerID)
                continue;

            VictimState state = ReadVictimState(player, order.LocationName);
            var rules = new OrderRules(order.RequireOutdoors, order.RequireStorm, order.RequireRing);
            string? hard = StrikeMath.HardBlockReason(state, rules);
            string? transient = StrikeMath.TransientBlockReason(state);
            switch (StrikeMath.Decide(hard, transient, age))
            {
                case OrderAction.Wait:
                    continue;
                case OrderAction.Drop:
                    Orders.Remove(id);
                    this.Monitor.Log($"Lightning missed {player.Name}: {hard ?? transient + " for too long"}.", LogLevel.Trace);
                    continue;
                case OrderAction.Execute:
                    Orders.Remove(id);
                    this.Strike(player, id);
                    continue;
            }
        }
    }

    private static VictimState ReadVictimState(Farmer player, string locationName)
    {
        GameLocation? loc = player.currentLocation;
        return new VictimState(
            EventUp: Game1.eventUp,
            Festival: Game1.isFestival(),
            KillScreen: Game1.killScreen,
            HealthPositive: player.health > 0,
            PassedOut: player.passedOut,
            PassingOut: player.FarmerSprite?.isPassingOut() == true,
            Warping: Game1.isWarping,
            Fading: Game1.globalFade || Game1.fadeToBlack,
            NewDay: Game1.newDay,
            LocationMatches: loc != null && loc.NameOrUniqueName == locationName,
            Outdoors: loc?.IsOutdoors == true,
            Storm: loc?.IsLightningHere() == true,
            RingCount: StrikeMath.RingCount(player.leftRing.Value != null, player.rightRing.Value != null),
            CanMove: player.CanMove,
            UsingTool: player.UsingTool,
            Eating: player.isEating,
            MenuOpen: Game1.activeClickableMenu != null,
            TimePasses: Game1.shouldTimePass());
    }

    /// <summary>Kill the local player, then tell everyone to show the bolt. Setting health to 0 hands the death to
    /// vanilla Farmer.Update (screen glow, killScreen, chat message, revive at the clinic, item loss). It skips
    /// takeDamage on purpose, so defense and the Ring of Yoba don't apply.</summary>
    private void Strike(Farmer player, string strikeId)
    {
        player.health = 0;
        Vector2 pos = player.getStandingPosition();
        var visual = new StrikeVisual
        {
            StrikeId = strikeId,
            VictimId = player.UniqueMultiplayerID,
            LocationName = player.currentLocation.NameOrUniqueName,
            X = pos.X,
            Y = pos.Y
        };
        // The inbox reaches local split-screen screens; the message reaches remote players. Duplicates are ignored.
        Visuals.TryAdd(visual.StrikeId, visual, NowMs);
        this.Helper.Multiplayer.SendMessage(visual, VisualType, new[] { this.ModManifest.UniqueID });
        this.Monitor.Log($"Lightning struck {player.Name} at {visual.LocationName}.", LogLevel.Debug);
    }

    private void RenderVisuals(long now)
    {
        HashSet<string> seen = this.SeenVisuals.Value;
        GameLocation? here = Game1.currentLocation;
        foreach (var (id, visual, _) in Visuals.Snapshot(now))
        {
            StrikeView view = StrikeMath.ViewFor(
                hasLocation: here != null,
                sameLocation: here?.NameOrUniqueName == visual.LocationName,
                outdoors: here?.IsOutdoors == true,
                storm: here?.IsLightningHere() == true);
            // No location yet (e.g. mid-load): leave it unseen so it can still show once there is one.
            if (view == StrikeView.Later || !seen.Add(id))
                continue;

            switch (view)
            {
                case StrikeView.Full:
                    Utility.drawLightningBolt(new Vector2(visual.X, visual.Y), here!);
                    Game1.flashAlpha = 1f;
                    Game1.playSound("thunder");
                    break;
                case StrikeView.Distant:
                    Game1.flashAlpha = 0.5f;
                    DelayedAction.playSoundAfterDelay("thunder_small", Game1.random.Next(500, 1500));
                    break;
            }
        }
    }

    // ---------- Console ----------

    private void OnStrikeCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady)
        {
            this.Monitor.Log("Load a save first.", LogLevel.Error);
            return;
        }
        if (!Context.IsMainPlayer)
        {
            this.Monitor.Log("Only the host can use this command.", LogLevel.Error);
            return;
        }

        Farmer? target;
        if (args.Length == 0)
            target = Game1.player;
        else
        {
            string query = string.Join(" ", args);
            var online = Game1.getOnlineFarmers().ToList();
            target = long.TryParse(query, out long id)
                ? online.FirstOrDefault(f => f.UniqueMultiplayerID == id)
                : online.FirstOrDefault(f => string.Equals(f.Name, query, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                this.Monitor.Log($"No online player matches '{query}'. Online: {string.Join(", ", online.Select(f => $"{f.Name} ({f.UniqueMultiplayerID})"))}.", LogLevel.Error);
                return;
            }
        }

        if (target.IsDedicatedPlayer || !this.PeerHasCompatibleMod(target))
        {
            this.Monitor.Log($"{target.Name} can't be struck: they need {this.ModManifest.Name} {this.ModManifest.Version.MajorVersion}.{this.ModManifest.Version.MinorVersion} or newer installed.", LogLevel.Error);
            return;
        }

        this.DispatchOrder(target, requireConditions: false);
        this.Monitor.Log($"Strike sent to {target.Name}. It still misses if they're in an event, warping, passed out, or stay busy (menu, tool, eating) for 3 seconds.", LogLevel.Info);
    }
}
