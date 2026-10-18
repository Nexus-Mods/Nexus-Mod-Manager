# Managed-content game-mode authoring

Use `GameModes/Templates/ManagedContentSample/game.json` as a runnable synthetic example. Copy it to `GameModes/Definitions/<ModeId>/game.json`, change the stable identity/discovery/executables and configure the literal rules for the game's known layout. No new game-specific C# class or factory branch is needed when these rules suffice. The sample is an engine fixture, not a supported commercial game definition.

Use root `schemaVersion: 2`, `behaviorProfile: "managed-content"` and `managedContent.schemaVersion: 1`. This is an optional, formally versioned extension; existing schema-2 definitions without it retain their behavior. Older NMM binaries do not understand the extension. Version 1 declares `installLayout` and optional `componentOrdering`; layout target metadata also supplies the deployed component inventory. Ordering observes settings and can opt into the reviewed editing provider described below. Unsupported fields and provider versions fail strict loading rather than being ignored.

The host requires `modInstall.pathAdjustmentProfile: "none"`, `supportsGameRootInstall: false`, no `managedInstallationPath` and no nonempty game-root package rules. `targetRoot` is always `Data`: the existing native Data destination, which is the Witcher game directory for that definition. It is not a new filesystem root or an absolute path supplied by an archive.

## Exact rule vocabulary

`installLayout.provider` must be `bounded-folder-layout-v1`; `revision` must be positive. Increment the revision when authoring changes to rule semantics. The provider snapshots its rules once per mode instance. Prepared layout identities bind the effective configuration and the exact operation inputs, but do not constitute a native write permit.

| Field | Meaning |
| --- | --- |
| `id` | Unique rule identifier, case-insensitively validated. |
| `kind` | `component-folder` or `generated-folder`. No arbitrary globs, regular expressions, scripts or path substitution. |
| `sourceRoot` | Optional literal root segment for component-folder rules. Required and equal to `contentFolder` for generated-folder rules. |
| `componentPrefix` | Literal prefix; a captured folder must have at least one additional character. For generated folders this prefixes the sanitized name and stable hash. |
| `contentFolder` | Literal child containing files beneath a component, e.g. `content` or `assets`. |
| `targetFolder` | Literal folder beneath the native Data root. |
| `targetRoot` | Exactly `Data`. |
| `componentKind` | Descriptive identifier such as `mod-folder`, `dlc-folder` or `asset-package`; does not assert native ownership or load-order eligibility. |
| `maximumComponentNameLength` | At most 255 and large enough for prefix + one stem character + underscore + ten hash characters. Explicit overlong folders are blocked; they are never silently renamed. |

A component-folder rule matches `[sourceRoot/]componentPrefix*/contentFolder/<file>` and maps it to `targetFolder/<captured-component>/contentFolder/<file>`. The `*` in this explanation means a nonempty suffix on that single folder; it is not an accepted JSON pattern.

A generated-folder rule matches `sourceRoot/<file>` and maps it to `targetFolder/<prefix><safe-name>_<hash>/contentFolder/<file>`. The archive/package identity is mandatory; source file paths remain unchanged. The same archive yields the same component name in Direct and Virtual modes. The exact selected installer destination is examined, not a guessed unselected FOMOD alternative. Basic expansion of scripted archives remains unsupported.

`maximumWrapperSegments` is 0 by default, bounded to 2. Recognition uses the outermost matching shape and requires all payloads to agree on a common wrapper. Multiple matching rules at that root block the plan. `ignoredFileNames` lists up to 32 exact metadata/documentation names, ignored only at the archive or resolved wrapper root. `rejectedFileSuffixes` lists up to 32 suffixes requiring a separate effect adapter, e.g. Witcher `.part.txt`. Other unrecognized payload blocks the whole plan; no payload is silently dropped.

All names are literal Windows-safe single segments. Runtime validation rejects traversal, absolute/device paths, ADS, reserved names, trailing-dot/space aliases, duplicate IDs, conflicting case spellings, duplicate sources/targets, file/directory collisions and selections beyond 100,000 files. The editor schema handles structural errors; the production validator is authoritative for semantic relationships and case-insensitive uniqueness.

## Native and game-specific boundaries

