# Collections Gate A / Gate L Validation

## Purpose

This document records the validation boundary that must be satisfied before beginning C8 replacement work.
It does not widen Collection compatibility and does not replace the existing native NMM test suite.

Gate A covers the currently supported write-capable Additive Collections slice.
Gate L covers the currently supported sealed Local Collection capture/restore slice through complete Step 9 final verification and restart composition.

## Authoritative baseline

- Source archive: `Nexus-Mod-Manager-Patch8-forreal.zip`
- Source archive SHA-256: `96c198bc69e25b7b05b08896f5377c7353788d7e9653f0c69ae1e8ad18855695`
- Step 9 patch: `NMM-Step9-Complete-Local-Restore-Workflow.patch`
- Step 9 patch SHA-256: `087bfcb3f4d1990e2874b39edb5d38a77c99214fe3a0954e8e034bf9b44f13a5`
- Maintainer-reported Step 9 Release / Any CPU build: **PASS**, 27 September 2026.

The successful build is build evidence only. Gate A / Gate L are not complete until the automated and disposable-setup checks below have been executed and their results recorded.

## Supported slice under test

Gate A is intentionally limited to the capabilities already implemented and reviewed in C6:

- exact concrete Nexus revision/file identities;
- additive install into the current setup;
- characterized explicit/basic file operations;
- reviewed Direct and Virtual install contexts;
- supported plugin effects;
- deterministic reviewed file-winner reconciliation;
- many-to-many provenance;
- safe detach and safe effect removal;
- durable child intent, restart reconciliation and aggregate final verification.

Gate L is intentionally limited to sealed Local Collections whose capture declares `LocallyRestorableWithinScope` and whose stored scope is fully supported by the current C7 implementation:

- native member identity/context and exact retained archives;
- managed file ownership and retained original/fallback payloads;
- scripted replay XML and generated payloads;
- supported plugin state;
- supported INI owner/value history;
- supported game-specific value history;
- supported Sort/screenshot user metadata;
- profile preservation/detachment boundary;
- outgoing association reconciliation;
- final aggregate verification and restart/resume through Step 9.

Unsupported Vortex fields, uncharacterized merge/tool/root transforms, unresolved `latest` / `prefer`, arbitrary scripts/tools and other explicitly blocked capabilities remain outside these gates.

## Automated suites

Step 10 adds NUnit categories so the gate-relevant tests can be run independently from the entire solution.

### Gate A category

Run tests with NUnit category:

`CollectionsGateA`

The category includes the existing vertical preparation/apply/effects suites plus native-operation, cross-process reservation, Virtual-store failure injection, final verification, winner reconciliation, detach and safe-uninstall coverage.

Key failure boundaries already covered include:

- durable child intent persisted before native start;
- failure before native worker start;
- safe pause before native commit;
- failure after native commit but before terminal Collection checkpoint;
- failure after durable native terminal checkpoint but before provenance reconciliation;
- cancellation after a committed member with restart of only remaining work;
- Direct and Virtual rows in the Gate-A failure matrix;
- Virtual primary-store / XML-shadow failure behavior;
- reviewed winner reconciliation and final-state verification;
- cross-process target reservation and manual native mutation coordination.

### Gate L category

Run tests with NUnit category:

`CollectionsGateL`

The category includes:

- Local capture contracts and sealing;
- durable package/store round trips and target-scoped capture enumeration;
- owner/original/fallback payload capture;
- scripted replay/generated payload capture;
- plugin, INI and game-specific effect capture/state comparisons;
- Sort/screenshot metadata capture;
- C7.9 restore planning, exact archive matching, remapping, extra-effect recreation and retained-input validation;
- native retained-owner restore contracts;
- replay replacement and transition safety;
- plugin/INI/game-value exact-state comparisons;
- user-metadata transition validation;
- C7.11 profile preservation and outgoing-association rules.

### Running from Visual Studio

Use Test Explorer on the `NexusClientTests` project and filter by NUnit Category / TestCategory:

- `CollectionsGateA`
- `CollectionsGateL`

Record separately for each category:

- build configuration (`Release | Any CPU`);
- total / passed / failed / skipped;
- first primary failure if any;
- TRX/test-results artifact location;
- source commit or exact patch stack used.

Do not treat an environmental skip (for example a privilege-dependent Windows symlink case) as a product failure without checking the test's documented prerequisites.

## Disposable manual Gate A matrix

Run on a disposable game/storage setup. Do not use the maintainer's primary modded installation.

