# pyNavis end-user installer. Per-user: no administrator rights, nothing under Program Files.
#
# For every Navisworks release that has a pyNavis build in bin\<year>:
#   1. Loader     -> %APPDATA%\Autodesk\ApplicationPlugins\pyNavis.bundle\Contents\<year>\PyNavis.dll
#   2. Manifest   -> %APPDATA%\Autodesk\ApplicationPlugins\pyNavis.bundle\PackageContents.xml
#   3. Runtime    -> %APPDATA%\pyNavis\<year>\runtime\            (incl. the python stdlib)
#   4. pynavislib -> %APPDATA%\pyNavis\<year>\runtime\pynavislib\
#   5. Extensions -> %APPDATA%\pyNavis\extensions\pyNavis.extension\  (with its .pynavis-manifest)
#
# pyNavis.extension is replaced whole, as the setup program replaces it. Before anything
# changes, what the user added to or edited there is backed up to %APPDATA%\pyNavis\backups,
# and the buttons and panels they added are copied to My pyNavis.extension, which this
# script never touches. That is the setup program's own guard (protect-extension) when the
# command line is built in bin\cli, as a solution build does, and a plainer PowerShell
# equivalent when it is not.
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

# ---- Keeping the user's work in pyNavis.extension -------------------------------------

# The command line a solution build leaves in bin\cli (the setup program ships it as
# cli\pynavis.exe). Version-independent, so one copy serves every year.
$cliExe = Join-Path $BinRoot 'cli\PyNavis.Cli.exe'

# Output captured, so a line the program writes to stderr is not a PowerShell error.
function Invoke-Native([string]$Exe, [string[]]$Arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = & $Exe @Arguments 2>&1 | ForEach-Object { "$_" }
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($lines -join [Environment]::NewLine) }
    }
    finally { $ErrorActionPreference = $previous }
}

