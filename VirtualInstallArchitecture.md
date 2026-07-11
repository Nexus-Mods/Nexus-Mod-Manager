# Virtual Install Architecture

## Purpose

The virtual install system is the single authority for constructing and restoring the game-visible mod file state. It must expose a coherent orchestration boundary so callers request an operation rather than independently changing links, profiles, plugins, or persistence.

## Owned Responsibilities

The virtual install system owns:

- Virtual mod metadata.
- File link metadata.
- Active and inactive conflict state, including link priority.
- Source-file resolution within `VirtualInstall` and `NMMLink` roots.
- Materialization into the game folder using the required link or copy strategy.
- Profile link restoration.
- Persistence, validation, migration, and recovery of virtual state.

It owns the relationship between a desired mod state and the concrete files exposed to the game.

## Responsibilities It Must Not Absorb

The virtual install system must not casually absorb or duplicate responsibility for:

- Mod archive installation or extraction in `ModInstaller`.
- Removal of archive/install records in `ModUninstaller`.
- Generic profile selection and UI workflow in `ProfileSwitchSetupTask`.
- Batch-operation policy in `DeactivateMultipleModsTask` and `DeleteMultipleModsTask`.
- Plugin activation policy owned by the plugin subsystem.
- UI progress presentation, prompts, and view-model updates.
- Direct XML or other virtual-state writes from unrelated flows.

Those components may request a virtual install operation, but they must not modify virtual links, conflict state, or persisted virtual state themselves.

## Required Orchestration Boundary

Activation, deactivation, profile restoration, purge, repair, and migration must enter through one virtual-install orchestration API. The API is responsible for planning the change, applying filesystem work, updating virtual state, and committing persistence as one recoverable operation.

Callers provide intent and domain context. They do not choose individual link operations, write activation XML, or independently combine plugin and filesystem actions.

## Current Boundary Violation

`ModManager` currently wires activation, deletion, deactivation, profile setup, and the virtual activator across several task flows. That spreads orchestration decisions across multiple owners and permits direct state changes outside a single transaction boundary.

This structure makes it difficult to reason about correctness, recover interrupted work, measure deployment performance, or replace XML persistence. Future work should route those flows through the virtual-install orchestration API while retaining existing caller-facing behavior during migration.

## Known Virtual-State Mutation Entry Points

The following current flows are allowed to be audited and gradually redirected through the new orchestration boundary. This inventory defines the initial migration scope; adding another mutation path requires documenting it here before changing its behavior.

- `LinkActivationTask`
  - Enumerates virtual install files.
  - Calls `IModLinkInstaller.AddFileLink`.
  - Currently mixes file linking, plugin activation, and progress reporting.
- `VirtualModActivator`
  - Owns the current in-memory virtual mod and link lists.
  - Performs conflict checks, link creation and removal, persistence, profile copy, purge, and migration helpers.
  - Remains the main compatibility implementation during migration, but should shrink as responsibilities move behind the orchestration boundary.
- `ModLinkInstaller`
  - Performs overwrite and conflict decisions, then delegates link creation to `VirtualModActivator`.
  - Should eventually become part of the deployment planning and apply steps.
- `ModInstaller`
  - Runs activation and install scripts.
  - Currently causes virtual-state persistence through `VirtualModActivator.SaveList`.
  - Should request a deployment commit instead of directly causing persistence.
- `ModUninstaller`
  - Runs uninstall and deactivation flows.
  - Must not independently mutate virtual links outside the orchestration boundary.
- `ProfileActivationTask`
  - Purges and reinstalls profile links.
  - Should eventually compute a profile deployment plan and execute it through the orchestration API.
- `ProfileSwitchSetupTask`
  - Mixes profile selection, virtual-file disablement, uninstall, reinstall, XML cleanup, and progress reporting.
  - Should be split so the profile workflow requests virtual deployment operations instead of performing them inline.
- `DeactivateMultipleModsTask`
  - Repeatedly calls virtual deactivation logic for individual mods.
  - Should use a batch deployment operation and commit once.
- `DeleteMultipleModsTask`
  - Mixes deletion, deactivation, registry updates, and XML cleanup.
  - Should route virtual-state changes through the deployment boundary.

## Phase 0 Acceptance Criteria

Phase 0 is complete when:

- The virtual install ownership and orchestration boundary is documented.
- All known virtual-state mutation entry points are listed.
- No runtime behavior has changed.
- No XML schema has changed.
- No SQLite database or other persistence backend has been introduced.
- The next implementation phase has one clear target: centralize one operation at a time behind a virtual deployment service.

## Phase 1 Scope

Phase 1 introduces `IVirtualDeploymentService` as a compatibility boundary for mod link activation. `LinkActivationTask` requests deployment and retains UI progress and plugin activation policy. `VirtualModActivator` remains the low-level compatibility backend; no persistence format or profile behavior changes in this phase.

## Migration Principle

The boundary is an architectural target, not permission for a broad rewrite. New work must centralize one operation at a time, preserve existing profiles and installs, and leave compatibility adapters in place until all callers use the common orchestration path.
