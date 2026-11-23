# Deadly Lightning

Rings attract lightning. If you wear a ring outdoors during a thunderstorm, there's a very small chance on each 10-minute clock tick that lightning strikes you and knocks you out, just like dying in the mines: you wake up at the clinic and lose items the usual way.

## Requirements

- [SMAPI](https://smapi.io) 4.5.0 or later
- Stardew Valley 1.6
- Optional: [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098), to change settings in game
- Multiplayer: the **host must have the mod**, or nothing happens. Every player should install it. A farmhand without the mod (or with an older major.minor version than the host's) is never struck.

## How it works

- **When:** on each in-game 10-minute tick from 6:10 to 23:50, the same 107 ticks a day on which the game runs its own lightning. Only the host rolls.
- **Who can be struck:** a player who
  - wears at least one ring (a combined ring counts as one ring),
  - is in a location where it's storming right now (each area uses its own weather, so the valley and Ginger Island are checked separately),
  - is outdoors (unless **Indoors Safe** is off),
  - isn't in an event, a festival, passing out, or already knocked out.
- **Odds:** each eligible player rolls separately. If more than one roll hits, one of them is picked at random, so at most one player is struck per tick.
- **The strike:** the victim's game shows a lightning bolt, a white flash and thunder, then the vanilla knock-out (red screen, the game's chat message, revive at the clinic or the island doctor, normal item loss). Other players in the same location see the bolt, flash and thunder. Players outdoors in a storm elsewhere see a small flash and hear distant thunder.
- **The strike is instant.** It sets your health to 0. Defense, buffs and the Ring of Yoba's protection don't help.
- **Busy players:** if you're in a menu, using a tool, eating, otherwise can't move, or time is paused, the strike waits up to about 3 seconds for you to finish. If you're still busy, it misses. It also misses at once if you're in an event or festival, warping or fading between maps, passing out, the day is ending, have moved to another location, or (for a random strike) went indoors, left the storm or took your rings off. A miss isn't rerolled, and you only see a bolt when someone is actually struck.
- **Disconnects:** if the victim leaves the game before the strike lands, nothing happens and there's no reroll.

### Chance math

With the default 0.05% per tick and one ring, outdoors in a storm all day: `1 - 0.9995^107`, about **5.2% per storm day**. Outdoors for about half the day: about 2.6%. With **Scale With Ring Count** on, two rings double the chance per tick (about 10.2% per full storm day).

## Settings

Change these in game with [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) (optional), or edit `config.json`, which is created the first time you run the game with the mod. In multiplayer only the host's settings are used.

| Setting | Default | Description |
|---|---|---|
| `Enabled` | `true` | Turns random strikes on or off. |
| `StrikeChancePerCheck` | `0.0005` | Chance per eligible player per 10-minute tick (0.05%). Clamped to 0 to 1. The in-game slider goes up to 5%. |
| `ScaleWithRingCount` | `false` | Multiply the chance by the number of rings worn (1 or 2). Off by default because most players wear two rings later on, which would quietly double the odds. |
| `IndoorsSafe` | `true` | Players inside buildings can't be struck. Turn off to allow strikes anywhere it's storming. |

## Console command

`deadlylightning_strike [player name or id]` (host only) strikes a player right away. Without an argument it strikes you. It ignores the chance, rings, storm and Indoors Safe, and works even when `Enabled` is off. It still misses if the player is in an event or festival, warping, passed out, has moved to another location, or is busy for longer than about 3 seconds. Players without the mod, or with an older major.minor version than the host's, can't be targeted.

## Install

1. Install [SMAPI](https://smapi.io).
2. Unzip the mod into your `Mods` folder.
3. Launch the game through SMAPI.

## Build from source

From this folder, run `dotnet build`. Pathoschild.Stardew.ModBuildConfig finds the game, deploys the mod to `Mods/DeadlyLightning`, and writes a release zip to `bin/`.

## Manual test steps

1. Single player, stormy day, wear a ring, stand outdoors. Run `deadlylightning_strike`: you see the bolt and are knocked out.
2. Open a menu, then run the command and close the menu within 3 seconds: the strike lands. Keep it open longer: nothing happens (SMAPI log at Trace says why).
3. Set `StrikeChancePerCheck` to `1` and wait one 10-minute tick in a storm with a ring: you're struck. Take the rings off: never struck.
4. Two-player co-op (both with the mod): strike the farmhand by name. Only the farmhand is knocked out, and the host sees the bolt if in the same location.
5. Split-screen: strike the second player. Exactly one knock-out, and each screen shows the bolt once.

## Design notes

- The host rolls but never changes another player's health. The victim's own game checks the conditions and applies the knock-out, because health, events, menus and warps are local to each player's game.
- No Harmony. The mod uses SMAPI events and the game's own lightning bolt, sounds and death flow.

## Credits

Cover text uses the 'Stardew Valley' font by RRDome (https://fontstruct.com/fontstructions/show/1254619).
