using System;
using GenericModConfigMenu;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace DodgeRoll;

internal sealed class ModEntry : Mod
{
    private const string GmcmId = "spacechase0.GenericModConfigMenu";
    private const float StickDeadzone = 0.2f;
    /// <summary>Max pixels moved per collision check, so a fast roll can't skip over thin obstacles.</summary>
    private const float MaxStepPx = 4f;
    private const float DustIntervalMs = 50f;

    private ModConfig Config = null!;

    // PerScreen: in split-screen each local player has an independent roll and cooldown.
    private readonly PerScreen<RollState?> ActiveRoll = new();
    private readonly PerScreen<float> CooldownMs = new();

    private sealed class RollState
    {
        public GameLocation Location = null!;
        public Vector2 Direction;
        public float DistancePx;
        public float DurationMs;
        public int StartFacing;
        public float ElapsedMs;
        public float MovedPx;
        public bool Blocked;
        public float DustMs;
        /// <summary>Remaining ms of i-frames this roll granted; 0 if the game already had the player invincible.</summary>
        public float OwnIFrameMs;
        public int OwnIFrameDuration;
        /// <summary>The player's forceTimePass before the roll, restored when it ends.</summary>
        public bool SavedForceTimePass;
        /// <summary>Whether this roll set CanMove=false, so EndRoll only undoes its own lock.</summary>
        public bool OwnsCanMove;
    }

    public override void Entry(IModHelper helper)
    {
        this.Config = helper.ReadConfig<ModConfig>();
        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.Input.ButtonsChanged += this.OnButtonsChanged;
        helper.Events.Input.ButtonPressed += this.OnButtonPressed;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.Player.Warped += this.OnWarped;
        helper.Events.GameLoop.ReturnedToTitle += (_, _) =>
        {
            // No EndRoll needed: the farmer whose CanMove/forceTimePass a roll changed is discarded with the save.
            this.ActiveRoll.ResetAllScreens();
            this.CooldownMs.ResetAllScreens();
        };
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        var gmcm = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>(GmcmId);
        if (gmcm == null)
            return;

        var m = this.ModManifest;
        gmcm.Register(m, reset: () => this.Config = new ModConfig(), save: () => this.Helper.WriteConfig(this.Config));

        gmcm.AddBoolOption(m, () => this.Config.Enabled, v => this.Config.Enabled = v,
            () => "Enabled", () => "Turn dodge rolling on or off.");
        gmcm.AddKeybindList(m, () => this.Config.RollKey, v => this.Config.RollKey = v,
            () => "Roll Keybind", () => "Button(s) that start a roll. Default: LeftStick (click the left stick).");

        gmcm.AddSectionTitle(m, () => "Movement");
        gmcm.AddNumberOption(m, () => this.Config.RollDistanceTiles, v => this.Config.RollDistanceTiles = v,
            () => "Roll Distance (tiles)", () => "How far one roll moves you. Walls and obstacles stop the roll early.",
            min: 0.5f, max: 6f, interval: 0.25f);
        gmcm.AddNumberOption(m, () => this.Config.RollDurationMs, v => this.Config.RollDurationMs = v,
            () => "Roll Duration (ms)", () => "How long a roll takes. You can't move or use tools during it.",
            min: 100, max: 1000, interval: 25);

        gmcm.AddSectionTitle(m, () => "Timing");
        gmcm.AddNumberOption(m, () => this.Config.IFrameDurationMs, v => this.Config.IFrameDurationMs = v,
            () => "I-frame Duration (ms)", () => "How long you can't take damage, from the start of the roll. Never longer than the roll itself. 0 = no i-frames.",
            min: 0, max: 1000, interval: 25);
        gmcm.AddNumberOption(m, () => this.Config.CooldownMs, v => this.Config.CooldownMs = v,
            () => "Cooldown (ms)", () => "Wait after a roll ends before you can roll again.",
            min: 0, max: 3000, interval: 50);

        gmcm.AddSectionTitle(m, () => "Stamina");
        gmcm.AddNumberOption(m, () => this.Config.StaminaCostSwings, v => this.Config.StaminaCostSwings = v,
            () => "Stamina Cost in Pickaxe Swings", () => "Each roll costs this many pickaxe swings of stamina (lower with higher Mining level). 0 = free.",
            min: 0f, max: 10f, interval: 0.5f);
        gmcm.AddBoolOption(m, () => this.Config.UseFlatStaminaCost, v => this.Config.UseFlatStaminaCost = v,
            () => "Use Flat Stamina Cost", () => "Use the flat cost below instead of pickaxe swings.");
        gmcm.AddNumberOption(m, () => this.Config.FlatStaminaCost, v => this.Config.FlatStaminaCost = v,
            () => "Flat Cost", () => "Stamina per roll when Use Flat Stamina Cost is on.",
            min: 0f, max: 50f, interval: 0.5f);
        gmcm.AddBoolOption(m, () => this.Config.AllowRollWithLowStamina, v => this.Config.AllowRollWithLowStamina = v,
            () => "Allow Roll With Low Stamina", () => "Roll even if you have less stamina than the cost (it can drop below zero, like tools). You still can't roll when exhausted.");

        gmcm.AddSectionTitle(m, () => "Effects");
        gmcm.AddBoolOption(m, () => this.Config.PlaySound, v => this.Config.PlaySound = v,
            () => "Play Sound", () => "Play a sound when you roll.");
    }

