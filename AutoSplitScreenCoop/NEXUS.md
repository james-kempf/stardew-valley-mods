# Nexus Mods page text

## Short description (Brief overview)

```text
Load a save that has a local co-op farmhand and split-screen opens automatically with that farmhand already on the second screen. No Start press, no slot picking. Saves without a farmhand load single player as normal. Steam Deck friendly.
```

## Full description (BBCode)

```bbcode
[size=5][b]Auto Split-Screen Co-op[/b][/size]

Load a save that has a local co-op farmhand, and the mod opens split-screen and loads that farmhand into the second screen for you. You don't press Start or pick a slot. Saves without an eligible farmhand load single player as normal.

[size=4][b]What it does[/b][/size]
[list]
[*]After a save finishes loading on the main screen, the mod opens a second screen using the game's own split-screen call (the same one the vanilla "press Start to join" menu makes).
[*]The second screen shows the vanilla farmhand selection menu. When the target farmhand appears, the mod selects that slot using the same call a slot click makes.
[*]No simulated button presses or mouse clicks, and no Harmony patches.
[*]Runs once per save load. Returning to title and loading again starts it fresh, with no duplicate screens.
[/list]

[b]Eligible farmhand:[/b] a customized farmhand (not an empty cabin) who is not already online. If several are eligible, the mod picks the one who played most recently, unless you name one in the config.

[b]The mod does nothing when:[/b]
[list]
[*]Enabled is false in the config.
[*]Split-screen is already running.
[*]You joined someone else's game as a farmhand.
[*]The save was loaded through Co-op > Host (online/LAN hosting).
[*]The save has no eligible farmhand, or none matching FarmhandName.
[/list]

[size=4][b]Requirements[/b][/size]
[list]
[*][url=https://smapi.io]SMAPI[/url] 4.5.0 or later
[*]Stardew Valley 1.6
[*]A gamepad for player 2 that the host isn't using. On Steam Deck, or if the host plays on a gamepad, connect a second controller.
[/list]

[size=4][b]Install[/b][/size]
[list=1]
[*]Install SMAPI.
[*]Unzip the download into your game's Mods folder.
[*]Run the game through SMAPI once to create config.json.
[*]Load a save that already has a farmhand (created earlier through split-screen or co-op) from the main Load menu.
[/list]

[size=4][b]Config[/b][/size]
Edit config.json in the mod folder. It is created the first time you run the game with the mod installed.

[list]
[*][b]Enabled[/b] (default: true) - Turn the mod on or off.
[*][b]FarmhandName[/b] (default: empty) - Farmhand to load, case-insensitive. Empty means the most recently played eligible farmhand.
[*][b]PlayerIndex[/b] (default: Auto) - Controller for the second screen: Auto, One, Two, Three or Four. An invalid value logs a warning and falls back to Auto.
[*][b]ConfigVersion[/b] - Managed by the mod. Don't edit it.
[/list]

[b]How Auto picks player 2's controller:[/b]
[list=1]
[*]The first connected controller from Two to Four that the host isn't using.
[*]Otherwise controller One, but only if the host plays with keyboard and mouse. The host keeps keyboard and mouse, the same as the vanilla join menu.
[*]Otherwise split-screen doesn't start and the SMAPI log shows a warning.
[/list]
The mod reads the host's input from the game's Gamepad Mode setting and last input. On Steam Deck the host counts as a gamepad player and keeps the built-in controls.

The mod never gives the host's controller to player 2. If you set PlayerIndex to the controller the host is playing on, it logs a warning and uses Auto instead.

[b]Upgrading from 1.0.0:[/b] HostUsesKeyboard was removed. The mod updates config.json on first launch: the old default (PlayerIndex One with HostUsesKeyboard true) becomes Auto, and any other PlayerIndex is kept.

Example:
[code]{
  "ConfigVersion": 1,
  "Enabled": true,
  "FarmhandName": "",
  "PlayerIndex": "Auto"
}[/code]

[size=4][b]Limitations[/b][/size]
[list]
[*]Local split-screen only. It does not start or join online or LAN co-op.
[*]Load the save from the main Load menu. Saves loaded through Co-op > Host are skipped, so use the vanilla join menu there.
[*]Player 2 needs a gamepad the host isn't using. With PlayerIndex Auto and no free controller, split-screen doesn't start and the SMAPI log says why. On Steam Deck that means connecting a second controller.
[*]With an explicit PlayerIndex (not the host's controller), split-screen starts even if that controller isn't connected yet, and the log shows a warning.
[*]It loads one existing farmhand. It does not create new farmhands; make one through vanilla split-screen first.
[*]If the farmhand slot does not appear within about 30 seconds, or the menu lists other farmhands but not the target, the mod logs a warning and leaves the vanilla farmhand menu open so you can pick manually.
[/list]

[size=4][b]Troubleshooting[/b][/size]
[list]
[*]Open the SMAPI log and search for "Auto Split-Screen Co-op".
[*]"Controllers: host pad=..., hostOnKeyboard=..., connected=[...], ..., player 2 pad=..." shows which controller each screen got.
[*]"Starting split-screen for farmhand ..." followed by "Loading farmhand ... on screen 1." means it worked.
[*]"Not starting split-screen: ..." gives the reason it skipped. Some reasons are logged only at TRACE level.
[*]Warnings cover no free controller, a PlayerIndex that is the host's controller, an invalid PlayerIndex, and a farmhand slot that never appeared.
[*]When reporting a problem, upload your log at [url=https://smapi.io/log]smapi.io/log[/url] and post the link.
[/list]

[size=4][b]Compatibility[/b][/size]
The mod uses no Harmony patches and calls only the game's own split-screen and farmhand menu methods.

[size=4][b]Version[/b][/size]
1.1.0 - Fixed the host losing its controller on Steam Deck and other gamepad hosts (player 2 was given controller One). New PlayerIndex default Auto picks a free controller. HostUsesKeyboard removed (detected automatically); existing configs are migrated.
1.0.0 - Initial release.

[size=4][b]Credits[/b][/size]
Cover text uses the 'Stardew Valley' font by RRDome ([url=https://fontstruct.com/fontstructions/show/1254619]https://fontstruct.com/fontstructions/show/1254619[/url]).
```
