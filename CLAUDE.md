# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Purpose

Monorepo for Stardew Valley mods (SMAPI C# mods and Content Patcher content packs). No mods exist yet.

## Versions (checked 2026-11-21)

| Component | Version | Source |
|---|---|---|
| Stardew Valley | 1.6.15 build 24356 (installed) | local `SMAPI-latest.txt` header |
| SMAPI | 4.5.2 (latest release, 2026-03-14; requires game 1.6.14+) | github.com/Pathoschild/SMAPI release notes; also installed |
| Game/SMAPI runtime | .NET 6 (`tfm: net6.0`) | `Stardew Valley.runtimeconfig.json` in game folder |
| `Pathoschild.Stardew.ModBuildConfig` | 4.4.0 (2025-04-13, SMAPI 4.1.0+) | nuget.org, SMAPI `mod-package-release-notes.md` |
| Content Patcher | 2.9.1 (installed and latest release); use `"Format": "2.9.0"` | StardewMods `ContentPatcher/docs` |
| StardewXnbHack | 1.1.2 | GitHub releases |

Re-check these before pinning versions in new projects. stardewvalleywiki.com blocks automated fetches (Cloudflare); use the GitHub docs in Pathoschild/SMAPI and Pathoschild/StardewMods instead.

## Environment (this machine, Linux / Nobara Fedora 44)

- Game folder: `~/.local/share/Steam/steamapps/common/Stardew Valley`
- SMAPI is installed: the `StardewValley` launcher is SMAPI's bash script (original saved as `StardewValley-original`), so launching from Steam starts SMAPI.
- `Mods/` already holds ~250 player mods (incl. ContentPatcher, ConsoleCommands, SaveBackup). Builds deploy into this same folder by default; see "Isolated test mods folder".
- SMAPI log: `~/.config/StardewValley/ErrorLogs/SMAPI-latest.txt`. Saves: `~/.config/StardewValley/Saves`.
- `dotnet` is NOT installed. Fedora/Nobara repos offer `dotnet-sdk-8.0`, `-9.0`, `-10.0`, `-11.0` only (no 6.0).
  - The wiki's Get Started guide says to install the .NET 6 SDK. Options: Microsoft's `dotnet-install.sh --channel 6.0`, or a newer SDK (e.g. `sudo dnf install dotnet-sdk-8.0`) building `net6.0`. Unverified: that ModBuildConfig 4.4.0 works without warnings on SDK 8+; expect an end-of-life warning (NETSDK1138) for `net6.0`. Ask the user before installing anything.

## Choosing a mod type

- **Content Patcher content pack** (JSON + assets, no compiling): use it to change or add game assets such as sprites, maps, dialogue, and `Data/*` entries (items, NPCs, shops, events). Prefer it whenever the change can be expressed as an asset edit.
- **SMAPI C# mod**: use it for new behavior, such as reacting to events, custom logic, UI, console commands, or APIs for other mods.
- A C# mod can bundle content packs (see ModBuildConfig `ContentPacks` below).

## Repo layout

```
stardew-valley-mods/
  StardewValleyMods.sln          # optional; add each C# project
  <ModName>/                     # C# mod: folder name = project name = default Mods folder name
    <ModName>.csproj
    manifest.json
    ModEntry.cs
    i18n/default.json            # optional
    assets/                      # optional
  [CP] <ModName>/                # content pack
    manifest.json
    content.json
    assets/
```

- One folder per mod. Add C# projects to the solution with `dotnet sln add <ModName>/<ModName>.csproj`.
- UniqueID convention: `JamesKempf.<ModName>` (no spaces or special characters), unless the user specifies otherwise. Never change a released mod's UniqueID.

## manifest.json

Schema: `https://smapi.io/schemas/manifest.json`. Required: `Name`, `Author`, `Version`, `Description`, `UniqueID`. A mod has exactly one of `EntryDll` (C#) or `ContentPackFor` (content pack).

```json
{
  "$schema": "https://smapi.io/schemas/manifest.json",
  "Name": "Example Mod",
  "Author": "James Kempf",
  "Version": "%ProjectVersion%",
  "Description": "One sentence.",
  "UniqueID": "JamesKempf.ExampleMod",
  "EntryDll": "ExampleMod.dll",
  "MinimumApiVersion": "4.5.0",
  "UpdateKeys": [ "Nexus:12345" ]
}
```

- `%ProjectVersion%` is replaced by the csproj `<Version>` (ModBuildConfig 4.4.0+, C# projects only). Content packs use a literal version, e.g. `"1.0.0"`.
- Content pack: replace `EntryDll` with `"ContentPackFor": { "UniqueID": "Pathoschild.ContentPatcher" }`.
- `UpdateKeys` formats: `Nexus:<id>`, `ModDrop:<id>`, `CurseForge:<id>`, `GitHub:<owner>/<repo>`, `Chucklefish:<id>`. Omit until the mod page exists.
- Optional: `MinimumGameVersion`, `Dependencies` (`[{ "UniqueID": "...", "IsRequired": true }]`).

## C# mod

`<ModName>.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <Version>1.0.0</Version>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Pathoschild.Stardew.ModBuildConfig" Version="4.4.0" />
  </ItemGroup>
</Project>
```

Create with `dotnet new classlib -n <ModName> -f net6.0`, delete `Class1.cs`, then `dotnet add package Pathoschild.Stardew.ModBuildConfig`.

`ModEntry.cs`:

```csharp
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace ExampleMod;

internal sealed class ModEntry : Mod
{
    private ModConfig Config = null!;

    public override void Entry(IModHelper helper)
    {
        this.Config = helper.ReadConfig<ModConfig>();   // creates config.json from defaults
        helper.Events.Input.ButtonPressed += this.OnButtonPressed;
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady) return;
        this.Monitor.Log($"Pressed {e.Button}", LogLevel.Debug);
    }
}

internal sealed class ModConfig { public bool Enabled { get; set; } = true; }
```

Key APIs (from `IModHelper` / `Mod` in SMAPI source):
- `Mod.Helper`, `Mod.Monitor` (logging), `Mod.ModManifest`; override `GetApi()` to expose an API to other mods.
- `helper.Events.{GameLoop, Input, Display, World, Player, Content, Multiplayer, Specialized}`. For example, `GameLoop.SaveLoaded` and `DayStarted`; `Content.AssetRequested` edits assets from C#.
- `helper.ReadConfig<T>()` / `WriteConfig<T>()` for `config.json`. Do not commit `config.json`; it is generated.
- `helper.Data` stores per-save or global JSON data. `helper.ModContent` loads the mod's own files. `helper.GameContent` loads game assets.
- `helper.Translation.Get("key")` reads `i18n/<locale>.json` (`default.json` is the fallback). Reload in game with `reload_i18n`.
- `helper.ConsoleCommands.Add(...)`, `helper.Reflection`, `helper.ModRegistry.GetApi<T>(uniqueId)`, `helper.Multiplayer`.

## ModBuildConfig behavior

- `dotnet build` copies the built mod into `$(GameModsPath)/<ModFolderName>` (default `$(GamePath)/Mods/<ProjectName>`) and creates a release zip in the project's `bin/` folder. Rebuild (`dotnet build --no-incremental`) to refresh the zip.
- Game path is auto-detected from default install paths. If detection fails, create `~/stardewvalley.targets`:
  ```xml
  <Project><PropertyGroup>
    <GamePath>/home/jkempf/.local/share/Steam/steamapps/common/Stardew Valley</GamePath>
  </PropertyGroup></Project>
  ```
  Unverified: whether auto-detection finds this Steam path on Linux. Check the build output for path errors.
- Properties: `EnableModDeploy` (true), `EnableModZip` (true), `ModFolderName`, `ModZipPath`, `GameModsPath`, `EnableHarmony` (false), `BundleExtraAssemblies` (only `ThirdParty` if bundling 3rd-party DLLs), `IgnoreModFilePaths` / `IgnoreModFilePatterns`.
- Bundle content packs with a C# mod:
  `<ItemGroup><ContentPacks Include="[CP] Foo" Version="$(Version)" /></ItemGroup>`. The bundled pack's manifest version must match.
- Extra non-code files (e.g. `assets/`) must be in the build output to be deployed. If a file is missing from the deployed folder, add it with `CopyToOutputDirectory`.
- IDE debugger attach is disabled on Linux/macOS. Debug with `Monitor.Log` and the SMAPI console.

### Isolated test mods folder (optional)

To keep dev mods out of the player's `Mods/`, set `<GameModsPath>$(GamePath)/Mods (dev)</GameModsPath>` (in the csproj or `~/stardewvalley.targets`). Launch with `SMAPI_MODS_PATH="Mods (dev)"`. That folder also needs dependencies such as ContentPatcher and ConsoleCommands copied in. SMAPI docs say the `--mods-path` argument may not work on Linux, so use the env var.

## Content Patcher pack

`content.json`:

```json
{
  "Format": "2.9.0",
  "ConfigSchema": {
    "EnableRecolor": { "AllowValues": "true, false", "Default": true }
  },
  "Changes": [
    { "LogName": "Abigail portrait", "Action": "Load", "Target": "Portraits/Abigail", "FromFile": "assets/abigail.png",
      "When": { "EnableRecolor": true } },
    { "Action": "EditData", "Target": "Data/Objects",
      "Entries": { "{{ModId}}_ExampleItem": { /* object data fields */ } } },
    { "Action": "EditImage", "Target": "Maps/springobjects", "FromFile": "assets/sprites.png",
      "FromArea": { "X": 0, "Y": 0, "Width": 16, "Height": 16 }, "ToArea": { "X": 0, "Y": 0, "Width": 16, "Height": 16 } },
    { "Action": "Include", "FromFile": "assets/{{season}}.json" }
  ]
}
```

- Actions: `Load` (replace whole asset), `EditData`, `EditImage`, `EditMap`, `Include` (split patches across files).
- `Target` is the game asset name without extension (e.g. `Data/Objects`, `Portraits/Abigail`).
- Tokens use `{{...}}` (e.g. `{{season}}`, `{{i18n:key}}`, `{{ModId}}`). Prefix new 1.6 data IDs with `{{ModId}}_`. `When` gates a patch on conditions or config values.
- `ConfigSchema` fields become the pack's `config.json` options.
- Validate `manifest.json` and `content.json` at https://smapi.io/json (format "Manifest" / "Content Patcher").
- Content packs have no build step. Deploy by copying, e.g. `rsync -a --delete "[CP] Foo/" "$GAME/Mods/[CP] Foo/"`. Zip the folder for release.

## Running and testing

- Launch: through Steam, or `"$HOME/.local/share/Steam/steamapps/common/Stardew Valley/StardewValley"`. Add `SMAPI_USE_CURRENT_SHELL=true` to log to the current shell instead of a new terminal.
- Developer mode: `SMAPI_DEVELOPER_MODE=true`, or enable it in the SMAPI options via Generic Mod Config Menu (SMAPI 4.5.0+ has no separate developer build).
- Log: `~/.config/StardewValley/ErrorLogs/SMAPI-latest.txt`. Share or parse it at https://smapi.io/log.
- SMAPI console commands: `help`, `reload_i18n`, `harmony_summary`. The ConsoleCommands mod adds `debug <game command>`, `player_add`, `list_items`, `world_settime`, `world_setday`, `world_freezetime`, `show_data_files`, `log_context`.
- Content Patcher commands: `patch summary [UniqueID] [full]`, `patch reload <UniqueID>` (re-read JSON without restarting), `patch export <asset>` (writes to `<game>/patch export/`), `patch parse`, `patch invalidate <asset>`.
- CP debug mode: set `EnableDebugFeatures: true` in `Mods/ContentPatcher/config.json`. In game, `F3` shows textures and `Ctrl` cycles through them. For verbose logs, set `VerboseLogging` in `smapi-internal/StardewModdingAPI.config.json` (slow). Add `LogName` to patches so they can be found in the log.

## Unpacking game content (reference only)

Extract the StardewXnbHack Linux release into the game folder and run `./StardewXnbHack`. It writes `Content (unpacked)/` (`.png`, `.tmx`, CP-compatible `.json`). It unpacks only (no repacking). Never copy unpacked game assets into this repo or a release.

## Harmony

- Use Harmony only when events, the content API, or Content Patcher can't do the job. Enable it with `<EnableHarmony>true</EnableHarmony>`, then `new Harmony(this.ModManifest.UniqueID).Patch(...)` / `PatchAll()`.
- Patches can conflict with other mods. Use `harmony_summary` to inspect them, and test with the user's installed mod set.
- SMAPI disables .NET tiered compilation because it causes Harmony issues (comment in `StardewModdingAPI.runtimeconfig.json`). Don't assume behavior seen outside SMAPI.
- Game updates can rename or inline patched methods. Re-check patches after each game patch.

## Release

1. Bump `<Version>` (C#) or manifest `Version` (CP) using semver.
2. Rebuild. The zip is in `<ModName>/bin/`. For CP, zip the `[CP] <ModName>` folder so the archive contains the folder.
3. Upload to Nexus Mods (nexusmods.com/stardewvalley) and/or ModDrop. Add the page ID to `UpdateKeys` and release again so SMAPI update checks work.

## Rules for Claude

- Never commit unless asked. Do not modify the player's existing mods in `Mods/` or their saves.
- Prefer Content Patcher over C#, and C# events/content API over Harmony.
