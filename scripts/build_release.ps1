<#
.SYNOPSIS
  Builds the release packages into <repo>\artifacts:
    - TileTerm-v<version>-win-x64-portable.zip   unzip-and-run build (keeps its data next to the exe)
    - TileTerm-v<version>-win-x64-setup.exe      installer (needs Inno Setup 6.3+)
    - SHA256SUMS.txt                             checksums of the two files above

.DESCRIPTION
  This is the single source of truth for how a release is packaged: the GitHub Actions workflow
  (.github/workflows/release.yml) just runs it, so a release can be reproduced on a dev machine.
  The version comes from <Version> in src\TileTerm\TileTerm.csproj.

.PARAMETER Tag
  The git tag being released (for example v0.2.0). When given it must equal "v" + the csproj
  <Version>, so a tag that does not match the code fails here instead of shipping a mislabeled build.

.PARAMETER SkipInstaller
  Build only the portable zip (useful when Inno Setup is not installed).

.NOTES
  Keep this file ASCII-only: Windows PowerShell 5.1 misreads UTF-8 without a BOM.
  The Inno Setup compiler is found on PATH, in the default install folders, or via $env:ISCC_PATH.
#>
[CmdletBinding()]
param(
    [string]$Tag = "",
    [switch]$SkipInstaller
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

$artifacts = Join-Path $root 'artifacts'
$publishDir = Join-Path $artifacts 'publish'
if (Test-Path -LiteralPath $artifacts) { Remove-Item -LiteralPath $artifacts -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir | Out-Null

# --- 1. publish: self-contained (no .NET install needed), one exe ------------------------------------
dotnet publish $csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:DebugSymbols=false -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit code $LASTEXITCODE)" }

$exe = Join-Path $publishDir 'TileTerm.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Expected $exe was not produced" }

# --- 2. portable zip ------------------------------------------------------------------------------------
# The empty TileTerm.portable marker switches the app to keep profiles/settings in a "data" folder beside
# the exe (see AppPaths.cs). The installer build does not have it and uses %AppData%\TileTerm instead.
$portableName = "TileTerm-v$version-win-x64-portable"
$stage = Join-Path $artifacts "stage\$portableName"
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item -LiteralPath $exe -Destination $stage
New-Item -ItemType File -Path (Join-Path $stage 'TileTerm.portable') | Out-Null
foreach ($doc in 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'README.md') {
    $src = Join-Path $root $doc
    if (Test-Path -LiteralPath $src) { Copy-Item -LiteralPath $src -Destination $stage }
}

# Built entry by entry so the paths inside the zip always use "/" as the spec requires (Windows PowerShell 5.1's
# ZipFile.CreateFromDirectory writes "\", which unzip tools on other systems turn into odd file names).
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $artifacts "$portableName.zip"
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $stage -Recurse -File) {
        $entryName = ($portableName + '/' + $file.FullName.Substring($stage.Length + 1)) -replace '\\', '/'
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $file.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal)
    }
}
finally {
    $archive.Dispose()
}
Write-Host "Portable zip: $zip"

# --- 3. installer ------------------------------------------------------------------------------------------
function Find-Iscc {
    if ($env:ISCC_PATH -and (Test-Path -LiteralPath $env:ISCC_PATH)) { return $env:ISCC_PATH }
    $onPath = Get-Command iscc -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    foreach ($dir in @("${env:ProgramFiles(x86)}\Inno Setup 6", "$env:ProgramFiles\Inno Setup 6", "$env:LOCALAPPDATA\Programs\Inno Setup 6")) {
        $candidate = Join-Path $dir 'ISCC.exe'
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    return $null
}

if ($SkipInstaller) {
    Write-Host "Skipping the installer (-SkipInstaller)."
}
else {
    $iscc = Find-Iscc
    if (-not $iscc) {
        throw "Inno Setup (ISCC.exe) was not found. Install Inno Setup 6.3 or newer (choco install innosetup), or pass -SkipInstaller."
    }
    & $iscc "/DAppVersion=$version" "/DSourceDir=$publishDir" "/DOutputDir=$artifacts" (Join-Path $root 'installer\TileTerm.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed (exit code $LASTEXITCODE)" }
}

# --- 4. checksums ------------------------------------------------------------------------------------------
$lines = foreach ($file in Get-ChildItem -LiteralPath $artifacts -File | Where-Object { $_.Extension -in '.zip', '.exe' } | Sort-Object Name) {
    "{0}  {1}" -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $file.Name
}
Set-Content -LiteralPath (Join-Path $artifacts 'SHA256SUMS.txt') -Value $lines -Encoding ascii

Write-Host ""
Write-Host "Done. Files in ${artifacts}:"
Get-ChildItem -LiteralPath $artifacts -File | ForEach-Object { "  {0,-52} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }
