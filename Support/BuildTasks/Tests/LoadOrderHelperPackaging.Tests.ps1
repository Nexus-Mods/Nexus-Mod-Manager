# Regression source only. Run explicitly at the maintainer's request; no SDK, builds, helper or game is used.
$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../LarianPackageInspector'))
$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$fixture = Join-Path $temporaryParent ('NmmHelperPackageFixture-' + [Guid]::NewGuid().ToString('N'))
$reader = Join-Path $fixture 'Support/LarianPackageInspector'
$package = Join-Path $fixture 'Stage/Release/Tools/LoadOrder'
$powershell = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
$utf8 = New-Object Text.UTF8Encoding($false)

# The synthetic files exercise packaging integrity, not executable/PAK parsing.
function Write-FixtureManifest {
    $lines = @(Get-ChildItem -LiteralPath $reader -File | Where-Object { $_.Extension -in '.cs', '.csproj', '.props', '.ps1', '.txt' } | ForEach-Object {
        '/' + $_.Name + '|' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    })
    [Array]::Sort($lines, [StringComparer]::Ordinal)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $sourceHash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines -join "`n")))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
    $manifest = [ordered]@{
        schemaVersion = 1; configuration = 'Release'; runtimeIdentifier = 'win-x64'; runtimeVersion = $runtimeVersion
        lslibVersion = [string]$pins.Project.PropertyGroup.LarianDependencyVersion
        lslibArchiveSha256 = [string]$pins.Project.PropertyGroup.LarianDependencyArchiveSha256
        sourceSha256 = $sourceHash
        files = @(Get-ChildItem -LiteralPath $package -File | Where-Object { $_.Name -ne 'package-manifest.json' } | ForEach-Object {
            [ordered]@{ path = $_.Name; length = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
        })
    }
    [IO.File]::WriteAllText((Join-Path $package 'package-manifest.json'), ($manifest | ConvertTo-Json -Depth 6), $utf8)
}

# Each gate runs the read-only mode in the same Windows PowerShell host used by MSBuild/Inno Setup.
function Assert-FixtureVerification([bool]$Expected, [string]$Case) {
    $outputLog = Join-Path $fixture ('output-' + [Guid]::NewGuid().ToString('N') + '.txt')
    $errorLog = Join-Path $fixture ('error-' + [Guid]::NewGuid().ToString('N') + '.txt')
    $arguments = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', ('"' + (Join-Path $reader 'Build.ps1') + '"'),
        '-Configuration', 'Release', '-VerifyOnly', '-OutputDirectory', ('"' + $package + '"'))
    $process = Start-Process -FilePath $powershell -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput $outputLog -RedirectStandardError $errorLog
    try {
        if (!$process.WaitForExit(30000)) { $process.Kill(); throw "Verification timed out: $Case" }
        if (($process.ExitCode -eq 0) -ne $Expected) { throw "Unexpected package verification result: $Case. $(Get-Content -LiteralPath $errorLog -Raw)" }
    } finally { $process.Dispose() }
}

try {
    New-Item -ItemType Directory -Path $reader, $package -Force | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $source -File) { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $reader $file.Name) }
    [xml]$pins = [IO.File]::ReadAllText((Join-Path $reader 'Dependencies.props'))
    $runtimeVersion = [string]$pins.Project.PropertyGroup.LarianRuntimeFrameworkVersion
    $libraries = ([string]$pins.Project.ItemGroup.LarianDependencyAssembly.Include).Split(';') | ForEach-Object { $_ + '.dll' }
    foreach ($name in @($libraries) + @('LarianPackageInspector.exe', 'LarianPackageInspector.dll', 'LSLibNative.dll', 'ThirdPartyNotices.txt',
        'hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll', 'clrjit.dll', 'System.Private.CoreLib.dll')) { [IO.File]::WriteAllText((Join-Path $package $name), 'fixture', $utf8) }
    $runtimeFile = Join-Path $package 'LarianPackageInspector.runtimeconfig.json'
    $runtimeJson = @{ runtimeOptions = @{ tfm = 'net8.0'; includedFrameworks = @(@{ name = 'Microsoft.NETCore.App'; version = $runtimeVersion }) } } | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText($runtimeFile, $runtimeJson, $utf8)
    [IO.File]::WriteAllText((Join-Path $package 'LarianPackageInspector.deps.json'), '{"runtimeTarget":{"name":".NETCoreApp,Version=v8.0/win-x64"}}', $utf8)
    Write-FixtureManifest
    Assert-FixtureVerification $true 'complete matching package'

    $native = Join-Path $package 'LSLibNative.dll'
    Remove-Item -LiteralPath $native
    Assert-FixtureVerification $false 'missing native dependency'
    [IO.File]::WriteAllText($native, 'fixture', $utf8)
    [IO.File]::WriteAllText($native, 'tampered', $utf8)
    Assert-FixtureVerification $false 'changed dependency bytes'
    [IO.File]::WriteAllText($native, 'fixture', $utf8)

    $extra = Join-Path $package 'obsolete.dll'
    [IO.File]::WriteAllText($extra, 'stale', $utf8)
    Assert-FixtureVerification $false 'extra stale runtime file'
    Remove-Item -LiteralPath $extra

    $program = Join-Path $reader 'Program.cs'
    $programBytes = [IO.File]::ReadAllBytes($program)
    [IO.File]::AppendAllText($program, "`n// source changed", $utf8)
    Assert-FixtureVerification $false 'helper sources changed after publication'
    [IO.File]::WriteAllBytes($program, $programBytes)

    [IO.File]::WriteAllText($runtimeFile, $runtimeJson.Replace($runtimeVersion, '8.0.0'), $utf8)
    Write-FixtureManifest
    Assert-FixtureVerification $false 'unpinned runtime despite matching file hashes'
    [IO.File]::WriteAllText($runtimeFile, '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"}}}', $utf8)
    Write-FixtureManifest
    Assert-FixtureVerification $false 'framework-dependent output instead of bundled runtime'
    [IO.File]::WriteAllText($runtimeFile, $runtimeJson, $utf8)
    Write-FixtureManifest
    Assert-FixtureVerification $true 'restored exact package'
    Remove-Item -LiteralPath (Join-Path $package 'package-manifest.json')
    Assert-FixtureVerification $false 'failed publication without completion manifest'
} finally {
    $absoluteFixture = [IO.Path]::GetFullPath($fixture)
    if (!$absoluteFixture.StartsWith($temporaryParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($absoluteFixture) -notlike 'NmmHelperPackageFixture-*') { throw 'Unsafe packaging fixture cleanup path.' }
    if (Test-Path -LiteralPath $absoluteFixture) { Remove-Item -LiteralPath $absoluteFixture -Recurse -Force }
}
