# Builds a pyNavis release: Release binaries for every supported Navisworks year, a staged
# payload, and the per-user setup.exe.
#
#   1. dotnet build -c Release -p:NavisVersion=<year>   for 2023-2027. A year whose
#      Autodesk reference cannot be resolved is skipped; any other build error is fatal.
#   2. dist\stage\bundle\    PackageContents.xml + Contents\<year>\PyNavis.dll
#      dist\stage\appdata\   <year>\runtime\ (pynavislib inside it, no .pdb),
#                            extensions\pyNavis.extension\ and cli\pynavis.exe
#   3. ISCC.exe compiles tools\installer\pyNavis.iss to
#      dist\pyNavis-<version>-setup.exe   (version comes from pynavislib\pynavis\__init__.py)
#
# The version is plain MAJOR.MINOR.PATCH and CHANGELOG.md must lead with it (see
# "Releasing" in README.md); the script stops before building when either is off.
#
# The Release build writes to the same bin\<year> folders as a Debug build, so the repo's
# dev output is replaced. Run tools\deploy-dev.ps1 afterwards to get back to a dev deploy.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File tools\package.ps1
#   powershell -ExecutionPolicy Bypass -File tools\package.ps1 -Versions 2026
# Run with Navisworks CLOSED.

param(
    [string[]]$Versions = @('2023', '2024', '2025', '2026', '2027'),
    [string]$Iscc,        # default: the usual Inno Setup 6 locations
    [string]$OutputDir    # default: <repo>\dist
)

$ErrorActionPreference = 'Stop'
# powershell.exe -File hands an array parameter only its first space-separated value and
# passes the rest positionally, so "-Versions 2025,2026" arrives as one string. Split it.
$Versions = $Versions | ForEach-Object { $_ -split ',' } | Where-Object { $_.Trim() } | ForEach-Object { $_.Trim() }
$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) { $OutputDir = Join-Path $repo 'dist' }

# Fixed for the life of the product: Autodesk matches an upgrade by these two codes.
$productCode = '{7F3E9A2C-5B14-4D6E-9C7A-2E8B1F4D6A90}'
$upgradeCode = '{A1C4E7B2-3D58-4F9A-8B6C-5E2D7A9F1C43}'

