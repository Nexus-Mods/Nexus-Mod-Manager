# Larian package inspector

Read-only win-x64 helper for the existing NMM load-order inventory protocol. The application remains on .NET Framework 4.8; the helper targets .NET 8 and is published with its runtime. It receives one bounded JSON request on stdin and returns one response on stdout. No extraction, script execution, child processes or configuration writes are performed.

Building `NexusClient.csproj` or the classic solution in **Release** now publishes the helper automatically through `Support/BuildTasks/LoadOrderHelper.targets`. The helper remains outside the classic solution graph. Designer and ordinary Debug builds do not download dependencies or require a .NET SDK. Release build agents require a .NET 8-or-newer SDK on PATH, in addition to NMM's existing toolchain. The AppVeyor image is Visual Studio 2022.

For standalone publication or a Debug helper, run from the repository root:

```powershell
./Support/LarianPackageInspector/Build.ps1 -Configuration Release
```

`Dependencies.props` is the shared source of dependency pins: official LSLib **v1.20.4**, archive SHA-256 `5e02368fb8acafda9b45acba37a3f3bf507fc3d65a083a159abbeab06337190e`, and the self-contained **.NET 8.0.32** runtime (matching the previously staged helper). Update these pins deliberately when servicing dependencies. `RuntimeFrameworkVersion` selects the [exact runtime for a self-contained deployment](https://learn.microsoft.com/en-us/dotnet/core/versions/selection#self-contained-deployments-include-the-selected-runtime), independently of the SDK installed on the build agent.

The script caches and checks the upstream ZIP, extracts fresh dependency bytes for each publication, and checks the published DLLs against that extraction. Default cached reference paths are also refreshed for direct helper/fixture compilation. It publishes to `Stage/Release/Tools/LoadOrder`; Debug uses the corresponding Debug directory. The MSBuild target supplies its actual `TargetDir`, so custom application output directories work too. Publication is serialized per checkout, stages a fresh payload, verifies it before replacing the previous output, and writes `package-manifest.json` with source/pin identity and every file's length/SHA-256. A failed publication cannot leave a stale completion manifest. Build-directory cleanup is restricted to checked child paths and refuses reparse points.

Distribution requires the **entire** published directory: helper, bundled runtime, managed/native dependencies, notices and manifest. Nothing is copied into NMM's application-root assembly directory. Never substitute upstream assembly versions for the pinned release identity: those versions can lag release versions. Runtime/reference-pack restore may require network access; end users do not need a separately installed .NET 8 runtime.

`Setup/setup.iss` runs the following read-only gate at compile time using [ISPP Exec](https://jrsoftware.org/ishelp/topic_exec.htm). Missing, edited, extra or stale helper files stop installer compilation; the helper can no longer be silently omitted:

```powershell
./Support/LarianPackageInspector/Build.ps1 -Configuration Release -VerifyOnly
```

Verification never downloads, invokes the SDK, builds, runs tests or executes the helper. Changing helper source, notices, the publisher or pins requires a fresh publication. Installer upgrades replace only the application-owned `Tools/LoadOrder` directory to remove obsolete runtime files. Helper installation is restricted to 64-bit Windows; NMM's existing x86 installer eligibility is preserved. No user profiles, settings or mod folders belong to this directory.

Package indexes and bounded text metadata are read in memory. A single text entry is limited to 1 MiB; aggregate text inspection is limited to 16 MiB per package. Solid packages are deliberately Partial, as are unclassified metadata-free packages. Positive feature observations do not prove that a mod requires an extender, that the extender is installed, or that the game will accept the package. Numbered package names are continuations only when a valid primary header declares the corresponding part.

Regression source is in `Tests/LarianPackageInspectorTests` and `Support/BuildTasks/Tests/LoadOrderHelperPackaging.Tests.ps1`. The latter uses synthetic files to exercise complete, missing, tampered, stale-source, extra-file and wrong-runtime package gates without an SDK or real helper execution. The maintainer handles all compilation and test execution. The release-packaging changes have received static inspection only; no build, helper publication, installer compilation or regression execution was performed in this implementation turn.

See `GameModes/LoadOrder-Infrastructure-Handoff.md` for protocol details, implemented lifecycle/UI behavior and remaining maintainer validation.
