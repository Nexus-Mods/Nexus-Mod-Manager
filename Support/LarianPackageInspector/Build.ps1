param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$readerRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$readerDependency = Join-Path $readerRoot 'Stage/Dependencies/LSLib-1.20.4'
$readerArchive = Join-Path $readerDependency 'ExportTool-v1.20.4.zip'
$readerExpectedHash = '5e02368fb8acafda9b45acba37a3f3bf507fc3d65a083a159abbeab06337190e'
New-Item -ItemType Directory -Path $readerDependency -Force | Out-Null
if (!(Test-Path -LiteralPath $readerArchive)) {
    Invoke-WebRequest 'https://github.com/Norbyte/lslib/releases/download/v1.20.4/ExportTool-v1.20.4.zip' -OutFile $readerArchive
}
if ((Get-FileHash -LiteralPath $readerArchive -Algorithm SHA256).Hash -ne $readerExpectedHash) {
    throw 'LSLib archive checksum mismatch. The cached archive was not used.'
}
Expand-Archive -LiteralPath $readerArchive -DestinationPath (Join-Path $readerDependency 'dist') -Force
$readerOutput = Join-Path $readerRoot "Stage/$Configuration/Tools/LoadOrder"
& dotnet publish (Join-Path $PSScriptRoot 'LarianPackageInspector.csproj') --configuration $Configuration --runtime win-x64 --self-contained true --output $readerOutput --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'Package helper publication failed.' }
Write-Output "Package helper published to $readerOutput"
