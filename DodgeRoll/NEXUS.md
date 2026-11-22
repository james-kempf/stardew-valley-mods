# Nexus Mods page text

## Short description (Brief overview)

254 characters (limit 255).

```text
Click the left stick to dodge roll a short way with brief invincibility frames, paid for with stamina. Rebind to any key or button. Distance, timing, i-frames, cooldown and stamina cost are all configurable, with optional Generic Mod Config Menu support.
```

## Full description (BBCode)

```bbcode
[size=5][b]Dodge Roll[/b][/size]

A Dark Souls style dodge roll for Stardew Valley. Click the left stick (L3) to roll a short distance, with a brief window where you can't take damage. Each roll costs stamina.

[size=4][b]Controls[/b][/size]
[list]
[*][b]Roll:[/b] click the left stick (LeftStick). You can rebind it to any SMAPI keybind, including keyboard keys and combos such as LeftShift + Space.
[*]The roll button is suppressed, so it never also does its normal game action, even when a roll is refused (cooldown or not enough stamina).
[*][b]Direction:[/b] you roll the way you push the left stick. If the stick is centred, you roll the way you hold the movement keys (8 directions). If you're standing still, you roll the way you're facing.
[/list]

[size=4][b]Features[/b][/size]
[list]
[*][b]Smooth, collision-aware movement.[/b] The roll eases out over its distance and checks collisions every 4 pixels, so it can't skip through thin obstacles. Walls and obstacles stop it early, and you slide along a wall when only one direction is blocked.
[*][b]Never warps you.[/b] Map edges and warps (doorways and map exits) stop the roll like walls do. Walk into a warp as usual to use it.
[*][b]Invincibility frames.[/b] You can't be damaged for a short window from the start of the roll. It never lasts longer than the roll. If the game already made you invincible (for example, right after you take a hit), the roll leaves that alone and doesn't extend it.
[*][b]Stamina cost.[/b] By default a roll costs 2 pickaxe swings, using the game's own formula: one swing costs 2 - 0.1 x Mining level (minimum 0.1). That makes a roll cost 4 stamina at Mining 0 and 2 at Mining 10. You can switch to a flat cost instead.
[*][b]Feedback.[/b] You spin through all four facing directions, kick up dust, and hear a sound (can be turned off).
[*]You can't move, use tools, or interact while rolling.
[*]The roll pauses with the game: if the game is paused or a menu opens, the roll freezes and continues when you return.
[/list]

[b]You can't roll when:[/b]
[list]
[*]You're riding a horse, sitting, swimming, or using a tool.
[*]A menu, dialogue box, cutscene, or festival is active, or the screen is fading (warping).
[*]You're already rolling, or the cooldown after your last roll hasn't finished.
[*]You don't have enough stamina for the cost (see Allow Roll With Low Stamina below).
[/list]

[b]A roll ends early when:[/b] you warp or change location, a cutscene or festival starts, you return to title, or you turn Enabled off mid-roll.

[size=4][b]Requirements[/b][/size]
[list]
[*][url=https://smapi.io]SMAPI[/url] 4.5.0 or later
[*]Stardew Valley 1.6
[*]Optional: [url=https://www.nexusmods.com/stardewvalley/mods/5098]Generic Mod Config Menu[/url], to change settings in game
[/list]

[size=4][b]Install[/b][/size]
[list=1]
[*]Install SMAPI.
[*]Unzip the download into your game's Mods folder.
[*]Run the game through SMAPI. config.json is created the first time you run the game with the mod installed.
[/list]

[size=4][b]Config[/b][/size]
Change these in game with Generic Mod Config Menu (sections Movement, Timing, Stamina and Effects), or edit config.json in the mod folder. Ranges are the Generic Mod Config Menu slider limits.

[list]
[*][b]Enabled[/b] (default: true) - Turn dodge rolling on or off.
[*][b]RollKey[/b] (default: LeftStick) - The button or buttons that start a roll. Any SMAPI keybind.
[*][b]RollDistanceTiles[/b] (default: 2.5; 0.5 to 6, step 0.25) - How far one roll moves you, in tiles.
[*][b]RollDurationMs[/b] (default: 350; 100 to 1000, step 25) - How long a roll takes, in milliseconds.
[*][b]IFrameDurationMs[/b] (default: 250; 0 to 1000, step 25) - How long you can't take damage, from the start of the roll. Never longer than the roll. 0 turns i-frames off.
[*][b]CooldownMs[/b] (default: 300; 0 to 3000, step 50) - How long you wait after a roll ends before you can roll again.
[*][b]StaminaCostSwings[/b] (default: 2; 0 to 10, step 0.5) - How many pickaxe swings of stamina a roll costs. 0 makes rolls free.
[*][b]UseFlatStaminaCost[/b] (default: false) - Use FlatStaminaCost instead of pickaxe swings.
[*][b]FlatStaminaCost[/b] (default: 4; 0 to 50, step 0.5) - Stamina per roll when UseFlatStaminaCost is on.
[*][b]AllowRollWithLowStamina[/b] (default: false) - Roll even with less stamina than the cost. Stamina can drop below zero, like it does with tools. You still can't roll when you're exhausted (0 stamina or less).
[*][b]PlaySound[/b] (default: true) - Play a sound when you roll.
[/list]

Example (defaults):
[code]{
  "Enabled": true,
  "RollKey": "LeftStick",
  "RollDistanceTiles": 2.5,
  "RollDurationMs": 350,
  "IFrameDurationMs": 250,
  "CooldownMs": 300,
  "StaminaCostSwings": 2.0,
  "UseFlatStaminaCost": false,
  "FlatStaminaCost": 4.0,
  "AllowRollWithLowStamina": false,
  "PlaySound": true
}[/code]

[size=4][b]Limitations[/b][/size]
[list]
[*]Rolls don't carry you through doorways or off the map. Walk into a warp to use it.
[*]The roll button is always suppressed while you're free to move, so a button you bind to rolling loses its normal action.
[*]Configured values outside the slider ranges aren't clamped, except that negative distance, duration and cooldown count as 0 (duration at least 1 ms).
[/list]

[size=4][b]Compatibility[/b][/size]
[list]
[*]No Harmony patches.
[*]Split-screen: each local player has their own roll and cooldown.
[*]Multiplayer: the mod only moves your own farmer, so it works for farmhands as well as the host.
[/list]

[size=4][b]Troubleshooting[/b][/size]
[list]
[*]Nothing happens when you press the button: check that Enabled is true, that RollKey is the button you're pressing, and that you have enough stamina and aren't in the cooldown.
[*]Check the SMAPI log for errors that mention Dodge Roll.
[*]When reporting a problem, upload your log at [url=https://smapi.io/log]smapi.io/log[/url] and post the link.
[/list]

[size=4][b]Version[/b][/size]
1.0.0 - Initial release.

[size=4][b]Credits[/b][/size]
Cover text uses the 'Stardew Valley' font by RRDome ([url=https://fontstruct.com/fontstructions/show/1254619]https://fontstruct.com/fontstructions/show/1254619[/url]).
```
