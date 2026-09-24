# pyNavis end-user installer. Per-user: no administrator rights, nothing under Program Files.
#
# For every Navisworks release that has a pyNavis build in bin\<year>:
#   1. Loader     -> %APPDATA%\Autodesk\ApplicationPlugins\pyNavis.bundle\Contents\<year>\PyNavis.dll
#   2. Manifest   -> %APPDATA%\Autodesk\ApplicationPlugins\pyNavis.bundle\PackageContents.xml
#   3. Runtime    -> %APPDATA%\pyNavis\<year>\runtime\            (incl. the python stdlib)
#   4. pynavislib -> %APPDATA%\pyNavis\<year>\runtime\pynavislib\
#   5. Extensions -> %APPDATA%\pyNavis\extensions\pyNavis.extension\
#
# Autodesk's ApplicationPlugins mechanism loads a *.bundle folder out of the user profile,
# which is why this install needs no elevation. The manifest carries one <Components> block
# per year being installed; Navisworks' internal series is the year minus 2003, so 2026 is
# Nw23. The runtime may later add its own PyNavisPanes.dll next to the loader and register
# it in the same manifest, so a reinstall rewrites the manifest but never deletes files it
# did not put there.
#
# config.json and the logs live in %APPDATA%\pyNavis. This script never writes and never
# removes config.json - the loader resolves the runtime as: PYNAVIS_RUNTIME env var, then
# config.json "runtime<year>", then %APPDATA%\pyNavis\<year>\runtime, then
# %PROGRAMDATA%\pyNavis\<year>\runtime.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File tools\install.ps1                # every year with a build
#   powershell -ExecutionPolicy Bypass -File tools\install.ps1 -Versions 2026
# Run with Navisworks CLOSED.

