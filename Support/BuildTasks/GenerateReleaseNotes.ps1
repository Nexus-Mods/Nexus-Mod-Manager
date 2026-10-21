<#
.SYNOPSIS
Creates the installer's formatted release notes from the editable Markdown source.
.DESCRIPTION
Supports headings, bullet lists (including nested bullets), paragraphs and **bold** text.
Colors New Feature: labels blue and Bugfix: labels red; other text uses automatic color.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SourcePath,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'

function ConvertTo-RtfText {
    <# .SYNOPSIS Escapes text and encodes Unicode as signed UTF-16 RTF characters. #>
    param([string]$Text)
    $escaped = New-Object Text.StringBuilder
    foreach ($character in $Text.ToCharArray()) {
        $code = [int]$character
        if ($character -eq '\' -or $character -eq '{' -or $character -eq '}') {
            [void]$escaped.Append('\').Append($character)
        }
        elseif ($code -gt 127) {
            if ($code -gt 32767) { $code -= 65536 }
            [void]$escaped.Append('\u').Append($code).Append('?')
        }
        elseif ($character -eq "`t") { [void]$escaped.Append('\tab ') }
        else { [void]$escaped.Append($character) }
    }
    return $escaped.ToString()
}

function ConvertTo-RtfInline {
    <# .SYNOPSIS Formats bold spans and change labels while escaping their contents as plain text. #>
    param([string]$Text)
    $parts = [regex]::Split($Text, '(\*\*[^*]+\*\*)')
    $formatted = (($parts | ForEach-Object {
        if ($_ -match '^\*\*(.+)\*\*$') { '{\b ' + (ConvertTo-RtfText $Matches[1]) + '}' }
        else { ConvertTo-RtfText $_ }
    }) -join '')
    if ($formatted -match '^(\{\\b )?(New Feature:|Bugfix:)(.*)$') {
        $color = if ($Matches[2] -ieq 'New Feature:') { 1 } else { 2 }
        return $Matches[1] + '{\cf' + $color + ' ' + $Matches[2] + '}' + $Matches[3]
    }
    return $formatted
}

$rtf = New-Object Text.StringBuilder
[void]$rtf.AppendLine('{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fnil Segoe UI;}}{\colortbl ;\red97\green97\blue255;\red235\green0\blue0;}\uc1\f0\fs20\cf0')
foreach ($line in [IO.File]::ReadAllLines($SourcePath, [Text.Encoding]::UTF8)) {
    if ([String]::IsNullOrWhiteSpace($line)) { continue }
    if ($line -match '^(#{1,6})\s+(.+)$') {
        $size = if ($Matches[1].Length -eq 1) { 30 } else { 24 }
        $text = ConvertTo-RtfInline $Matches[2]
        [void]$rtf.AppendLine(('\pard\keepn\sb180\sa100\b\fs{0} {1}\b0\fs20\par' -f $size, $text))
    }
    elseif ($line -match '^(\s*)-\s+(.+)$') {
        $indent = 240 + [int]($Matches[1].Length / 2) * 240
        $text = ConvertTo-RtfInline $Matches[2]
        [void]$rtf.AppendLine(('\pard\li{0}\fi-180\tx{0}\sa60\bullet\tab {1}\par' -f $indent, $text))
    }
    else {
        [void]$rtf.AppendLine(('\pard\sa100 {0}\par' -f (ConvertTo-RtfInline $line)))
    }
}
[void]$rtf.AppendLine('}')
$fullOutputPath = [IO.Path]::GetFullPath($OutputPath)
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutputPath))
[IO.File]::WriteAllText($fullOutputPath, $rtf.ToString(), [Text.Encoding]::ASCII)
