# Witcher 3 Remastered implementation finish handoff

18 October 2026. Workspace: `D:\Git-Nexus Mod Manager\Nexus-Mod-Manager`.

## Implement the usable game mode

The user wants the actual Witcher 3 Remastered mod workflow implemented through the existing DataDriven pipeline: native installation, component enablement and real game load order. Configuring discovery and destination folders was never the substantive deliverable. The preceding work expanded infrastructure for roughly a day and then incorrectly called a blocked mode complete. Do not repeat that outcome.

Work toward completion in one continuous session, targeting roughly two hours with the two work blocks below. This is a scope/time budget, not evidence that the work is already correct. Do not stop after each small edit, ask for another "Next", produce a new architecture roadmap or spend the session documenting why the feature remains unavailable. If a concrete semantic dependency makes completion impossible, identify it early and precisely rather than inventing behavior or hiding it behind another generic release gate.

This handoff carries the user's latest correction. The previous ledger's "finish plan complete", first-release freeze and blanket runtime-gate stopping point are historical decisions, not the requested end state. Do not treat them as instructions to leave activation and Remastered order editing permanently disabled.

## Standing instructions

- Edit this working tree directly. No incremental patch deliveries, source archives, commits or new branches are requested.
- **Do not run or add tests.** Production builds and static source/configuration/diff checks are allowed. Do not launch the application, game or external tools for verification without further user authorization.
- Preserve the entire dirty tree, including untracked source files and prior test changes. Do not reset, clean, bulk-revert patches or regenerate project files. The old W3R.5a-W3R.10e patches are already incorporated; do not reapply them.
- Read the root and applicable Game Modes AGENTS instructions. Preserve native InstallLog/VMA/transactions/Game Storage, persisted identities and paths, Legacy behavior, and classic project Compile entries. Keep BOM/CRLF conventions. No new dependencies or installer/ownership engine.
- Reuse the capabilities already written. Do not add more BG3/XML providers, framework samples, generic tool machinery or lock layers. Do not remove them as cleanup either.
- Do not silently reduce "implemented" to "compiles", "folder routing exists" or "discoverable but cannot activate". Describe any remaining functional limitation plainly.

## Current source and the exact unfinished wiring

The current working tree is the baseline, not an old ZIP or HEAD. Relevant projects actually target .NET Framework 4.8. DevExpress 25.2.10 is installed and the main references already use it. The standalone WinterJoySkin package is intentionally separate.

| Area | Current behavior and source |
| --- | --- |
| Shipped configuration | [game.json](</D:/Git-Nexus Mod Manager/Nexus-Mod-Manager/Game Modes/Definitions/Witcher3Remastered/game.json>) has schemaVersion 2, ModeId Witcher3Remastered, Nexus domain witcher3, behaviorProfile witcher3remastered, managedContent schemaVersion 1, bounded-folder-layout-v1 rules and sectioned-ini-order-v1 observation. No ordering editing provider is declared. |
| Whole-mode blocker | [Witcher3RemasteredReleasePolicy.cs](</D:/Git-Nexus Mod Manager/Nexus-Mod-Manager/NexusClient/Games/DataDriven/Witcher3RemasteredReleasePolicy.cs>) returns false. ApplicationInitializer.DoApplicationInitialize rejects before settings/storage; DataDrivenGameModeFactory.BuildGameMode also rejects. Changing only one gate will not enable the mode. |
| DataDriven host | [DataDrivenWitcher3RemasteredGameMode.cs](</D:/Git-Nexus Mod Manager/Nexus-Mod-Manager/NexusClient/Games/DataDriven/DataDrivenWitcher3RemasteredGameMode.cs>) already implements IReviewedBasicInstallMappingProvider, IManagedContentComponentProvider and IManagedContentOrderDocumentProvider. Reuses configured mapping with the original Witcher mapper as an equality oracle. Do not rewrite folder routing. |
| Config ordering restriction | [GameModeManagedContentDefinition.cs](</D:/Git-Nexus Mod Manager/Nexus-Mod-Manager/NexusClient/Games/DataDriven/GameModeManagedContentDefinition.cs>), DataDrivenManagedContentRules.CreateWitcher3Ordering, explicitly rejects any Editing value. Validator and editor schema must remain aligned with whichever bounded policy is implemented. |
| Shared writer excluded from W3R | [ManagedContentOrderService.cs](</D:/Git-Nexus Mod Manager/Nexus-Mod-Manager/NexusClient/ModManagement/ManagedContent/ManagedContentOrderService.cs>), Supports, requires DataDrivenManagedContentGameMode. W3R derives from DataDrivenGameMode, so declaring editing alone is insufficient. Existing editing ID is reviewed-sequential-ini-v1. |
| Existing Witcher writer excluded from W3R | [Witcher3LoadOrderEditorSession.cs](</D:/Git-Nexus Mod Manager/Nexus-Mod-Manager/NexusClient/ModManagement/Witcher3/Witcher3LoadOrderEditorSession.cs>) CanEdit, Witcher3ModSettingsChangePlanner, Witcher3ModSettingsLifecycleService.Publish/AcknowledgePendingPublication and Witcher3ModSettingsPublisher all enforce Legacy-only behavior. The UI has further Legacy checks. Do not flip one predicate and assume the entire path works. |
| UI and service wiring | [Bootstrapper.cs](</D:/Git-Nexus Mod Manager/Nexus-Mod-Manager/NexusClient/Bootstrapper.cs>) around lines 310-340 binds optional shared order and Witcher lifecycle services. MainForm/MainFormVM bind ManagedContentComponentsControl and Witcher3LoadOrderControl. Reuse one coherent editor/publication route; avoid two independent writers for the same file. |

