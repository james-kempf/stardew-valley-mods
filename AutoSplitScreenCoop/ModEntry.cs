using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace AutoSplitScreenCoop;

internal sealed class ModEntry : Mod
{
    /// <summary>Ticks (on the new screen) to wait for the farmhand slot before giving up (~30 s at 60 FPS).</summary>
    private const int SlotTimeoutTicks = 30 * 60;

    private ModConfig Config = null!;

    // Plain fields, not PerScreen<T>: the host screen writes them and the new screen reads them.
    private bool PendingStart;
    private long? TargetFarmhandId;
    private string TargetFarmhandName = "";
    private int SlotWaitTicks;

    public override void Entry(IModHelper helper)
    {
        this.Config = helper.ReadConfig<ModConfig>();
        string oldIndex = this.Config.PlayerIndex;
        if (this.Config.Migrate())
        {
            helper.WriteConfig(this.Config);
            if (oldIndex != this.Config.PlayerIndex)
                this.Monitor.Log($"Migrated config.json to version {ModConfig.CurrentVersion}: PlayerIndex '{oldIndex}' -> '{this.Config.PlayerIndex}', HostUsesKeyboard removed (now detected automatically).", LogLevel.Info);
        }
        helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        // SaveLoaded also fires on the new screen when the farmhand finishes loading; only the host arms the trigger.
        if (Context.ScreenId != 0 || !Context.IsMainPlayer)
            return;
        if (!this.Config.Enabled)
        {
            this.Monitor.Log("Disabled in config; not starting split-screen.", LogLevel.Trace);
            return;
        }
        this.PendingStart = true;
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        this.Reset();
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (Context.ScreenId == 0)
        {
            if (this.PendingStart && Context.IsWorldReady && Game1.gameMode == Game1.playingGameMode)
            {
                this.PendingStart = false; // one attempt per save load
                this.TryStartSplitScreen();
            }
        }
        else if (this.TargetFarmhandId.HasValue)
        {
            this.TrySelectFarmhand();
        }
    }

    /// <summary>Host screen: create the second game instance if every guard passes.</summary>
    private void TryStartSplitScreen()
    {
        string? skip = this.GetSkipReason();
        if (skip != null)
        {
            this.Monitor.Log($"Not starting split-screen: {skip}.", LogLevel.Trace);
            return;
        }

        Farmer? farmhand = this.FindFarmhand();
        if (farmhand == null)
        {
            string which = string.IsNullOrWhiteSpace(this.Config.FarmhandName)
                ? "no customized, inactive farmhand in this save"
                : $"no customized, inactive farmhand named '{this.Config.FarmhandName}'";
            this.Monitor.Log($"Not starting split-screen: {which}.", LogLevel.Info);
            return;
        }

        Game1 host = GameRunner.instance.gameInstances[0];
        int hostPad = (int)host.instancePlayerOneIndex;
        var mode = Game1.options.gamepadMode;
        // Same rule the game uses for the host's input: ForceOff, or Auto while the last input was keyboard/mouse.
        // On Steam Deck the game ignores keyboard/mouse, so gamepadControls stays true and the host counts as a gamepad user.
        bool hostOnKeyboard = hostPad == PadSelection.NoPad
            || mode == Options.GamepadModes.ForceOff
            || (mode == Options.GamepadModes.Auto && !Game1.options.gamepadControls);
        string connected = string.Join(",", Enumerable.Range(0, 4).Where(IsPadConnected).Select(PadSelection.Name));

        var choice = PadSelection.Choose(this.Config.PlayerIndex, hostPad, hostOnKeyboard, IsPadConnected);
        this.Monitor.Log(
            $"Controllers: host pad={PadSelection.Name(hostPad)}, hostOnKeyboard={hostOnKeyboard}, gamepadMode={mode}, gamepadControls={Game1.options.gamepadControls}, "
            + $"connected=[{connected}], PlayerIndex config={this.Config.PlayerIndex}, player 2 pad={(choice.Pad.HasValue ? PadSelection.Name(choice.Pad.Value) : "none")}{(choice.HostGivesUpPad ? " (host releases it, keeps keyboard/mouse)" : "")}.",
            LogLevel.Info);
        foreach (string warning in choice.Warnings)
            this.Monitor.Log(warning, LogLevel.Warn);
        if (choice.Pad is not int pad)
        {
            this.Monitor.Log($"Not starting split-screen: {choice.SkipReason}.", LogLevel.Warn);
            return;
        }

        if (choice.HostGivesUpPad)
            // Same as vanilla LocalCoopJoinMenu: the keyboard host stops reading this pad so only player 2 uses it.
            host.instancePlayerOneIndex = (PlayerIndex)(-1);
        var index = (PlayerIndex)pad;

        this.TargetFarmhandId = farmhand.UniqueMultiplayerID;
        this.TargetFarmhandName = farmhand.Name;
        this.SlotWaitTicks = 0;
        this.Monitor.Log($"Starting split-screen for farmhand '{farmhand.Name}' ({farmhand.UniqueMultiplayerID}) on controller {index}.", LogLevel.Info);
        GameRunner.instance.AddGameInstance(index);
    }

