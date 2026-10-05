[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Platform = "Any CPU",
    [switch]$SkipBuild,
    [switch]$RunFullSuite,
    [switch]$RunPerformance,
    [switch]$BuildInstaller,
    [string]$ResultsDirectory,
    [string]$ManualEvidencePath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

function Resolve-RepositoryRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
}

function Resolve-VisualStudioTool {
    param(
        [Parameter(Mandatory = $true)][string]$CommandName,
        [Parameter(Mandatory = $true)][string[]]$RelativeCandidates
    )

    $fromPath = Get-Command $CommandName -ErrorAction SilentlyContinue
    if ($fromPath -ne $null) {
        return $fromPath.Source
    }

    $programFilesX86 = ${env:ProgramFiles(x86)}
    if ([String]::IsNullOrWhiteSpace($programFilesX86)) {
        $programFilesX86 = $env:ProgramFiles
    }

    $vswhere = Join-Path $programFilesX86 "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $installationPath = (& $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath | Select-Object -First 1)
        if (-not [String]::IsNullOrWhiteSpace($installationPath)) {
            foreach ($relative in $RelativeCandidates) {
                $candidate = Join-Path $installationPath $relative
                if (Test-Path $candidate) {
                    return $candidate
                }
            }
        }
    }

    throw "Unable to locate $CommandName. Run from a Visual Studio Developer shell or install the Visual Studio build/test tools."
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    Write-Host ""
    Write-Host "=== $Label ===" -ForegroundColor Cyan
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed with exit code $LASTEXITCODE."
    }
}

function Invoke-TestGate {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$Category,
        [Parameter(Mandatory = $true)][string]$ResultFile,
        [Parameter(Mandatory = $true)][string]$VSTest,
        [Parameter(Mandatory = $true)][string]$Assembly
    )

    Invoke-Checked -Label $Label -FilePath $VSTest -Arguments @(
        $Assembly,
        "/TestCaseFilter:TestCategory=$Category",
        "/Logger:trx;LogFileName=$ResultFile",
        "/ResultsDirectory:$ResultsDirectory"
    )
}

$repositoryRoot = Resolve-RepositoryRoot
$solution = Join-Path $repositoryRoot "NexusClient.sln"
$testAssembly = Join-Path $repositoryRoot ("Stage\Tests\NexusClientTests\{0}\NexusClientTests.dll" -f $Configuration)

if ([String]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $ResultsDirectory = Join-Path $repositoryRoot ("Stage\C12\Validation-{0}" -f $stamp)
} elseif (-not [IO.Path]::IsPathRooted($ResultsDirectory)) {
    $ResultsDirectory = Join-Path $repositoryRoot $ResultsDirectory
}

New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null

$msbuild = Resolve-VisualStudioTool -CommandName "MSBuild.exe" -RelativeCandidates @(
    "MSBuild\Current\Bin\MSBuild.exe",
    "MSBuild\15.0\Bin\MSBuild.exe"
)
$vstest = Resolve-VisualStudioTool -CommandName "vstest.console.exe" -RelativeCandidates @(
    "Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe"
)

Write-Host "Repository: $repositoryRoot"
Write-Host "Configuration: $Configuration / $Platform"
Write-Host "Results: $ResultsDirectory"

if (-not $SkipBuild) {
    Invoke-Checked -Label "Rebuild solution" -FilePath $msbuild -Arguments @(
        $solution,
        "/t:Rebuild",
        "/m",
        "/p:Configuration=$Configuration",
        "/p:Platform=$Platform",
        "/verbosity:minimal"
    )
}

if (-not (Test-Path $testAssembly)) {
    throw "Test assembly not found: $testAssembly"
}

Invoke-TestGate -Label "C12 compatibility matrix" -Category "CollectionsC12Compatibility" -ResultFile "C12-Compatibility.trx" -VSTest $vstest -Assembly $testAssembly
Invoke-TestGate -Label "C12 end-to-end workflow gate" -Category "CollectionsC12Workflow" -ResultFile "C12-Workflow.trx" -VSTest $vstest -Assembly $testAssembly
Invoke-TestGate -Label "C12 failure-injection campaign" -Category "CollectionsC12FailureInjection" -ResultFile "C12-FailureInjection.trx" -VSTest $vstest -Assembly $testAssembly

if ($RunPerformance) {
    Invoke-TestGate -Label "C12 explicit performance characterization" -Category "CollectionsC12Performance" -ResultFile "C12-Performance.trx" -VSTest $vstest -Assembly $testAssembly
}