    private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
    {
        if (!this.Config.Enabled || !Context.IsPlayerFree || !this.Config.RollKey.JustPressed())
            return;

        // Suppress even if the roll is refused (cooldown, stamina) so the key never also does its vanilla action.
        this.Helper.Input.SuppressActiveKeybinds(this.Config.RollKey);
        this.TryStartRoll();
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        // Block tool use and interaction while rolling.
        if (this.ActiveRoll.Value != null && (e.Button.IsUseToolButton() || e.Button.IsActionButton()))
            this.Helper.Input.Suppress(e.Button);
    }

    private void OnWarped(object? sender, WarpedEventArgs e)
    {
        if (e.IsLocalPlayer)
            this.EndRoll();
    }

    private void TryStartRoll()
    {
        Farmer player = Game1.player;
        if (this.ActiveRoll.Value != null || this.CooldownMs.Value > 0f)
            return;
        // Context.IsPlayerFree allows festivals (eventUp), which OnUpdateTicked cancels; refuse before charging stamina.
        if (!RollMath.CanStartRoll(Context.IsWorldReady, Game1.eventUp, Game1.fadeToBlack || Game1.globalFade || Game1.isWarping))
            return;
        if (player.UsingTool || player.isRidingHorse() || player.IsSitting() || player.swimming.Value || player.currentLocation == null)
            return;

        float cost = RollMath.RollCost(player.MiningLevel, this.Config.StaminaCostSwings, this.Config.UseFlatStaminaCost, this.Config.FlatStaminaCost);
        if (!RollMath.CanAfford(player.Stamina, cost, this.Config.AllowRollWithLowStamina))
            return;

        Vector2 stick = Game1.input.GetGamePadState().ThumbSticks.Left;
        var (dx, dy) = RollMath.ResolveDirection(stick.X, stick.Y, player.movementDirections, player.FacingDirection, StickDeadzone);

        player.Stamina -= cost;
        player.Halt();
        bool savedForceTimePass = player.forceTimePass;
        bool ownsCanMove = player.CanMove;
        // With CanMove=false and no tool in use, single-player Game1.shouldTimePass() returns forceTimePass;
        // without this the roll's own pause check below would freeze it forever.
        player.forceTimePass = true;
        player.CanMove = false;

        int duration = Math.Max(1, this.Config.RollDurationMs);
        var roll = new RollState
        {
            Location = player.currentLocation,
            Direction = new Vector2(dx, dy),
            DistancePx = Math.Max(0f, this.Config.RollDistanceTiles) * Game1.tileSize,
            DurationMs = duration,
            StartFacing = RollMath.FacingFor(dx, dy),
            SavedForceTimePass = savedForceTimePass,
            OwnsCanMove = ownsCanMove,
        };

        // Don't touch invincibility the game already granted (e.g. post-hit), so the roll never extends it.
        int iframes = Math.Min(this.Config.IFrameDurationMs, duration);
        if (iframes > 0 && !player.temporarilyInvincible)
        {
            player.temporarilyInvincible = true;
            player.flashDuringThisTemporaryInvincibility = false;
            player.temporaryInvincibilityTimer = 0;
            player.currentTemporaryInvincibilityDuration = iframes;
            roll.OwnIFrameMs = iframes;
            roll.OwnIFrameDuration = iframes;
        }

        this.ActiveRoll.Value = roll;
        if (this.Config.PlaySound)
            Game1.playSound("dwop");
        Utility.addSmokePuff(roll.Location, FeetPosition(player), baseScale: 1.5f, alpha: 0.6f);
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        float dt = (float)Game1.currentGameTime.ElapsedGameTime.TotalMilliseconds;
        RollState? roll = this.ActiveRoll.Value;
        if (roll == null)
        {
            if (this.CooldownMs.Value > 0f)
                this.CooldownMs.Value = Math.Max(0f, this.CooldownMs.Value - dt);
            return;
        }

        Farmer player = Game1.player;
        if (!this.Config.Enabled || RollMath.RollInterrupted(Context.IsWorldReady, Game1.eventUp) || player.currentLocation != roll.Location)
        {
            this.EndRoll();
            return;
        }
        // Reassert before the pause check: something else may have cleared either flag mid-roll.
        player.CanMove = false;
        player.forceTimePass = true;
        // Freeze mid-roll while the game is paused or a menu is open.
        if (!Game1.shouldTimePass() || Game1.activeClickableMenu != null)
            return;

        roll.ElapsedMs += dt;
        float progress = Math.Min(1f, roll.ElapsedMs / roll.DurationMs);

        if (!roll.Blocked)
            this.MoveAlong(player, roll, RollMath.EaseOut(progress) * roll.DistancePx - roll.MovedPx);

        player.faceDirection(RollMath.SpinFacing(roll.StartFacing, progress));

        roll.DustMs += dt;
        if (roll.DustMs >= DustIntervalMs && !roll.Blocked)
        {
            roll.DustMs = 0f;
            Utility.addSmokePuff(roll.Location, FeetPosition(player), baseScale: 1f, alpha: 0.5f);
        }

        if (roll.OwnIFrameMs > 0f)
        {
            roll.OwnIFrameMs -= dt;
            if (roll.OwnIFrameMs <= 0f)
                ClearOwnIFrames(player, roll);
        }

        if (progress >= 1f)
            this.EndRoll();
    }

