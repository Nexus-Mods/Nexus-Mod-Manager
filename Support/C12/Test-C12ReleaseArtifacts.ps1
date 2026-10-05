[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string]$ResultsDirectory,
    [string]$InstallerPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

function Get-RepositoryRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $pathFull = [IO.Path]::GetFullPath($Path)
    if (-not $pathFull.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside repository root: $pathFull"
    }

    return $pathFull.Substring($rootFull.Length)
}

function Test-Excluded {
    param(
        [Parameter(Mandatory = $true)][IO.FileInfo]$File,
        [Parameter(Mandatory = $true)][string]$SourceRoot,
        [Parameter(Mandatory = $true)][string[]]$Patterns
    )

    if ($Patterns.Count -eq 0) {
        return $false
    }

    $relative = $File.FullName.Substring($SourceRoot.TrimEnd('\').Length).TrimStart('\')
    foreach ($pattern in $Patterns) {
        if ($File.Name -like $pattern -or $relative -like $pattern) {
            return $true
        }
    }

    return $false
}

function Resolve-SetupPackageFiles {
    param(
        [Parameter(Mandatory = $true)][string]$SetupScript
    )

    $setupDirectory = Split-Path -Parent $SetupScript
    $lines = Get-Content -LiteralPath $SetupScript
    $inFiles = $false
    $resolved = @{}
    $ruleCount = 0

    foreach ($line in $lines) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '[Files]') {
            $inFiles = $true
            continue
        }

        if ($inFiles -and $trimmed.StartsWith('[')) {
            break
        }

        if (-not $inFiles -or $trimmed.Length -eq 0 -or $trimmed.StartsWith(';')) {
            continue
        }

        $match = [Regex]::Match($line, '^\s*Source:\s*"(?<source>[^"]+)"(?<rest>.*)$', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if (-not $match.Success) {
            continue
        }

        $ruleCount++
        $source = $match.Groups['source'].Value
        $rest = $match.Groups['rest'].Value
        $recurse = [Regex]::IsMatch($rest, '\brecursesubdirs\b', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        $excludePatterns = @()
        $excludeMatch = [Regex]::Match($rest, ';\s*Excludes:\s*"(?<excludes>[^"]*)"', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($excludeMatch.Success -and -not [String]::IsNullOrWhiteSpace($excludeMatch.Groups['excludes'].Value)) {
            $excludePatterns = @($excludeMatch.Groups['excludes'].Value.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 })
        }

        $sourcePath = $source.Replace('\', [IO.Path]::DirectorySeparatorChar)
        $fullPattern = [IO.Path]::GetFullPath((Join-Path $setupDirectory $sourcePath))
        $sourceRoot = Split-Path -Parent $fullPattern
        $leafPattern = Split-Path -Leaf $fullPattern

        if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) {
            throw "Installer source directory does not exist for rule '$source': $sourceRoot"
        }

        $matches = @(Get-ChildItem -LiteralPath $sourceRoot -File -Recurse:$recurse | Where-Object {
            $_.Name -like $leafPattern -and -not (Test-Excluded -File $_ -SourceRoot $sourceRoot -Patterns $excludePatterns)
        })

        if ($matches.Count -eq 0) {
            throw "Installer source rule matched no files after exclusions: $source"
        }

        foreach ($file in $matches) {
            $resolved[$file.FullName.ToLowerInvariant()] = $file
        }
    }

    if ($ruleCount -eq 0) {
        throw "No [Files] Source rules were found in $SetupScript"
    }

    return @($resolved.Values | Sort-Object FullName)
}

$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null

$setupScript = Join-Path $RepositoryRoot 'Setup\setup.iss'
if (-not (Test-Path -LiteralPath $setupScript -PathType Leaf)) {
    throw "Installer definition not found: $setupScript"
}

$releaseRoot = Join-Path $RepositoryRoot 'Stage\Release'
if (-not (Test-Path -LiteralPath $releaseRoot -PathType Container)) {
    throw "Release output directory not found: $releaseRoot"
}

$requiredReleaseFiles = @(
    'NexusClient.exe',
    'data\License.rtf',
    'data\NewVersionDisclaimer.rtf',
    'data\releasenotes.rtf'
)
foreach ($relative in $requiredReleaseFiles) {
    $requiredPath = Join-Path $releaseRoot $relative
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required release artifact missing: $requiredPath"
    }
}

$packageFiles = @(Resolve-SetupPackageFiles -SetupScript $setupScript)
if ($packageFiles.Count -eq 0) {
    throw 'Installer package inventory is empty.'
}

$nexusClientExe = [IO.Path]::GetFullPath((Join-Path $releaseRoot 'NexusClient.exe'))
if (-not ($packageFiles | Where-Object { $_.FullName -eq $nexusClientExe })) {
    throw 'NexusClient.exe exists in Stage\Release but is not selected by Setup\setup.iss.'
}

$unexpectedPdb = @($packageFiles | Where-Object { $_.Extension -ieq '.pdb' })
if ($unexpectedPdb.Count -gt 0) {
    throw ("Installer package inventory unexpectedly includes PDB files: {0}" -f (($unexpectedPdb | Select-Object -First 5 -ExpandProperty FullName) -join ', '))
}

$entries = New-Object System.Collections.Generic.List[object]
$totalBytes = [Int64]0
foreach ($file in $packageFiles) {
    $hash = Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256
    $totalBytes += $file.Length
    $entries.Add([ordered]@{
        path = Get-RepositoryRelativePath -Root $RepositoryRoot -Path $file.FullName
        bytes = [Int64]$file.Length
        sha256 = $hash.Hash.ToLowerInvariant()
    })
}

$installerEntry = $null
if (-not [String]::IsNullOrWhiteSpace($InstallerPath)) {
    $installerFull = [IO.Path]::GetFullPath($InstallerPath)
    if (-not (Test-Path -LiteralPath $installerFull -PathType Leaf)) {
        throw "Installer path was supplied but does not exist: $installerFull"
    }

    $installerFile = Get-Item -LiteralPath $installerFull
    $installerHash = Get-FileHash -LiteralPath $installerFull -Algorithm SHA256
    $installerEntry = [ordered]@{
        path = Get-RepositoryRelativePath -Root $RepositoryRoot -Path $installerFull
        bytes = [Int64]$installerFile.Length
        sha256 = $installerHash.Hash.ToLowerInvariant()
    }
}

$manifest = [ordered]@{
    schema = 'nmm-c12-release-artifacts-v1'
    generatedUtc = [DateTime]::UtcNow.ToString('o')
    setupScript = 'Setup\setup.iss'
    packageFileCount = $entries.Count
    packageBytes = $totalBytes
    packageFiles = $entries
    installer = $installerEntry
}

$manifestPath = Join-Path $ResultsDirectory 'C12-ReleaseArtifacts.json'
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host "Release package files: $($entries.Count)"
Write-Host "Release package bytes: $totalBytes"
Write-Host "Release artifact manifest: $manifestPath"
if ($installerEntry -ne $null) {
    Write-Host "Installer SHA-256: $($installerEntry.sha256)"
}
