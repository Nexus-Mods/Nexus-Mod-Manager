param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputDirectory,
    [switch]$VerifyOnly
)

$ErrorActionPreference = 'Stop'
$readerRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
[xml]$readerPins = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Dependencies.props'))
$readerVersion = [string]$readerPins.Project.PropertyGroup.LarianDependencyVersion
$readerExpectedHash = [string]$readerPins.Project.PropertyGroup.LarianDependencyArchiveSha256
$readerRuntime = [string]$readerPins.Project.PropertyGroup.LarianRuntimeFrameworkVersion
$readerAssemblies = ([string]$readerPins.Project.ItemGroup.LarianDependencyAssembly.Include).Split(';')
if ($readerExpectedHash -notmatch '^[a-f0-9]{64}$' -or $readerRuntime -notmatch '^8\.0\.\d+$' -or $readerVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw 'Invalid load-order helper dependency pins.'
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $readerRoot "Stage/$Configuration/Tools/LoadOrder" }
$readerOutput = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\', '/')
if ([IO.Path]::GetFileName($readerOutput) -ne 'LoadOrder' -or [IO.Path]::GetFileName([IO.Path]::GetDirectoryName($readerOutput)) -ne 'Tools') {
    throw 'The helper output must be an application-owned Tools/LoadOrder directory.'
}
$readerManifestName = 'package-manifest.json'

