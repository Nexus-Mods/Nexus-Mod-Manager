[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ResultsDirectory,
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",
    [switch]$RunPerformance,
    [switch]$RunFullSuite,
    [switch]$BuildInstaller,
    [string]$ManualEvidencePath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

function Get-XmlAttributeInt {
    param(
        [Parameter(Mandatory = $true)][System.Xml.XmlNode]$Node,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $attribute = $Node.Attributes[$Name]
    if ($attribute -eq $null -or [String]::IsNullOrWhiteSpace($attribute.Value)) {
        return 0
    }

    return [Int32]$attribute.Value
}

function Read-TrxGate {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$FileName,
        [Parameter(Mandatory = $true)][string]$Directory
    )

    $path = Join-Path $Directory $FileName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        return [ordered]@{
            name = $Name
            file = $FileName
            present = $false
            passed = $false
            total = 0
            executed = 0
            succeeded = 0
            failed = 0
            skipped = 0
            outcome = 'Missing'
        }
    }

    [xml]$trx = Get-Content -LiteralPath $path -Raw
    $counters = $trx.SelectSingleNode("//*[local-name()='Counters']")
    $summary = $trx.SelectSingleNode("//*[local-name()='ResultSummary']")
    if ($counters -eq $null -or $summary -eq $null) {
        throw "TRX file does not contain ResultSummary/Counters: $path"
    }

    $total = Get-XmlAttributeInt -Node $counters -Name 'total'
    $executed = Get-XmlAttributeInt -Node $counters -Name 'executed'
    $succeeded = Get-XmlAttributeInt -Node $counters -Name 'passed'
    $failed = Get-XmlAttributeInt -Node $counters -Name 'failed'
    $skipped = (Get-XmlAttributeInt -Node $counters -Name 'notExecuted') +
        (Get-XmlAttributeInt -Node $counters -Name 'inconclusive') +
        (Get-XmlAttributeInt -Node $counters -Name 'notRunnable') +
        (Get-XmlAttributeInt -Node $counters -Name 'aborted') +
        (Get-XmlAttributeInt -Node $counters -Name 'timeout') +
        (Get-XmlAttributeInt -Node $counters -Name 'disconnected')

    $outcomeAttribute = $summary.Attributes['outcome']
    $outcome = if ($outcomeAttribute -eq $null) { 'Unknown' } else { $outcomeAttribute.Value }
    $passedGate = $total -gt 0 -and $executed -gt 0 -and $failed -eq 0 -and $skipped -eq 0 -and $outcome -ne 'Failed'

    return [ordered]@{
        name = $Name
        file = $FileName
        present = $true
        passed = $passedGate
        total = $total
        executed = $executed
        succeeded = $succeeded
        failed = $failed
        skipped = $skipped
        outcome = $outcome
    }
}

function Read-ManualEvidence {
    param([string]$Path)

    $requiredIds = @(
        'native-post-collection-management',
        'additive-populated-setup',
        'replace-backup-declined',
        'replace-capture-failure-blocks',
        'update-preserves-customization',
        'verify-repair',
        'local-restore',
        'restart-recovery',
        'cross-game-direct',
        'cross-game-virtual',
        'specialized-adapter'
    )

    if ([String]::IsNullOrWhiteSpace($Path)) {
        return [ordered]@{
            present = $false
            complete = $false
            path = $null
            required = $requiredIds
            missing = $requiredIds
            failed = @()
        }
    }

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Manual regression evidence file not found: $fullPath"
    }

    $evidence = Get-Content -LiteralPath $fullPath -Raw | ConvertFrom-Json
    if ($evidence.schema -ne 'nmm-c12-manual-regression-v1') {
        throw "Unsupported manual regression evidence schema in $fullPath"
    }

    $checksById = @{}
    foreach ($check in @($evidence.checks)) {
        if ($check -ne $null -and -not [String]::IsNullOrWhiteSpace([string]$check.id)) {
            $checksById[[string]$check.id] = $check
        }
    }

    $missing = New-Object System.Collections.Generic.List[string]
    $failed = New-Object System.Collections.Generic.List[string]
    foreach ($id in $requiredIds) {
        if (-not $checksById.ContainsKey($id)) {
            $missing.Add($id)
            continue
        }

        $check = $checksById[$id]
        if ([string]$check.outcome -ne 'Passed') {
            $failed.Add($id)
        }
    }

    return [ordered]@{
        present = $true
        complete = $missing.Count -eq 0 -and $failed.Count -eq 0
        path = $fullPath
        required = $requiredIds
        missing = @($missing)
        failed = @($failed)
    }
}

$ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
if (-not (Test-Path -LiteralPath $ResultsDirectory -PathType Container)) {
    throw "Results directory not found: $ResultsDirectory"
}

