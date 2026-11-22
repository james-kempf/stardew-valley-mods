# Auto Split-Screen Co-op

When the host loads a save that has a local co-op farmhand, this mod opens split-screen and loads that farmhand into the second screen. You don't need to press Start or pick a slot. If the save has no eligible farmhand, the game stays single player.

## Requirements

- [SMAPI](https://smapi.io) 4.5.0 or later
- Stardew Valley 1.6
- A gamepad for player 2 that the host isn't using. On Steam Deck (or any host playing on a gamepad), connect a second controller.
- A save that already has a customized farmhand (create one first through vanilla split-screen or co-op)

Building from source also needs the .NET SDK 8 or later (the project targets `net6.0`).

## How it works

1. After a save loads on the main screen, the mod calls the game's own `GameRunner.AddGameInstance` (the same call the vanilla "press Start to join" menu makes).
2. The new screen opens the vanilla farmhand selection menu. When the target farmhand appears, the mod calls that slot's `Activate()` (the same call a slot click makes).

It does not simulate input and does not use Harmony.

**Eligible farmhand:** a customized farmhand (not an empty cabin slot) who is not already online. If several farmhands are eligible, the mod picks the one who played most recently.

**The mod does nothing when** `Enabled` is false, split-screen is already running, you are a client/farmhand, the save was loaded as an online/LAN co-op host, or no farmhand is eligible.

## Config (`config.json`, created on first launch)

| Option | Default | Meaning |
|---|---|---|
| `ConfigVersion` | `1` | Config schema version. Managed by the mod; don't edit. |
| `Enabled` | `true` | Turn the mod on or off. |
| `FarmhandName` | `""` | Farmhand to load (case-insensitive). Empty = most recently played eligible farmhand. |
| `PlayerIndex` | `"Auto"` | Controller for the second screen: `Auto`, `One`, `Two`, `Three`, `Four`. |

**How `Auto` picks player 2's controller:**

1. The first connected controller from Two to Four that the host isn't using (the same range vanilla "press Start to join" scans).
2. Otherwise controller One, but only if the host plays with keyboard/mouse. The host then stops reading controller One and keeps keyboard/mouse, which is what the vanilla join menu does.
3. Otherwise split-screen doesn't start and the SMAPI log shows a warning.

The mod reads the host's input from the game's own settings when the save loads. The host counts as a keyboard/mouse player when the game's Gamepad Mode is "Force Off", or when it is "Auto" and the last input was keyboard/mouse. On Steam Deck the game ignores keyboard/mouse, so the host counts as a gamepad player and keeps the built-in controls (controller One).

**Explicit `PlayerIndex`:** the mod never gives the host's controller to player 2. If you set it to the controller the host is playing on, the mod logs a warning and uses `Auto`. Any other explicit controller is used even if it isn't connected yet (with a warning), so you can plug it in afterward.

**Upgrading from 1.0.x:** `HostUsesKeyboard` was removed. On first launch the mod rewrites `config.json`: the old default (`PlayerIndex` `One` with `HostUsesKeyboard` `true`) becomes `Auto`, any other `PlayerIndex` is kept, and `HostUsesKeyboard` is dropped.

**Farmhand never shows up:** if the slot isn't offered within about 30 seconds, or the menu lists other farmhands but not the target, the mod logs a warning and leaves the vanilla menu open so you can pick a farmhand manually.

## Install

- From source: `dotnet build AutoSplitScreenCoop/AutoSplitScreenCoop.csproj` builds and copies the mod to `<game>/Mods/AutoSplitScreenCoop`. To skip that copy, add `<EnableModDeploy>false</EnableModDeploy>` to the csproj.
- From zip: unzip `bin/Debug/net6.0/AutoSplitScreenCoop 1.1.0.zip` into `<game>/Mods/`.

## Manual test

1. Back up the save first. Connect a controller for player 2.
2. Load a save that has a customized farmhand (created earlier through split-screen or co-op). Split-screen should open and that farmhand should load into the second screen.
3. Load a save with no farmhands, or only empty cabins. The game should stay single player.
4. Set `FarmhandName` to a farmhand that doesn't exist and load again. The game should stay single player and the log should say so.
5. Return to title, then load again. Split-screen should start once, with no duplicate screens.
6. Steam Deck (or host on a gamepad) with no second controller: the host keeps control, split-screen doesn't start, and the log warns that there is no free controller.
7. Steam Deck with a second controller connected: the host keeps the built-in controls and player 2 uses the second controller.

## Check the log

Open `~/.config/StardewValley/ErrorLogs/SMAPI-latest.txt` and search for `Auto Split-Screen Co-op`. Typical lines:

- `Controllers: host pad=..., hostOnKeyboard=..., gamepadMode=..., gamepadControls=..., connected=[...], ..., player 2 pad=...` (which controller each screen got and why)
- `Starting split-screen for farmhand '<name>' ...` and `Loading farmhand '<name>' on screen 1.` (success)
- `Not starting split-screen: ...` (skipped, with the reason; some reasons appear only at TRACE level)
- Warnings for no free controller, a `PlayerIndex` that is the host's controller, an invalid `PlayerIndex`, or a farmhand slot that never appeared.

## Changelog

- **1.1.0:** Fixed the host losing its controller on Steam Deck and other gamepad hosts (player 2 was given controller One). New `PlayerIndex` default `Auto` picks a free controller. `HostUsesKeyboard` removed (detected automatically); old configs are migrated.
- **1.0.0:** Initial release.

## Credits

Cover text (`nexus/cover.*`) uses the 'Stardew Valley' font by RRDome (https://fontstruct.com/fontstructions/show/1254619).
