# Deploys the pyNavis DEV build:
#   1. Removes any pre-bundle loader from <Navisworks>\Plugins\PyNavis\ (it would double-load)
#   2. Copies the loader (PyNavis.dll) into the per-user Autodesk bundle:
#      %APPDATA%\Autodesk\ApplicationPlugins\pyNavis.bundle\Contents\<year>\PyNavis.dll
#      and merges a <Components> block for this year into the bundle's PackageContents.xml,
#      leaving any other year's block alone.
#   3. Writes %APPDATA%\pyNavis\config.json pointing the runtime at this repo's bin folder,
#      so C#/python changes only need a rebuild (+ Navisworks restart for C#), never a redeploy.
#
# Usage:  powershell -ExecutionPolicy Bypass -File tools\deploy-dev.ps1 [-Version 2026] [-NavisDir <path>]
# Run with Navisworks CLOSED (the loader DLL is locked while it runs).

param(
    [string]$Version = '2026',
    [string]$NavisDir
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

if ([int]$Version -lt 2023 -or [int]$Version -gt 2027) { throw "Unsupported Navisworks version '$Version' (expected 2023-2027)." }
# Navisworks' internal version is year - 2003 (2026 -> 23.0); same rule as Directory.Build.props.
$regVer = "$([int]$Version - 2003).0"
# The ApplicationPlugins manifest spells the same number as a series id (2026 -> Nw23).
$series = "Nw$([int]$Version - 2003)"

# Fixed for the life of the product: Autodesk matches an upgrade by these two codes.
$productCode = '{7F3E9A2C-5B14-4D6E-9C7A-2E8B1F4D6A90}'
$upgradeCode = '{A1C4E7B2-3D58-4F9A-8B6C-5E2D7A9F1C43}'

$initPy = Join-Path $repo 'pynavislib\pynavis\__init__.py'
$initText = [System.IO.File]::ReadAllText($initPy)
if ($initText -match "__version__\s*=\s*['`"]([^'`"]+)['`"]") { $pyNavisVersion = $Matches[1] }
else { throw "Could not read __version__ from $initPy" }

# The bundle DLL is memory-mapped by a running host, so a copy would fail halfway.
if (Get-Process -Name 'Roamer' -ErrorAction SilentlyContinue) {
    Write-Host 'Navisworks is running. Close it and run this script again.' -ForegroundColor Red
    exit 1
}

# NOTE: this resolves the Navisworks INSTALL dir. Since the loader moved to the per-user
# ApplicationPlugins bundle it is only needed to find (and clear) a pre-bundle install.
# The build's NavisApiDir is a different concept (reference dir - may be refs\<year> or an
# env override), so this lookup stays separate on purpose.
if (-not $NavisDir) {
    $regPath = "HKLM:\SOFTWARE\Autodesk\Navisworks Manage\$regVer\Location"
    if (Test-Path $regPath) { $NavisDir = (Get-ItemProperty $regPath).Path }
    if (-not $NavisDir) {
        foreach ($root in 'C:\Program Files', 'D:\Program Files') {
            $candidate = Join-Path $root "Autodesk\Navisworks Manage $Version"
            if (Test-Path $candidate) { $NavisDir = $candidate; break }
        }
    }
}
if ($NavisDir) { $NavisDir = $NavisDir.TrimEnd('\') }

$loaderDll = Join-Path $repo "bin\$Version\PyNavis.dll"
if (-not (Test-Path $loaderDll)) { throw "Loader not built: $loaderDll  (run: dotnet build -p:NavisVersion=$Version)" }

$runtimeDir = Join-Path $repo "bin\$Version\runtime"
if (-not (Test-Path (Join-Path $runtimeDir 'PyNavis.Runtime.dll'))) { throw "Runtime not built: $runtimeDir" }

# --- 1. Clear the pre-bundle loader, if this machine still has one ---
#        A DLL in <Navisworks>\Plugins\PyNavis still loads alongside the bundle, so
#        leaving it would build the ribbon twice. It lives under Program Files, so the
#        delete may need elevation; that is a hard stop, not a warning.
if ($NavisDir) {
    $legacyDir = Join-Path $NavisDir 'Plugins\PyNavis'
    if (Test-Path $legacyDir) {
        try {
            Remove-Item $legacyDir -Recurse -Force
            Write-Host "Legacy loader removed -> $legacyDir" -ForegroundColor Green
        }
        catch {
            Write-Host "Could not remove the legacy loader at $legacyDir" -ForegroundColor Red
            Write-Host '  Both copies would load and the ribbon would be built twice.' -ForegroundColor Yellow
            Write-Host '  Remove it from an elevated (Administrator) PowerShell, then run this script again:' -ForegroundColor Yellow
            Write-Host "    Remove-Item -Recurse -Force `"$legacyDir`""
            Write-Host "  $($_.Exception.Message)"
            exit 1
        }
    }
}

