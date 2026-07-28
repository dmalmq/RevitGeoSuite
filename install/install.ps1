<#
.SYNOPSIS
    Installs the RevitGeoSuite add-in for the selected Revit year (all users).
.DESCRIPTION
    Copies the add-in payload and manifest to the system-wide Revit add-ins folder
    for the selected Revit year.
    Must be run as Administrator.

    The script looks for build output in this order:
      1. install/dist/ folder (from build-release.ps1)
      2. bin/Deploy/ (direct build output)
#>
param(
    [string]$RevitYear = "2024"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$excludedFilePatterns = @(
    ".msCoverageSourceRootsMapping*",
    "*.Tests.dll",
    "*.Tests.dll.config",
    "*.Tests.exe",
    "*.Tests.pdb",
    "*.pdb",
    "Castle.Core.dll",
    "Microsoft.TestPlatform.*",
    "Microsoft.VisualStudio.CodeCoverage*",
    "Microsoft.VisualStudio.TestPlatform.ObjectModel*",
    "Moq.dll",
    "RevitGeoSuite.addin",
    "xunit*"
)

function Get-AddinManifestContents {
    param(
        [Parameter(Mandatory = $true)][string]$TemplatePath,
        [Parameter(Mandatory = $true)][string]$TargetRevitYear
    )

    if (-not (Test-Path -LiteralPath $TemplatePath)) {
        throw "Cannot find .addin template at $TemplatePath."
    }

    return (Get-Content -LiteralPath $TemplatePath -Raw).Replace("__REVIT_YEAR__", $TargetRevitYear)
}

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

$addinsRoot = "C:\ProgramData\Autodesk\Revit\Addins\$RevitYear"
$installDir = Join-Path $addinsRoot "RevitGeoSuite"
$addinTemplate = Join-Path $scriptDir "RevitGeoSuite.addin.template"
$addinFile = Join-Path $addinsRoot "RevitGeoSuite.addin"

$distDir = Join-Path $scriptDir "dist"
$deployDir = Join-Path $repoRoot "bin\Deploy"

if (Test-Path -LiteralPath (Join-Path $distDir "RevitGeoSuite.Shell.dll")) {
    $sourceDir = $distDir
    Write-Host "Using pre-built dist/ folder for Revit $RevitYear." -ForegroundColor Cyan
}
elseif (Test-Path -LiteralPath (Join-Path $deployDir "RevitGeoSuite.Shell.dll")) {
    $sourceDir = $deployDir
    Write-Host "Using build output from bin/Deploy for Revit $RevitYear." -ForegroundColor Cyan
}
else {
    Write-Host "ERROR: No build output found." -ForegroundColor Red
    Write-Host "Run build-release.ps1 -RevitYear $RevitYear first, or build the shell project in Release configuration." -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path -LiteralPath $addinTemplate)) {
    Write-Error "Cannot find .addin template at $addinTemplate"
    exit 1
}

if (-not (Test-Path -LiteralPath $addinsRoot)) {
    New-Item -ItemType Directory -Path $addinsRoot -Force | Out-Null
}

if (Test-Path -LiteralPath $installDir) {
    Remove-Item -LiteralPath $installDir -Recurse -Force
}
New-Item -ItemType Directory -Path $installDir -Force | Out-Null

Write-Host "Copying files to $installDir ..." -ForegroundColor Cyan
$copied = Copy-PayloadFiles -SourceRoot $sourceDir -DestinationRoot $installDir

Set-Content -LiteralPath $addinFile -Value (Get-AddinManifestContents -TemplatePath $addinTemplate -TargetRevitYear $RevitYear) -Encoding UTF8
Write-Host "Generated .addin manifest for Revit $RevitYear at $addinFile" -ForegroundColor Cyan

Write-Host ""
Write-Host "Installation complete! ($copied files installed)" -ForegroundColor Green
Write-Host "  Add-in folder: $installDir" -ForegroundColor Green
Write-Host "  Manifest:      $addinFile" -ForegroundColor Green
Write-Host ""
Write-Host "Please restart Revit $RevitYear to load the add-in." -ForegroundColor Yellow
