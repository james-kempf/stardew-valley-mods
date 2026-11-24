# Nexus Mods page text

## Short description (Brief overview)

249 characters (limit 255).

```text
Reduces stutter with background garbage collection, a memory cleanup at each day start and background pre-compiling of game code. Just play: a report file then names slow mods, frame spikes and Content Patcher reload storms. No Harmony, no commands.
```

## Full description (BBCode)

```bbcode
[size=5][b]Performance Bundle[/b][/size]

A set of small performance modules for a heavily modded game, each with its own on/off switch. Three of them try to reduce stutter from garbage collection pauses and first-time code compilation. The rest are diagnostics and recommendations that show where frame time goes, so you can find the mod, Content Patcher pack or setting behind it.

[b]None of this has been benchmarked.[/b] This page makes no promise about frames or seconds saved. Compare the session reports with modules on and off to see the effect on your own game.

[size=4][b]Finding the slow mods[/b][/size]
No console commands needed.
[list=1]
[*]Install the mod (and SMAPI).
[*]Play a normal session with your full mod set.
[*]Afterwards, open the newest file in [b]Mods/PerformanceBundle/reports/[/b] (session-YYYYMMDD-HHMMSS.txt). The same report is also in the SMAPI log (SMAPI-latest.txt) each time you return to title and when the game closes normally.
[/list]
The report is written at each in-game day end, every 5 minutes while a save is loaded, on returning to title and when the game closes normally, so after a crash the file from the last write (at most about 5 minutes old) is still there. One file per game session; the newest 10 are kept. It starts with [b]Likely culprits[/b]: the mods with the most event handler time, the mods most often slowest in spike frames, the Content Patcher packs whose location- or time-dependent patches target assets reloaded after warps, and the slowest warps by location. Then one line per in-game day, every module's counters, each day's worst frames (with the three mod/event pairs that took the most handler time in that frame) and warp times per location, and the settings check. To ask for help, share the report file or upload your log at [url=https://smapi.io/log]smapi.io/log[/url].

[size=4][b]What each module does[/b][/size]
Symptoms: [b]initial load[/b] (launching and loading a save), [b]in-area stutter[/b] (hitches while you play in one place), [b]warp load[/b] (the pause when you enter another location).

[table]
[tr][th]Module[/th][th]What it does[/th][th]Symptoms helped[/th][th]Default[/th][th]Risk[/th][/tr]
[tr][td]GcLatency[/td][td]While a save is loaded, runs full (gen2) garbage collections in the background instead of pausing the game. Restored before saving and on returning to title. Does nothing if background GC is off in the runtime.[/td][td]in-area stutter, warp load[/td][td]on[/td][td]low[/td][/tr]
[tr][td]GcTransitionCollect[/td][td]One full, compacting garbage collection at the start of each day and once after a save loads (at most once per day), so less garbage is left to cause pauses during play.[/td][td]in-area stutter[/td][td]on[/td][td]low[/td][/tr]
[tr][td]PreJit[/td][td]Compiles the game's location, NPC, character, monster, terrain, object and rendering code on a low-priority background thread after the title screen appears.[/td][td]initial load (loading a save), in-area stutter, warp load[/td][td]on[/td][td]low-medium[/td][/tr]
[tr][td]AdjacentPrewarm[/td][td]After a warp, loads the tilesheet textures of connected locations a little per tick. Maps already load with the save, so the gain is usually small.[/td][td]warp load[/td][td]off[/td][td]medium-high[/td][/tr]
[tr][td]SpikeDiagnostics[/td][td]Records slow frames with the GC activity, asset reloads and warp in each, plus warp times and save load time.[/td][td]finds causes of in-area stutter and warp load[/td][td]on[/td][td]low[/td][/tr]
[tr][td]InvalidationReport[/td][td]Counts assets that mods force to reload, per minute and per warp, and logs a line when a minute has very many.[/td][td]finds causes of in-area stutter and warp load[/td][td]on[/td][td]low[/td][/tr]
[tr][td]HandlerProfiler[/td][td]Times every mod's SMAPI event handlers while a save is loaded (ProfileMode Continuous), for the report and to name the slowest handlers in each spike frame. Turns itself off for the session if its own cost stays over the cap for 60 seconds. Also runs perf_profile.[/td][td]finds causes of in-area stutter and warp load[/td][td]on[/td][td]medium[/td][/tr]
[tr][td]EnvironmentReport[/td][td]Checks SMAPI and runtime settings and Content Patcher packs once at launch and logs tips. Read-only.[/td][td]recommendations for all three[/td][td]on[/td][td]low[/td][/tr]
[tr][td]AutoReport[/td][td]Writes the whole report to Mods/PerformanceBundle/reports/ at each day end, every 5 minutes, on returning to title and when the game closes normally.[/td][td]finds causes of all three[/td][td]on[/td][td]low[/td][/tr]
[/table]

[size=4][b]About initial load[/b][/size]
Most initial load time is spent before any mod can act on it: SMAPI loading and rewriting mods, each mod's startup code, and Content Patcher loading its packs. This mod can't shorten that part. It helps in two smaller ways:
[list]
[*][b]PreJit:[/b] SMAPI turns off .NET tiered compilation (it causes problems with Harmony patches), so each game method is fully compiled the first time it runs. PreJit compiles game code in the background once the title screen is up. If it has finished before you load a save, loading and first visits do less compiling. perf_report shows its progress.
[*][b]perf_env:[/b] lists settings that slow the game (such as verbose logging or developer mode) and the Content Patcher packs that re-check patches on every warp or clock change. Removing mods you don't use is the main way to shorten initial load.
[/list]

[size=4][b]Risks[/b][/size]
[list]
[*][b]PreJit:[/b] if a mod patches a small game method with Harmony [i]after[/i] PreJit compiled a method that calls it, the patch can be skipped on that path (the small method may have been inlined). PreJit waits until all mods have finished launching, which covers mods that patch at launch. If a mod feature stops working only with PreJit on, turn PreJit off.
[*][b]AdjacentPrewarm:[/b] off by default, and the expected gain is small. It only looks at locations that already exist, so it never creates one (such as a mine level). It loads through the game's own content manager, so other mods' edits apply as usual. It never pre-loads assets that Content Patcher patches change on warps or clock changes, or any asset reloaded this session. One large texture can take longer than the per-tick budget.
[*][b]HandlerProfiler:[/b] SMAPI has no timing feature, so the profiler inserts its own marker handlers into SMAPI's internal handler lists. It never replaces other mods' handlers. The markers go in after a save loads and come out on returning to title, on an error, when turned off and when the game closes normally. Their own cost is measured and shown in the report (a lower bound: SMAPI's own small cost of calling each marker isn't included); if it stays over ProfilerMaxOverheadUsPerTick (default 200 microseconds per tick) for 60 seconds, continuous profiling turns itself off for the session. If a mod subscribes with a priority during play and SMAPI re-sorts that event's handlers, some samples for it are dropped; the report counts them. If a future SMAPI changes these internals, the profiler turns itself off with a warning and everything else keeps running.
[*]A module that hits an error logs it once and turns only itself off, restoring anything it changed.
[/list]

[size=4][b]Console commands (optional)[/b][/size]
None of these are needed for the session report.
[list]
[*][b]perf_report[/b] - Likely culprits, one line per in-game day, and counters since launch or the last perf_reset: frame time, spikes with the 10 worst frames, GC counts and pause time, assets invalidated and loaded, recent warps, last save load time, PreJit progress, full-GC pause times, prewarm counters, handler times per mod and the diagnostics' own cost per tick.
[*][b]perf_reset[/b] - Clears the counters. Settings are unchanged.
[*][b]perf_env[/b] - Read-only check of SMAPI settings, runtime GC and compilation settings, installed mod counts and Content Patcher packs that update on warps or clock changes, each with a recommendation.
[*][b]perf_profile [seconds][/b] - Logs ranked tables per mod and per mod and event for the next 1 to 600 seconds (default 10). Needs HandlerProfiler on.
[*][b]perf_profile stop[/b] - Ends a run early and logs the tables.
[/list]

[size=4][b]Reading the report[/b][/size]
[list]
[*][b]GC collections and a large pause in the worst frames:[/b] garbage collection. Check that GcLatency is active; if the settings check says background GC is off, fix that first.
[*][b]Many assets invalidated, often on a warp:[/b] a Content Patcher pack reloading assets. The culprits list names the packs whose location- or time-dependent patches target those assets. Check their config for location- or time-based options you don't use, and run "patch summary <UniqueID>" to see which patches change.
[*][b]A mod named in the frame's handler column or near the top of the handler lists:[/b] that mod's code. ms/s shows the mods costing the most frame time overall; max us shows handlers that sometimes take a long time (single hitches). Times include any events a handler triggers. Only SMAPI event handlers are timed; time a mod spends in Harmony patches inside game code isn't shown.
[/list]

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
Change these in game with Generic Mod Config Menu (changes apply right away), or edit config.json in the mod folder (takes effect at the next launch).

[list]
[*][b]GcLatency[/b] (default: true) - Background gen2 garbage collection while a save is loaded.
[*][b]GcTransitionCollect[/b] (default: true) - One full, compacting garbage collection at the start of each day and after loading a save.
[*][b]PreJit[/b] (default: true) - Compile location, NPC and rendering code on a low-priority background thread after launch.
[*][b]AdjacentPrewarm[/b] (default: false) - After a warp, pre-load tilesheet textures of neighboring locations.
[*][b]SpikeDiagnostics[/b] (default: true) - Frame spike, GC, warp and save load timing for perf_report.
[*][b]InvalidationReport[/b] (default: true) - Count reloaded (invalidated) assets per minute and per warp.
[*][b]HandlerProfiler[/b] (default: true) - Time every mod's event handlers (see ProfileMode) and allow perf_profile.
[*][b]ProfileMode[/b] (default: Continuous) - Continuous: time handlers whenever a save is loaded, for the session report. Manual: only during perf_profile runs.
[*][b]EnvironmentReport[/b] (default: true) - Run the perf_env checks once at launch. The command always works.
[*][b]AutoReport[/b] (default: true) - Write the session report to Mods/PerformanceBundle/reports/. Keeps the newest 10 session files.
[*][b]SpikeThresholdMs[/b] (default: 50) - A frame longer than this many milliseconds counts as a spike. 60 frames per second is about 17 ms per frame. Config menu range 20 to 1000.
[*][b]InvalidationStormPerMinute[/b] (default: 100) - Reloaded assets within one minute that count as a storm and log a line. Config menu range 10 to 5000.
[*][b]ProfilerMaxOverheadUsPerTick[/b] (default: 200) - Continuous profiling turns itself off for the session if its markers cost more than this many microseconds per tick for 60 seconds in a row. Config menu range 20 to 5000.
[*][b]AdjacentPrewarmBudgetMs[/b] (default: 2) - Most main-thread time AdjacentPrewarm spends per update tick, in milliseconds. Config menu range 1 to 8.
[/list]

Example (defaults):
[code]{
  "GcLatency": true,
  "GcTransitionCollect": true,
  "PreJit": true,
  "AdjacentPrewarm": false,
  "SpikeDiagnostics": true,
  "InvalidationReport": true,
  "HandlerProfiler": true,
  "ProfileMode": "Continuous",
  "AutoReport": true,
  "EnvironmentReport": true,
  "SpikeThresholdMs": 50,
  "InvalidationStormPerMinute": 100,
  "ProfilerMaxOverheadUsPerTick": 200,
  "AdjacentPrewarmBudgetMs": 2
}[/code]

[size=4][b]Compatibility[/b][/size]
[list]
[*]No Harmony patches. The mod uses SMAPI events, .NET runtime settings and, for HandlerProfiler and AdjacentPrewarm, reflection on SMAPI internals.
[*]It writes files only inside its own folder (reports/).
[*]Split-screen: GC settings and PreJit are process-wide, so only the first screen controls them. Frame timing covers all screens. Warps on other screens are labeled with their screen number.
[*]Online multiplayer: nothing is synced. Each player's game runs its own copy, so install it wherever you want it.
[*]Generic Mod Config Menu is optional.
[*]It never changes other mods' files or the game's assets.
[/list]

[size=4][b]Version[/b][/size]
1.0.0 - Initial release.

[size=4][b]Credits[/b][/size]
Cover text uses the 'Stardew Valley' font by RRDome (https://fontstruct.com/fontstructions/show/1254619).
```