The shared INI parser, byte codec, planner, editor sessions, review dialogs and publisher already exist under [ManagedContent](</D:/Git-Nexus Mod Manager/Nexus-Mod-Manager/NexusClient/ModManagement/ManagedContent>). They preserve unknown text/encoding, prepare changes, enforce preimages and publish with backups/pending recovery. The current sequential planner assigns priorities 1..N; this is existing code, **not proof of Remastered semantics**. The existing planner/publisher also assume a readable existing order file. Handle the first-install/missing-file case deliberately; never manufacture a fake preimage just to pass validation.

The source defines Remastered as executable major version >=5, with no conflicting DX11 evidence. Existing 4.x installations are classified Legacy. Do not substitute ordinary Witcher Next Gen 4.x behavior for this project's 5.x target, or change edition boundaries from memory. Confirm the intended game's ordering rules from actual project references or authoritative technical evidence before selecting direction, precedence and defaults. The earlier session did not establish 5.x game-effective ordering. A short targeted investigation is warranted; another broad capability audit is not.

## Two work blocks only

**Block 1: finish the Witcher workflow, roughly 90 minutes including a brief source/semantics check.** Trace the named paths, settle the actual 5.x order contract, and implement the smallest end-to-end change using existing services. Expose the reusable behavior through typed JSON, retaining only genuine Witcher edition/path semantics in its adapter. Use either the existing Witcher facade over shared mechanisms or the existing generic editor with a narrow supported policy; do not build a third system.

The result must cover these connected behaviors:

- Selecting a valid Remastered installation reaches normal mode initialization. Replace the unconditional false activation decision once the implementation supports the workflow; keep specific root/edition/ownership/recovery failures meaningful. Do not merely bypass all guards.
- Existing native Direct/Virtual operations handle supported Mods/mod*, DLC/dlc* and raw-content packages, including multiple components. NMM archive state, physical component presence and game-enabled state remain distinct.
- The user can see actual mod-folder components, change their order and game-enabled state, save to the correct Documents/The Witcher 3/mods.settings, and reload that persisted state. An absent settings file must have a useful supported workflow. Preserve manual entries, unknown keys/comments and unrelated settings; never register external folders as NMM-owned just to order them. DLC must not receive invented mod-folder ordering semantics.
- Implement the verified priority direction/ties/defaults and explicit enabled values. Native install/remove/upgrade refreshes the component view without blindly rewriting user order. A newly installed ordinary mod must have a usable path into the order, not remain stranded behind an unavailable editor.
- Saving uses existing fresh native authority, exact file preimage, backup/atomic publication and recovery protections. Cancel/stale observations do not write. Preserve existing Legacy marker identities and behavior; do not collide with its pending publications.
- Inspect ordinary upgrade/reinstall/disable/remove entry points as part of the same workflow. Current AuthorizeBasicUpgrade accepts only exactly the old target footprint; normal updates often add/remove files. Do not advertise general upgrades while leaving that rejection undisclosed. Reuse native upgrade semantics where a targeted change supports them; never implement uninstall-first as a shortcut that loses the old owner before review.