    private static bool IsPadConnected(int pad) => GamePad.GetState((PlayerIndex)pad).IsConnected;

    private string? GetSkipReason()
    {
        if (!this.Config.Enabled) return "disabled in config";
        if (!Context.IsMainPlayer) return "not the main player";
        if (Game1.IsClient) return "connected as a client";
        if (GameRunner.instance.gameInstances.Count != 1) return "already in split-screen";
        if (Game1.IsServer) return "hosting a network co-op session"; // multiplayerMode 2 before split-screen = online/LAN host
        if (!Game1.game1.IsLocalCoopJoinable()) return "local co-op not joinable right now";
        return null;
    }

    /// <summary>Pick the configured farmhand, or the most recently played eligible one.</summary>
    private Farmer? FindFarmhand()
    {
        var eligible = Game1.netWorldState.Value.farmhandData.Values
            .Where(f => f != null && f.isCustomized.Value && !f.isActive());

        string name = this.Config.FarmhandName?.Trim() ?? "";
        if (name.Length > 0)
            eligible = eligible.Where(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

        return eligible
            .OrderByDescending(f => f.disconnectDay.Value)
            .ThenByDescending(f => f.millisecondsPlayed)
            .FirstOrDefault();
    }

    /// <summary>New screen: activate the target farmhand's slot once the vanilla FarmhandMenu lists it.</summary>
    private void TrySelectFarmhand()
    {
        if (GameRunner.instance.gameInstances.Count < 2)
        {
            this.Monitor.Log("Split-screen instance closed before the farmhand loaded.", LogLevel.Debug);
            this.ClearTarget();
            return;
        }

        if (++this.SlotWaitTicks > SlotTimeoutTicks)
        {
            this.Monitor.Log($"Farmhand '{this.TargetFarmhandName}' never appeared in the split-screen menu; leaving the menu open so you can pick manually.", LogLevel.Warn);
            this.ClearTarget();
            return;
        }

        if (Game1.activeClickableMenu is not FarmhandMenu menu
            || menu.client == null
            || menu.gettingFarmhands
            || menu.approvingFarmhand
            || menu.MenuSlots.Count == 0)
            return;

        long id = this.TargetFarmhandId!.Value;
        var slot = menu.MenuSlots
            .OfType<FarmhandMenu.FarmhandSlot>()
            .FirstOrDefault(s => s.Farmer?.UniqueMultiplayerID == id);

        string name = this.TargetFarmhandName;
        this.ClearTarget();
        if (slot == null)
        {
            this.Monitor.Log($"Farmhand '{name}' is not offered by the split-screen menu; leaving the menu open so you can pick manually.", LogLevel.Warn);
            return;
        }

        this.Monitor.Log($"Loading farmhand '{name}' on screen {Context.ScreenId}.", LogLevel.Info);
        slot.Activate(); // same call the vanilla menu makes after a slot click
    }

    private void ClearTarget()
    {
        this.TargetFarmhandId = null;
        this.TargetFarmhandName = "";
        this.SlotWaitTicks = 0;
    }

    private void Reset()
    {
        this.PendingStart = false;
        this.ClearTarget();
    }
}
