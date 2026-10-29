# Load-order infrastructure and implementation handoff

## Scope and current status

The BG3 backend now includes package inventory, native order reading/editing/saving/recovery, persistent order policies across native installation and Collection operations, NMM profile companions, actionable diagnosis, deterministic sorting and recoverable cleanup. The generic Load Order page remains outstanding. New lifecycle and repair code has received static inspection only; compilation and test execution are reserved for the maintainer.

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
- Isolated regression test source. Never compile or execute tests unless the maintainer explicitly requests it; the maintainer handles test verification.

The shipped BG3 definition opts into inventory configuration. Nothing invokes a scan at startup and no UI or order-write capability is advertised. The LSLib helper is implemented under `Support/LarianPackageInspector`. Run its `Build.ps1` to acquire the checksum-pinned v1.20.4 dependency and publish the self-contained win-x64 helper into `Stage/<Configuration>/Tools/LoadOrder`. The classic application build does not download or publish this helper automatically. A missing helper produces `InspectorUnavailable`, retains the package filenames and marks inspections unavailable. It must never be presented as a successful empty metadata scan.

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

Default application-owned helper location: `Tools/LoadOrder/LarianPackageInspector.exe`, relative to NMM's executable. Neither JSON configuration nor a package can select an executable or command line. The client uses no shell, creates no visible window, and passes a single UTF-8 JSON request on stdin, followed by EOF. Each request currently starts one process.

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
4. **Generic UI.** Build a capability-driven Load Order page with one ordered list, details, readable feature badges, problem explanations and explicit save state. Inventory presentation can start with the current contracts. Editing/Save and Fix actions depend on tasks 2 and 3; do not fake them. Reuse DevExpress skin, localization, fonts and layout infrastructure. No ModeId checks or BG3-specific UI class names. Distinguish module inclusion from physical disablement and always-loaded override behavior.

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

`LarianOrderGameValueInstaller` wraps the retained structured installer. Finalization reapplies saved inclusion/order choices and reconciles physical XML with current winning module/aggregate payloads in the caller's native transaction. Newly installed unrelated modules append stably. No package helper runs inside install finalization. Native metadata produced by existing installation plans is still required for automatic module registration: PAK-only archives without `info.json` become inspectable/includable through the native draft, rather than being blanket-enabled merely because they exist in Mods.

A supplied curator preference is installed before the Collection aggregate. It always replaces prior order preferences on install/upgrade/replay, including promotion from a shadowed same-mod upgrade. `Managed` identifies the curator's module scope. Aggregate replay/removal takes unrelated records from the current manifest, avoiding stale restoration of unrelated modules. New scoped Collection verification compares all declared curator records and order; old retained recipes without a preference effect keep exact byte comparison and whole-aggregate execution, without silently adding an unreviewed preference effect. Regenerate those legacy recipes to acquire persistent scoped curator policies. Manual saves after installation remain explicit user overrides; the next supplied curator recipe wins again.

NMM profiles use an additive `native-load-order.json`, version one, bound to ModeId and game-profile names. Existing plugin `loadorder.txt` is untouched. Save/clone/ZIP export includes the companion; profiles without it preserve legacy behavior. Companions are validated before deployment. A live pre-switch rollback policy is captured, automatic companion capture is suspended during transitional deployment, and native restoration runs on the existing background-task mechanism after mod files are deployed but before committing the profile. Failed switches restore the prior policy after compensating mod deployment. Each game-profile publication uses the native journal protocol; multi-profile restoration is not one filesystem transaction, and an unknown recovery conflict remains explicit.

## Diagnosis and automatic repair API

Call `NativeLoadOrderService.Analyze(profile, cancellationToken)` on a worker. Its immutable `LoadOrderAnalysis` contains native/package observations, localizable issues with directions, suggested order and concrete repair steps. Positive script flags and required extender versions remain available on module metadata. Extender requirements are informational runtime checks: a package declaration cannot establish which extender version the game actually loads. Embedded Mod Fixer is reported without declaring every such package incompatible. Overrides are clearly identified as physically effective independently of module inclusion.

