<#
.SYNOPSIS
  Starts a release: sets the version, commits, tags and pushes. The tag push makes GitHub Actions
  (.github/workflows/release.yml) build the packages and publish the GitHub release.

.EXAMPLE
  .\scripts\release.ps1 -Version 0.2.0          # release 0.2.0
  .\scripts\release.ps1 -Version 0.2.0 -NoPush  # do everything locally, look at it, push by hand later

.PARAMETER Version
  The new version, "MAJOR.MINOR.PATCH" (a pre-release such as 1.0.0-beta.1 is allowed and is published as a
  pre-release). This is written to <Version> in src\TileTerm\TileTerm.csproj, the one place the version lives.

.PARAMETER NoPush
  Commit and tag locally but do not push.

.NOTES
  Keep this file ASCII-only: Windows PowerShell 5.1 misreads UTF-8 without a BOM.
  The checks below refuse to release from a dirty tree, from a branch other than master, from a master that
  is behind origin, or with a tag that already exists - the usual ways a release goes wrong.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [switch]$NoPush
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$csproj = Join-Path $root 'src\TileTerm\TileTerm.csproj'
$tag = "v$Version"

function Git {
    $ErrorActionPreference = 'Continue'
    $out = & git -C $root @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed: $out" }
    return $out
}

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$') {
    throw "Version '$Version' is not MAJOR.MINOR.PATCH (optionally with a -prerelease suffix)."
}

$branch = (Git rev-parse --abbrev-ref HEAD | Select-Object -First 1).Trim()
if ($branch -ne 'master') { throw "Release from master (you are on '$branch')." }

$dirty = @(Git status --porcelain --untracked-files=no)
if ($dirty.Count -gt 0) { throw "The working tree has uncommitted changes to tracked files; commit or stash them first:`n$($dirty -join "`n")" }

Git fetch origin --tags --quiet | Out-Null
$behind = [int](Git rev-list --count 'HEAD..origin/master' | Select-Object -First 1)
if ($behind -gt 0) { throw "master is $behind commit(s) behind origin/master; pull first." }

$localTag = @(Git tag --list $tag)
$remoteTag = @(Git ls-remote --tags origin "refs/tags/$tag")
if ($localTag.Count -gt 0 -or $remoteTag.Count -gt 0) { throw "Tag $tag already exists." }

# --- set the version (keeping the file's BOM and line endings as they are) ---------------------------------
$bytes = [System.IO.File]::ReadAllBytes($csproj)
$hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
$text = [System.IO.File]::ReadAllText($csproj)   # strips the BOM
$current = [regex]::Match($text, '<Version>([^<]+)</Version>')
if (-not $current.Success) { throw "No <Version> element in $csproj" }

if ($current.Groups[1].Value -ne $Version) {
    $text = $text.Substring(0, $current.Groups[1].Index) + $Version + $text.Substring($current.Groups[1].Index + $current.Groups[1].Length)
    [System.IO.File]::WriteAllText($csproj, $text, (New-Object System.Text.UTF8Encoding($hasBom)))
    Git add -- $csproj | Out-Null
    Git commit -m "Release $tag" | Out-Null
    Write-Host "Version $($current.Groups[1].Value) -> $Version committed."
}
else {
    Write-Host "The csproj already says $Version; tagging the current commit."
}

Git tag -a $tag -m $tag | Out-Null
Write-Host "Tagged $tag at $((Git rev-parse --short HEAD | Select-Object -First 1).Trim())."

if ($NoPush) {
    Write-Host "Not pushed (-NoPush). To publish:  git push --atomic origin master $tag"
}
else {
    Git push --atomic origin master $tag | Out-Null
    Write-Host "Pushed. GitHub Actions now builds the release: https://github.com/syanmi/TileTerm/actions"
}
