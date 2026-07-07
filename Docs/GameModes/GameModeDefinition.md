# Game Mode Definition

Data-driven game modes describe the parts of an old compiled `GameMode` project that can be represented safely as data. The runtime definitions are packaged under `GameModes\Definitions\<GameId>\game.json`; the `GameModes` folder at repository root contains authoring aids.

## Authoring Files

- `GameModes/Templates/GameMode.template.jsonc`: commented starter definition for new modes.
- `GameModes/schema/game-mode.schema.json`: JSON schema for editor validation.
- `GameModes/Cyberpunk2077.json`: flat example based on the Cyberpunk 2077 runtime definition.
- `Game Modes/Definitions/<GameId>/game.json`: runtime definitions copied into the output as `GameModes/Definitions/<GameId>/game.json`.
- `Game Modes/Definitions/<GameId>/supportedTools.json`: optional split file for supported tools.

## Validator Command

Run NMM with the validation command to check data-driven definitions without opening the main UI:

```text
NexusClient.exe --validate-game-definitions
NexusClient.exe --validate-game-definitions "Game Modes\Definitions\Cyberpunk2077\game.json"
NexusClient.exe --validate-game-definitions "Game Modes\Definitions"
```

With no path, the command validates `GameModes\Definitions` beside the executable. A non-zero exit code means at least one definition has errors. The command uses the same `GameModeDefinitionValidator` used by runtime loading.

## Section Mapping

### Root

`schemaVersion` maps to the version of `GameModeDefinition` expected by the loader. Current value is `1`.

`modeId` maps to `IGameModeDescriptor.ModeId`. It replaces the constant or overridden id used by old `GameModeDescriptor` classes. Treat it as persistent user data: changing it breaks existing settings and installed game references.

`name` maps to `IGameModeDescriptor.Name` and replaces the display name in old descriptors.

`behaviorProfile` chooses the runtime base class. `generic` maps to `GameModeBase`; `gamebryo` maps to `GamebryoGameModeBase` through `DataDrivenGamebryoGameMode`.

`installerProfile` selects installer behavior. `gamebryo` preserves Data-folder/FOMOD behavior for Bethesda-style games.

`legacyFallback` controls replacement of old DLL modes. If true and a legacy DLL registers the same `modeId`, the JSON definition is skipped.

`gameExecutables` maps to old `GameExecutables`, executable validation, and game-version probing.

`stopFolders` maps to archive-root detection used when mod authors omit the correct top-level folder.

`supportedFormats` maps to `SupportedFormats`, usually `fomod`.

`compatibilityNotes` is maintainer documentation for preserved legacy behavior.

### discovery

Replaces per-game Steam/GOG/Epic/Microsoft Store path lookup code from descriptors, setup VMs, and launchers. Prefer `stores` for new definitions. The legacy shortcuts `steamAppId`, `steamInstallFolderName`, `steamExecutableName`, `gogRegistryKey`, and `gogPathValueName` are retained for converted definitions.

### plugin

Maps to plugin-management overrides such as `UsesPlugins`, `PluginDirectorySuffix`, `SupportsPluginAutoSorting`, `MaxAllowedActivePluginsCount`, and official unmanaged plugin list handling.

`supportsPluginAutoSorting` is currently valid only for `behaviorProfile: gamebryo` because the sorter infrastructure is still tied to Gamebryo-style plugin management.

### launcher

Maps to old `GameLauncher` classes: default executable, command names, display text, and whether the user can configure a custom launch command.

### resources

Maps to embedded resource usage from old game mode projects. Values must be file names beside the runtime `game.json`:

- `iconPath`: game logo/icon.
- `categoriesPath`: default categories XML.
- `baseFilesPath`: base-game file list used by Gamebryo-style modes.

### theme

Maps to descriptor/UI color hints such as the old per-game primary color.

### setup

`useGenericSetup` replaces simple per-game `SetupForm` and setup VM classes with `DataDrivenSetupForm` where no custom setup behavior is needed.

### settings

`useGenericSettings` replaces simple per-game general settings pages with `DataDrivenGameModeSettingsPage`. `allowCustomLaunchCommand` maps to the old custom launch command setting.

### modInstall

Maps to old `GetModFormatAdjustedPath`, `HardlinkRequiredFilesType`, `RealFileRequired`, and game-root install overrides.

`pathAdjustmentProfile` selects known path-fix logic for games where mod authors often omit required folders. Current profiles are `none`, `cyberpunk2077`, `subnautica`, `stardewvalley`, `sims4`, and `nomanssky`.

`supportsGameRootInstall` enables explicit installs beside the game executable where the old Gamebryo Data-folder-only behavior is too restrictive.

### gamebryo

Only applies to `behaviorProfile: gamebryo`. It maps to `GamebryoSettingsFiles`, script extender executable detection, INI paths, renderer path, and plugins.txt path.

Path placeholders supported by the runtime include:

- `{PersonalData}`
- `{LocalApplicationData}`
- `{GamePath}`
- `{ExecutablePath}`
- `{ModeId}`
- `{UserGameData}`

Do not use parent-directory traversal. The validator rejects paths containing `..` segments.

### supportedTools

Maps to old `SupportedToolsLauncher`, `ToolLauncher`, and supported-tools settings classes. Tools can be declared inline or in `supportedTools.json` beside `game.json`.

Security rules enforced by the validator:

- `executableName` and `executableNames` must be file names, not paths.
- blocked shell executables such as `cmd.exe`, `powershell.exe`, and `pwsh.exe` are rejected.
- `arguments` is rejected; use `argumentTokens` instead.
- resource references must be file names only.
- configured paths and path placeholders cannot traverse upward with `..`.

## Adding a New Definition

1. Copy `GameModes/Templates/GameMode.template.jsonc` while authoring.
2. Convert it to runtime JSON as `Game Modes/Definitions/<GameId>/game.json`.
3. Put icons, categories, base file lists, and optional `supportedTools.json` beside that `game.json`.
4. Run `NexusClient.exe --validate-game-definitions <path>`.
5. Test game detection, setup, launch, install, uninstall, and plugin behavior.

Keep legacy DLL game modes until the JSON definition fully preserves meaningful behavior.