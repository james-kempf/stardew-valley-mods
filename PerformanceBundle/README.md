# Performance Bundle

A set of small, separately switchable performance modules for a heavily modded game. Three of them try to reduce stutter (garbage collection pauses and first-time code compilation). The rest are diagnostics and recommendations that show where frame time goes, so you can find the mod, Content Patcher pack or setting behind it.

None of the modules has been benchmarked. The mod makes no claim about how many frames or seconds you'll save. Compare the session reports (see [Finding the slow mods](#finding-the-slow-mods)) with modules on and off to see the effect on your own game.

## Requirements

- [SMAPI](https://smapi.io) 4.5.0 or later
- Stardew Valley 1.6
- Optional: [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098), to change settings in game
- Multiplayer: nothing is synced between players. Each player's game runs its own copy, so install it wherever you want it.

## Finding the slow mods

No console commands are needed.

1. Install the mod (and SMAPI).
2. Play a normal session with your full mod set, for as long as you like.
3. Afterwards, open the newest file in `Mods/PerformanceBundle/reports/` (named `session-YYYYMMDD-HHMMSS.txt`). The same report is also in the SMAPI log (`SMAPI-latest.txt`) each time you return to title and when the game closes normally.

The report is written at the end of each in-game day, every 5 minutes while a save is loaded, when you return to title and when the game closes normally (SMAPI shuts its mods down on the main thread before exiting, and the report is written then). If the game crashes or is killed, the file from the last write is still there, at most about 5 minutes old. One file per game session; the newest 10 are kept and older ones are deleted. It contains:

- **Likely culprits** at the top: the mods with the most event handler time (ms per second), the mods most often slowest in spike frames, the Content Patcher packs whose location- or time-dependent patches target the assets reloaded after warps, and the slowest warps by location.
- **Days**: one line per in-game day (frame times, spikes, warps, save load, GC, asset reloads). Counters reset at each day end.
- **Session totals**: every module's section, the same as `perf_report`.
- **Day details**: each day's worst frames (with the three mod/event pairs that took the most handler time in that frame), warp times per location, GC and asset reloads.
- **Environment**: the `perf_env` settings check.

To ask for help, share the report file, or upload your log at [smapi.io/log](https://smapi.io/log) and share the link.

## What it helps with

Three symptoms are tracked separately:

1. **Initial load**: launching the game and loading a save.
2. **In-area stutter**: hitches while you play in one place.
3. **Warp load**: the pause when you enter another location.

| Module | What it does | Symptoms helped | Default | Risk |
|---|---|---|---|---|
| GcLatency | While a save is loaded, asks .NET to run full (gen2) garbage collections in the background instead of pausing the game. Restored before saving and on returning to title, then set again after the save. Does nothing if background GC is turned off in the runtime. | in-area stutter, warp load | on | low |
| GcTransitionCollect | Runs one full, compacting garbage collection at the start of each day and once after a save loads, at most once per day, so less garbage is left to cause pauses during play. | in-area stutter | on | low |
| PreJit | Compiles the game's location, NPC, character, monster, terrain, object and rendering code on a low-priority background thread after the title screen appears, so that code is ready before its first use. | initial load (loading a save), in-area stutter, warp load | on | low-medium |
| AdjacentPrewarm | After a warp, loads the tilesheet textures of the locations connected to the new one, a little per tick. Maps are already loaded with the save, so the gain is usually small. | warp load | off | medium-high |
| SpikeDiagnostics | Records slow frames with the GC activity, asset reloads and warp in each one, plus warp times and save load time. Diagnostic only. | finds causes of in-area stutter and warp load | on | low |
| InvalidationReport | Counts assets that mods invalidate (force to reload) per minute and per warp, and logs a line when a minute has very many. Diagnostic only. | finds causes of in-area stutter and warp load | on | low |
| HandlerProfiler | Times every mod's SMAPI event handlers while a save is loaded (`ProfileMode` `Continuous`), for the report's per-mod tables and to name the slowest handlers in each spike frame. Turns itself off for the session if its own cost stays over `ProfilerMaxOverheadUsPerTick` for 60 seconds. Also runs `perf_profile`. | finds causes of in-area stutter and warp load | on | medium |
| EnvironmentReport | Checks SMAPI and runtime settings and Content Patcher packs once at launch and logs tips. `perf_env` works either way. Read-only. | recommendations for initial load, in-area stutter, warp load | on | low |
| AutoReport | Writes the whole report to `Mods/PerformanceBundle/reports/` at each day end, every 5 minutes, on returning to title and when the game closes normally (while SMAPI shuts its mods down), and logs it at both of those. If the process ends any other way, it rewrites the last report built during play. The report is built on the main thread (it shows how long that took); only the file write runs in the background. | finds causes of all three | on | low |

### Initial load

Most initial load time is spent before any mod can act on it: SMAPI loading and rewriting mods, each mod's `Entry`, and Content Patcher loading its packs. This mod can't shorten that part. It helps in two smaller ways:

- **PreJit** compiles game code in the background once the title screen is up. SMAPI turns off .NET tiered compilation (it causes problems with Harmony patches), so each game method is fully compiled the first time it runs. If PreJit has finished before you load a save, loading and first visits do less of that compiling. `perf_report` shows its progress.
- **`perf_env`** lists settings that slow the game (for example verbose logging or developer mode) and the Content Patcher packs that re-check patches on every warp or clock change. Removing mods you don't use is the main way to shorten initial load.

### Risks in detail

- **PreJit**: if a mod adds a Harmony patch to a small game method *after* PreJit has compiled a method that calls it, the compiler may have copied (inlined) the small method into the caller, and calls through that caller would skip the patch. Normal compilation has the same exposure, but PreJit widens it to methods that haven't run yet. PreJit waits until all mods have finished `GameLaunched` before it starts, which covers mods that patch at launch. If a mod's feature stops working only with PreJit on, turn PreJit off.
- **AdjacentPrewarm** (off by default; the expected gain is small): it only looks at locations that already exist, so it never creates a location such as a mine or volcano level. It loads textures through the game's own content manager, so other mods' edits apply as usual. It never pre-loads an asset that a Content Patcher patch with `OnLocationChange` or `OnTimeChange` targets, or any asset invalidated this session. It runs on the main thread within a time budget per tick, but one large texture can take longer than the budget.
- **HandlerProfiler**: SMAPI has no timing feature, so the profiler inserts its own marker handlers into SMAPI's internal handler lists through reflection, under the same lock SMAPI uses. It never replaces other mods' handlers, so their subscribe and unsubscribe calls keep working. With `ProfileMode` `Continuous` the markers are inserted on the first tick after a save loads and removed on returning to title, on an error, when the module is turned off and when SMAPI shuts its mods down at game close; a `perf_profile` run in `Manual` mode removes them when it ends. The markers' own time is measured every tick and shown in the report; if it stays over `ProfilerMaxOverheadUsPerTick` (default 200 microseconds per tick) for 60 seconds, continuous profiling turns itself off for the rest of the session with an Info line in the log. Handlers that a mod adds after the markers are in place aren't timed until the next save load. If a mod subscribes with a priority during play and SMAPI re-sorts that event's handlers, segments that then hold two mods' handlers are dropped (the report counts them), so coverage of that event can drop. The overhead figure is a lower bound: it measures time inside the markers, not SMAPI's own small per-handler call cost for them. It depends on SMAPI internals (checked against SMAPI 4.5.2). If they change, the profiler turns itself off with a warning and the other modules keep running.
- If any module throws an error, it logs it once and turns only itself off, restoring any setting it changed.

## Console commands

None of these are needed for the session report. Type them in the SMAPI console.

| Command | What it does |
|---|---|
| `perf_report` | Shows the likely culprits, one line per in-game day, and each module's counters since launch or the last `perf_reset`: frame time average and maximum, spikes over the threshold with the 10 worst frames, GC counts and pause time, assets invalidated and loaded, recent warps and their frame times, last save load time, PreJit progress, the full-GC pause times, prewarm counters, handler times per mod and the diagnostics' own per-tick cost. |
| `perf_reset` | Clears all counters. Settings are unchanged. |
| `perf_env` | Read-only check of SMAPI settings (`VerboseLogging`, `DeveloperMode`, `ParanoidWarnings`, `LogNetworkTraffic`, `CheckForUpdates`), runtime settings (background GC, tiered compilation, server GC), installed mod counts and Content Patcher packs with `OnLocationChange`/`OnTimeChange` patches, with a recommendation for each. It also lists every such pack with its patch counts. |
| `perf_profile [seconds]` | Logs two ranked tables for the next 1 to 600 seconds (default 10): total time per mod, and per mod and event (calls, average and maximum time). Needs `HandlerProfiler` on. With `ProfileMode` `Continuous` it uses the markers that are already in place. |
| `perf_profile stop` | Ends a running profile early and logs the tables. |

At launch, with `EnvironmentReport` on, only the warnings and a one-line summary go to the console; the full report is in the SMAPI log.

## Reading the report

1. Start with **Likely culprits**. A mod near the top of both handler lists is the first one to try removing or reconfiguring.
2. In the day details, read the worst frames. Each row shows the frame time, GC collections (gen0/gen1/gen2), GC pause time, assets invalidated, assets loaded, the warp (if any) and the mod/event pairs that took the most handler time in that frame.
   - **GC collections and a large pause**: garbage collection. Check that GcLatency is active in its section. If the Environment section says background GC is off, fix that first.
   - **Many assets invalidated, often with a warp**: a Content Patcher pack reloading assets. The culprits list names the packs whose location- or time-dependent patches target the reloaded assets, and the InvalidationReport section lists the most invalidated assets. Check those packs' config for options that turn off location- or time-based features you don't use, and run `patch summary <UniqueID>` to see which patches change.
   - **A mod in the handler column**: that mod's code. In the HandlerProfiler section, **ms/s** in the per-mod table shows the mods that cost the most frame time overall, and **max us** in the per-event table shows handlers that are usually fast but sometimes take a long time.
   - Handler times are inclusive: if a handler triggers another event (for example an asset load), those handlers' time is counted too. Only SMAPI event handlers are timed; time a mod spends in Harmony patches inside game code isn't shown.
3. To focus on one place, you can still run `perf_reset`, reproduce the stutter, then run `perf_report` or `perf_profile 30`.

## Settings

Change these in game with [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) (optional; changes apply right away), or edit `config.json`, which is created the first time you run the game with the mod (edits take effect at the next launch).

| Setting | Default | Description |
|---|---|---|
| `GcLatency` | `true` | Background gen2 garbage collection while a save is loaded. |
| `GcTransitionCollect` | `true` | One full, compacting garbage collection at the start of each day and after loading a save. |
| `PreJit` | `true` | Compile location, NPC and rendering code on a low-priority background thread after launch. |
| `AdjacentPrewarm` | `false` | After a warp, pre-load tilesheet textures of neighboring locations. |
| `SpikeDiagnostics` | `true` | Frame spike, GC, warp and save load timing for `perf_report`. |
| `InvalidationReport` | `true` | Count invalidated assets per minute and per warp. |
| `HandlerProfiler` | `true` | Time every mod's event handlers (see `ProfileMode`) and allow `perf_profile`. |
| `ProfileMode` | `Continuous` | `Continuous`: time handlers whenever a save is loaded, for the session report and spike attribution. `Manual`: only during `perf_profile` runs. |
| `EnvironmentReport` | `true` | Run the `perf_env` checks once at launch. The command always works. |
| `AutoReport` | `true` | Write the session report to `Mods/PerformanceBundle/reports/` (day ends, every 5 minutes, return to title, normal game close). Keeps the newest 10 session files. |
| `SpikeThresholdMs` | `50` | A frame longer than this many milliseconds is recorded as a spike. 60 frames per second is about 17 ms per frame. Config menu range: 20 to 1000. |
| `InvalidationStormPerMinute` | `100` | Invalidated assets within one minute that count as a storm and log a line (once per minute). Config menu range: 10 to 5000. |
| `ProfilerMaxOverheadUsPerTick` | `200` | Continuous profiling turns itself off for the session if its markers cost more than this many microseconds per tick for 60 seconds in a row. Config menu range: 20 to 5000. |
| `AdjacentPrewarmBudgetMs` | `2` | Most main-thread time AdjacentPrewarm spends loading per update tick, in milliseconds. Config menu range: 1 to 8. |

## Compatibility

- No Harmony patches. The mod uses SMAPI events, .NET runtime settings and, for HandlerProfiler and AdjacentPrewarm, reflection on SMAPI internals.
- It writes files only inside its own folder (`Mods/PerformanceBundle/reports/`).
- Split-screen: GC settings and PreJit are process-wide, so only the first screen controls them. Frame timing is measured on the first screen and covers all screens. Warps on other screens are labeled with their screen number. AdjacentPrewarm keeps a separate queue per screen.
- Online multiplayer: no messages are sent. A farmhand's game applies the GC modules and PreJit to its own process.
- Generic Mod Config Menu is optional.
- It never changes other mods' files or the game's assets.

## Install

1. Install [SMAPI](https://smapi.io).
2. Unzip the mod into your `Mods` folder.
3. Launch the game through SMAPI.

## Build from source

From this folder, run `dotnet build`. Pathoschild.Stardew.ModBuildConfig finds the game, deploys the mod to `Mods/PerformanceBundle`, and writes a release zip to `bin/`.

## Manual test steps

1. Launch and load a save. The console shows the environment check summary. Run `perf_report`: GcLatency shows `current GC mode SustainedLowLatency` and `active True`, PreJit shows `running` or `done`, GcTransitionCollect shows one collection, and HandlerProfiler shows `markers: attached to N events`.
2. Sleep to the next day: `Mods/PerformanceBundle/reports/session-*.txt` appears, with a "Days" line for the first day and handler tables. Run `perf_report`: GcTransitionCollect shows two collections.
3. Warp a few times and run `perf_report`: SpikeDiagnostics lists the recent warps, InvalidationReport lists what each warp invalidated, and the culprits list shows the slowest warps by location.
4. Run `perf_profile 5`: after about 5 seconds the tables are logged and the continuous markers stay attached. Set `ProfileMode` to `Manual` in Generic Mod Config Menu: the SMAPI log (Trace) says the markers were removed. Run `perf_profile 5` again: markers are inserted for the run and removed when it ends.
5. Set `ProfileMode` back to `Continuous`, then return to title: the log says the markers were removed, and the full report is logged at Info and written to the session file.
6. Set `ProfilerMaxOverheadUsPerTick` to 20 and play for over a minute with many mods: if the markers cost more than that, an Info line says continuous profiling is off for the session.
7. Close the game window normally: the session file's first line says `game closing`, and the same text is in the SMAPI log at Info. Kill the process instead: the file still holds the last checkpoint. Launch 11 sessions: only the newest 10 session files remain.
8. Turn `AdjacentPrewarm` on, warp several times, run `perf_report`: its counters (prewarmed, used, already cached, excluded) change.
9. Split-screen: start a second player, warp on both screens. `perf_report` labels the second screen's warps, and the session file gets one Days line per day (not one per screen).

## Design notes

- Every module is a separate switch, and turning one off (in Generic Mod Config Menu) unsubscribes it and restores anything it changed.
- The passive diagnostics measure their own handler time. `perf_report` shows it per tick against a target of under 50 microseconds.
- The diagnostics never listen to `AssetRequested`, which runs on every asset load.
- PreJit only uses reflection and the runtime's compile call on its thread, never game or SMAPI APIs, and skips static constructors so none runs off the main thread.

## Credits

Cover text uses the 'Stardew Valley' font by RRDome (https://fontstruct.com/fontstructions/show/1254619).