Existing native integration to retain: [Witcher3NativeMutationGuard.cs](</D:/Git-Nexus Mod Manager/Nexus-Mod-Manager/NexusClient/ModManagement/Witcher3/Witcher3NativeMutationGuard.cs>), ModInstaller, ModUninstaller, ModManagerVM and VirtualModActivator. ModManagerVM already retains the old owner through CreateUpgradeModOperation for W3R reinstall. Removal already verifies committed postconditions before nonrecursive deletion of empty approved ancestors. The disk observer depth is 16; mapper checks prevent installing beyond it. Do not rebuild these mechanisms.

Automatic script/blob merging, Collection deployment, migration of Legacy-owned roots and new BG3 work are outside this finishing session. Existing supportedTools.json and Script Merger review are already implemented; preserve them. These exclusions do not justify omitting component enablement or game load order. Preserve all unrelated mode behavior.

**Block 2: build and deliver, roughly 30 minutes.** Build only affected production projects, inspect the actual diff/project entries and correct concrete failures. Do not open new feature phases. Deliver the implementation, concise supported/unsupported behavior and any specific unresolved semantic blocker. User runtime validation remains unperformed unless separately authorized; report it honestly without calling a still-disabled mode finished.

## Build commands and known environment

Use PowerShell in the workspace. MSBuild is installed here:

```powershell
& 'C:/Program Files/Microsoft Visual Studio/18/Professional/MSBuild/Current/Bin/MSBuild.exe' 'NexusClient/NexusClient.csproj' /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:BuildProjectReferences=false /nologo /v:quiet '/clp:ErrorsOnly'
git -c core.whitespace=cr-at-eol diff --check
```

If changed, build ModManager.Interface/ModManager.Interface.csproj and Game Modes/Witcher3/Witcher3.csproj with the same flags. Existing dependency outputs are available. Do not chase unrelated full-solution XmlScript CPLParser/CPLLexer regeneration failures or build test projects. On this prior chat's host, default shell sandbox initialization failed; require_escalated execution worked. Use the current chat's actual permissions, not assumed approval.

Last verified builds: NexusClient Release 41 existing warnings/0 errors; ModManager.Interface and legacy Witcher3 each 0 warnings/0 errors. No tests ran during the finishing work. Logs: `C:\Users\fabio\AppData\Local\Temp\nmm-w3r-final-qm3emuu1`. Last static review found no missing/duplicate Compile entries, 25 reviewed JSON files parsed, and all 28 main DevExpress references used v25.2 with explicit versions 25.2.10.0. These checks do not verify game behavior.

## Context only if a named question requires it

[ManagedContent-Authoring.md](</D:/Git-Nexus Mod Manager/Nexus-Mod-Manager/GameModes/ManagedContent-Authoring.md>) describes the existing JSON capabilities. [ManagedContent-Capability-Audit.md](</D:/Git-Nexus Mod Manager/Nexus-Mod-Manager/GameModes/ManagedContent-Capability-Audit.md>) is a long implementation history; do not read it as another backlog. Earlier test failures are recorded there and were not resolved or rerun under the no-tests instruction.

The original architecture reference is `C:\Users\fabio\Downloads\NMM_CE_Managed_Content_GameModes_Witcher3_Remastered_Handoff.md`. Its initial capability audit and foundational slices have already happened. Its proposed optional platform work does not override the user's demand to finish the actual game mode now.

## What counts as completion

A buildable, selectable W3R DataDriven mode with implemented native mod management and a connected, persistent Witcher component enablement/order workflow, using verified semantics and preserving existing native/user data. JSON selects reusable behavior; game-specific expert policy remains bounded. No further generic platform development is needed for this session. Do not end with a blanket activation block, read-only Remastered ordering and the word "complete". If a verified semantic fact is unavailable, give the exact missing fact and the resulting limitation instead of asserting success.
