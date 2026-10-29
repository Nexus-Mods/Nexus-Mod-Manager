# Load-order infrastructure and implementation handoff

## Scope and current status

The BG3 work now includes package inventory, native order reading/editing/saving/recovery, persistent order policies across native installation and Collection operations, NMM profile companions, actionable diagnosis, deterministic sorting, recoverable cleanup and a generic DevExpress Load Order page. The UI, lifecycle and repair changes have received static inspection only; compilation, visual/runtime validation and test execution remain with the maintainer.

Implemented:

- Optional typed `loadOrder` configuration, runtime validation and JSON schema.
- Immutable capability on data-driven modes through `ILoadOrderCapabilityProvider.LoadOrder`.
- Built-in inventory provider dispatch by provider identifier, without a BG3 ModeId branch.
- Read-only, bounded top-level folder scanning, including hidden entries, non-package files and subfolders.
- Immutable package/module metadata, positive content feature observations, dependencies, conflicts, diagnostics, inspection completeness and split-package relationships.
- A bounded process client and strict versioned JSON contract for a separately packaged LSLib reader.
- Native LSX observation and detached inclusion/order drafts, retaining campaign records and unknown XML fields.
- Native publication coordinated with winning module/Collection owner values, exact original backups and explicit interrupted-save recovery.
- Versioned preferences in the existing native value owner history, with newly supplied curator order taking precedence over existing positions.
- Scoped Collection replay/removal that retains current unrelated modules, plus exact legacy-recipe fallback.
- Additive NMM profile snapshots restored after deployment and compensated through the existing profile rollback flow.
- Localizable diagnosis, bounded stable dependency sorting, explicit inclusion/exclusion/metadata repair previews, and journaled cleanup of eligible unowned unexpected folder content.
- A capability-driven Load Order document with one inclusion/order list, native DevExpress feature badges, mod/dependency details, actionable problems, Save/Discard, reviewed automatic repairs and explicit recovery controls.
- Native PAK-only registration from selected archive packages before mutation, and fresh scoped curator recipe regeneration through Collection review/repair.
- Automatic Release helper publication with shared dependency/runtime pins, complete-file integrity manifests and a required installer verification gate.
- Isolated regression test source. Never compile or execute tests unless the maintainer explicitly requests it; the maintainer handles test verification.

The shipped BG3 definition enables the capability and the generic Load Order page. Package scanning starts on activation of that page rather than application startup. The LSLib helper is implemented under `Support/LarianPackageInspector`. Release builds now invoke `Build.ps1` automatically through `Support/BuildTasks/LoadOrderHelper.targets`, publishing into the actual application output's `Tools/LoadOrder`. Ordinary Debug/designer builds do not invoke publication. `Dependencies.props` pins LSLib v1.20.4 by its archive SHA-256 and the self-contained .NET 8.0.32 runtime. Release agents require a .NET 8-or-newer SDK. The classic application and existing projects retain their formats/frameworks.

Publication verifies freshly extracted dependency bytes, runtime identity, source pre/postimages and the full output file set before promoting a staged directory. `package-manifest.json` records source/pin identity and every file's length/SHA-256. `Setup/setup.iss` requires `Build.ps1 -VerifyOnly` to pass before compilation and no longer skips missing helper output. That mode performs no SDK/network/build/helper/test execution. The installer replaces only its application-owned helper directory to remove stale runtime files and installs the win-x64 helper only on 64-bit Windows, preserving NMM's x86 eligibility. See the helper README for servicing and custom-output details. A manually incomplete development output still produces `InspectorUnavailable`; it is never treated as a successful empty metadata scan.

The helper reads multiple modules, relationships, tags, Lua/Osiris evidence, extender requirements and recognizable Mod Fixer/override content in memory. Split membership is verified against the primary header. Solid archives return Partial because LSLib otherwise decompresses the whole archive. Unclassified metadata-free packages also remain Partial. Filesystem ownership, game activation and installed extender availability are not inferred. Only production helper build/publication has been verified; no package fixture, helper process, test compilation or test execution was performed in this helper implementation slice.

Existing BG3 structured effects and native ownership remain the authority for installation behavior. Capability-enabled modes wrap the existing record installer to reapply preferences; modes without the capability keep the old adapters. Discovery, custom paths and legacy definition/profile fallbacks remain intact.

