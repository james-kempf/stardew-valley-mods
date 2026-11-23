# Nexus Mods page text

## Short description (Brief overview)

254 characters (limit 255).

```text
Wear a ring outdoors in a thunderstorm and each 10 in-game minutes there is a very small chance lightning strikes you and knocks you out. No ring, no risk. Co-op and split-screen aware, with configurable odds and optional Generic Mod Config Menu support.
```

## Full description (BBCode)

```bbcode
[size=5][b]Deadly Lightning[/b][/size]

Rings attract lightning. If you wear a ring outdoors during a thunderstorm, there's a very small chance on each 10-minute clock tick that lightning strikes you and knocks you out, just like running out of health in the mines: you wake up at the clinic and lose items the usual way.

[size=4][b]What it does[/b][/size]
[list]
[*][b]Rings only.[/b] Without a ring the chance is exactly 0, whatever the config says. A combined ring counts as one ring.
[*][b]Storms only.[/b] It only happens where it's storming right now, not in plain rain. Each area uses its own weather, so the valley and Ginger Island are checked separately.
[*][b]Outdoors by default.[/b] Players inside buildings are safe unless you turn Indoors Safe off.
[*][b]When.[/b] On each in-game 10-minute tick from 6:10 to 23:50, the same 107 ticks a day on which the game runs its own lightning.
[*][b]The strike.[/b] A lightning bolt hits you with a white flash and thunder, then the game's normal knock-out follows: red screen, the game's chat message, waking up at the clinic (or the island doctor), and the usual item loss.
[*][b]No way to block it.[/b] The strike sets your health to 0 directly, so defense, buffs and the Ring of Yoba don't protect you.
[*][b]Busy players.[/b] If you're in a menu, using a tool, eating, can't move, or time is paused, the strike waits up to 3 seconds for you to finish. If you're still busy, it misses.
[*][b]Silent misses.[/b] A strike also misses at once if you're in an event or festival, warping or fading between maps, passing out, the day is ending, you've moved to another location, or (for a random strike) you went indoors, left the storm or took your rings off. A miss isn't rerolled, and nobody sees a bolt unless someone is actually struck.
[/list]

[size=4][b]Odds[/b][/size]
Each eligible player rolls separately on each of the 107 checks a day. With the default 0.05% per check and one ring, outdoors in a storm all day, that's 1 - 0.9995^107, about [b]5.2% per storm day[/b]. Outdoors for about half the day: about 2.6%. With Scale With Ring Count on, two rings double the chance per check: about [b]10.2%[/b] per full storm day.

[size=4][b]Multiplayer and split-screen[/b][/size]
[list]
[*][b]The host decides.[/b] Only the host rolls, and only the host's config is used. The host must have the mod, or nothing happens.
[*][b]One victim at a time.[/b] If more than one player's roll hits on the same check, one of them is picked at random, so at most one player is struck per check.
[*][b]Everyone sees it.[/b] Players (with the mod) in the same location as the victim see the bolt and flash and hear the thunder. Players outdoors in a storm somewhere else see a faint flash and hear distant thunder.
[*][b]Mod required to be struck.[/b] A farmhand without the mod, or with an older version (major.minor) than the host's, is never struck.
[*]Split-screen players work like any other player: exactly one knock-out per strike.
[*]If the victim disconnects before the strike lands, nothing happens and there's no reroll.
[/list]

[size=4][b]Requirements[/b][/size]
[list]
[*][url=https://smapi.io]SMAPI[/url] 4.5.0 or later
[*]Stardew Valley 1.6
[*]Optional: [url=https://www.nexusmods.com/stardewvalley/mods/5098]Generic Mod Config Menu[/url], to change settings in game
[*]Multiplayer: the host must have the mod. Every player who wants to be at risk needs it too.
[/list]

[size=4][b]Install[/b][/size]
[list=1]
[*]Install SMAPI.
[*]Unzip the download into your game's Mods folder.
[*]Run the game through SMAPI. config.json is created the first time you run the game with the mod installed.
[/list]

[size=4][b]Config[/b][/size]
Change these in game with Generic Mod Config Menu, or edit config.json in the mod folder. In multiplayer only the host's settings are used.

[list]
[*][b]Enabled[/b] (default: true) - Turn random lightning strikes on or off. The console command still works when this is off.
[*][b]StrikeChancePerCheck[/b] (default: 0.0005, which is 0.05%) - Chance per eligible player on each 10-minute check. The Generic Mod Config Menu slider goes from 0% to 5% in steps of 0.01% and shows the value as a percentage. In config.json any value from 0 to 1 works; values outside that are clamped.
[*][b]ScaleWithRingCount[/b] (default: false) - Multiply the chance by the number of rings worn (1 or 2; a combined ring counts as one). Off by default, because most players wear two rings later on and that would quietly double the odds.
[*][b]IndoorsSafe[/b] (default: true) - Players inside buildings can't be struck. Turn it off to allow strikes anywhere it's storming.
[/list]

Example (defaults):
[code]{
  "Enabled": true,
  "StrikeChancePerCheck": 0.0005,
  "ScaleWithRingCount": false,
  "IndoorsSafe": true
}[/code]

[size=4][b]Console command[/b][/size]
[code]deadlylightning_strike [player name or id][/code]
Host only. Strikes a player right away; without an argument it strikes you. It ignores the chance, rings, storm, Indoors Safe and Enabled, but keeps the safety checks: it still misses if the player is in an event or festival, warping, passed out, moves to another location, or stays busy (menu, tool, eating) for more than 3 seconds. Players without the mod, or with an older version than the host's, can't be targeted.

[size=4][b]Compatibility[/b][/size]
[list]
[*]No Harmony patches. The mod uses SMAPI events and the game's own lightning bolt, sounds and knock-out.
[*]Works in single player, online co-op and split-screen.
[/list]

[size=4][b]Troubleshooting[/b][/size]
[list]
[*]Never struck: check that you wear a ring, that it's a thunderstorm (not just rain) where you are, that you're outdoors (or Indoors Safe is off), and that the host has the mod with Enabled on. In co-op, the farmhand needs the mod at the host's version or newer.
[*]To test, the host can run deadlylightning_strike in the SMAPI console.
[*]A strike that missed is logged in the SMAPI log with the reason (for example "menu open for too long").
[*]When reporting a problem, upload your log at [url=https://smapi.io/log]smapi.io/log[/url] and post the link.
[/list]

[size=4][b]Version[/b][/size]
1.0.0 - Initial release.

[size=4][b]Credits[/b][/size]
Cover text uses the 'Stardew Valley' font by RRDome ([url=https://fontstruct.com/fontstructions/show/1254619]https://fontstruct.com/fontstructions/show/1254619[/url]).
```
