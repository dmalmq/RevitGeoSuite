<#
.SYNOPSIS
    Installs the Kanizu Revit add-in for the selected Revit year (all users).
.DESCRIPTION
    Copies the add-in payload and manifest to the system-wide Revit add-ins folder
    for the selected Revit year (ProgramData up to Revit 2026, Program Files from
    Revit 2027; see RevitAddinPaths.ps1), and removes any legacy RevitGeoSuite
    installation so both add-ins never load side-by-side.
    Must be run as Administrator.

    The script looks for build output in this order:
      1. install/dist/ folder (from build-release.ps1)
      2. bin/Deploy/<RevitYear>/ (direct build output)
#>
param(
    [string]$RevitYear = "2024"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "RevitAddinPaths.ps1")

$excludedFilePatterns = @(
    ".msCoverageSourceRootsMapping*",
    "*.Tests.dll",
    "*.Tests.dll.config",
    "*.Tests.exe",
    "*.Tests.pdb",
    "*.pdb",
    "Castle.Core.dll",
    "KanizuRevit.addin",
    "Microsoft.TestPlatform.*",
    "Microsoft.VisualStudio.CodeCoverage*",
    "Microsoft.VisualStudio.TestPlatform.ObjectModel*",
    "Moq.dll",
    "RevitGeoSuite.addin",
    "xunit*"
)

function Test-IsExcludedFile {
    param([Parameter(Mandatory = $true)][string]$Name)

    foreach ($pattern in $excludedFilePatterns) {
        if ($Name -like $pattern) {
            return $true
        }
    }

    return $false
}

function Copy-PayloadFiles {
    param(
        [Parameter(Mandatory = $true)][string]$SourceRoot,
        [Parameter(Mandatory = $true)][string]$DestinationRoot
    )

    $copied = 0
    foreach ($file in Get-ChildItem -LiteralPath $SourceRoot -Recurse -File -Force) {
        if (Test-IsExcludedFile -Name $file.Name) {
            continue
        }

        $relativePath = $file.FullName.Substring($SourceRoot.Length).TrimStart("\", "/")
        $destinationPath = Join-Path $DestinationRoot $relativePath
        $destinationDirectory = Split-Path -Path $destinationPath -Parent
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destinationPath -Force
        $copied++
    }

    return $copied
}

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "ERROR: This script must be run as Administrator." -ForegroundColor Red
    Write-Host "Right-click PowerShell and select 'Run as administrator', then re-run this script." -ForegroundColor Yellow
    exit 1
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Split-Path -Parent $scriptDir

$addinsRoot = Get-RevitAddinsRoot -RevitYear $RevitYear
$staleAddinsRoot = Get-StaleRevitAddinsRoot -RevitYear $RevitYear
$installDir = Join-Path $addinsRoot "KanizuRevit"
$addinTemplate = Join-Path $scriptDir "KanizuRevit.addin.template"
$addinFile = Join-Path $addinsRoot "KanizuRevit.addin"

$legacyInstallDir = Join-Path $addinsRoot "RevitGeoSuite"
$legacyAddinFile = Join-Path $addinsRoot "RevitGeoSuite.addin"
$legacyUserAddinFile = if ($env:APPDATA) {
    Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitYear\RevitGeoSuite.addin"
} else {
    $null
}

$distDir = Join-Path $scriptDir "dist"
$deployDir = Get-RevitDeployDirectory -RepoRoot $repoRoot -RevitYear $RevitYear

if (Test-Path -LiteralPath (Join-Path $distDir "RevitGeoSuite.Shell.dll")) {
    Assert-RevitPayloadMatchesYear -SourceDirectory $distDir -RevitYear $RevitYear -RequireYearStamp
    $sourceDir = $distDir
    Write-Host "Using pre-built dist/ folder for Revit $RevitYear." -ForegroundColor Cyan
}
elseif (Test-Path -LiteralPath (Join-Path $deployDir "RevitGeoSuite.Shell.dll")) {
    Assert-RevitPayloadMatchesYear -SourceDirectory $deployDir -RevitYear $RevitYear
    $sourceDir = $deployDir
    Write-Host "Using build output from bin/Deploy/$RevitYear for Revit $RevitYear." -ForegroundColor Cyan
}
else {
    Write-Host "ERROR: No build output found for Revit $RevitYear." -ForegroundColor Red
    Write-Host "Run build-release.ps1 -RevitYear $RevitYear first, or build with -p:RevitYear=$RevitYear in Release configuration." -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path -LiteralPath $addinTemplate)) {
    Write-Error "Cannot find .addin template at $addinTemplate"
    exit 1
}

if (-not (Test-Path -LiteralPath $addinsRoot)) {
    New-Item -ItemType Directory -Path $addinsRoot -Force | Out-Null
}

# Cleanly remove any legacy RevitGeoSuite installation so both add-ins never load side-by-side,
# and any copy in the machine-wide folder this Revit year does not read.
$staleTargets = @(
    @{ Path = $legacyAddinFile; IsDir = $false; Label = "legacy manifest" },
    @{ Path = $legacyInstallDir; IsDir = $true; Label = "legacy add-in folder" },
    @{ Path = (Join-Path $staleAddinsRoot "KanizuRevit.addin"); IsDir = $false; Label = "misplaced manifest" },
    @{ Path = (Join-Path $staleAddinsRoot "KanizuRevit"); IsDir = $true; Label = "misplaced add-in folder" },
    @{ Path = (Join-Path $staleAddinsRoot "RevitGeoSuite.addin"); IsDir = $false; Label = "misplaced legacy manifest" },
    @{ Path = (Join-Path $staleAddinsRoot "RevitGeoSuite"); IsDir = $true; Label = "misplaced legacy add-in folder" }
)
if ($legacyUserAddinFile) {
    $staleTargets += @{ Path = $legacyUserAddinFile; IsDir = $false; Label = "legacy user manifest" }
}
foreach ($stale in $staleTargets) {
    if (Test-Path -LiteralPath $stale.Path) {
        Remove-Item -LiteralPath $stale.Path -Recurse:$stale.IsDir -Force
        Write-Host "Removed $($stale.Label): $($stale.Path)" -ForegroundColor Cyan
    }
}

if (Test-Path -LiteralPath $installDir) {
    Remove-Item -LiteralPath $installDir -Recurse -Force
}
New-Item -ItemType Directory -Path $installDir -Force | Out-Null

Write-Host "Copying files to $installDir ..." -ForegroundColor Cyan
$copied = Copy-PayloadFiles -SourceRoot $sourceDir -DestinationRoot $installDir

Set-Content -LiteralPath $addinFile -Value (Get-AddinManifestContents -TemplatePath $addinTemplate -RevitYear $RevitYear) -Encoding UTF8
Write-Host "Generated .addin manifest for Revit $RevitYear at $addinFile" -ForegroundColor Cyan

Write-Host ""
Write-Host "Installation complete! ($copied files installed)" -ForegroundColor Green
Write-Host "  Add-in folder: $installDir" -ForegroundColor Green
Write-Host "  Manifest:      $addinFile" -ForegroundColor Green
Write-Host ""
Write-Host "Please restart Revit $RevitYear to load Kanizu Revit." -ForegroundColor Yellow