    /// <summary>Move up to <paramref name="distance"/> px in small collision-checked steps, sliding along walls when one axis is clear.</summary>
    private void MoveAlong(Farmer player, RollState roll, float distance)
    {
        while (distance > 0.01f)
        {
            float step = Math.Min(MaxStepPx, distance);
            Vector2 delta = roll.Direction * step;
            if (!this.TryMove(player, delta) && !this.TryMove(player, new Vector2(delta.X, 0f)) && !this.TryMove(player, new Vector2(0f, delta.Y)))
            {
                roll.Blocked = true;
                return;
            }
            roll.MovedPx += step;
            distance -= step;
        }
    }

    private bool TryMove(Farmer player, Vector2 delta)
    {
        if (delta.LengthSquared() < 0.0001f)
            return false;
        Vector2 next = player.Position + delta;
        Rectangle box = player.GetBoundingBox();
        box.X += (int)next.X - (int)player.Position.X;
        box.Y += (int)next.Y - (int)player.Position.Y;
        GameLocation location = player.currentLocation;
        // Map edges and warp strips count as walls: the roll must not leave the map or trigger a warp itself.
        if (location.isCollidingPosition(box, Game1.viewport, isFarmer: true, damagesFarmer: 0, glider: false, character: player)
            || location.IsOutOfBounds(box)
            || location.isCollidingWithWarp(box, player) != null)
            return false;
        player.Position = next;
        return true;
    }

    private void EndRoll()
    {
        RollState? roll = this.ActiveRoll.Value;
        if (roll == null)
            return;
        this.ActiveRoll.Value = null;
        this.CooldownMs.Value = Math.Max(0, this.Config.CooldownMs);

        Farmer player = Game1.player;
        ClearOwnIFrames(player, roll);
        player.forceTimePass = roll.SavedForceTimePass;
        // Events and screen fades (warps) manage CanMove themselves; don't fight them if one interrupted the roll.
        if (roll.OwnsCanMove && !Game1.eventUp && !Game1.fadeToBlack && !Game1.globalFade)
            player.CanMove = true;
        player.faceDirection(roll.StartFacing);
    }

    /// <summary>End this roll's i-frames early, but only if they're still ours (not replaced by the game's own).</summary>
    private static void ClearOwnIFrames(Farmer player, RollState roll)
    {
        if (roll.OwnIFrameDuration <= 0)
            return;
        // A negative timer means the game re-armed invincibility itself (Farmer.MovePosition resets it to 1200 ms).
        if (player.temporarilyInvincible && player.temporaryInvincibilityTimer >= 0 && !player.flashDuringThisTemporaryInvincibility && player.currentTemporaryInvincibilityDuration == roll.OwnIFrameDuration)
        {
            player.temporarilyInvincible = false;
            player.temporaryInvincibilityTimer = 0;
            player.flashDuringThisTemporaryInvincibility = true;
        }
        roll.OwnIFrameMs = 0f;
        roll.OwnIFrameDuration = 0;
    }

    private static Vector2 FeetPosition(Farmer player)
    {
        Rectangle box = player.GetBoundingBox();
        return new Vector2(box.Center.X - 16, box.Center.Y - 16);
    }
}