if ($RunFullSuite) {
    Invoke-Checked -Label "Full NexusClientTests suite" -FilePath $vstest -Arguments @(
        $testAssembly,
        "/Logger:trx;LogFileName=C12-FullSuite.trx",
        "/ResultsDirectory:$ResultsDirectory"
    )
}

$releaseExe = Join-Path $repositoryRoot ("Stage\{0}\NexusClient.exe" -f $Configuration)
if (-not (Test-Path $releaseExe)) {
    throw "Expected application output not found: $releaseExe"
}

$setupScript = Join-Path $repositoryRoot "Setup\setup.iss"
if (-not (Test-Path $setupScript)) {
    throw "Installer definition not found: $setupScript"
}

if ($BuildInstaller -and $Configuration -ne "Release") {
    throw "-BuildInstaller requires -Configuration Release because Setup\setup.iss packages Stage\Release."
}

$installerPath = $null
if ($BuildInstaller) {
    $installerProgramFiles = ${env:ProgramFiles(x86)}
    if ([String]::IsNullOrWhiteSpace($installerProgramFiles)) {
        $installerProgramFiles = $env:ProgramFiles
    }
    $isccCandidates = @(
        (Join-Path $installerProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path $installerProgramFiles "Inno Setup 5\ISCC.exe")
    )
    $iscc = $isccCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if ([String]::IsNullOrWhiteSpace($iscc)) {
        throw "-BuildInstaller was requested but ISCC.exe was not found in Inno Setup 5/6."
    }

    Invoke-Checked -Label "Build installer" -FilePath $iscc -Arguments @($setupScript)
    $installer = Get-ChildItem (Join-Path $repositoryRoot "Stage\Installer") -Filter "NMM-*.exe" -File | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($installer -eq $null) {
        throw "Installer build completed without producing Stage\Installer\NMM-*.exe."
    }
    $installerPath = $installer.FullName
    Write-Host "Installer: $installerPath"
}

if (-not [String]::IsNullOrWhiteSpace($ManualEvidencePath) -and -not [IO.Path]::IsPathRooted($ManualEvidencePath)) {
    $ManualEvidencePath = Join-Path $repositoryRoot $ManualEvidencePath
}

if ($Configuration -eq "Release") {
    $artifactValidator = Join-Path $repositoryRoot "Support\C12\Test-C12ReleaseArtifacts.ps1"
    if (-not (Test-Path $artifactValidator)) {
        throw "Release artifact validator not found: $artifactValidator"
    }

    $artifactArguments = @{
        RepositoryRoot = $repositoryRoot
        ResultsDirectory = $ResultsDirectory
    }
    if (-not [String]::IsNullOrWhiteSpace($installerPath)) {
        $artifactArguments["InstallerPath"] = $installerPath
    }

    Write-Host ""
    Write-Host "=== C12 release artifact validation ===" -ForegroundColor Cyan
    & $artifactValidator @artifactArguments
} else {
    Write-Host "NOTE: release artifact/package validation is skipped for non-Release configurations." -ForegroundColor Yellow
}

$summaryWriter = Join-Path $repositoryRoot "Support\C12\Write-C12ValidationSummary.ps1"
if (-not (Test-Path $summaryWriter)) {
    throw "C12 validation summary writer not found: $summaryWriter"
}

$summaryArguments = @{
    ResultsDirectory = $ResultsDirectory
    Configuration = $Configuration
}
if ($RunPerformance) { $summaryArguments["RunPerformance"] = $true }
if ($RunFullSuite) { $summaryArguments["RunFullSuite"] = $true }
if ($BuildInstaller) { $summaryArguments["BuildInstaller"] = $true }
if (-not [String]::IsNullOrWhiteSpace($ManualEvidencePath)) {
    $summaryArguments["ManualEvidencePath"] = $ManualEvidencePath
}

Write-Host ""
Write-Host "=== C12 validation evidence summary ===" -ForegroundColor Cyan
& $summaryWriter @summaryArguments

Write-Host ""
Write-Host "C12 focused release validation completed successfully." -ForegroundColor Green
Write-Host "Results: $ResultsDirectory"
if (-not $RunFullSuite) {
    Write-Host "NOTE: final release certification still requires a clean full test suite (-RunFullSuite)." -ForegroundColor Yellow
}
if (-not $RunPerformance) {
    Write-Host "NOTE: opt-in performance characterization was not run (-RunPerformance)." -ForegroundColor Yellow
}
if (-not $BuildInstaller) {
    Write-Host "NOTE: installer compilation was not run (-BuildInstaller)." -ForegroundColor Yellow
}