## Configuration

```json
"loadOrder": {
  "schemaVersion": 1,
  "enabled": true,
  "provider": "larian-module-settings-v1",
  "revision": 1,
  "packageRoot": "ManagedInstallation",
  "profileSource": "structuredEffects",
  "directoryPolicy": {
    "allowedExtensions": [".pak"],
    "allowSubdirectories": false
  }
}
```

`ManagedInstallation` resolves through `ModDeploymentTargetResolver` to the existing Data deployment root. For BG3 this is the configured package directory, including existing custom paths; it is not the discovered executable/Data/bin root.

`structuredEffects` is the sole source for the profile-relative LocalAppData root, manifest filename and excluded profiles. An enabled declaration requires compatible `Mods` / `ModuleShortDesc` / `UUID` records and Folder, Name and Version64 fields. No second profile path is introduced. Absence or a valid disabled declaration exposes no capability. Disabled declarations still require a valid provider/version/root/directory declaration, but do not require structured effects. Unsupported combinations are rejected through the existing definition validator and loader isolation.

Optional `builtInModuleIdentities` contains at most 256 unique nonempty UUIDs. These game-provided module dependencies are not diagnosed as missing downloadable packages. The BG3 definition supplies core identities from [upstream's vanilla-module data](https://github.com/LaughingLeader/BG3ModManager/blob/master/src/GUI/Resources/IgnoredMods.json). This is configuration data, not a game-name branch in UI or a claim that an installed game/DLC version has been verified. The field is additive; older definitions default to an empty list.

Do not repurpose `managedContent.componentOrdering`: it is an existing INI/folder capability with different semantics. The current inventory provider rejects simultaneous declarations.

## Integration contracts

`LoadOrderProviderRegistry.Create(IGameMode)` returns null when the mode has no capability. Creating a provider does not scan directories or start a process. Call `ILoadOrderInventoryProvider.ReadInventory(CancellationToken)` explicitly on a worker thread. The method is synchronous and does not schedule background work itself.

`ILoadOrderPackageInspector` is the injection boundary for package inspection. It receives one observed package and the bounded top-level sibling inventory, allowing a helper to identify real split packages. Do not infer continuations merely because a filename ends in `_1` or another number.

`IsComplete` means that enumeration/inspection completed. It does not mean a valid game configuration: a fully inspected inventory can still contain duplicate UUIDs or invalid folder contents. Inspect `Diagnostics` too. Error severity currently expresses a finding; it is not a publication API or a complete future publication policy.

Feature flags are positive observations. A missing flag in a partial/failed/unavailable inspection does not prove that scripts or override content are absent. Package-wide flags and module-specific flags have separate scopes. Versions and publishing handles remain strings; do not convert them to floating point. Metadata observations never confer native ownership or enabled state.

The directory scanner does not recurse, flatten, quarantine, delete, create profile files or inspect archives. It includes ordinary hidden/system entries. Directory links are not traversed. File links remain readable to preserve NMM's symbolic-link deployment fallback; filesystem metadata is only observational evidence. Length/write time are cache hints and change detection, not an integrity proof or future publication authority. The initial entry limit is 10,000.

## Helper process protocol, version 1

Default application-owned helper location: `Tools/LoadOrder/LarianPackageInspector.exe`, relative to NMM's executable. Neither JSON configuration nor a package can select an executable or command line. The client uses no shell and creates no visible window. Scans share one lazily started helper with `--session-v1`; fully cached display scans start none. The helper announces `NMM-PAK-SESSION-1`. Frames use a four-byte little-endian UTF-8 byte count bounded to 16 MiB. The first request is the existing observed-inventory JSON; subsequent request frames contain only a validated sibling filename, and response frames contain schema-one JSON. Package-local failures do not end the session. Cancellation/timeout kills the scan-owned helper; a confirmed session permits a bounded restart after an isolated process failure. The original single-JSON/EOF protocol remains supported for independent callers. Updated applications must ship the matching helper; Release publication already binds its source identity.

Request fields:

- `schemaVersion`: integer 1.
- `provider`: `larian-module-settings-v1`.
- `root`: absolute configured package directory.
- `fileName`: one observed top-level input filename.
- `files`: bounded observed siblings, each with `fileName`, `length` and `lastWriteTimeUtcTicks`.

Stdout must contain exactly one response document. Send logs to stderr. Exit zero after returning the response, including a classified partial/failed result; an unexpected nonzero exit becomes an inspection failure. The client limits stdout to 4 Mi characters, retains at most 8 Ki characters of stderr, and defaults to a 60-second per-package deadline. Cancellation kills only this helper process. The helper must not spawn children or leave inherited pipe handles open.

Representative response:

```json
{
  "schemaVersion": 1,
  "provider": "larian-module-settings-v1",
  "fileName": "Example.pak",
  "status": "Complete",
  "kind": "Modules",
  "features": "LuaScripts, ScriptExtender",
  "readFiles": ["Example.pak"],
  "modules": [{
    "identity": "11111111-1111-1111-1111-111111111111",
    "name": "Example",
    "folder": "Example",
    "version": "36028797018963968",
    "kind": "AddOn",
    "features": "LuaScripts, ScriptExtender",
    "requiredScriptExtenderVersion": 29,
    "dependencies": [],
    "conflicts": [],
    "tags": ["Example tag"]
  }]
}
```

Envelope fields shown above are required except optional `primaryFileName` and `technicalDetails`. Module `tags`, `author`, `description`, `md5`, `publishHandle` and `requiredScriptExtenderVersion` are optional. Module fields otherwise shown above are required. Dependencies and conflicts contain objects with required UUID `identity` and optional `name` and unsigned integer-string `version`.

Enums are strings, with the exact C# enum names in `LoadOrderInventory.cs`. `None` denotes no positive feature observations. Status is Complete, Partial, Failed or Unavailable. Package kind is Unknown, Modules, Override or Continuation. Module kind is AddOn or Campaign. A complete response must classify the package and identify all files actually read. A module package must contain at least one module; other kinds contain no module records. A continuation requires `primaryFileName` naming a different observed sibling. Partial results must still identify their input and any other read files. Failed/unavailable results may use Unknown with empty modules and readFiles arrays.

UUIDs are normalized; empty/invalid UUIDs, duplicate module UUIDs within a response, unsafe filenames, unknown fields, ambiguous duplicate properties, unsupported protocol versions, oversized collections and numeric overflow are rejected. Cross-package duplicate UUIDs are reported without choosing a winner. The inventory orchestrator verifies reported input filenames against the observed siblings and rechecks their length/write time. It does not establish an atomic filesystem snapshot.

The helper must read package indexes and only the metadata/script evidence needed for classification; do not extract entire packages or execute scripts. Preserve separate modules in multi-module packages. Missing metadata does not automatically mean a valid override. Unknown, encrypted, unsupported and corrupted packages must not be described as safe, script-free or orderable.

## Specialized task boundaries

1. **LSLib reader verification and release integration.** The helper and reproducible publication script are implemented. The maintainer should verify its isolated fixtures and representative real packages. Extend fixture source for split/compressed/corrupt packages and process cancellation as needed; do not compile or run it without a request. Connect explicit helper publication to the release process before distributing the feature. Keep NMM on .NET Framework 4.8. No native configuration writes or competing transport.
2. **Lifecycle verification.** Preferences, curator precedence, native callback reconciliation, scoped Collection verification/replay/removal and NMM profile capture/restore are implemented below. Maintainer verification should cover installation, same-mod shadowed upgrades, overlapping Collections, removal, profile cancellation and interrupted publication. Legacy recipes without explicit preference membership retain their previous exact aggregate behavior.
3. **Diagnosis/repair verification.** The backend below is implemented. Maintainer verification should cover complete/partial/split package inventories, cycles, explicit exclusions, curator/dependency contradictions, native history reconciliation and interrupted cleanup. Never arbitrarily select duplicate UUIDs or conflicting alternatives.
4. **Generic UI verification.** The capability-driven page and review/recovery dialogs are implemented in `ModManagement/LoadOrder/UI`. Verify light/dark skins, larger user fonts, DPI scaling, keyboard/drag ordering, blocked inventory states, cancellation, stale reviews and dirty-draft protection. The page uses existing native services and DevExpress controls throughout, with no BG3 ModeId branch or generated-designer edits.

Tasks 2 and 3 should use `LarianLoadOrderDocument`, `NativeLoadOrderDraft`, `NativeLoadOrderPreview` and `NativeLoadOrderReview` rather than creating competing order models. Each task should own its new files. Shared GameMode definition, schema, bootstrap, MainForm and classic csproj changes need small coordinated patches. Another agent may be editing this checkout: never reset, clean, overwrite or normalize unrelated files.

## Native read/edit/save/recovery service

`MainFormVM.NativeLoadOrder` is initialized by Bootstrapper only for the configured Larian capability. Construction does not scan packages, read/repair manifests, or publish anything. There are no BG3 ModeId checks in the UI registration.

Use `ListProfiles()` to list existing eligible profiles, then explicitly select the effective game profile (normally Public for current BG3). `Read(profile)` returns a bounded existing LSX document. Missing/ambiguous/unsupported files are rejected, never recreated. Formats with LSX major 4, minor below 7 use ModOrder; minor 7/8 use the Mods sequence. Legacy record/order disagreement is rejected. A protected existing Gustav/GustavDev/GustavX campaign record is required; the writer neither fabricates campaign UUIDs nor silently migrates an old game format.

`CreateDraft(profile, cancellationToken)` performs a package inspection and imports native order. Draft `Move` uses zero-based add-on positions, with game records fixed. `SetIncluded` changes native module inclusion, which is independent of physical disablement and override effects. Existing module XML retains foreign fields. Newly included modules require inspected metadata. Adding modern package metadata to a legacy manifest is deliberately blocked until an explicit format migration is implemented. A no-op draft retains exact original bytes.

`Review(profile, draft)` captures proposed bytes and current winning native owner values under the existing native target lease/reload. `Save(review, approved, cancellationToken)` requires that review, rechecks the physical preimage, winning owners, shadow histories, key set and freshly inspected package metadata. Only current winning module/Collection values are reconciled. External unowned records acquire no fictitious module owner. Original module/aggregate values and shadowed owner values remain intact. Manual preference baselines are state and may be updated under their original-value owner. Full-file/link/INI ownership of the manifest blocks standalone editing.

Saving flushes a bounded version-two pending JSON record and exact original backup beside the selected manifest, outside the package folder. Version-one recovery records remain readable. Version two additionally captures a preference key that was previously absent. The complete file is staged and atomically replaced on the same volume. Native value replacements and the file snapshot participate in the existing NMM transaction. The pending record persists through exceptions/interruption until file and history agree. Successful publication retains the original backup. The service does not start a background writer.

`InspectRecovery(profile)` reloads native authority and reports None, OriginalIntact, PublishedIntact, Interrupted or Conflict without repairing the manifest. `Recover` requires an explicit choice: acknowledge aligned evidence, restore original bytes/current-owner values, or complete publication. Completion revalidates package metadata. Unknown physical images, changed winning/shadow owners, altered backups/stages and malformed journals are not overwritten or discarded. Recovery affects only the explicitly selected profile. Snapshot exact-byte backup paths are returned as evidence; copying a backup over a native-owned manifest by hand does not reconcile ownership.

The structured-record resolver and retained data-driven BG3 legacy callbacks reject a manifest with an outstanding manual publication marker. This prevents later install/removal callbacks from silently consuming unresolved state. Publication failure-injection checkpoints exist for maintainer-owned fixtures. Native read/draft regression source is in `Tests/NexusClientTests/NativeLoadOrderDocumentTests.cs`; it has not been compiled or run. No game, helper, or real-profile operation was executed to verify this service. Production compilation alone is not runtime/recovery certification.

## Installation, Collections and profiles

`LarianOrderPreference` persists `Version`, `Source`, `Order`, `Excluded` and `Managed` under `larian-order-preference-v1|<game-profile>|state` in the existing InstallLog game-value history. `ICurrentGameSpecificValueReader` is an optional transaction-aware reader implemented by InstallLog; no existing IInstallLog implementation contract was expanded. Missing preference state keeps existing behavior.

`LarianOrderGameValueInstaller` wraps the retained structured installer. Ordinary install/upgrade/removal finalization preserves the pre-operation relative sequence of surviving modules, appends newly introduced/restored entries stably, and reconciles physical XML with current winning module/aggregate payloads in the caller's native transaction. Historical saved positions are not automatically replayed. Saved inclusion/exclusion choices affect only the modules touched by that lifecycle operation. Baselines are captured before the first native edit, including scoped and legacy aggregate removal. A newly supplied explicit curator/retained order recipe can still apply its requested order; explicit profile restoration and reviewed Save/Fix/recovery retain their own publication paths. No package helper runs inside install finalization. `IDeterministicPackageGameValuePlanProvider` adds selected-package metadata to the existing basic planner without expanding `IGameMode`. Capability-enabled data-driven modes use native PAK metadata even when a sidecar exists, so display versions such as `2.63` cannot become native integer versions. Existing valid record fields and positions remain intact; an explicit reinstall can correct only its own invalid Version64. Numeric sidecar fields are validated when the sidecar-only path is used. Disabled-capability and retained legacy fallback behavior remains in place.

`LarianPackageInstallPlanner` streams only selected primary-root PAKs into a unique temporary directory, preserving destination filenames for verified split membership. It uses the same isolated inspector and configured profile store as the Load Order page. The batch is bounded to 1,024 files, 32 GiB per file and 64 GiB total; whole PAKs are not allocated in memory. Complete unique add-ons become the established per-profile native module keys, with existing record bytes/foreign XML retained. Overrides, campaign modules, built-in identities and continuation parts produce no independent module inclusion. Missing tooling, partial/unknown/duplicate metadata, nested primary package destinations and unsupported legacy-profile additions fail before native publication. Temporary cleanup deletes only this invocation's created files and empty staging directory.

Ordinary non-scripted native PAK installs, with or without info.json, freeze this basic plan before replay cleanup/native mutation, then use the existing recipe executor, transactional game-value installer and owner history for Direct/Virtual install, upgrade and uninstall. Collection basic, selected-file, FOMOD and captured-installer recipes infer package metadata after their final file overrides/patches and before preview/fingerprinting. Explicit installer game values retain precedence. Generated/patched PAKs without explicit module metadata remain unsupported; curator metadata bypasses archive inference entirely. Native scripted installers retain their existing script contract rather than scanning the deployed folder after mutation.

A supplied curator preference is installed before the Collection aggregate. It always replaces prior order preferences on install/upgrade/replay, including promotion from a shadowed same-mod upgrade. `Managed` identifies the curator's selected-package module scope, including explicit exclusions; deselected optional packages do not remove independently installed modules. Aggregate replay/removal takes unrelated records from the current manifest, avoiding stale restoration of unrelated modules. New scoped Collection verification compares all declared curator records and order. Manual saves after installation remain explicit user overrides; the next supplied curator recipe wins again.

Fresh Collection preparation regenerates aggregate and preference effects from the exact retained curator manifest. It removes stale per-archive records, aggregates and preferences from retained captured operations, emits one policy before each rebuilt aggregate on the owning member, and removes stale order effects from non-owning members. This transformation is idempotent and leaves retained input artifacts/provider recipe IDs unchanged; regenerated effect bytes participate in the existing prepared/executable fingerprints. Verify / Repair identifies a missing installed scoped policy as a reviewable configuration repair and uses scoped aggregate comparison to avoid false repairs for unrelated records. Apply the reviewed repair to acquire the policy. Already approved/submitted operations and local-restore artifacts are not silently migrated: their exact effect/identity guards remain authoritative, and legacy aggregate replay without an explicit policy retains its old contract.

NMM profiles use an additive `native-load-order.json`, version one, bound to ModeId and game-profile names. Existing plugin `loadorder.txt` is untouched. Save/clone/ZIP export includes the companion; profiles without it preserve legacy behavior. Companions are validated before deployment. A live pre-switch rollback policy is captured, automatic companion capture is suspended during transitional deployment, and native restoration runs on the existing background-task mechanism after mod files are deployed but before committing the profile. Failed switches restore the prior policy after compensating mod deployment. Each game-profile publication uses the native journal protocol; multi-profile restoration is not one filesystem transaction, and an unknown recovery conflict remains explicit.

## Diagnosis and automatic repair API

Call `NativeLoadOrderService.Analyze(profile, cancellationToken)` on a worker. Its immutable `LoadOrderAnalysis` contains native/package observations, localizable issues with directions, suggested order and concrete repair steps. Positive script flags and required extender versions remain available on module metadata. Extender requirements are informational runtime checks: a package declaration cannot establish which extender version the game actually loads. Embedded Mod Fixer is reported without declaring every such package incompatible. Overrides are clearly identified as physically effective independently of module inclusion.

The analyzer reports missing packages/dependencies, explicit exclusions, inactive dependencies, older declared dependency versions, wrong dependency positions, declared incompatibilities, cyclic constraints, curator contradictions, stale native metadata and inventory/folder failures. It only enables a dependency closure when all modules are uniquely identified, available, sufficiently versioned and free of declared conflicts with the effective set. Explicit exclusions are preserved. Missing downloads, duplicate alternatives and conflicting choices require directions rather than guessed repairs. Current curator member positions are hard constraints; contradictory declarations remain visible. Cyclic proposals never introduce new active modules or invent an arbitrary cycle order. Sorting uses existing positions as deterministic tie-breakers. The graph is bounded at 10,000 modules and 200,000 dependency edges/closure visits.

For order-only operations use `ReviewRepairs(profile, analysis)` and `ApplyRepairs(review, explicitlyApproved, token)`. These create existing `NativeLoadOrderDraft` / `NativeLoadOrderReview` objects and publish through the same stale-sensitive Save/recovery service. Modern metadata refresh changes verified known fields while retaining unknown XML. Repair does not migrate legacy LSX formats or rewrite PAKs.

For the generic Fix action use `ReviewAutomaticRepairs(profile, analysis, token)` and `ApplyAutomaticRepairs(review, explicitlyApproved, token)`. The review includes eligible folder items and the expected post-cleanup native preview. Use this combined review's `HasRepairs` for Fix availability: `analysis.HasAutomaticRepairs` describes only order repairs and can be false while folder cleanup is available. A native order preview is blocked if any unhandled inventory error remains. Each phase reloads native authority; actual package/owner/file preimages are checked again during execution. Refresh diagnosis afterwards and present remaining manual issues, rather than claiming every incompatibility was repaired.

Folder cleanup moves only bounded, unowned non-PAK content to a same-volume sibling `.nmm-load-order-recovery/<id>` outside Mods. It never flattens folders, alters package bytes, deletes content, or bypasses file/INI/virtual ownership including shadow owners. Links, redirected ancestors, directories containing misplaced PAKs, more than 1,000 tree entries/16 levels, trees above 32 MiB, and more than 128 items per batch require manual handling. Content hashes, rather than timestamps, bind reviewed items. The flushed `repair.json` intent remains through interruption and success. Both unknown bytes and occupied destinations stop recovery without overwriting them.

Use `ListFolderRecoveryIds`, `InspectFolderRecovery` and explicit `RecoverFolderRepairs(id, restoreOriginal, explicitlyApproved, token)` after restart. The inspector reports original, retained, mixed or conflicting evidence. Completing cleanup and restoring content are separate choices; restoring unexpected content can reintroduce the game's order-reset condition. Resolve pending native publication before folder recovery. Cleanup and native order publication have separate recovery records. A partial cleanup exposes `LoadOrderFolderRepairException.RetainedContentDirectory`; an order failure after completed cleanup exposes the same evidence on `LoadOrderAutomaticRepairException`. Never compensate by moving unknown content over a new installation.

New regression source: `LarianLoadOrderPlanningTests.cs` and `LoadOrderFolderRepairTests.cs`, alongside the native document/inventory/helper fixtures. None of this turn's new code or fixtures has been compiled or executed. Real-game package compatibility, owner-history transaction/recovery behavior and profile rollback still require maintainer validation.

## Generic Load Order page

`LoadOrderControl` is a `ManagedFontXtraForm` hosted as the stable `LoadOrderDocument` through the existing MDI document infrastructure. It is created and registered only when `MainFormVM.NativeLoadOrder` is present. The existing data-driven `loadOrder.enabled` capability is the opt-in; no new game-specific UI gate is introduced. First activation inspects profiles/packages on a worker. Multiple game profiles require a selection; a sole existing profile is selected visibly. The last selection is stored under the additive `loadOrder.SelectedProfile.<ModeId>` UI settings key and flushed by the existing host settings lifecycle.

The single list contains fixed native game records, included modules in their actual sequence, excluded available modules and non-orderable package observations. Physical overrides use a dash instead of an inclusion checkbox and say that they remain active independently of the order. The UI never implies that excluding a module uninstalls scripts/overrides. Duplicate or partial inspections remain read-only, with positive content evidence retained and uncertainty marked. Legacy additions and campaign selection are not falsely offered as supported edits.

Drag-and-drop and Alt+Up/Down reorder included add-ons. Search/filtering remains available, but reordering is disabled while filtered to avoid confusing visible indexes with game positions. External drops are rejected; a private in-session nonce authorizes only the current module drag. `LoadOrderEditorSession` contains detached draft and display state; it never writes files. Feature badges are DevExpress TokenEdit tokens. Details include authors, packed versions, module/package paths, tags, description, dependencies/conflicts and declared extender requirements. Information-level feature guidance is separated from the actionable error/warning problems panel.

Save performs the existing reviewed native publication and reports retained backup evidence. A failed or cancelled following refresh never labels a confirmed save as an unsaved draft. Fix automatically prepares the combined folder/order review, displays concrete before/after changes and retained destinations, then applies exactly that review. Remaining manual problems are shown after inspection. Recovery provides separate explicit original/proposed/current native choices and retained-folder batch inspection/completion/restoration; unknown/conflicting evidence disables replacement. Returning unexpected folder content requires confirmation because it can reintroduce the game's order-reset condition.

Native mutation signals and top-level filesystem notifications invalidate observations. Notifications are hints, not a substitute for publication's fresh package, file and ownership checks. Busy work is serialized, cancellation reaches both package inspection and native reservation waits, and failed/cancelled reads do not immediately restart. Dirty drafts are retained on external changes and require a choice before refresh, game-profile selection, NMM profile switching, application close or game teardown. Active work cannot lose its game services. The host reapplies the current `DevExpressDisplaySettings` font/density/icon/palette settings; all new visible controls and dialogs are DevExpress, with table layouts and resizable panes rather than hardcoded skins/colors/fonts.

`LoadOrderEditorSessionTests.cs` adds isolated regression source for single-list semantics, dirty-state round trips, drag positions, fixed records, partial/duplicate inventory, legacy restrictions, extender requirements and independent profile drafts. It has not been compiled or executed. No application/helper/game was launched to validate the UI.

The DevExpress checkbox **Automatically fix module order after installing or removing mods** is off by default. Its additive per-game UI key is `loadOrder.AutomaticOrderRepair.<ModeId>`, saved through the existing host settings lifecycle. Enabling it never changes the current order. Future successful, verified-committed manual mod operations can schedule order-only repair of the explicitly selected game profile after the native queue drains, even while the tab is hidden. Native invalidations, file watchers, page activation, Collection/profile/recovery tasks and failed/unknown outcomes do not authorize automatic repair. Unsaved drafts and active drags are protected; user edits, profile changes and consent changes invalidate queued completion hints. The existing analyzer/review/publication path performs safe dependency sorting, inclusion and metadata repairs only with fresh evidence; no files/folders are moved, conflicting choices are not guessed, and unresolved problems/recovery remain explicit. Cancellation/failure does not immediately retry or show an unsolicited modal error.

`LoadOrderLifecycleOrderingTests.cs` adds isolated native file/owner-history regression source for ordinary install/removal/upgrade positions, scoped and legacy Collection removal (both order formats), untouched-module exclusions, explicit curator application, and opt-in trigger eligibility. The existing planning fixture now names saved-policy reordering as an explicit curator operation. None of these fixtures has been compiled or executed.

The page now reads existing profile names and the saved manifest before waiting for ownership/recovery checks or PAK inspection. It immediately shows a read-only `LoadOrderEditorSession.Preview`, preserving native positions and labelling uninspected entries as awaiting inspection. The optional request-scoped `LoadOrderInspectionProgress` reports current filename, completed/total counts and completed package observations; the UI enriches the preview at bounded repaint intervals. Preview sessions contain no editable draft, missing-package diagnoses or automatic repair proposals. Final authoritative analysis replaces the preview only after the existing native/file revision checks. Cancellation/failure retains visible saved-order evidence, while editing/Save/Fix stay blocked. Late queued progress cannot overwrite final state. Existing provider/service signatures remain available; native serialization remains unchanged. The session-only display cache added subsequently is described below. Preview/progress regression source has been added but not compiled or executed.

Diagnosis distinguishes unresolved inspection from proven absence in a complete package scan. Failed/partial reader observations and their downstream unmatched modules/dependencies are warnings with explicit uncertainty; they never authorize repairs. Metadata differences and declared version gaps are warnings with exact saved/package values, not claims of runtime incompatibility. Inventory completeness and publication guards remain independent from displayed severity. BG3's configured built-in UUID list now includes DiceSet_01 through DiceSet_07, verified against BG3MM's `src/GUI/Resources/IgnoredMods.json`; this is configuration-level recognition, not DLC entitlement/runtime verification.

The DevExpress **Export diagnostic report** action saves the current detached observation as versioned JSON without a rescan or native write. It remains available for cancelled/failed or stale observations after active work ends. The report retains package paths/status/kind/features and exact reader details, native record XML/hash/versions, displayed order, saved curator/manual policy, configured built-ins, dependency resolution, findings, proposed repairs and recovery/error context. Raw packed versions remain strings. The report includes local paths; the button tooltip states this. Regression source covers uncertainty classification, retained comparison details and JSON evidence; no build or tests were run.

Background enrichment now restores the focused mod, top visible identity/index, focused column and horizontal offset after rebinding. Transient focus events during the bind do not replace the user's details selection. Manual row movement keeps its normal focus behavior. A session-only display cache reuses complete/partial package observations when every recorded input still matches the current path/size/write time; split primary/continuation inputs invalidate together. Helper executable/dependency metadata changes invalidate the cache, failed reads are retried, and explicit Refresh discards all display hints. Every refresh still checks the flat directory policy and rebuilds dependency diagnosis. Save/recovery/repair use the existing uncached inventory provider and full preflight fingerprint; metadata cache hints never authorize publication. Source regression cases cover new/changed PAKs, helper invalidation, split inputs, failed-read retry and scrolling/focus across inspection updates. No regression source has been executed.

## Remaining implementation plan

Maintainer verification remains: build/publish Release and compile/install/upgrade the installer, then validate real packages, installs/upgrades/removal, regenerated legacy Collection policies, Collection/profile transitions, interrupted recovery, and the UI across skins/fonts/DPI. Compilation and all test execution remain maintainer-owned unless explicitly requested. `LoadOrderInstallationIntegrationTests.cs` covers selected-file registration, deferral, split/partial/duplicate metadata, legacy profiles, staging cleanup, retained XML, optional scope and idempotent recipe regeneration. `Support/BuildTasks/Tests/LoadOrderHelperPackaging.Tests.ps1` adds synthetic integrity-gate regression source without requiring the SDK or real helper. Neither source has been executed; the new release-publishing/installer path has received static verification only.

## Verification requirements

Respect the user's instruction to leave compilation and test execution to the maintainer. Inspect the final diff, project entries, encoding, line endings and source XML/JSON. Add regression source where meaningful. Do not run helper fixtures, application, game, or real-profile operations as an implementation check. Historical production builds of earlier slices are not evidence that the current lifecycle/repair code compiles or that runtime recovery/game compatibility is correct.

## Explicit order archives and installation evidence

Installing an LSX-only archive is an explicit order action. A descriptive filename or archive wrapper is supported; ordinary loose game-file directories are not mistaken for order exports. The bounded LSX parser validates the entire imported order before mutation. Native planning produces preference/aggregate effects, with no LSX or sidecar files deployed into the PAK directory and no package scan. Each existing configured game profile retains its protected campaign and unrelated XML; cross-format imports fail rather than silently migrating legacy profiles. The supplied custom module inclusion/order wins for this explicit action, through the existing native ownership/undo stack. Curator-owned Collection metadata still takes precedence over archive inference.

Ordinary per-module installation/removal finalization also verifies that unrelated entries retain inclusion, relative sequence and metadata. A mismatch aborts the native transaction. Each changed lifecycle operation records its exact before/after LSX bytes, hashes, module additions/removals, metadata differences and explicit-order flag in `LoadOrder.LastChange.<manifest-hash>.json` in that game's existing Install Info directory. This is a bounded latest-change record per profile, committed or rolled back with the native file transaction; it is diagnostic evidence, never publication/recovery authority. Diagnostic export includes it and indicates whether its after hash still matches the current manifest. Earlier snapshots are replaced by the next committed lifecycle change. Source fixtures cover the new protocol, cancellation lifetime, invalid-version handling, LSX-only planning and audit rollback; none have been compiled or run during this change.