# The rules of ShippedFiles (src\Shared\ShippedFiles.cs), for when the command line is not
# built: paths relative to the extension with forward slashes; Python caches and the
# manifest itself are nobody's work; text is hashed with every CRLF read as LF, and a file
# with a NUL byte is binary and hashed as it is.
function Get-ExtensionFiles([string]$Dir) {
    $root = (Get-Item -LiteralPath $Dir).FullName.TrimEnd('\')
    Get-ChildItem -LiteralPath $root -Recurse -File -Force |
        ForEach-Object { $_.FullName.Substring($root.Length).Replace('\', '/').TrimStart('/') }
}

function Test-IgnoredFile([string]$Rel) {
    ($Rel -eq '.pynavis-manifest') -or
        $Rel.EndsWith('.pyc', [StringComparison]::OrdinalIgnoreCase) -or
        $Rel.StartsWith('__pycache__/', [StringComparison]::OrdinalIgnoreCase) -or
        ($Rel.IndexOf('/__pycache__/', [StringComparison]::OrdinalIgnoreCase) -ge 0)
}

function Get-ContentHash([string]$Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ([Array]::IndexOf($bytes, [byte]0) -lt 0) {
        # ISO-8859-1 maps every byte to one char and back, so only the CRLFs change.
        $latin1 = [System.Text.Encoding]::GetEncoding(28591)
        $bytes = $latin1.GetBytes($latin1.GetString($bytes).Replace("`r`n", "`n"))
    }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return -join ($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') }) }
    finally { $sha.Dispose() }
}

function Add-KnownFile($Known, [string]$Rel, [string]$Hash) {
    if (-not $Known.ContainsKey($Rel)) {
        $Known[$Rel] = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    }
    [void]$Known[$Rel].Add($Hash.ToLowerInvariant())
}

# Manifest lines are "<sha256>  <path>"; blank lines and lines starting with # are skipped.
function Read-Manifest([string]$Path, $Known) {
    foreach ($raw in [System.IO.File]::ReadAllLines($Path)) {
        $line = $raw.Trim()
        if ($line.Length -eq 0 -or $line[0] -eq '#') { continue }
        $space = $line.IndexOf(' ')
        if ($space -le 0) { continue }
        $rel = $line.Substring($space).Trim().Replace('\', '/').TrimStart('/')
        if ($rel) { Add-KnownFile $Known $rel $line.Substring(0, $space) }
    }
}

# An added file inside a button or stack that pyNavis ships only means something there,
# so it is backed up but not copied out (ShippedFiles.InsideShippedBundle).
$bundleSuffixes = '.pushbutton', '.toggle', '.smartbutton', '.nobutton', '.urlbutton', '.linkbutton',
    '.dockpane', '.stack', '.pulldown', '.splitbutton', '.splitpushbutton'
function Test-InsideShippedBundle([string]$Rel, $Known) {
    $parts = $Rel.Split('/')
    for ($depth = 1; $depth -lt $parts.Length; $depth++) {
        $folder = $parts[$depth - 1]
        if (-not ($bundleSuffixes | Where-Object { $folder.EndsWith($_, [StringComparison]::OrdinalIgnoreCase) })) { continue }
        $prefix = ($parts[0..($depth - 1)] -join '/') + '/'
        foreach ($key in $Known.Keys) {
            if ($key.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { return $true }
        }
    }
    return $false
}

# The manifest package.ps1 ships inside the extension: "# pyNavis <version>", then one
# "<sha256>  <path>" line per file in ordinal order, LF line endings, UTF-8 without BOM.
# The command line writes it when it is built, so the hashing is the guard's own.
function Write-ExtensionManifest([string]$ExtensionDir, [string]$OutFile, [string]$Version) {
    if (Test-Path $cliExe) {
        $result = Invoke-Native $cliExe @('write-manifest', $ExtensionDir, $OutFile, '--version', $Version)
        if ($result.ExitCode -ne 0) {
            Write-Host $result.Output
            throw "Could not write the shipped-files manifest $OutFile."
        }
        return
    }
    $root = (Get-Item -LiteralPath $ExtensionDir).FullName
    $rels = New-Object 'System.Collections.Generic.List[string]'
    foreach ($rel in (Get-ExtensionFiles $root)) { if (-not (Test-IgnoredFile $rel)) { $rels.Add($rel) } }
    $rels.Sort([StringComparer]::OrdinalIgnoreCase)
    $lines = @("# pyNavis $Version") + @($rels | ForEach-Object { (Get-ContentHash (Join-Path $root $_)) + '  ' + $_ })
    [System.IO.File]::WriteAllText($OutFile, (($lines -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))
}

function Get-FreeName([string]$Dir) {
    $candidate = $Dir
    for ($n = 2; Test-Path -LiteralPath $candidate; $n++) { $candidate = "$Dir $n" }
    return $candidate
}

function Stop-Unprotected([string]$Shipped) {
    Write-Host "pyNavis could not back up what you added to pyNavis.extension, so nothing has been changed. Copy $Shipped somewhere safe, then run this script again." -ForegroundColor Red
    exit 1
}

# What the setup program does before it replaces pyNavis.extension (PrepareToInstall in
# tools\installer\pyNavis.iss): the files the user added to or edited there are backed up,
# and the buttons and panels they added are copied to My pyNavis.extension, where they stay
# on the ribbon because tabs and panels merge by name. The copy about to be installed counts
# as shipped, as every release does, so a tool that is new in this checkout and was put
# there by an earlier run is not taken for the user's. Stops the script, before anything
# changes, when not even a backup can be made.
function Protect-ShippedExtension([string]$Shipped, [string]$Source, [string]$Version) {
    if (-not (Test-Path -LiteralPath $Shipped)) { return }
    $backupRoot = Join-Path $AppDataRoot 'backups'
    $userExt = Join-Path (Split-Path -Parent $Shipped) 'My pyNavis.extension'
    $history = Join-Path $PSScriptRoot 'installer\manifests'

    if (Test-Path $cliExe) {
        $work = Join-Path ([System.IO.Path]::GetTempPath()) ('pynavis-install-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Force -Path $work | Out-Null
        try {
            if (Test-Path $history) { Copy-Item (Join-Path $history '*.txt') $work }
            Write-ExtensionManifest $Source (Join-Path $work 'this-checkout.txt') $Version
            $result = Invoke-Native $cliExe @('protect-extension', '--extension', $Shipped, '--manifests', $work,
                '--backup-root', $backupRoot, '--user-extension', $userExt, '--version', $Version,
                '--report', (Join-Path $AppDataRoot 'last-upgrade.json'))
        }
        finally { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
        if ($result.Output) { Write-Host $result.Output }
        if ($result.ExitCode -ne 0) { Stop-Unprotected $Shipped }
        return
    }

    # No command line: the same plan, kept simpler. Any work of the user's means a copy of
    # the whole folder, and with no manifest to tell their files from pyNavis's, that copy
    # is made every time.
    Write-Host "No command line in $(Split-Path -Parent $cliExe), so pyNavis.extension is checked here."
    $known = New-Object System.Collections.Hashtable ([StringComparer]::OrdinalIgnoreCase)
    $added = @()
    $manifest = Join-Path $Shipped '.pynavis-manifest'
    if (Test-Path -LiteralPath $manifest) {
        if (Test-Path $history) { Get-ChildItem $history -Filter '*.txt' | ForEach-Object { Read-Manifest $_.FullName $known } }
        Read-Manifest $manifest $known
        $sourceRoot = (Get-Item -LiteralPath $Source).FullName
        foreach ($rel in (Get-ExtensionFiles $sourceRoot)) { Add-KnownFile $known $rel (Get-ContentHash (Join-Path $sourceRoot $rel)) }
        $edited = 0
        foreach ($rel in (Get-ExtensionFiles $Shipped)) {
            if (Test-IgnoredFile $rel) { continue }
            if (-not $known.ContainsKey($rel)) { $added += $rel }
            elseif (-not $known[$rel].Contains((Get-ContentHash (Join-Path $Shipped $rel)))) { $edited++ }
        }
        if ($added.Count -eq 0 -and $edited -eq 0) {
            Write-Host 'pyNavis.extension holds nothing of yours.'
            return
        }
    }
    try {
        New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null
        $backupDir = Get-FreeName (Join-Path $backupRoot ("pyNavis.extension before $Version ({0}) full" -f (Get-Date).ToString('yyyy-MM-dd HHmm')))
        Copy-Item -LiteralPath $Shipped $backupDir -Recurse -Force
    }
    catch {
        Write-Host $_.Exception.Message
        Stop-Unprotected $Shipped
    }
    Write-Host "pyNavis.extension backed up to $backupDir" -ForegroundColor Green
    $moved = 0
    foreach ($rel in $added) {
        if (Test-InsideShippedBundle $rel $known) { continue }
        $target = Join-Path $userExt $rel
        if (Test-Path -LiteralPath $target) { continue }   # My pyNavis.extension has its own: left as it is
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath (Join-Path $Shipped $rel) $target -Force
        $moved++
    }
    if ($moved -gt 0) { Write-Host "Copied $moved file(s) you had added to $userExt" -ForegroundColor Green }
}

# The DLL in the bundle is memory-mapped by a running host, so a copy would fail
# halfway and leave a torn install.
if (Get-Process -Name 'Roamer' -ErrorAction SilentlyContinue) {
    Write-Host 'Navisworks is running. Close it and run this script again.' -ForegroundColor Red
    exit 1
}

$version = Get-PyNavisVersion
if (-not $Versions) { $Versions = 2023..2027 | ForEach-Object { "$_" } }

# Which years have a build is settled first, so nothing is touched when there is nothing
# to install, and the user's work is kept before anything changes.
$toInstall = @()
foreach ($v in $Versions) {
    if (-not (Test-Path (Join-Path $BinRoot "$v\PyNavis.dll")) -or
        -not (Test-Path (Join-Path $BinRoot "$v\runtime\PyNavis.Runtime.dll"))) {
        Write-Host "NW$v : no build in $BinRoot\$v (run: dotnet build -p:NavisVersion=$v) - skipped."
        continue
    }
    $toInstall += $v
}
if ($toInstall.Count -eq 0) {
    Write-Host 'Nothing installed.' -ForegroundColor Yellow
    exit 0
}

# --- 0. Keep the user's work in pyNavis.extension (see Protect-ShippedExtension) ---
$extDst = Join-Path $AppDataRoot 'extensions'
$shipped = Join-Path $extDst 'pyNavis.extension'
$extSrc = Join-Path $repo 'extensions\pyNavis.extension'
Protect-ShippedExtension $shipped $extSrc $version

$installed = @()
foreach ($v in $toInstall) {
    $loaderDll = Join-Path $BinRoot "$v\PyNavis.dll"
    $runtimeSrc = Join-Path $BinRoot "$v\runtime"

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
# Replaced whole, so a bundle renamed in the repo cannot survive beside its replacement;
# step 0 has kept what the user had there. The manifest lists the files as installed: the
# next install tells the user's work from pyNavis's by it, and the runtime warns at startup
# when someone adds to or edits the folder.
New-Item -ItemType Directory -Force -Path $extDst | Out-Null
if (Test-Path $shipped) { Remove-Item $shipped -Recurse -Force }
Copy-Item $extSrc $shipped -Recurse -Force
Write-ExtensionManifest $shipped (Join-Path $shipped '.pynavis-manifest') $version
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
