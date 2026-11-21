# Auto Split-Screen Co-op

When the host loads a save that has a local co-op farmhand, this mod opens split-screen and loads that farmhand into the second screen. You don't need to press Start or pick a slot. If the save has no eligible farmhand, the game stays single player.

## Requirements

- [SMAPI](https://smapi.io) 4.5.0 or later
- Stardew Valley 1.6
- A gamepad for player 2
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
| `Enabled` | `true` | Turn the mod on or off. |
| `FarmhandName` | `""` | Farmhand to load (case-insensitive). Empty = most recently played eligible farmhand. |
| `PlayerIndex` | `"One"` | Controller for the second screen: `One`, `Two`, `Three`, `Four`. |
| `HostUsesKeyboard` | `true` | With `PlayerIndex` `One`, the host keeps keyboard/mouse and controller One goes to the second screen (vanilla behavior). If `false`, use `Two` or higher. |

**No controller connected:** split-screen still starts and the SMAPI log shows a warning. Connect the controller afterward to control the farmhand.

**Farmhand never shows up:** if the slot isn't offered within about 30 seconds, or the menu lists other farmhands but not the target, the mod logs a warning and leaves the vanilla menu open so you can pick a farmhand manually.

## Install

- From source: `dotnet build AutoSplitScreenCoop/AutoSplitScreenCoop.csproj` builds and copies the mod to `<game>/Mods/AutoSplitScreenCoop`. To skip that copy, add `<EnableModDeploy>false</EnableModDeploy>` to the csproj.
- From zip: unzip `bin/Debug/net6.0/AutoSplitScreenCoop 1.0.0.zip` into `<game>/Mods/`.

## Manual test

1. Back up the save first. Connect a controller.
2. Load a save that has a customized farmhand (created earlier through split-screen or co-op). Split-screen should open and that farmhand should load into the second screen.
3. Load a save with no farmhands, or only empty cabins. The game should stay single player.
4. Set `FarmhandName` to a farmhand that doesn't exist and load again. The game should stay single player and the log should say so.
5. Return to title, then load again. Split-screen should start once, with no duplicate screens.

## Check the log

Open `~/.config/StardewValley/ErrorLogs/SMAPI-latest.txt` and search for `Auto Split-Screen Co-op`. Typical lines:

- `Starting split-screen for farmhand '<name>' ...` and `Loading farmhand '<name>' on screen 1.` (success)
- `Not starting split-screen: ...` (skipped, with the reason; some reasons appear only at TRACE level)
- Warnings for a missing controller or a farmhand slot that never appeared.