param(
    [string[]]$Versions,          # default: every year 2023-2027 that has a build in bin\<year>
    [string]$BinRoot,             # default: <repo>\bin
    [string]$BundleRoot,          # default: %APPDATA%\Autodesk\ApplicationPlugins\pyNavis.bundle  (override for tests)
    [string]$AppDataRoot,         # default: %APPDATA%\pyNavis                                     (override for tests)
    [hashtable]$NavisDirOverride  # e.g. @{ '2026' = 'D:\fake\NW2026' }  (tests / odd installs)
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $BinRoot) { $BinRoot = Join-Path $repo 'bin' }
if (-not $BundleRoot) { $BundleRoot = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\pyNavis.bundle' }
if (-not $AppDataRoot) { $AppDataRoot = Join-Path $env:APPDATA 'pyNavis' }

# Fixed for the life of the product: Autodesk matches an upgrade by these two codes.
$productCode = '{7F3E9A2C-5B14-4D6E-9C7A-2E8B1F4D6A90}'
$upgradeCode = '{A1C4E7B2-3D58-4F9A-8B6C-5E2D7A9F1C43}'

function Get-PyNavisVersion {
    $initPy = Join-Path $repo 'pynavislib\pynavis\__init__.py'
    $text = [System.IO.File]::ReadAllText($initPy)
    if ($text -match "__version__\s*=\s*['`"]([^'`"]+)['`"]") { return $Matches[1] }
    throw "Could not read __version__ from $initPy"
}

function Resolve-NavisDir([string]$Version) {
    if ($NavisDirOverride -and $NavisDirOverride.ContainsKey($Version)) { return $NavisDirOverride[$Version] }
    $regVer = "$([int]$Version - 2003).0"   # Navisworks internal version = year - 2003
    $regPath = "HKLM:\SOFTWARE\Autodesk\Navisworks Manage\$regVer\Location"
    if (Test-Path $regPath) {
        $dir = (Get-ItemProperty $regPath).Path
        if ($dir -and (Test-Path $dir)) { return $dir.TrimEnd('\') }
    }
    foreach ($root in 'C:\Program Files', 'D:\Program Files') {
        $candidate = Join-Path $root "Autodesk\Navisworks Manage $Version"
        if (Test-Path $candidate) { return $candidate }
    }
    return $null
}

# One <Components> block per year. SeriesMin = SeriesMax: a loader built against one
# year's API is only ever offered to that year's host.
function New-ComponentsBlock([string]$Year, [string]$AppVersion) {
    $series = "Nw$([int]$Year - 2003)"
    @"
  <Components Description="Navisworks $Year">
    <RuntimeRequirements OS="Win64" Platform="NAVMAN|NAVSIM" SeriesMin="$series" SeriesMax="$series" />
    <ComponentEntry AppName="pyNavis" AppType="ManagedPlugin" Version="$AppVersion" ModuleName="./Contents/$Year/PyNavis.dll" AppDescription="pyNavis loader" />
  </Components>
"@
}

# The DLL in the bundle is memory-mapped by a running host, so a copy would fail
# halfway and leave a torn install.
if (Get-Process -Name 'Roamer' -ErrorAction SilentlyContinue) {
    Write-Host 'Navisworks is running. Close it and run this script again.' -ForegroundColor Red
    exit 1
}

$version = Get-PyNavisVersion
if (-not $Versions) { $Versions = 2023..2027 | ForEach-Object { "$_" } }

$installed = @()
foreach ($v in $Versions) {
    $loaderDll = Join-Path $BinRoot "$v\PyNavis.dll"
    $runtimeSrc = Join-Path $BinRoot "$v\runtime"
    if (-not (Test-Path $loaderDll) -or -not (Test-Path (Join-Path $runtimeSrc 'PyNavis.Runtime.dll'))) {
        Write-Host "NW$v : no build in $BinRoot\$v (run: dotnet build -p:NavisVersion=$v) - skipped."
        continue
    }

    # --- 1. Loader -> the bundle's Contents\<year>\ ---
    # Copied in place, not into a wiped folder: the runtime generates PyNavisPanes.dll
    # here when the user adds dock panel slots, and that file must survive an update.
    $contentsDir = Join-Path $BundleRoot "Contents\$v"
    New-Item -ItemType Directory -Force -Path $contentsDir | Out-Null
    Copy-Item $loaderDll $contentsDir -Force
    Get-ChildItem $contentsDir -File | Unblock-File

    # --- 2+3. Runtime (+ pynavislib inside it) -> per-user AppData, clean update ---
    $runtimeDst = Join-Path $AppDataRoot "$v\runtime"
    if (Test-Path $runtimeDst) { Remove-Item $runtimeDst -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $runtimeDst | Out-Null
    Copy-Item (Join-Path $runtimeSrc '*') $runtimeDst -Recurse -Force
    Copy-Item (Join-Path $repo 'pynavislib') (Join-Path $runtimeDst 'pynavislib') -Recurse -Force
    Get-ChildItem $runtimeDst -Recurse -File | Unblock-File

    Write-Host "NW$v : loader -> $contentsDir ; runtime -> $runtimeDst" -ForegroundColor Green
    $installed += $v
}

if ($installed.Count -eq 0) {
    Write-Host 'Nothing installed.' -ForegroundColor Yellow
    exit 0
}

# --- 4. The bundle manifest, covering exactly the years just installed ---
$blocks = ($installed | ForEach-Object { New-ComponentsBlock $_ $version }) -join "`r`n"
$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<ApplicationPackage SchemaVersion="1.0" ProductType="Application" Name="pyNavis" Description="Python scripting and ribbon tools for Navisworks" AppVersion="$version" FriendlyVersion="$version" ProductCode="$productCode" UpgradeCode="$upgradeCode" Author="pyNavis" SupportedLocales="Enu" OnlineDocumentation="https://wiki.pynavis.com">
  <CompanyDetails Name="pyNavis" Url="https://pynavis.com" />
$blocks
</ApplicationPackage>
"@
$manifestPath = Join-Path $BundleRoot 'PackageContents.xml'
# Temp file then swap: Navisworks reads this at every start, so an interrupted
# install must never leave it truncated.
$tempPath = "$manifestPath.tmp"
[System.IO.File]::WriteAllText($tempPath, $manifest, (New-Object System.Text.UTF8Encoding($false)))
# [NullString]::Value, not $null: PowerShell hands a string parameter "" for $null, and
# File.Replace rejects an empty backup path ("The path is not of a legal form").
if (Test-Path $manifestPath) { [System.IO.File]::Replace($tempPath, $manifestPath, [NullString]::Value) }
else { [System.IO.File]::Move($tempPath, $manifestPath) }
Unblock-File $manifestPath
Write-Host "Bundle manifest -> $manifestPath" -ForegroundColor Green

# --- 5. Extensions (shared across versions), clean update of the shipped one only ---
$extDst = Join-Path $AppDataRoot 'extensions'
New-Item -ItemType Directory -Force -Path $extDst | Out-Null
$shipped = Join-Path $extDst 'pyNavis.extension'
if (Test-Path $shipped) { Remove-Item $shipped -Recurse -Force }
Copy-Item (Join-Path $repo 'extensions\pyNavis.extension') $shipped -Recurse -Force
Get-ChildItem $shipped -Recurse -File | Unblock-File
Write-Host "Extensions -> $shipped" -ForegroundColor Green

# --- 6. Warn about any leftover pre-bundle install ---
# A loader in <Navisworks>\Plugins\PyNavis still loads, so both copies would run and
# the ribbon would be built twice. Removing it needs administrator rights, so this
# only reports the folder instead of trying.
foreach ($v in (2023..2027 | ForEach-Object { "$_" })) {
    $navisDir = Resolve-NavisDir $v
    if (-not $navisDir) { continue }
    $legacyDir = Join-Path $navisDir 'Plugins\PyNavis'
    if (Test-Path $legacyDir) {
        Write-Host ''
        Write-Host "WARNING: an older pyNavis is still installed for NW$v at:" -ForegroundColor Yellow
        Write-Host "  $legacyDir" -ForegroundColor Yellow
        Write-Host '  Both copies would load and the ribbon would be built twice. Delete that folder' -ForegroundColor Yellow
        Write-Host '  from an elevated (Administrator) PowerShell:' -ForegroundColor Yellow
        Write-Host "    Remove-Item -Recurse -Force `"$legacyDir`""
    }
}

$userConfig = Join-Path $AppDataRoot 'config.json'
if (Test-Path $userConfig) {
    $configText = [System.IO.File]::ReadAllText($userConfig)
    $overrides = $installed | Where-Object { $configText -match "runtime$_" }
    if ($overrides) {
        Write-Host ''
        Write-Host "NOTE: $userConfig overrides runtime$($overrides -join ', runtime') for THIS user (dev setup). Delete those keys to use the installed runtime." -ForegroundColor Yellow
    }
}

Write-Host ''
Write-Host "Installed pyNavis $version for: NW$($installed -join ', NW'). Start Navisworks and look for the 'pyNavis' ribbon tab."
