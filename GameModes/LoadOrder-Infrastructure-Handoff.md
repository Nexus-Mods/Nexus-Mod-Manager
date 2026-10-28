# Load-order infrastructure and implementation handoff

## Scope and current status

This is the read-only foundation for the BG3 load-order work. The UI, native order reader/writer, repair planner, ownership reconciliation and profile persistence are not implemented by this slice.

Implemented:

- Optional typed `loadOrder` configuration, runtime validation and JSON schema.
- Immutable capability on data-driven modes through `ILoadOrderCapabilityProvider.LoadOrder`.
- Built-in inventory provider dispatch by provider identifier, without a BG3 ModeId branch.
- Read-only, bounded top-level folder scanning, including hidden entries, non-package files and subfolders.
- Immutable package/module metadata, positive content feature observations, dependencies, conflicts, diagnostics, inspection completeness and split-package relationships.
- A bounded process client and strict versioned JSON contract for a separately packaged LSLib reader.
- Isolated regression test source. Do not execute tests without explicit user authorization, including targeted tests.

The shipped BG3 definition opts into inventory configuration. Nothing invokes a scan at startup and no UI or order-write capability is advertised. The LSLib helper executable is **not implemented or bundled yet**. A missing helper produces `InspectorUnavailable`, retains the package filenames and marks inspections unavailable. It must never be presented as a successful empty metadata scan.

Existing BG3 structured effects, Collection ordering, native owner histories, discovery, custom paths and legacy fallback code are unchanged. These remain the authority for existing installation behavior.

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

1. **LSLib helper and packaging.** Implement the protocol above using a pinned compatible LSLib version. Keep NMM on .NET Framework 4.8; use a separate helper project and package its runtime/native dependencies. Review licensing/distribution. Read meta.lsx, dependency/conflict metadata, tags, ScriptExtender/Config.json, Lua/Osiris evidence, recognizable Mod Fixer evidence, override content and genuine split-package relationships. Add fixture coverage for corruption, multiple modules and compressed metadata. No native configuration writes. The process client is already present; do not add a competing transport.
2. **Native order and lifecycle.** Add typed observed/draft order contracts and integrate current/legacy LSX formats, installed campaign identity, native transactions, existing per-module/Collection ownership, recovery and NMM profile switching. Scope this work to additional services; keep the inventory models read-only. Do not implement an independent full-file writer that ignores existing owner histories. Prove the compatibility contract before exposing Save.
3. **Diagnostics and sorting.** Consume inventory and the native order contracts from task 2. Build stable dependency sorting and explicit repair proposals. Preserve unrelated relative order, identify cycles, distinguish overlaps from declared incompatibility, and never arbitrarily select duplicate UUIDs or conflicting alternatives. Folder repair must respect native ownership and provide recoverable handling outside Mods.
4. **Generic UI.** Build a capability-driven Load Order page with one ordered list, details, readable feature badges, problem explanations and explicit save state. Inventory presentation can start with the current contracts. Editing/Save and Fix actions depend on tasks 2 and 3; do not fake them. Reuse DevExpress skin, localization, fonts and layout infrastructure. No ModeId checks or BG3-specific UI class names. Distinguish module inclusion from physical disablement and always-loaded override behavior.

Tasks 2 and 3 need an agreed observed/draft order contract before being implemented concurrently. Each task should own its new files. Shared GameMode definition, schema, bootstrap, MainForm and classic csproj changes need small coordinated patches. Another agent may be editing this checkout: never reset, clean, overwrite or normalize unrelated files.

## Verification requirements

Build the narrowest affected projects and inspect the final diff/encoding/line endings. Add regression source where meaningful. Do not run any tests, helper fixtures, application, game, or real-profile operations without the appropriate user authorization. A build is not evidence of helper correctness, runtime recovery or game compatibility. Source XML/JSON inspection is static verification.
