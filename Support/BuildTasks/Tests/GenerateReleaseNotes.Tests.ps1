<# .SYNOPSIS Verifies Markdown formatting, escaping and the complete release-history round trip. #>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$generator = Join-Path $PSScriptRoot '..\GenerateReleaseNotes.ps1'
$notes = Join-Path $PSScriptRoot '..\..\..\NexusClient\data\releasenotes.md'
$temporaryRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('NmmReleaseNotes_' + [Guid]::NewGuid().ToString('N'))))
[void][IO.Directory]::CreateDirectory($temporaryRoot)
$viewer = New-Object Windows.Forms.RichTextBox

function Assert-Condition {
    <# .SYNOPSIS Fails the script when a document invariant is not satisfied. #>
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

try {
    $sample = @'
# Release notes

## Version 1.2.3

- **Added:** Unicode “quotes”, café, 漢字 and 🙂; escaped {braces} and \path.
  - Nested detail.

- New Feature: A feature description mentioning Bugfix: in its body.
- Bugfix: A fix description.
- **New Feature:** A bold feature label.
- **Bugfix:** A bold fix label.

A paragraph.
'@
    $source = Join-Path $temporaryRoot 'sample.md'
    $output = Join-Path $temporaryRoot 'sample.rtf'
    [IO.File]::WriteAllText($source, $sample, [Text.Encoding]::UTF8)
    & $generator -SourcePath $source -OutputPath $output
    $viewer.LoadFile($output)
    Assert-Condition ($viewer.Text.Contains('Unicode “quotes”, café, 漢字 and 🙂; escaped {braces} and \path.')) 'Unicode or RTF escaping was lost.'
    Assert-Condition (-not $viewer.Text.Contains('**')) 'Bold Markdown markers leaked into the document.'
    $viewer.Select(0, 'Release notes'.Length)
    Assert-Condition ($viewer.SelectionFont.Bold -and $viewer.SelectionFont.Size -eq 15) 'The title is not formatted.'
    $viewer.Select($viewer.Text.IndexOf('Added:'), 'Added:'.Length)
    Assert-Condition $viewer.SelectionFont.Bold 'Bold spans are not formatted.'
    $viewer.Select($viewer.Text.IndexOf('Nested detail.'), 1)
    $nestedIndent = $viewer.SelectionIndent
    $viewer.Select($viewer.Text.IndexOf('Added:'), 1)
    Assert-Condition ($nestedIndent -gt $viewer.SelectionIndent) 'Nested bullets lost their indentation.'

    foreach ($label in @('New Feature:', 'Bugfix:')) {
        $expectedColor = if ($label -eq 'New Feature:') { [Drawing.Color]::FromArgb(97, 97, 255) } else { [Drawing.Color]::FromArgb(235, 0, 0) }
        $index = $viewer.Text.IndexOf($label + ' A ')
        $viewer.Select($index, $label.Length)
        Assert-Condition ($viewer.SelectionColor.ToArgb() -eq $expectedColor.ToArgb()) ('Incorrect label color: ' + $label)
        $viewer.Select($index + $label.Length, 1)
        Assert-Condition ($viewer.SelectionColor.ToArgb() -eq $viewer.ForeColor.ToArgb()) ('Label color leaked into the description: ' + $label)
        $boldIndex = $viewer.Text.LastIndexOf($label)
        $viewer.Select($boldIndex, $label.Length)
        Assert-Condition ($viewer.SelectionFont.Bold -and $viewer.SelectionColor.ToArgb() -eq $expectedColor.ToArgb()) ('Bold label lost its color or formatting: ' + $label)
    }
    $viewer.Select($viewer.Text.IndexOf('Bugfix: in its body.'), 'Bugfix:'.Length)
    Assert-Condition ($viewer.SelectionColor.ToArgb() -eq $viewer.ForeColor.ToArgb()) 'A label mentioned in body text was colored.'

    & $generator -SourcePath $notes -OutputPath $output
    $viewer.LoadFile($output)
    $expected = @([IO.File]::ReadAllLines($notes, [Text.Encoding]::UTF8) | Where-Object { $_.Trim().Length -gt 0 } | ForEach-Object {
        ($_ -replace '^#{1,6}\s+', '' -replace '^\s*-\s+', '' -replace '\*\*', '').Trim()
    })
    $actual = @($viewer.Lines | Where-Object { $_.Trim().Length -gt 0 } | ForEach-Object {
        ($_ -replace '^\s*•\s*', '').Trim()
    })
    Assert-Condition ($expected.Count -eq $actual.Count) 'Release-history paragraph count changed.'
    for ($index = 0; $index -lt $expected.Count; $index++) {
        Assert-Condition ($expected[$index] -ceq $actual[$index]) ('Release-history text changed at paragraph ' + $index)
    }
    Write-Output ('PASS: label colors, formatting, Unicode, escaping, nested bullets and all {0} release-history paragraphs.' -f $expected.Count)
}
finally {
    $viewer.Dispose()
    $temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $temporaryRoot.StartsWith($temporaryParent, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected test directory.' }
    [IO.Directory]::Delete($temporaryRoot, $true)
}