The analyzer reports missing packages/dependencies, explicit exclusions, inactive dependencies, older declared dependency versions, wrong dependency positions, declared incompatibilities, cyclic constraints, curator contradictions, stale native metadata and inventory/folder failures. It only enables a dependency closure when all modules are uniquely identified, available, sufficiently versioned and free of declared conflicts with the effective set. Explicit exclusions are preserved. Missing downloads, duplicate alternatives and conflicting choices require directions rather than guessed repairs. Current curator member positions are hard constraints; contradictory declarations remain visible. Cyclic proposals never introduce new active modules or invent an arbitrary cycle order. Sorting uses existing positions as deterministic tie-breakers. The graph is bounded at 10,000 modules and 200,000 dependency edges/closure visits.

For order-only operations use `ReviewRepairs(profile, analysis)` and `ApplyRepairs(review, explicitlyApproved, token)`. These create existing `NativeLoadOrderDraft` / `NativeLoadOrderReview` objects and publish through the same stale-sensitive Save/recovery service. Modern metadata refresh changes verified known fields while retaining unknown XML. Repair does not migrate legacy LSX formats or rewrite PAKs.

For the generic Fix action use `ReviewAutomaticRepairs(profile, analysis, token)` and `ApplyAutomaticRepairs(review, explicitlyApproved, token)`. The review includes eligible folder items and the expected post-cleanup native preview. Use this combined review's `HasRepairs` for Fix availability: `analysis.HasAutomaticRepairs` describes only order repairs and can be false while folder cleanup is available. A native order preview is blocked if any unhandled inventory error remains. Each phase reloads native authority; actual package/owner/file preimages are checked again during execution. Refresh diagnosis afterwards and present remaining manual issues, rather than claiming every incompatibility was repaired.

Folder cleanup moves only bounded, unowned non-PAK content to a same-volume sibling `.nmm-load-order-recovery/<id>` outside Mods. It never flattens folders, alters package bytes, deletes content, or bypasses file/INI/virtual ownership including shadow owners. Links, redirected ancestors, directories containing misplaced PAKs, more than 1,000 tree entries/16 levels, trees above 32 MiB, and more than 128 items per batch require manual handling. Content hashes, rather than timestamps, bind reviewed items. The flushed `repair.json` intent remains through interruption and success. Both unknown bytes and occupied destinations stop recovery without overwriting them.

Use `ListFolderRecoveryIds`, `InspectFolderRecovery` and explicit `RecoverFolderRepairs(id, restoreOriginal, explicitlyApproved, token)` after restart. The inspector reports original, retained, mixed or conflicting evidence. Completing cleanup and restoring content are separate choices; restoring unexpected content can reintroduce the game's order-reset condition. Resolve pending native publication before folder recovery. Cleanup and native order publication have separate recovery records. A partial cleanup exposes `LoadOrderFolderRepairException.RetainedContentDirectory`; an order failure after completed cleanup exposes the same evidence on `LoadOrderAutomaticRepairException`. Never compensate by moving unknown content over a new installation.

New regression source: `LarianLoadOrderPlanningTests.cs` and `LoadOrderFolderRepairTests.cs`, alongside the native document/inventory/helper fixtures. None of this turn's new code or fixtures has been compiled or executed. Real-game package compatibility, owner-history transaction/recovery behavior and profile rollback still require maintainer validation.

## Verification requirements

Respect the user's instruction to leave compilation and test execution to the maintainer. Inspect the final diff, project entries, encoding, line endings and source XML/JSON. Add regression source where meaningful. Do not run helper fixtures, application, game, or real-profile operations as an implementation check. Historical production builds of earlier slices are not evidence that the current lifecycle/repair code compiles or that runtime recovery/game compatibility is correct.