# --- 2. Loader -> the per-user Autodesk bundle ---
$bundleRoot = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\pyNavis.bundle'
$contentsDir = Join-Path $bundleRoot "Contents\$Version"
New-Item -ItemType Directory -Force -Path $contentsDir | Out-Null
Copy-Item $loaderDll $contentsDir -Force
$pdb = [System.IO.Path]::ChangeExtension($loaderDll, '.pdb')
if (Test-Path $pdb) { Copy-Item $pdb $contentsDir -Force }
Get-ChildItem $contentsDir -File | Unblock-File
Write-Host "Loader deployed -> $contentsDir" -ForegroundColor Green

# --- 2b. Merge this year's <Components> block into the bundle manifest ---
#         Other years' blocks survive, and so does any extra ComponentEntry the runtime
#         added for a generated PyNavisPanes.dll: only this year's PyNavis.dll entry is
#         rewritten, never a whole block that already exists.
$manifestPath = Join-Path $bundleRoot 'PackageContents.xml'
$xml = New-Object System.Xml.XmlDocument
$loaded = $false
if (Test-Path $manifestPath) {
    # SelectSingleNode, not $xml.DocumentElement.Name: PowerShell's XML adapter exposes
    # attributes as properties and shadows the real ones, and ApplicationPackage has a
    # Name attribute ("pyNavis"), so the element name check would never match and every
    # deploy would silently rebuild the manifest and drop the other years.
    try { $xml.Load($manifestPath); $loaded = ($null -ne $xml.SelectSingleNode('/ApplicationPackage')) }
    catch {
        Write-Host "PackageContents.xml would not parse, so it is being rebuilt: $manifestPath" -ForegroundColor Yellow
        Write-Host "  $($_.Exception.Message)"
    }
}
if (-not $loaded) {
    $xml.LoadXml(@"
<?xml version="1.0" encoding="utf-8"?>
<ApplicationPackage SchemaVersion="1.0" ProductType="Application" Name="pyNavis" Description="Python scripting and ribbon tools for Navisworks" AppVersion="$pyNavisVersion" FriendlyVersion="$pyNavisVersion" ProductCode="$productCode" UpgradeCode="$upgradeCode" Author="pyNavis" SupportedLocales="Enu" OnlineDocumentation="https://wiki.pynavis.com">
  <CompanyDetails Name="pyNavis" Url="https://pynavis.com" />
</ApplicationPackage>
"@)
}
$root = $xml.DocumentElement
$root.SetAttribute('AppVersion', $pyNavisVersion)
$root.SetAttribute('FriendlyVersion', $pyNavisVersion)

$block = $null
foreach ($c in @($root.SelectNodes('Components'))) {
    $req = $c.SelectSingleNode('RuntimeRequirements')
    if ($req -and $req.GetAttribute('SeriesMin') -eq $series) { $block = $c; break }
}
if (-not $block) {
    $block = $xml.CreateElement('Components')
    $block.SetAttribute('Description', "Navisworks $Version")
    $req = $xml.CreateElement('RuntimeRequirements')
    $req.SetAttribute('OS', 'Win64')
    $req.SetAttribute('Platform', 'NAVMAN|NAVSIM')
    $req.SetAttribute('SeriesMin', $series)
    $req.SetAttribute('SeriesMax', $series)
    $block.AppendChild($req) | Out-Null
    $root.AppendChild($block) | Out-Null
}
$moduleName = "./Contents/$Version/PyNavis.dll"
$entry = $null
foreach ($e in @($block.SelectNodes('ComponentEntry'))) {
    if ($e.GetAttribute('ModuleName') -eq $moduleName) { $entry = $e; break }
}
if (-not $entry) {
    $entry = $xml.CreateElement('ComponentEntry')
    $block.AppendChild($entry) | Out-Null
}
$entry.SetAttribute('AppName', 'pyNavis')
$entry.SetAttribute('AppType', 'ManagedPlugin')
$entry.SetAttribute('Version', $pyNavisVersion)
$entry.SetAttribute('ModuleName', $moduleName)
$entry.SetAttribute('AppDescription', 'pyNavis loader')

# Temp file then swap: Navisworks reads this at every start, so an interrupted deploy
# must never leave it truncated.
$writerSettings = New-Object System.Xml.XmlWriterSettings
$writerSettings.Indent = $true
$writerSettings.IndentChars = '  '
$writerSettings.Encoding = New-Object System.Text.UTF8Encoding($false)
$tempPath = "$manifestPath.tmp"
$writer = [System.Xml.XmlWriter]::Create($tempPath, $writerSettings)
try { $xml.Save($writer) } finally { $writer.Close() }
# [NullString]::Value, not $null: PowerShell hands a string parameter "" for $null, and
# File.Replace rejects an empty backup path ("The path is not of a legal form").
if (Test-Path $manifestPath) { [System.IO.File]::Replace($tempPath, $manifestPath, [NullString]::Value) }
else { [System.IO.File]::Move($tempPath, $manifestPath) }
Write-Host "Bundle manifest updated -> $manifestPath  ($series)" -ForegroundColor Green

# --- 3. Dev config: runtime resolves straight from the repo build output.
#        Keys are per-version ("runtime2026") so multiple Navisworks releases can
#        coexist; merge into any existing config instead of overwriting it. ---
$configDir = Join-Path $env:APPDATA 'pyNavis'
New-Item -ItemType Directory -Force -Path $configDir | Out-Null
$configPath = Join-Path $configDir 'config.json'
$config = [ordered]@{}
if (Test-Path $configPath) {
    # [IO.File], not Get-Content -Raw: PS 5.1 reads BOM-less UTF-8 as ANSI, which
    # mojibakes every non-ASCII character on the round trip.
    $existing = [System.IO.File]::ReadAllText($configPath)
    if ($existing.Trim()) {
        # A config that will not parse STOPS the deploy. "Recreate it" would drop the
        # shortcut bindings, the for-life dock-panel slots and the other releases'
        # runtime keys over one stray comma; same rule as PyNavisConfig in the runtime.
        try { $parsed = $existing | ConvertFrom-Json }
        catch {
            Write-Host "config.json is not valid JSON, so it was left untouched: $configPath" -ForegroundColor Red
            Write-Host "Fix the file (or delete it to start fresh), then run this script again." -ForegroundColor Yellow
            Write-Host "  $($_.Exception.Message)"
            exit 1
        }
        $parsed.PSObject.Properties | ForEach-Object { $config[$_.Name] = $_.Value }
    }
}
$config["runtime$Version"] = $runtimeDir
$config["extensions"] = @(Join-Path $repo 'extensions')
$config["pynavislib"] = Join-Path $repo 'pynavislib'

# -Depth: the default of 2 flattens anything nested deeper into a type-name string.
# Temp file then swap: the loader regex-reads this file at every Navisworks start, so
# an interrupted deploy must never leave it truncated.
$json = $config | ConvertTo-Json -Depth 32
$tempPath = "$configPath.tmp"
[System.IO.File]::WriteAllText($tempPath, $json, (New-Object System.Text.UTF8Encoding($false)))
# [NullString]::Value, not $null: PowerShell hands a string parameter "" for $null, and
# File.Replace rejects an empty backup path ("The path is not of a legal form").
if (Test-Path $configPath) { [System.IO.File]::Replace($tempPath, $configPath, [NullString]::Value) }
else { [System.IO.File]::Move($tempPath, $configPath) }
Write-Host "Dev config updated -> $configPath  (runtime$Version = $runtimeDir)" -ForegroundColor Green

Write-Host "`nDone. Start Navisworks Manage $Version and look for the 'pyNavis' ribbon tab."
Write-Host "Logs: %APPDATA%\pyNavis\logs\{loader.log, pyNavis.log}"
