<#
.SYNOPSIS
  Builds the release packages with Velopack into <repo>\artifacts (and the upload set in artifacts\release):
    - TileTerm-win-Setup.exe             installer (one click, per-user, no administrator rights)
    - TileTerm-win-Portable.zip          unzip-and-run build (data stays in a "data" folder beside it)
    - TileTerm-<version>-full.nupkg      the full update package the in-app updater downloads
    - TileTerm-<version>-delta.nupkg     only the changes since the previous release (when there is one)
    - releases.win.json, assets.win.json, RELEASES   the update feed Velopack reads
    - SHA256SUMS.txt, release-notes.md

.DESCRIPTION
  This is the single source of truth for how a release is packaged: the GitHub Actions workflow
  (.github/workflows/release.yml) just runs it, so a release can be reproduced on a dev machine.
  The version comes from <Version> in src\TileTerm\TileTerm.csproj.

  Delta packages need the previous release's full package. On a real release it is downloaded from the
  GitHub release (vpk download github); on a first release there is nothing to download and that is fine.

.PARAMETER Tag
  The git tag being released (for example v0.2.0). When given it must equal "v" + the csproj <Version>,
  so a tag that does not match the code fails here instead of shipping a mislabeled build.

.PARAMETER Repo
  owner/name on GitHub (where previous releases are downloaded from).

.PARAMETER SkipDeltaBase
  Do not download the previous release (offline / local runs). No delta package is built.

.PARAMETER DeltaBaseDir
  Use the packages in this folder as the previous release instead of downloading (for testing updates locally).

.NOTES
  Keep this file ASCII-only: Windows PowerShell 5.1 misreads UTF-8 without a BOM.
  vpk is a pinned local dotnet tool (.config\dotnet-tools.json); "dotnet tool restore" installs it.
#>
[CmdletBinding()]
param(
    [string]$Tag = "",
    [string]$Repo = "syanmi/TileTerm",
    [switch]$SkipDeltaBase,
    [string]$DeltaBaseDir = ""
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$csproj = Join-Path $root 'src\TileTerm\TileTerm.csproj'

[xml]$project = Get-Content -LiteralPath $csproj -Raw
$version = @($project.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
if (-not $version) { throw "No <Version> found in $csproj" }
if ($Tag -and $Tag -ne "v$version") {
    throw "Tag '$Tag' does not match the csproj <Version> '$version' (expected 'v$version'). Fix the version in the csproj, commit, and tag again."
}
Write-Host "Building TileTerm $version"

Push-Location $root
try {
    dotnet tool restore | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed" }

    $artifacts = Join-Path $root 'artifacts'
    $publishDir = Join-Path $artifacts 'publish'
    if (Test-Path -LiteralPath $artifacts) { Remove-Item -LiteralPath $artifacts -Recurse -Force }
    New-Item -ItemType Directory -Path $publishDir | Out-Null

    # --- 1. publish: self-contained (no .NET install needed) ------------------------------------------------
    # Deliberately NOT a single file: Velopack builds delta packages per file, so an update that changes only
    # TileTerm.dll downloads a few hundred KB instead of the whole runtime again.
    dotnet publish $csproj -c Release -r win-x64 --self-contained true `
        -p:DebugType=none -p:DebugSymbols=false -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit code $LASTEXITCODE)" }
    foreach ($doc in 'LICENSE', 'THIRD-PARTY-NOTICES.md') {
        Copy-Item -LiteralPath (Join-Path $root $doc) -Destination $publishDir
    }

    # --- 2. previous release (for deltas) ---------------------------------------------------------------------
    if ($DeltaBaseDir) {
        Write-Host "Using $DeltaBaseDir as the previous release"
        Get-ChildItem -LiteralPath $DeltaBaseDir -File | Where-Object { $_.Extension -eq '.nupkg' -or $_.Name -like 'releases.*' -or $_.Name -eq 'RELEASES' } |
            Copy-Item -Destination $artifacts
    }
    elseif (-not $SkipDeltaBase) {
        $downloadArgs = @('vpk', 'download', 'github', '--repoUrl', "https://github.com/$Repo", '--outputDir', $artifacts)
        if ($env:GITHUB_TOKEN) { $downloadArgs += @('--token', $env:GITHUB_TOKEN) }
        dotnet @downloadArgs
        if ($LASTEXITCODE -ne 0) { throw "vpk download failed (exit code $LASTEXITCODE)" }
    }

    # --- 3. release notes --------------------------------------------------------------------------------------
    $notes = Join-Path $artifacts 'release-notes.md'
    & (Join-Path $PSScriptRoot 'release_notes.ps1') -Tag "v$version" -OutFile $notes -Repo $Repo

    # --- 4. pack: installer, portable zip, full/delta packages, update feed ------------------------------------
    dotnet vpk pack `
        --packId TileTerm --packVersion $version --packTitle TileTerm --packAuthors syanmi `
        --packDir $publishDir --mainExe TileTerm.exe --runtime win-x64 `
        --icon (Join-Path $root 'src\TileTerm\Assets\TileTerm.ico') `
        --releaseNotes $notes --outputDir $artifacts
    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed (exit code $LASTEXITCODE)" }

    # --- 5. what gets uploaded to the GitHub release -----------------------------------------------------------
    # Only this version's files: packages of older versions are already on their own releases.
    $release = Join-Path $artifacts 'release'
    New-Item -ItemType Directory -Path $release | Out-Null
    $wanted = @('TileTerm-win-Setup.exe', 'TileTerm-win-Portable.zip', "TileTerm-$version-full.nupkg", "TileTerm-$version-delta.nupkg",
                'releases.win.json', 'assets.win.json', 'RELEASES')
    foreach ($name in $wanted) {
        $path = Join-Path $artifacts $name
        if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $release }
    }
    Copy-Item -LiteralPath $notes -Destination $release

    $sums = foreach ($file in Get-ChildItem -LiteralPath $release -File | Where-Object { $_.Extension -in '.zip', '.exe', '.nupkg' } | Sort-Object Name) {
        "{0}  {1}" -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $file.Name
    }
    Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Value $sums -Encoding ascii

    Write-Host ""
    Write-Host "Done. Files to upload (${release}):"
    Get-ChildItem -LiteralPath $release -File | ForEach-Object { "  {0,-40} {1,8:N2} MB" -f $_.Name, ($_.Length / 1MB) }
}
finally {
    Pop-Location
}
