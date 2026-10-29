# Larian package inspector

Read-only win-x64 helper for the existing NMM load-order inventory protocol. The application remains on .NET Framework 4.8; the helper targets .NET 8 and is published with its runtime. It receives one bounded JSON request on stdin and returns one response on stdout. No extraction, script execution, child processes or configuration writes are performed.

From the repository root, run:

```powershell
./Support/LarianPackageInspector/Build.ps1 -Configuration Release
```

The script downloads the official LSLib v1.20.4 release to the ignored Stage dependency cache, verifies SHA-256 `5e02368fb8acafda9b45acba37a3f3bf507fc3d65a083a159abbeab06337190e`, and publishes to `Stage/Release/Tools/LoadOrder`. Debug publication uses the corresponding Debug directory. This is an explicit production build step; it is not attached to the classic application's build or solution and does not compile or run tests. Run it before building the installer: `Setup/setup.iss` includes the published directory when present. A source checkout without helper publication can still build the existing application/installer with inventory inspection unavailable.

Build from a current .NET SDK capable of targeting .NET 8. Restore may download Microsoft runtime/reference packs. Distribution requires the entire published helper directory, including runtime, native library and ThirdPartyNotices.txt. Never substitute the library's assembly version for the pinned release identity: upstream assembly versions can lag release versions.

Package indexes and bounded text metadata are read in memory. A single text entry is limited to 1 MiB; aggregate text inspection is limited to 16 MiB per package. Solid packages are deliberately Partial, as are unclassified metadata-free packages. Positive feature observations do not prove that a mod requires an extender, that the extender is installed, or that the game will accept the package. Numbered package names are continuations only when a valid primary header declares the corresponding part.

Regression source is in `Tests/LarianPackageInspectorTests`. The maintainer handles compiling and running tests. Production build/publication alone does not verify package parsing at runtime.

See `GameModes/LoadOrder-Infrastructure-Handoff.md` for protocol details and the remaining native lifecycle, diagnostics/sorting and generic UI tasks.