| ID | Scenario | Required result |
|---|---|---|
| A1 | Verified compatible member already installed | Apply performs zero native reinstall/write for that member; association becomes Applied only after final verification. |
| A2 | Exact simple member, Direct install | Reviewed context remains Direct through native execution; normal NMM uninstall/reinstall still works afterward. |
| A3 | Exact simple member, Virtual install | Reviewed context remains Virtual; Virtual state is durable and normal disable/uninstall restores the correct previous owner/fallback. |
| A4 | Plugin-producing supported member | Review shows the plugin side effect and final native activation/order matches the reviewed effect. |
| A5 | Two selected writers with deterministic before/after rule | Reviewed winner is established through native owner-switch services, not install order. |
| A6 | Existing unrelated current winner | Apply remains blocked/ActionRequired; no native mutation occurs. |
| A7 | Two Collections share a compatible native member | One native instance remains; both associations survive. |
| A8 | Detach one Collection | Tracking is removed without uninstalling the native mod/effects needed as standalone or by another association. |
| A9 | Uninstall Collection effects | Only dispensable native effects are removed; unrelated/shared content remains. |
| A10 | Cancel after one member has committed | Journal reports accurate partial state; restart does not reinstall the committed member and continues remaining work. |

For A2/A3, inspect the member afterward through ordinary Mods/File Manager paths. Collection installation must not make the native member dependent on a Collection-only cleanup engine.

## Disposable manual Gate A recovery matrix

The following cases require a debugger/failure-injection build or deliberate process termination at the named boundary. Always restart NMM against the same disposable storage and let native recovery run before Collections reconciliation.

| ID | Interruption boundary | Required restart result |
|---|---|---|
| AR1 | After durable child intent, before native worker start | Same operation/attempt resumes; no duplicate child is created. |
| AR2 | After native files/state commit, before terminal child checkpoint | Authoritative reality is inspected; committed work is reconciled and not blindly replayed. |
| AR3 | After terminal child checkpoint, before C6.10 provenance | Existing terminal evidence is consumed; no second native install; partial association cannot be falsely Applied. |
| AR4 | After Virtual primary save / compatibility-shadow failure | Valid primary state is not replaced by stale fallback; degraded state remains explicit/recoverable. |
| AR5 | After child commit while cancellation is requested | Cancellation does not claim rollback of committed native effects; restart resumes from the next safe boundary. |

## Disposable manual Gate L baseline scenario

Use at least one plugin-heavy Bethesda-style game and one pluginless/non-Bethesda game supported by NMM. Keep the tested Local Capture deliberately inside the currently supported scope.

1. Create a disposable setup containing a mix of Direct and Virtual members where the game supports both.
2. Include at least one managed overwrite path with a meaningful previous/original fallback.
3. On the plugin-heavy game, include a supported plugin state/order case.
4. Include a scripted replay case with at least one generated payload when a suitable fixture/mod is available.
5. Include supported INI/game-specific effects where the game exposes them.
6. Set at least one explicit Sort value; include a supported screenshot override if available.
7. If an NMM profile is currently attached, note its identity and preserve its files for comparison.
8. Save the current setup as a Local Collection and confirm it is reported `LocallyRestorableWithinScope`.
9. Record the capture identity and retain the Collections operation/database/log artifacts.
10. Deliberately change the managed setup: remove/reinstall members, change an owner winner, change plugin state, and add an extra managed effect to a captured reusable member where safe to do so.
11. Select the saved Local Collection and open the restore review.
12. Confirm the review is explicit and does not silently mutate the game.
13. Execute restore.
14. Verify the operation reaches `Completed / Committed` only after final verification.

Required final result:

- every captured member is present with the verified current native-key remap;
- no extra managed effect that is absent from the capture remains merely because a native registration was reusable;
- file owner order/current winner and retained fallback bytes match the sealed capture;
- replay XML and generated payloads match the retained set;
- supported plugin state/order matches the capture;
- supported INI/game-value owner history and effective values match;
- supported logical user metadata matches without replacing unrelated shared database content;
- the outgoing profile remains preserved/detached according to the reviewed C7.11 boundary;
- superseded outgoing Collection associations are not falsely left `Applied`;
- unknown/unmanaged files outside the declared managed scope are not wiped;
- archives retained by NMM remain in the library unless a separate explicit cleanup action is performed.

## Disposable manual Gate L recovery matrix

Interrupt the same disposable restore at one boundary per run. A debugger breakpoint immediately after the named phase has durably advanced is the preferred method; terminate the process rather than converting the exercise into graceful cancellation.