# Paths used for publication/cleanup must not traverse redirected directories.
function Assert-ReaderDirectPath([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            if (((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Redirected helper build path: $current" }
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}

# Enumerate without following reparse points, with a bound shared by source and package verification.
function Get-ReaderFiles([string]$Directory) {
    Assert-ReaderDirectPath $Directory
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push([IO.Path]::GetFullPath($Directory))
    $count = 0
    while ($pending.Count -gt 0) {
        foreach ($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            if (++$count -gt 16384) { throw 'The helper file tree exceeds the packaging limit.' }
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Redirected helper file: $($entry.FullName)" }
            if ($entry.PSIsContainer) { $pending.Push($entry.FullName) } else { $entry }
        }
    }
}

# Delete only a verified child of the named build directory, never its parent or an arbitrary output path.
function Remove-ReaderBuildDirectory([string]$Path, [string]$Parent) {
    $absolute = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $boundary = [IO.Path]::GetFullPath($Parent).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (!$absolute.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe helper build cleanup path.' }
    if (Test-Path -LiteralPath $absolute) {
        @(Get-ReaderFiles $absolute) | Out-Null
        Remove-Item -LiteralPath $absolute -Recurse -Force
    }
}

# Source bytes and dependency pins bind a package to this checkout, including the publisher/validator itself.
function Get-ReaderSourceHash {
    $lines = @(Get-ReaderFiles $PSScriptRoot | Where-Object { $_.Extension -in '.cs', '.csproj', '.props', '.ps1', '.txt' } | ForEach-Object {
        $_.FullName.Substring($PSScriptRoot.Length).Replace('\', '/') + '|' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    })
    [Array]::Sort($lines, [StringComparer]::Ordinal)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines -join "`n")))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

# Require the full self-contained runtime, native reader, referenced libraries and notices, never merely an EXE.
function Assert-ReaderPayload([string]$Directory) {
    $required = @('LarianPackageInspector.exe', 'LarianPackageInspector.dll', 'LarianPackageInspector.deps.json', 'LarianPackageInspector.runtimeconfig.json',
        'LSLibNative.dll', 'ThirdPartyNotices.txt', 'hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll', 'clrjit.dll', 'System.Private.CoreLib.dll') + @($readerAssemblies | ForEach-Object { $_ + '.dll' })
    foreach ($name in $required) {
        $path = Join-Path $Directory $name
        if (!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) { throw "Incomplete helper package: $name is missing or empty. Rebuild NMM Release." }
    }
    $runtime = Get-Content -LiteralPath (Join-Path $Directory 'LarianPackageInspector.runtimeconfig.json') -Raw | ConvertFrom-Json
    $frameworks = @($runtime.runtimeOptions.includedFrameworks)
    if ($runtime.runtimeOptions.tfm -ne 'net8.0' -or $frameworks.Count -ne 1 -or $frameworks[0].name -ne 'Microsoft.NETCore.App' -or $frameworks[0].version -ne $readerRuntime) {
        throw 'The helper must contain the exact pinned self-contained .NET runtime.'
    }
    $deps = Get-Content -LiteralPath (Join-Path $Directory 'LarianPackageInspector.deps.json') -Raw | ConvertFrom-Json
    if ($deps.runtimeTarget.name -ne '.NETCoreApp,Version=v8.0/win-x64') { throw 'The helper package is not the supported win-x64 deployment.' }
}

# Read-only release gate. No SDK, network access, helper execution, compilation or tests are used here.
function Assert-ReaderPackage([string]$Directory) {
    $files = @(Get-ReaderFiles $Directory)
    $manifestPath = Join-Path $Directory $readerManifestName
    if (!(Test-Path -LiteralPath $manifestPath -PathType Leaf) -or (Get-Item -LiteralPath $manifestPath).Length -gt 2MB) { throw 'A validated helper package manifest is required. Rebuild NMM Release.' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.configuration -ne $Configuration -or $manifest.runtimeIdentifier -ne 'win-x64' -or
        $manifest.lslibVersion -ne $readerVersion -or $manifest.lslibArchiveSha256 -ne $readerExpectedHash -or $manifest.runtimeVersion -ne $readerRuntime -or
        $manifest.sourceSha256 -ne (Get-ReaderSourceHash)) { throw 'The helper package is stale or uses different dependency pins. Rebuild NMM Release.' }
    $entries = @($manifest.files)
    if ($entries.Count -lt 1 -or $entries.Count -gt 1024 -or $files.Count -ne $entries.Count + 1) { throw 'The helper package file set differs from its release manifest.' }
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $entries) {
        if ([string]::IsNullOrWhiteSpace($entry.path) -or $entry.path -match '[\\:]|(^|/)\.{1,2}(/|$)|^/' -or $entry.sha256 -notmatch '^[a-f0-9]{64}$' -or !$seen.Add($entry.path)) {
            throw 'Invalid or duplicate helper package manifest entry.'
        }
        $path = Join-Path $Directory $entry.path
        if (!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -ne $entry.length -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) { throw "The helper package is incomplete or changed: $($entry.path)" }
    }
    Assert-ReaderPayload $Directory
}

if ($VerifyOnly) {
    Assert-ReaderPackage $readerOutput
    Write-Output "Verified helper package: $readerOutput"
    return
}

$readerSourceIdentity = Get-ReaderSourceHash
$readerDotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $readerDotnet) { throw 'Release packaging requires a .NET 8-or-newer SDK on PATH to publish the package inspector.' }
$readerDependency = Join-Path $readerRoot "Stage/Dependencies/LSLib-$readerVersion"
$readerArchive = Join-Path $readerDependency ([string]$readerPins.Project.PropertyGroup.LarianDependencyArchiveName)
$readerParent = [IO.Path]::GetDirectoryName($readerOutput)
$readerWorkParent = Join-Path $readerRoot 'Stage/obj/LarianPackageInspector/Packaging'
Assert-ReaderDirectPath $readerDependency
Assert-ReaderDirectPath $readerOutput
Assert-ReaderDirectPath $readerWorkParent
if ((Test-Path -LiteralPath $readerOutput) -and !(Test-Path -LiteralPath $readerOutput -PathType Container)) { throw 'The helper output path is occupied by a file.' }
$readerLockDigest = [Security.Cryptography.SHA256]::Create()
try { $readerLockHash = [BitConverter]::ToString($readerLockDigest.ComputeHash([Text.Encoding]::UTF8.GetBytes($readerRoot.ToLowerInvariant()))).Replace('-', '') }
finally { $readerLockDigest.Dispose() }
$readerMutex = New-Object Threading.Mutex($false, ('Local\NMM.LoadOrder.Publish.' + $readerLockHash))
$readerLocked = $false
$readerWork = Join-Path $readerWorkParent ([Guid]::NewGuid().ToString('N'))
$readerPayload = Join-Path $readerParent ('.LoadOrder.publish-' + [Guid]::NewGuid().ToString('N'))
$readerPrevious = Join-Path $readerParent ('.LoadOrder.previous-' + [Guid]::NewGuid().ToString('N'))
$readerDownload = $readerArchive + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
try {
    try { $readerLocked = $readerMutex.WaitOne(60000) } catch [Threading.AbandonedMutexException] { $readerLocked = $true }
    if (!$readerLocked) { throw 'Another helper publication is still running. Retry the Release build after it finishes.' }
    # Failed builds must not leave an older package marked as a completed publication.
    $oldManifest = Join-Path $readerOutput $readerManifestName
    if (Test-Path -LiteralPath $oldManifest) { Remove-Item -LiteralPath $oldManifest -Force }
    New-Item -ItemType Directory -Path $readerDependency, $readerWork, $readerParent -Force | Out-Null
    if (!(Test-Path -LiteralPath $readerArchive)) {
        $protocol = [Net.ServicePointManager]::SecurityProtocol
        try {
            [Net.ServicePointManager]::SecurityProtocol = $protocol -bor [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest ([string]$readerPins.Project.PropertyGroup.LarianDependencyArchiveUrl) -UseBasicParsing -OutFile $readerDownload -TimeoutSec 300
        } finally { [Net.ServicePointManager]::SecurityProtocol = $protocol }
        if ((Get-FileHash -LiteralPath $readerDownload -Algorithm SHA256).Hash -ne $readerExpectedHash) { throw 'Downloaded LSLib archive checksum mismatch.' }
        Move-Item -LiteralPath $readerDownload -Destination $readerArchive
    }
    if ((Get-FileHash -LiteralPath $readerArchive -Algorithm SHA256).Hash -ne $readerExpectedHash) { throw 'Cached LSLib archive checksum mismatch. Remove the corrupt cache file and rebuild.' }
    # Fresh extraction prevents edited/stale cached DLLs from entering a release.
    $readerExtract = Join-Path $readerWork 'dependencies'
    Expand-Archive -LiteralPath $readerArchive -DestinationPath $readerExtract
    $readerPacked = Join-Path $readerExtract 'Packed'
    # Preserve the default reference paths used by direct helper and maintainer fixture builds.
    $readerCachedPacked = Join-Path $readerDependency 'dist/Packed'
    Assert-ReaderDirectPath $readerCachedPacked
    New-Item -ItemType Directory -Path $readerCachedPacked -Force | Out-Null
    foreach ($name in @($readerAssemblies | ForEach-Object { $_ + '.dll' }) + @('LSLibNative.dll')) {
        Assert-ReaderDirectPath (Join-Path $readerCachedPacked $name)
        Copy-Item -LiteralPath (Join-Path $readerPacked $name) -Destination (Join-Path $readerCachedPacked $name) -Force
    }
    if ((Get-ReaderSourceHash) -ne $readerSourceIdentity) { throw 'Helper sources changed during preparation. Retry the Release build.' }
    & $readerDotnet.Source publish (Join-Path $PSScriptRoot 'LarianPackageInspector.csproj') --configuration $Configuration --runtime win-x64 --self-contained true --output $readerPayload "-p:LSLibDirectory=$readerPacked" --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Package helper publication failed. NMM release packaging cannot continue.' }
    if ((Get-ReaderSourceHash) -ne $readerSourceIdentity) { throw 'Helper sources changed during publication. Retry the Release build.' }
    Assert-ReaderPayload $readerPayload
    foreach ($name in @($readerAssemblies | ForEach-Object { $_ + '.dll' }) + @('LSLibNative.dll')) {
        if ((Get-FileHash -LiteralPath (Join-Path $readerPayload $name) -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath (Join-Path $readerPacked $name) -Algorithm SHA256).Hash) {
            throw "Published dependency differs from the checksum-pinned LSLib archive: $name"
        }
    }
    $manifest = [ordered]@{
        schemaVersion = 1; configuration = $Configuration; runtimeIdentifier = 'win-x64'; runtimeVersion = $readerRuntime
        lslibVersion = $readerVersion; lslibArchiveSha256 = $readerExpectedHash; sourceSha256 = $readerSourceIdentity
        files = @(Get-ReaderFiles $readerPayload | Sort-Object FullName | ForEach-Object {
            [ordered]@{ path = $_.FullName.Substring($readerPayload.Length + 1).Replace('\', '/'); length = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
        })
    }
    [IO.File]::WriteAllText((Join-Path $readerPayload $readerManifestName), ($manifest | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))
    Assert-ReaderPackage $readerPayload
    if (Test-Path -LiteralPath $readerOutput) { Move-Item -LiteralPath $readerOutput -Destination $readerPrevious }
    try { Move-Item -LiteralPath $readerPayload -Destination $readerOutput }
    catch {
        if (!(Test-Path -LiteralPath $readerOutput) -and (Test-Path -LiteralPath $readerPrevious)) { Move-Item -LiteralPath $readerPrevious -Destination $readerOutput }
        throw
    }
    Remove-ReaderBuildDirectory $readerPrevious $readerParent
    Write-Output "Package helper published and verified: $readerOutput"
} finally {
    if ($readerLocked) {
        try {
            if (Test-Path -LiteralPath $readerDownload) { Remove-Item -LiteralPath $readerDownload -Force }
            try { Remove-ReaderBuildDirectory $readerWork $readerWorkParent } catch { Write-Warning "Helper build staging retained: $readerWork. $($_.Exception.Message)" }
            try { Remove-ReaderBuildDirectory $readerPayload $readerParent } catch { Write-Warning "Unpublished helper staging retained: $readerPayload. $($_.Exception.Message)" }
        } finally { $readerMutex.ReleaseMutex() }
    }
    $readerMutex.Dispose()
}