The same `IReviewedBasicInstallMappingProvider` result feeds live BasicInstallTask and BasicInstallPlanBuilder/Collections preview. `ManagedContentInstallSelection` provides both the generic host and Witcher adapter with one bounded inventory-membership check and an immutable snapshot of actual selected files, package identity and suggested name. It validates every configured destination against the canonical path validator before returning any mappings. Malformed or duplicate inventory paths are rejected even if an installer selection omits them. Native InstallLog, Direct/Virtual file ownership, rollback, collision checks and target leases still own deployment. Relinking validates canonical planned paths and cannot infer a new wrapper, raw name or target root. Witcher retains its fixed persisted-target policy through the shared validator, without rerunning the full archive planner for each native file.

`PreparedBasicInstallMapping` hands reviewed file-only Data-root mappings to the existing native Direct/Virtual worker. The worker rechecks archive/mode identity, physical root, method/root and canonical projections, then consumes those mappings without rerunning layout recognition or new-component preflight. This matters for upgrades: an approved existing-owner replacement must not be remapped as a first install. Ordinary managed-content modes use the same handoff after their mapping provider runs once. Existing callers without a prepared mapping keep the original BasicInstallTask path. The snapshot is operation-local input, not a persisted format or ownership authorization.

The Witcher definition keeps `witcher3remastered` and its reserved ModeId. It consumes this capability through its existing adapter with an equivalence check against its characterized mapper. Changing JSON cannot approve Remastered activation, 5.x order writes, a Legacy takeover or an unsupported native effect. Older Witcher definitions without `managedContent` retain the compatibility path.

## Read-only deployed components

Managed-content hosts expose `IManagedContentComponentProvider`. `ManagedContentNativeComponentIndexer.Build(nativeState, gameMode)` consumes a detached committed `NativeStateCaptureSnapshot` and the mode's immutable layout rules. It projects native targets under the configured Data root into `targetFolder/componentPrefix*` components, labels them with `componentKind`, and detects `contentFolder` payload. It does not inspect archive contents, enumerate directories, or load a second ownership database.

For example, the existing sample rules identify `Packages/pkgExample` as `asset-package`, with payload under `assets`; the Witcher adapter uses the same engine for Mods and DLC. Several source rules may describe the same deployed identity. Conflicting target metadata produces an `AmbiguousComponentRule` diagnostic, without guessing an identity.

The inventory preserves promoted InstallLog precedence, active Virtual-link winners, legacy Direct histories, shadowed contributors, original fallbacks and unresolved/ambiguous owners. Each contributor includes native key, archive/name, installation method and file/winner counts. One archive may contribute several components and several archives may contribute one component. These observations do not authorize deployment, uninstall, adoption of manual files or order-file writes.

Optional `ManagedContentComponentDiskObservation` input must describe the same physical root and configured component scope. An omitted observation, or an incomplete enumeration, leaves absent files/folders unknown. Only a complete valid observation can report a missing native file. The shared indexer performs no disk scan.

Use `ManagedContentComponentDiskScan.Scan(gameMode)` to explicitly enumerate the configured native Data root before supplying its result to `ManagedContentNativeComponentIndexer.Build(nativeState, gameMode, disk)`. Schedule this read outside UI painting; the scanner starts no background work. No new JSON fields are required: it snapshots the existing rules and scans each distinct `targetFolder` once, including unrecognized immediate component folders so the index can diagnose them. The sample's two source rules therefore scan `Packages` once, while Witcher's fixed compatibility rules scan Mods and DLC.

Default limits remain 100,000 files, 20,000 directories and 16 nested levels beneath a component. Observed junctions/symlinks, access failures and exceeded limits produce typed diagnostics and an incomplete result; no file contents are read or written. A missing optional target folder may be empty scope, but an inaccessible folder or a file occupying that directory path is an incomplete observation. The scan is a point-in-time observation, not a lock against external changes.

Scanner-produced observations include immutable `TargetFolders` coverage and `Issues`. The index checks that the coverage includes every currently configured target folder before inferring absence. The original four-argument observation constructor remains available for explicit caller-supplied evidence; its caller remains responsible for complete scope. Witcher's existing scan API and observation types adapt the shared scanner without changing its page's refresh scheduling, native ownership or write gates. Modes exposing `IManagedContentComponentProvider` now receive a localized, read-only **Components** document in the main window. The ordinary managed-content host supplies this capability directly; the Remastered adapter supplies its fixed historical Mods/DLC read scope, with activation still gated. No additional JSON fields or per-game UI classes are required. Witcher's separate load-order/health page remains available through its existing lifecycle.

The component page shows folder/type, contributor origin, observed/missing/unknown presence, file and contributor counts, selected contributor details and bounded diagnostics. It uses the existing DevExpress document order, skins and display settings. Reads occur on activation or explicit Refresh and after coalesced native invalidations, outside painting. Inactive pages do not start automatic scans. Refresh keeps the selected component when it remains present.

