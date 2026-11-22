# Dodge Roll

A Dark Souls style dodge roll for Stardew Valley. Click the left stick (L3) to roll a short distance, with a brief window where you can't take damage. Each roll costs stamina.

## Requirements

- [SMAPI](https://smapi.io) 4.5.0 or later
- Stardew Valley 1.6
- Optional: [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098), to change settings in game

## Features

- **Roll on a button.** Default: `LeftStick` (click the left stick). Any SMAPI keybind works, including keyboard keys and combos such as `LeftShift + Space`. The button is suppressed, so it doesn't also trigger its normal game action.
- **Direction.** You roll the way you're pushing the left stick or movement keys. When you're standing still, you roll the way you're facing.
- **Smooth, collision-aware movement.** The roll eases out over its distance. Walls, obstacles, map edges, and warps (doorways and map exits) stop it, and you slide along walls when only one direction is blocked. A roll never carries you through a warp; walk into it as usual. You can't move, use tools, or interact while rolling.
- **Invincibility frames.** You can't be damaged for a short window at the start of the roll. If the game already made you invincible (for example, right after you take a hit), the roll leaves that alone and doesn't extend it.
- **Stamina cost.** By default a roll costs 2 pickaxe swings, using the game's own formula: one swing costs `2 - 0.1 x Mining level` stamina (minimum 0.1), so a roll costs 4 at Mining 0 and 2 at Mining 10. In Stardew Valley 1.6 the pickaxe's upgrade level doesn't change its stamina cost, so the cost is the same with or without a pickaxe. You can switch to a flat cost instead.
- **Feedback.** You spin through each facing direction, kick up dust, and hear a sound.
- **Split-screen and multiplayer safe.** Each local player rolls independently, and the mod only affects your own farmer, so it works for farmhands as well as the host.

You can't roll while you're riding a horse, sitting, swimming, using a tool, in a menu, cutscene, or festival, during a screen transition (warp), or during the cooldown after a previous roll.

## Settings

Change these in game with [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) (optional), or edit `config.json`, which is created the first time you run the game with the mod.

| Setting | Default | Description |
|---|---|---|
| Enabled | `true` | Turns dodge rolling on or off. |
| Roll Keybind | `LeftStick` | The button or buttons that start a roll. |
| Roll Distance (tiles) | `2.5` | How far one roll moves you. |
| Roll Duration (ms) | `350` | How long a roll takes. |
| I-frame Duration (ms) | `250` | How long you can't take damage, from the start of the roll. It never lasts longer than the roll. `0` turns it off. |
| Cooldown (ms) | `300` | How long you wait after a roll ends before you can roll again. |
| Stamina Cost in Pickaxe Swings | `2` | How many pickaxe swings of stamina a roll costs. |
| Use Flat Stamina Cost | `false` | Uses Flat Cost instead of pickaxe swings. |
| Flat Cost | `4` | The stamina per roll when Use Flat Stamina Cost is on. |
| Allow Roll With Low Stamina | `false` | Lets you roll with less stamina than the cost, which can take you below zero like tools do. You still can't roll while you're exhausted (0 stamina or less). |
| Play Sound | `true` | Plays a sound when you roll. |

In `config.json`, these settings are named `Enabled`, `RollKey`, `RollDistanceTiles`, `RollDurationMs`, `IFrameDurationMs`, `CooldownMs`, `StaminaCostSwings`, `UseFlatStaminaCost`, `FlatStaminaCost`, `AllowRollWithLowStamina`, and `PlaySound`.

## Install

1. Install [SMAPI](https://smapi.io).
2. Unzip the mod into your `Mods` folder.
3. Launch the game through SMAPI.

## Build from source

From this folder, run `dotnet build`. Pathoschild.Stardew.ModBuildConfig finds the game, deploys the mod to `Mods/DodgeRoll`, and writes a release zip to `bin/`.
