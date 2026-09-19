<#
.SYNOPSIS
  Writes the release notes for a version: the commit subjects since the previous release tag, a link to the
  full comparison, and short install instructions.

.DESCRIPTION
  GitHub's own "generate release notes" lists merged pull requests, and this project commits straight to
  master, so it would come out empty. The commit subjects (one per milestone, "M19: ...") make better notes.

.PARAMETER Tag
  The release tag, for example v0.2.0. It does not have to exist yet: the notes then cover everything from
  the previous tag up to HEAD.

.PARAMETER OutFile
  Where to write the markdown (UTF-8, no BOM).

.PARAMETER Repo
  owner/name on GitHub, used for the comparison link.

.NOTES
  Keep this file ASCII-only: Windows PowerShell 5.1 misreads UTF-8 without a BOM. Commit subjects are
  Japanese, so git's output is decoded as UTF-8 explicitly.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][string]$OutFile,
    [string]$Repo = "syanmi/TileTerm"
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Invoke-Git {
    # git reports "no such tag" and the like on stderr with a non-zero exit code; that means "nothing found"
    # here. Windows PowerShell 5.1 turns redirected stderr into a terminating error under 'Stop', so relax it.
    $ErrorActionPreference = 'Continue'
    $output = & git -C $root -c core.quotepath=off @args 2>$null
    if ($LASTEXITCODE -ne 0) { return @() }
    return @($output)
}

# The range end: the tag itself when it exists, otherwise HEAD (a release that is about to be tagged).
$tagExists = (Invoke-Git rev-parse -q --verify "refs/tags/$Tag").Count -gt 0
$end = if ($tagExists) { $Tag } else { 'HEAD' }

# The previous release: the nearest tag before the end of the range (skipping the tag being released).
$previous = ""
$candidate = @(Invoke-Git describe --tags --abbrev=0 "$end^") | Select-Object -First 1
if ($candidate) { $previous = "$candidate" }
elseif (-not $tagExists -and $end -eq 'HEAD') {
    $candidate = @(Invoke-Git describe --tags --abbrev=0 HEAD) | Select-Object -First 1
    if ($candidate) { $previous = "$candidate" }
}

$range = if ($previous) { "$previous..$end" } else { $end }
$subjects = @(Invoke-Git log $range --no-merges --pretty=format:%s) |
    Where-Object { $_ -and $_ -notmatch '^Release v\d' }

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("## What's changed")
$lines.Add("")
if ($subjects.Count -gt 0) {
    foreach ($s in $subjects) { $lines.Add("- $s") }
}
else {
    $lines.Add("- Maintenance release.")
}
$lines.Add("")
if ($previous) {
    $lines.Add("**Full changelog**: https://github.com/$Repo/compare/$previous...$Tag")
    $lines.Add("")
}
$lines.Add("## Install")
$lines.Add("")
$lines.Add("- **Installer**: ``TileTerm-win-Setup.exe`` - one click, installs for the current user (no administrator rights).")
$lines.Add("- **Portable**: ``TileTerm-win-Portable.zip`` - unzip anywhere and run ``TileTerm.exe``; settings stay in the ``data`` folder beside it.")
$lines.Add("")
$lines.Add("Requires 64-bit Windows 10 version 1809 or later. The files are not code-signed yet, so Windows SmartScreen may warn on first run (*More info* -> *Run anyway*). Compare downloads with ``SHA256SUMS.txt``.")

$text = ($lines -join "`n") + "`n"
[System.IO.File]::WriteAllText($OutFile, $text, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Release notes for $Tag ($($subjects.Count) change(s) since '$previous') -> $OutFile"