`ManagedContentComponentViewService` resolves one physical root and immutable rule set, obtains fresh native metadata, scans and projects the detached inventory. `NativeComponentLifecycleObserver` shares the existing installer/deployment/Virtual invalidation signals with Witcher's compatibility observer. The page waits while native work runs, rejects observations crossed by native revision changes, and releases native subscriptions/timers when disposed. Native commit sequence alone is not used as a freshness guarantee. Disk changes made outside NMM require Refresh or tab reactivation. When declared, the page also displays configured order/enabled values from the game settings. Mutation commands require the separate editing declaration described below.

Witcher's public read model, DLC load-order exclusion and known merger-folder heuristics stay in its adapter. Generic component kinds carry no implicit order or merge capability. Order editing is a separate opt-in capability below; native-owned XML records are configured independently below, while merges remain a future capability. Do not use `UsesPlugins`, archive Sort or a standard INI rule to claim that BG3 `.pak`/`modsettings.lsx` behavior is implemented. See `ManagedContent-Capability-Audit.md` for source decisions, evidence, status and next slices.


## Shared INI order primitives (C# boundary)

`IniOrderFormat`, `IniOrderParser` and `IniOrderDocument` provide a reusable, bounded interpretation of section-based order files. The format specifies a literal component prefix and distinct priority/enabled key names. For example, `new IniOrderFormat("pkg", "Rank", "Active")` interprets package sections using the same codec that Witcher's adapter selects with `mod`, `Priority` and `Enabled`. The optional `componentOrdering` JSON block now selects this format through the shared read-only provider.

The parser retains original text and line numbers, including unknown fields/sections and mixed line endings. Priority values are signed integers; enabled values are exactly 0/1. Missing, invalid or duplicate values remain unknown, and duplicate component priorities are reported. The existing limits of 1,048,576 UTF-16 code units and 50,000 lines apply. Parsing neither determines game precedence nor grants ownership or write permission.

`IniOrderByteCodec` handles the existing strict UTF-8, UTF-8 BOM and BOM-marked UTF-16 encodings, rejects UTF-32/binary input during decoding, reproduces the original BOM and hashes exact bytes. `IniOrderTextEdits` supplies the format-preserving line split, value replacement, insertion/removal rendering and newline selection used by existing Witcher previews. These primitives are pure operations on caller-supplied data. The separate reviewed planner/publication boundary below adds controlled file publication.

Witcher's parser/read/preview APIs remain compatibility adapters. They keep `VK` metadata, path selection, original-byte preimage validation, complete-order review, explicit additions/default restoration, Legacy-only edit policy and publisher/recovery behavior. The typed JSON observation capability below uses these same mechanics. Witcher publication/recovery now adapts the shared publisher below; its Legacy-only policy and Remastered write gates remain compiled boundaries.


## JSON-configured order observation

Add optional `managedContent.componentOrdering` alongside `installLayout`:

```json
"componentOrdering": {
  "provider": "sectioned-ini-order-v1",
  "revision": 1,
  "fileRoot": "GameRoot",
  "relativePath": "Config\\package-order.ini",
  "componentKind": "asset-package",
  "componentPrefix": "pkg",
  "priorityKey": "Rank",
  "enabledKey": "Active"
}
```