function Get-PyNavisVersion {
    $initPy = Join-Path $repo 'pynavislib\pynavis\__init__.py'
    $text = [System.IO.File]::ReadAllText($initPy)
    if ($text -match "__version__\s*=\s*['`"]([^'`"]+)['`"]") { return $Matches[1] }
    throw "Could not read __version__ from $initPy"
}

# One <Components> block per year. SeriesMin = SeriesMax: a loader built against one
# year's API is only ever offered to that year's host (series = year - 2003).
function New-ComponentsBlock([string]$Year, [string]$AppVersion) {
    $series = "Nw$([int]$Year - 2003)"
    @"
  <Components Description="Navisworks $Year">
    <RuntimeRequirements OS="Win64" Platform="NAVMAN|NAVSIM" SeriesMin="$series" SeriesMax="$series" />
    <ComponentEntry AppName="pyNavis" AppType="ManagedPlugin" Version="$AppVersion" ModuleName="./Contents/$Year/PyNavis.dll" AppDescription="pyNavis loader" />
  </Components>
"@
}

function Find-Iscc {
    if ($Iscc) {
        if (-not (Test-Path $Iscc)) { throw "ISCC.exe not found at $Iscc" }
        return $Iscc
    }
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    foreach ($c in $candidates) { if ($c -and (Test-Path $c)) { return $c } }
    $onPath = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    throw 'Inno Setup 6 not found. Install it (winget install --id JRSoftware.InnoSetup -e) or pass -Iscc <path to ISCC.exe>.'
}

# Native tools write progress to stderr, which a Stop-preference session would turn into
# a terminating error before the exit code could be read. Run them at Continue and judge
# them by $LASTEXITCODE instead.
function Invoke-Native([string]$Exe, [string[]]$Arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = & $Exe @Arguments 2>&1 | ForEach-Object { "$_" }
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($lines -join [Environment]::NewLine) }
    }
    finally { $ErrorActionPreference = $previous }
}

# A running host memory-maps the loader DLL, so the build would fail on the copy step.
if (Get-Process -Name 'Roamer' -ErrorAction SilentlyContinue) {
    Write-Host 'Navisworks is running. Close it and run this script again.' -ForegroundColor Red
    exit 1
}

function Assert-ReleaseNotes([string]$Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Version '$Version' is not plain MAJOR.MINOR.PATCH (the Autodesk manifest and the installer's version resource take digits and dots only)."
    }
    $changelog = Join-Path $repo 'CHANGELOG.md'
    if (-not (Test-Path $changelog)) { throw "CHANGELOG.md is missing from $repo." }
    $heading = [System.IO.File]::ReadAllLines($changelog) | Where-Object { $_ -match '^## \[' } | Select-Object -First 1
    if (-not $heading -or $heading -notmatch '^## \[([^\]]+)\] - \d{4}-\d{2}-\d{2}\s*$') {
        throw "CHANGELOG.md has no '## [x.y.z] - yyyy-mm-dd' heading."
    }
    if ($Matches[1] -ne $Version) {
        throw "CHANGELOG.md leads with $($Matches[1]) but __version__ is $Version. Add the $Version entry first."
    }
}

$version = Get-PyNavisVersion
Assert-ReleaseNotes $version
Write-Host "Packaging pyNavis $version" -ForegroundColor Cyan

# --- 1. Release builds, one per year that has an Autodesk reference ---
# Building the loader project pulls in PyNavis.Runtime as a project reference, so both
# bin\<year>\PyNavis.dll and bin\<year>\runtime\ come out of this one build.
$loaderProj = Join-Path $repo 'src\PyNavis\PyNavis.csproj'
$built = @()
$skipped = @()
foreach ($v in $Versions) {
    Write-Host "  Building NW$v ..." -NoNewline
    # "-p:NavisVersion=$v", not '-p:NavisVersion=' + $v: inside an array literal the
    # comma binds tighter than +, so the year would become its own argument.
    $result = Invoke-Native 'dotnet' @('build', $loaderProj, '-c', 'Release', "-p:NavisVersion=$v", '--nologo', '-v', 'minimal')
    if ($result.ExitCode -eq 0) {
        Write-Host ' ok' -ForegroundColor Green
        $built += "$v"
        continue
    }
    if ($result.Output -match 'Could not locate Autodesk\.Navisworks\.Api\.dll') {
        Write-Host ' no Navisworks reference - skipped' -ForegroundColor Yellow
        $skipped += "$v"
        continue
    }
    Write-Host ' FAILED' -ForegroundColor Red
    Write-Host $result.Output
    throw "Release build failed for Navisworks $v."
}
if ($built.Count -eq 0) { throw 'No year could be built, so there is nothing to package.' }

# The command line is version-independent (no Navisworks reference), so it builds once.
Write-Host '  Building the command line ...' -NoNewline
$result = Invoke-Native 'dotnet' @('build', (Join-Path $repo 'src\PyNavis.Cli\PyNavis.Cli.csproj'), '-c', 'Release', '--nologo', '-v', 'minimal')
if ($result.ExitCode -ne 0) {
    Write-Host ' FAILED' -ForegroundColor Red
    Write-Host $result.Output
    throw 'Release build failed for the pyNavis command line.'
}
Write-Host ' ok' -ForegroundColor Green

# --- 2. Stage the payload ---
$stage = Join-Path $OutputDir 'stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

$bundleStage = Join-Path $stage 'bundle'
$appDataStage = Join-Path $stage 'appdata'
foreach ($v in $built) {
    $contentsDir = Join-Path $bundleStage "Contents\$v"
    New-Item -ItemType Directory -Force -Path $contentsDir | Out-Null
    Copy-Item (Join-Path $repo "bin\$v\PyNavis.dll") $contentsDir -Force

    $runtimeDst = Join-Path $appDataStage "$v\runtime"
    New-Item -ItemType Directory -Force -Path $runtimeDst | Out-Null
    Copy-Item (Join-Path $repo "bin\$v\runtime\*") $runtimeDst -Recurse -Force
    Copy-Item (Join-Path $repo 'pynavislib') (Join-Path $runtimeDst 'pynavislib') -Recurse -Force
    # Debug symbols are dev-time only; they would roughly double the download.
    Get-ChildItem $runtimeDst -Recurse -File -Filter '*.pdb' | Remove-Item -Force
}

# The exe ships as pynavis.exe so "pynavis env" reads naturally at a prompt. The
# assembly is named PyNavis.Cli to avoid a CLR name clash with the loader, so the
# binding config has to be renamed with it or the framework stops finding it.
$cliStage = Join-Path $appDataStage 'cli'
New-Item -ItemType Directory -Force -Path $cliStage | Out-Null
Copy-Item (Join-Path $repo 'bin\cli\PyNavis.Cli.exe') (Join-Path $cliStage 'pynavis.exe') -Force
$cliConfig = Join-Path $repo 'bin\cli\PyNavis.Cli.exe.config'
if (Test-Path $cliConfig) { Copy-Item $cliConfig (Join-Path $cliStage 'pynavis.exe.config') -Force }

$extStage = Join-Path $appDataStage 'extensions\pyNavis.extension'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $extStage) | Out-Null
Copy-Item (Join-Path $repo 'extensions\pyNavis.extension') $extStage -Recurse -Force
Get-ChildItem $extStage -Recurse -Directory -Filter '__pycache__' | Remove-Item -Recurse -Force

$blocks = ($built | ForEach-Object { New-ComponentsBlock $_ $version }) -join "`r`n"
$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<ApplicationPackage SchemaVersion="1.0" ProductType="Application" Name="pyNavis" Description="Python scripting and ribbon tools for Navisworks" AppVersion="$version" FriendlyVersion="$version" ProductCode="$productCode" UpgradeCode="$upgradeCode" Author="pyNavis" SupportedLocales="Enu" OnlineDocumentation="https://wiki.pynavis.com">
  <CompanyDetails Name="pyNavis" Url="https://pynavis.com" />
$blocks
</ApplicationPackage>
"@
[System.IO.File]::WriteAllText((Join-Path $bundleStage 'PackageContents.xml'), $manifest, (New-Object System.Text.UTF8Encoding($false)))

$stageBytes = (Get-ChildItem $stage -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Host ("  Staged {0} for NW{1} ({2:N1} MB)" -f $stage, ($built -join ', NW'), ($stageBytes / 1MB)) -ForegroundColor Green

# --- 3. Compile the installer ---
$isccPath = Find-Iscc
$iss = Join-Path $PSScriptRoot 'installer\pyNavis.iss'
$baseName = "pyNavis-$version-setup"
$result = Invoke-Native $isccPath @(
    "/DAppVersion=$version",
    "/DStageDir=$stage",
    "/DOutputDir=$OutputDir",
    "/DOutputBaseFilename=$baseName",
    $iss
)
if ($result.ExitCode -ne 0) {
    Write-Host $result.Output
    throw "Inno Setup compilation failed (exit code $($result.ExitCode))."
}

$setupExe = Join-Path $OutputDir "$baseName.exe"
if (-not (Test-Path $setupExe)) { throw "Inno Setup reported success but $setupExe is missing." }
$setupMB = (Get-Item $setupExe).Length / 1MB

Write-Host ''
Write-Host "pyNavis $version packaged." -ForegroundColor Green
Write-Host ("  Installer : {0} ({1:N1} MB)" -f $setupExe, $setupMB)
Write-Host ("  Years     : NW{0}" -f ($built -join ', NW'))
if ($skipped.Count -gt 0) { Write-Host ("  Skipped   : NW{0} (no Navisworks reference on this machine)" -f ($skipped -join ', NW')) }