| ID | Boundary | Required restart result |
|---|---|---|
| LR1 | Member phase completed, before ownership restore | Startup rehydrates the same reviewed capture/plan/remaps and continues from the safe boundary. |
| LR2 | During owner/fallback restoration after durable intent | Exact preimage/postimage logic decides whether to continue; a third state remains RecoveryRequired. |
| LR3 | Ownership phase complete, before replay | Completed ownership is not repeated destructively; replay continues from its own durable boundary. |
| LR4 | Replay XML/payload publication interrupted | Only recognized preimage/desired transition states may continue; rogue/foreign payload state remains RecoveryRequired. |
| LR5 | Plugin phase completed | Restart preserves the verified plugin result and proceeds to INI without replaying earlier phases. |
| LR6 | INI phase completed | Restart verifies the completion marker and proceeds to game-specific values. |
| LR7 | Game-specific phase completed | Restart proceeds to logical user metadata without repeating earlier native mutations. |
| LR8 | User-metadata phase completed | Restart proceeds to profile/association reconciliation. |
| LR9 | Profile/association phase completed, before aggregate final commit | Startup runs final aggregate verification; it does not mark success from phase checkpoints alone. |
| LR10 | Mutate one verified domain immediately before final verification | Final verifier fails closed and journal becomes RecoveryRequired rather than `Completed / Committed`. |

After every recovery case, confirm there is no duplicate native installation, duplicate generated payload publication, stale Applied association, or silent reattachment of the outgoing profile.

## Cross-process / storage checks

Before declaring the gate passed:

- attempt an overlapping mutation from a second NMM process targeting the same physical game/storage; unsafe overlap must be rejected/serialized by the canonical target authority;
- exercise a supported storage relocation only on a disposable copy and confirm retained Collection references still resolve;
- confirm opening the Collections list with saved but unopened captures does not eagerly hash/open every archive;
- confirm no API credentials, temporary download keys or private retained-content payloads are emitted into ordinary diagnostics.

## Automated run 1 - 27 September 2026

Maintainer full-suite TRX: `NexusClientTests.trx`.

- Total: 1,326
- Executed: 1,320
- Passed: 1,295
- Failed: 25
- Not executed: 6
- Gate A classified from the Step 10 category-marked fixtures: 119 total, 110 passed, 9 failed.
- Gate L classified from the Step 10 category-marked fixtures: 80 total, 79 passed, 1 failed.

The nine Gate A failures all terminate in `CollectionAdditiveFinalStateVerifier` and expose deterministic vertical-test fixtures that claimed a native commit without fully materializing the reviewed physical file state, or recorded the reviewed winner barrier without changing the fake owner stack. The corrective patch updates those fixtures; production final verification remains unchanged.

The one Gate L failure expected the generated replay destination basename (`generated.bin`) to equal the intentionally GUID-based replay sidecar filename. The corrective patch validates the retained sidecar name against the actual replay operation and validates the destination separately.

Outside the gate categories, the same TRX contains 13 DevExpress `CategoryTreeViewportTests` synthetic-pointer failures, one asynchronous lifecycle characterization race, and one missing-English-localization-key failure. The lifecycle test is corrected to wait for terminal completion, and the missing static English keys are added. The six not-executed symlink tests all report Win32 error 1314 (required privilege not held) and are recorded as environmental skips, not product failures.

This first run does **not** pass Gate A or Gate L. Rerun the corrected automated suites before changing the gate decision below.

## Evidence record

Record results here or in the release issue/build artifact. Do not replace a missing runtime result with a source-review assertion.

| Evidence | Status | Artifact / notes |
|---|---|---|
| Release / Any CPU build | PASS (maintainer reported 2026-09-27) | Record final build log/path in release evidence. |
| `CollectionsGateA` automated tests | RUN 1 FAIL - 110 / 119 passed | Corrective gate-fixture patch required; rerun after apply. |
| `CollectionsGateL` automated tests | RUN 1 FAIL - 79 / 80 passed | Corrective replay-test expectation patch required; rerun after apply. |
| Gate A disposable baseline matrix | PENDING | |
| Gate A interruption/recovery matrix | PENDING | |
| Gate L plugin-heavy baseline restore | PENDING | |
| Gate L pluginless/non-Bethesda baseline restore | PENDING | |
| Gate L interruption/recovery matrix | PENDING | |
| Cross-process target authority check | PENDING | |
| Existing single-mod/profile/storage smoke regression | PENDING | |

## Gate decision

Do not begin C8 replacement merely because the solution builds or because individual C7 executors have unit tests.

Gate A is passed only when its automated category and disposable recovery matrix pass for the advertised additive slice.
Gate L is passed only when the Local Collection automated category and disposable full restore/restart matrix pass for the advertised local-restorable slice.

Any failure that exposes a product defect should be fixed as a narrow corrective patch against this exact baseline and the affected gate row rerun before C8 begins.