All shown fields are required when the block is present. `provider` must match exactly and `revision` must be positive. `fileRoot` is `GameRoot` (the current physical installation) or `Documents` (the existing environment's resolved Documents location, including redirects). `relativePath` is a literal Windows-safe relative file path: no absolute/device paths, traversal, environment/template expansion or executable substitution. The observer never creates the file or its directory. Priority/enabled keys must be distinct case-insensitive literal identifiers of at most 64 characters.

`componentKind` must reference existing layout rules that agree on one target folder and exactly the declared prefix. Several source shapes may share that identity. Section names identify folders under that target folder; the configured kind/prefix scopes association so DLC and other kinds do not accidentally inherit mod ordering. The synthetic sample uses the block above; Witcher declares Documents, `The Witcher 3\mods.settings`, `mod-folder`, `mod`, `Priority` and `Enabled`. The reserved Witcher adapter accepts only that existing observation scope, independently of its activation/publication gates.

`IniOrderFileReader` is the shared bounded reader used by both the configured capability and Witcher's existing read API. It retains the existing 2 MiB byte limit, strict encoding and parser bounds, original-byte identity, missing/unreadable/oversized/changed-file statuses, and all original text. Missing or unusable files do not suppress the native component inventory. No value is inferred from a failed or ambiguous document.

The Components page exposes order configuration, configured priority and game-enabled columns only for a declared capability. Eligible folders without a section are **Not configured**; absent fields remain **Unknown**. File failures and document ambiguity remain explicit states. A valid section without a matching indexed component appears as **Settings entry only**, with no invented native owners/files. Its disk presence is **Missing** only after a complete validated component scan; otherwise it is **Unknown**. These values describe settings observations, not verified game-effective precedence or installation/activation state.

Existing definitions without this optional block retain their previous component-only view. Invalid definitions remain isolated by the existing loader. The ordering declaration does not change game IDs, storage settings, native formats, download routing or Collections effects. Catalog routing is independently declared as described below. Order editing requires the optional provider below. BG3 LSX effects and merging are not advertised by this INI provider.


## Optional reviewed sequential INI editing

Only enable this provider for a game whose order document actually uses complete component sections, priorities assigned in sequence from 1 to 9999 and 0/1 enabled flags. The generic codec does not establish which priority wins in a game. Add one optional field to `componentOrdering`:

```json
"editing": "reviewed-sequential-ini-v1"
```

Omitting `editing` preserves read-only behavior. Unknown provider values fail validation. The synthetic ManagedContentSample declares this provider for its `pkg` / `Rank` / `Active` format. The reserved Witcher Remastered adapter rejects editing declarations; JSON cannot enable its unverified 5.x order writes or release support. Existing Legacy Witcher UI continues through its fixed edition/path policy and now uses the shared planner/publisher.

The Components page offers **Edit game order** only for an opted-in managed-content host with an existing, decoded, unambiguous file and no pending publication. The draft includes all configured sections, including external or currently absent components. Move commands change draft positions; explicit game-enable/disable commands preserve absent flags unless changed. Current generic UI edits configured sections only: it does not enlist unconfigured folders. **Review restoring defaults** removes only the interpreted priority/enabled keys from all eligible sections, superseding the draft. It preserves headers, comments, foreign sections/keys and the file itself. Cancellation discards the draft.

`IniOrderChangePlanner` requires the exact original encoded bytes, retains unknown text/BOM/newlines, validates complete selection and reparses the full proposal. The final review displays every semantic delta and the complete replacement text/path. Observation is refreshed before approval; native revision, physical root and file identity changes invalidate review. A preview never authorizes publication by itself.

`ManagedContentOrderService` resolves current Game Storage and acquires the existing root cross-process target reservation. It reloads/validates native ownership using `CollectionTargetOwnershipAuthorityValidator`; no new ownership store or lock is added. A different mode/rule/root, a linked path below the explicitly configured root, or native payload/INI ownership history blocks standalone editing. Existing native file histories must continue through their native workflow rather than being overwritten by an external-order command.

The shared `IniOrderPublisher` independently checks the target, format, current preimage, live host policy and native authority. It only replaces an existing file: it never creates missing settings/directories. It exclusively creates and flushes pending intent, verifies a flushed original backup and replacement stage, checks the preimage again while denying ordinary writers, atomically replaces and verifies the result. Failed/uncertain publications retain recovery evidence. Shared sidecars are `<orderfile>.nmm-order.pending` and `<orderfile>.nmm-order.<id>.backup` / `.tmp`, with marker version `nmm-ce.ini-order.pending/1`; these identities are compiled and cannot be renamed by JSON.

**Review pending recovery** observes the marker, file, stage and backup. Explicit acknowledgement is allowed only for exact known original/published bytes and runs under freshly validated native authority; it removes verified staging/pending state and retains the original backup. Conflicting, linked, invalid or unknown evidence remains for manual review. No startup/refresh action repairs or acknowledges state automatically. Witcher's public status/API types and historical `.nmm-w3r.*` sidecars/marker version remain compatible, including pending operations from before this extraction.

This workflow is independent of NMM archive Sort, activation, profiles and Collections. It neither registers manual components nor makes order-file changes into native mod effects. The structured-record capability below uses native ownership separately; order-file edits are still not Collection effects.


## Nexus catalog identity and local download destination

A mode can declare its canonical Nexus catalog independently of its stable local identity:

```json
"modeId": "Witcher3Remastered",
"nexusDomain": "witcher3"
```

`nexusDomain` is optional for every data-driven host. It must be a lowercase slug of 1-64 letters, digits or hyphens, starting with a letter or digit; URLs, paths, whitespace and known local aliases such as `falloutnv` are rejected by runtime validation. Omission retains the existing `GameDomainTranslator` behavior. The reserved Remastered identity accepts only `witcher3` when the field is present; older definitions without it retain their alias fallback. The synthetic sample also declares a fixture-only catalog name; it is not a live Nexus catalog.

`INexusGameDomainProvider` is optional and is consumed by `NexusGameDomain.Resolve` for both descriptors and built game modes. Generic/managed-content, BG3 and data-driven Gamebryo hosts expose it. Native legacy DLLs need no interface changes and keep their existing aliases. Application initialization passes the resolved catalog to the existing Nexus repository. API requests and Collection identity checks therefore share that catalog, while queue settings, IPC endpoints, native storage, profiles and ownership remain keyed by the original local ModeId.

For an inbound NXM mod or Collection link, startup checks installed catalog matches before acquiring a mode mutex or contacting a running instance. This configured routing applies only when at least one matching installed mode explicitly declares `nexusDomain`; other domains retain the existing routing/VR/GOG overrides. A single match is selected directly. Several matches use the existing themed game-selection form with a download-destination prompt. The choice applies to this link and does not save or change global remembered-game preferences. Cancel aborts dispatch. Rescan or cancelled setup invalidates the pending choice and resolves it again against the current installed modes. An already running instance receives the link through the selected ModeId's existing IPC endpoint.

Configured consumers verify the link catalog again in IPC before Collection dispatch and in AddModTask before fresh or cached descriptor processing, repository metadata requests or downloads. A mismatched catalog is reported without reinterpreting its mod/file IDs in the selected repository. Existing unconfigured legacy aliases and local/manual file imports retain their prior behavior. Selecting a destination does not bypass that mode's discovery, setup, release-support or native authority checks: Remastered remains disabled by its separate release gate.

Remastered Collections remain read-only metadata support. `Witcher3NativeMutationReview.RequireCollectionSupport` exposes the existing manual-only policy to additive plan preparation/refresh before acquisition and to shared native recipe validation before effect preparation. The original installer/removal guards remain. This deliberately reports unsupported installation early; it does not claim or enable Remastered Collections deployment, structured effects or recovery. Other modes retain their existing Collection workflows.


## Native-owned profiled XML records

Declare optional root `structuredEffects` to select `profiled-attribute-records-v1`, independently of `managedContent` layouts and INI order observation. The generic and managed-content hosts consume it directly. The BG3 compatibility host consumes the same provider while retaining its existing split-root file routing. Data-driven Gamebryo, No Man's Sky special routing and the reserved Remastered profile cannot silently absorb this declaration; unsupported combinations fail definition validation/factory construction.

This bounded strategy handles XML `node` records discriminated by literal `id` attributes, stored under one configured container's `children` element. Record fields are child `attribute` elements with literal `id`, `type` and `value` attributes. Identities are GUIDs. It is reusable for this characterized format, not an arbitrary XML/XPath/script language. LocalAppData profile-root paths, existing manifest filename, excluded profiles, root metadata filename/array/encoding, record/container/identity fields, stable native namespace and ordered field bindings are all data.

The shipped BG3 definition is the real consumer: `info.json` / `Mods` metadata creates `ModuleShortDesc` records under `Mods`, in existing PlayerProfiles `modsettings.lsx` files. Folder/Name aliases, empty MD5, UUID and Version64/default 1 remain the historical payload shape. Its native namespace stays `bg3-modsettings-v1|<profile>|<canonical-guid>`. A compiled compatibility check prevents JSON from changing the meaning/location of already persisted BG3 keys. Definitions without `structuredEffects` retain the old adapter/codec; existing public BG3 APIs remain available.

The synthetic ManagedContentSample instead declares `package.json` / `Packages`, UTF-8 metadata, `Packages` / `Package` XML records with Id/Label/Revision fields, a separate LocalAppData sample profile directory, `packages.xml` and `nmm-sample-records-v1|`. Its `fixtures/package.json` and `fixtures/Profiles/Example/packages.xml` are reviewable example inputs, including an unrelated external record/field and foreign XML settings. They are not installed or executed automatically. No per-game class/factory branch is needed to select this format. Use an explicit fixture root through the internal store seam for later verification; production always resolves the declared relative root under LocalAppData.

Every declaration requires `schemaVersion: 1`, `provider: profiled-attribute-records-v1`, a positive `revision`, `metadataFile`, `metadataArray`, `metadataEncoding` (`Ascii` or strict `Utf8`), `profileRoot`, `manifestFile`, explicit `excludedProfiles`, `keyPrefix`, `containerId`, `recordId`, `identityField` and 1-32 `fields`. See the shipped/sample JSON for complete declarations. `profileRoot` is a safe relative path; filenames/excluded profiles are safe single segments. The key prefix is a literal identifier ending in `|`. Treat it and the target record shape/path as persisted contracts: changing them after users own native values requires a separate explicit migration, not an ordinary revision increment.

Each field declares literal `id`/`type` and either 1-8 unique ordered metadata `sources` or a constant `value`. A source field may supply `defaultValue`; a constant cannot supply sources/defaults. The identity field must bind source metadata without inventing a default GUID. Duplicate field IDs, unsafe paths, invalid native namespaces, unknown providers, incompatible profile declarations and ambiguous metadata are rejected. Unknown metadata fields remain ignored. Missing metadata files produce no new effects; an explicitly empty array is valid. A present metadata file with a missing/null/wrong-type record array, invalid identities or duplicate selected GUIDs blocks planning instead of silently becoming an empty upgrade-removal plan.

Planning reads all eligible existing profiles before the first mutation, retains existing selected record bytes when present, and returns immutable `BasicInstallGameSpecificValue` entries. It preserves metadata order and deterministic profile ordering. No profile/manifest is created. Foreign XML nodes/fields and unselected records are retained; XML serialization keeps the established tab-indented writer and does not promise byte-for-byte whitespace preservation. Full-file native transaction snapshots retain original rollback bytes. BG3 ModOrder/dependency/engine-effective ordering semantics are not added by this record provider.

Bounds are 1 MiB metadata, JSON depth 32, 2,048 metadata/manifest records, 256 profiles, 256 KiB per record payload, 8,192 planned native values and 16 MiB aggregate planned payload. XML decoding limits document characters to 16,777,216 and prohibits DTD/external entity resolution. Duplicate selected XML identities/containers, type conflicts, observed linked profile/manifest paths and inaccessible evidence fail explicitly. Missing existing profile manifests are skipped as in the legacy contract; there is no automatic repair or profile creation.

Ordinary non-scripted basic installs apply the prepared records inside the existing ModInstaller transaction using `StructuredGameSpecificValueInstaller`; native Direct/Virtual file deployment remains in BasicInstallTask. The declared metadata is excluded from deployment and managed layout mapping uses the existing reviewed mapping hook. File-only prepared mapping remains in use for modes without structured effects, including Remastered. Failure/cancellation rolls back through native InstallLog and TxFileManager rather than a new effects journal/lock/store. Existing managed-value overwrite confirmation is reused.

The shared installer records original/current values, updates same-owner entries, preserves shadowed owner histories, restores only the winning owner's prior value on removal and snapshots each touched manifest once. The upgrade variant updates shadowed owned values without replacing the winner, and removes omitted previously owned keys only after successful basic execution. The native restore/read interfaces remain available to Collection verification/local recovery. BasicUninstallTask removes tracked values through the same owner stack; BG3's untracked pre-capability/manual special-removal fallback remains intact. For configured Virtual modes, Enable/Disable commands use the existing full native reinstall/uninstall flows so file links and record ownership change together. The UI does not run the old opaque post-task manifest callbacks for these modes. Definitions without the capability keep their previous quick-link callbacks; Direct-install toggle restrictions remain unchanged. The automatic basic metadata path is not a replacement for scripted installer semantics; BG3 retains its legacy scripted special callback.

Deterministic Collection planning uses the existing special-file/value interfaces and never invokes the live special installer. Explicit native recipe execution already applies those values through the same installer and existing Collection verification/replay/recovery boundaries. Existing unsupported replacement projections still block records; this delivery does not widen them or enable Remastered Collections. Definitions without the capability, legacy special handlers and all Remastered release/order/mutation gates remain unchanged. Runtime/rollback/profile/Collection certification still requires later execution evidence.


## Optional external tool review

Optional tools continue to live in `supportedTools.json` beside `game.json`; inline tools remain invalid. The existing directory picker, per-ModeId settings (and optional shared `settingsKey`), discovery order, argument-token expansion/quoting, supported-tools commands and process launch events are reused. Entries without `launchReview` retain their previous behavior. The feature is independent of install layout and structured effects and is available to all existing data-driven hosts.

An individual tool can opt in:

```json
"launchReview": {
  "schemaVersion": 1,
  "provider": "reviewed-external-tool-v1",
  "revision": 1,
  "compatibility": "manual-version-info-v1",
  "outputPolicy": "external-unmanaged-v1"
}
```

All fields are required; provider/output policy are fixed and revision must be positive. Executable alternatives must be 1-16 distinct safe direct executable names. `manual-version-info-v1` displays version evidence and requires the user to establish game compatibility. It does not infer compatibility from a version number. `witcher3-remastered-script-merger-v1` is a compiled compatibility adapter available only to the reserved Remastered host and the three executable names already recognized by the Witcher inspector. Unknown providers, inappropriate hosts/names and unsupported output policies are validation errors. The existing strict companion-file loader isolates invalid game definitions; no inline fallback is introduced.

The real Remastered companion declares Script Merger, without automatic discovery or command-line game-target assumptions. The user configures its directory through the existing Supported Tools menu. The independent synthetic companion declares `SamplePackageMerge.exe` and passes `--game-root` and `{GamePath}` as separate tokens. It is an authoring example; no example executable is supplied, installed, downloaded or run. No new per-game class/factory branch is needed to select the reviewed-launch provider. The shared schema and SupportedTools template describe the same opt-in extension.

Only an explicit launch command inspects the selected executable. Inspection runs off the UI thread, rejects missing/inaccessible/linked executable files, bounds reads to 512 MiB, sanitizes displayed VERSIONINFO fields and computes a SHA-256 byte identity while denying ordinary writers. Read failures or changed evidence cannot authorize launch. The review displays exact executable, resolved argument string, current root, product/version, hash, compatibility assessment and the external-output policy. Approval defaults to No and is requested every time; no persistent approval/trust flag is stored. After approval the executable is inspected again, its bytes/metadata/length/write time must still match, and the selected path, arguments and root are checked again. This freshness check is not executable authentication, process sandboxing or a guarantee against a hostile filesystem race.

The Remastered adapter reuses `Witcher3ExternalScriptMergerInspector` without changing its public APIs or assessment thresholds. Only its `RemasteredSupportClaimed` result can proceed to approval. Known original/pre-5.x families, unknown release evidence and SM-FAE rc.4 (known GOG-detection caveat) are blocked by this provider. Current game-root/edition launch policy is rechecked before review and immediately before process startup. VERSIONINFO remains self-reported: even an allowed result is not authenticity/runtime certification or proof that the external tool chose the right game directory.

`external-unmanaged-v1` is deliberately the only output policy. No output folder is registered, claimed, deleted, backed up or restored by this launch workflow. Generated/manual components remain externally observed content under the existing Components scan. The launch is not a Collection effect and is not invoked automatically by installation, upgrade, removal, repair or recovery. There is no process-exit watcher, completion/success inference or automatic rescan; the user finishes native mod tasks before launching and refreshes Components after closing the tool. This opt-in launcher does not hold a native mutation lease across an external process or prevent the user from starting other tasks. Native fragment-merging/generated-output ownership and replay require a separate characterized provider. Remastered activation, 5.x order editing and Collection-install gates remain unchanged.


## Package-local XML fragments and native generated files

The independent optional `packageFiles` capability combines XML fragments from **one archive** into one ordinary native-owned Data file. It is not a cross-mod union: if two packages target the same output, the existing whole-file overwrite/owner rules choose which complete package output is active. Removing that owner restores the native prior payload; it does not combine the remaining mods' XML. Witcher scripts/blobs and Dragon Age's shared chargenmorph rebuild are not represented by this provider.

```json
"packageFiles": {
  "schemaVersion": 1,
  "provider": "package-xml-elements-v1",
  "revision": 1,
  "sourceFolder": "PackageFragments",
  "destinationPath": "Packages\\pkgManifest\\assets\\packages.xml",
  "documentRoot": "PackageSet",
  "recordElement": "Package",
  "identityAttribute": "Id"
}
```

Every field is required. The provider accepts only the generic or managed-content host, `pathAdjustmentProfile: none`, ordinary Data-root installation and no secondary/game-root rules. Invalid hosts/providers/revisions, unsafe or overlapping input/output paths and nonliteral XML names fail validation. For a managed layout the output must already be a valid configured component destination; generated files do not widen that layout. Shipped real-game definitions are unchanged and no Remastered gate is relaxed.

`sourceFolder` is an exact archive-relative directory, without wrapper discovery, globs or expressions. Every file below it must be a selected XML fragment without a destination override; a partial selection is rejected. The directory may be absent, in which case this capability produces no generated file. An explicitly present empty/invalid fragment is an error. All remaining selected files still follow ordinary filtering and layout rules. A package containing only fragments can produce a valid generated-only native recipe without requiring an unrelated payload file. A managed package whose remaining ordinary selection consists only of layout-ignored files can still be rejected by the existing layout contract; no general empty-layout fallback is added.

Each fragment must have the exact unqualified `documentRoot`, no root attributes/non-whitespace text, and nonempty direct children with the configured `recordElement` and `identityAttribute`. Identity values are trimmed, case-sensitive, nonempty and at most 256 characters without control characters; the input must already have its trimmed spelling. Duplicate identities are rejected, even when their bytes match. Records are appended in canonical archive-path order (ordinal-ignore-case, then ordinal), preserving record order within each fragment and unknown nested content/record attributes. Root comments/processing instructions and source formatting are not output semantics. No implicit duplicate resolution, namespace matching, dependency/load-order logic or executable expressions are supplied.

Input XML follows its encoding declaration; DTD/external entity resolution is prohibited. Limits are 100,000 archive/selected paths, 32 fragments, 1 MiB per fragment, 8 MiB aggregate input, depth 64, 65,536 reader nodes per fragment, 4,096 output records and 8 MiB generated output. The serializer stops before exceeding the output limit, rather than checking only after allocation. Output uses an XML declaration, UTF-8 without BOM, two-space indentation and LF newlines. This is a semantic record merge; source whitespace/BOMs are not preserved.

`IDeterministicPackageFilePlanProvider` returns consumed source provenance and immutable `BasicInstallGeneratedFile` bytes. The existing `BasicInstallPlanBuilder` consumes those inputs before ordinary mapping, validates native game/Virtual projections and rejects generated/generated or generated/archive destination collisions. The output is always included in package-local planning; legacy cross-mod merge deferral does not apply. Collection preparation sees the same exact generated bytes and retains them through its existing generated-file artifact path; existing unsupported effect/replacement policies still apply.

Ordinary configured installation prepares the complete plan under the existing native operation's authority/read-only archive scope, before replay deletion and native registration. It reuses the existing replay-basename collision check, freezes the native context and executes the exact archive/generated/value operations through `ImmediateScriptedInstallOperationExecutor`. The existing Direct/Virtual installers provide overwrite checks, staging/linking, original/owner histories, transaction snapshots and upgrade handling. Generated-file replay payloads use the existing hashed sidecars; no new owner store, journal or deployment engine is added. Native uninstall removes/restores the logged output as an ordinary file; upgrade cleanup removes omitted owned outputs through existing finalization. The executor's current failure/cancellation behavior is retained; no new manual per-operation progress/cancellation UI is claimed.

Scripted packages and raw `BasicInstallTask` expansion are blocked for configured generation rather than silently deploying fragments. File-only prepared mappings also reject the capability. Native owner capture conservatively retains actual payload bytes instead of classifying configured output as an exact archive entry. Local Collection restore/replay therefore uses existing retained-byte/generated-operation contracts; ordinary profile reinstall rebuilds from the archive/configuration as before, and is not advertised as exact historic configuration replay. Existing replay/cache cleanup and recovery rules remain in force; this capability does not add automatic repair or certify crash durability.

Two independent non-executed configurations demonstrate the vocabulary: `Templates/ManagedContentSample/game.json` combines `PackageSet/Package/Id` into an existing Packages component while also declaring independent record/order/tool capabilities; `Templates/PackageXmlSample/game.json` uses the ordinary generic host with `Registry/Entry/key` and `Generated\registry.xml`. Each includes two reviewable XML inputs under its `fixtures` folder. Copy the appropriate fragment directory into a package root; the first sample's profile XML fixtures are external-profile examples, not archive content. These templates are not installed, executed or runtime-certified automatically. A real game's XML semantics must be characterized before opting it into this provider. Cross-mod rebuild/recovery and full Remastered Collections remain separate unsupported work.


## Frozen Witcher 3 Remastered release boundary

The current first-release scope and gate decision are maintained at the top of [ManagedContent-Capability-Audit.md](ManagedContent-Capability-Audit.md). W3R declares folder layout/order observation and an external reviewed tool. Its manual basic native workflow is source-implemented; activation remains blocked by the compiled `Witcher3RemasteredReleasePolicy` until real-installation runtime evidence exists. Startup and direct factory construction use that same decision and localized explanation.

Optional structured XML, package-local XML generation, shared cross-mod merging, Collections, scripted packages, Legacy migration and 5.x order writing are not prerequisites for that bounded release. Adding provider declarations to game.json cannot remove the reserved host's native/release restrictions. Keep Remastered order reads observational even after a future basic-mode activation decision. No tests or game runs were performed to freeze this scope.