$compatibility = Read-TrxGate -Name 'Compatibility' -FileName 'C12-Compatibility.trx' -Directory $ResultsDirectory
$workflow = Read-TrxGate -Name 'Workflow' -FileName 'C12-Workflow.trx' -Directory $ResultsDirectory
$failureInjection = Read-TrxGate -Name 'FailureInjection' -FileName 'C12-FailureInjection.trx' -Directory $ResultsDirectory
$performance = Read-TrxGate -Name 'Performance' -FileName 'C12-Performance.trx' -Directory $ResultsDirectory
$fullSuite = Read-TrxGate -Name 'FullSuite' -FileName 'C12-FullSuite.trx' -Directory $ResultsDirectory

$artifactManifestPath = Join-Path $ResultsDirectory 'C12-ReleaseArtifacts.json'
$artifactManifest = $null
if (Test-Path -LiteralPath $artifactManifestPath -PathType Leaf) {
    $artifactManifest = Get-Content -LiteralPath $artifactManifestPath -Raw | ConvertFrom-Json
}

$manual = Read-ManualEvidence -Path $ManualEvidencePath
$focusedPassed = $compatibility.passed -and $workflow.passed -and $failureInjection.passed
$performanceComplete = $RunPerformance -and $performance.passed
$fullSuiteComplete = $RunFullSuite -and $fullSuite.passed
$artifactComplete = $Configuration -eq 'Release' -and $artifactManifest -ne $null -and [Int32]$artifactManifest.packageFileCount -gt 0
$installerComplete = $BuildInstaller -and $artifactManifest -ne $null -and $artifactManifest.installer -ne $null
$releaseCertified = $Configuration -eq 'Release' -and $focusedPassed -and $performanceComplete -and $fullSuiteComplete -and $artifactComplete -and $installerComplete -and $manual.complete

$summary = [ordered]@{
    schema = 'nmm-c12-validation-summary-v1'
    generatedUtc = [DateTime]::UtcNow.ToString('o')
    configuration = $Configuration
    resultsDirectory = $ResultsDirectory
    focusedPassed = $focusedPassed
    releaseCertified = $releaseCertified
    gates = @($compatibility, $workflow, $failureInjection, $performance, $fullSuite)
    requested = [ordered]@{
        performance = [bool]$RunPerformance
        fullSuite = [bool]$RunFullSuite
        installer = [bool]$BuildInstaller
    }
    releaseArtifacts = [ordered]@{
        present = $artifactManifest -ne $null
        packageFileCount = if ($artifactManifest -eq $null) { 0 } else { [Int32]$artifactManifest.packageFileCount }
        installerPresent = $artifactManifest -ne $null -and $artifactManifest.installer -ne $null
    }
    manualRegression = $manual
}

$jsonPath = Join-Path $ResultsDirectory 'C12-ValidationSummary.json'
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$markdown = New-Object System.Collections.Generic.List[string]
$markdown.Add('# C12 Validation Summary')
$markdown.Add('')
$markdown.Add(('Generated UTC: `{0}`' -f $summary.generatedUtc))
$markdown.Add(('Configuration: `{0}`' -f $Configuration))
$markdown.Add('')
$markdown.Add('| Gate | Present | Passed | Total | Failed | Skipped |')
$markdown.Add('|---|---:|---:|---:|---:|---:|')
foreach ($gate in $summary.gates) {
    $markdown.Add(('| {0} | {1} | {2} | {3} | {4} | {5} |' -f $gate.name, $gate.present, $gate.passed, $gate.total, $gate.failed, $gate.skipped))
}
$markdown.Add('')
$markdown.Add(('Focused Collections gates passed: **{0}**' -f $focusedPassed))
$markdown.Add(('Release artifacts present: **{0}**' -f $artifactComplete))
$markdown.Add(('Installer evidence present: **{0}**' -f $installerComplete))
$markdown.Add(('Manual regression complete: **{0}**' -f $manual.complete))
$markdown.Add('')
$markdown.Add(('## Release certified: **{0}**' -f $releaseCertified))
if (-not $releaseCertified) {
    $markdown.Add('')
    $markdown.Add('Release certification remains incomplete until the full suite, opt-in performance gate, installer artifact and required manual regression evidence are all present and passing in the same validation result set.')
}

$markdownPath = Join-Path $ResultsDirectory 'C12-ValidationSummary.md'
$markdown | Set-Content -LiteralPath $markdownPath -Encoding UTF8

Write-Host "C12 validation summary: $markdownPath"
Write-Host "Focused gates passed: $focusedPassed"
Write-Host "Release certified: $releaseCertified"
