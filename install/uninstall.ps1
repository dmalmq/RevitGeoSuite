<#
.SYNOPSIS
    Uninstalls the Kanizu Revit add-in (and any legacy RevitGeoSuite install) for the selected Revit year.
.DESCRIPTION
    Removes the add-in payload and manifest from the system-wide Revit add-ins folder
    for the selected Revit year.
    Must be run as Administrator.
#>
param(
    [string]$RevitYear = "2024"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "ERROR: This script must be run as Administrator." -ForegroundColor Red
    Write-Host "Right-click PowerShell and select 'Run as administrator', then re-run this script." -ForegroundColor Yellow
    exit 1
}

$addinsRoot = "C:\ProgramData\Autodesk\Revit\Addins\$RevitYear"
$targets = @(
    @{ Path = (Join-Path $addinsRoot "KanizuRevit"); IsDir = $true; Label = "Add-in folder" },
    @{ Path = (Join-Path $addinsRoot "KanizuRevit.addin"); IsDir = $false; Label = ".addin manifest" },
    @{ Path = (Join-Path $addinsRoot "RevitGeoSuite"); IsDir = $true; Label = "Legacy add-in folder" },
    @{ Path = (Join-Path $addinsRoot "RevitGeoSuite.addin"); IsDir = $false; Label = "Legacy .addin manifest" }
)

if ($env:APPDATA) {
    $userAddinsRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitYear"
    $targets += @{ Path = (Join-Path $userAddinsRoot "KanizuRevit.addin"); IsDir = $false; Label = "User .addin manifest" }
    $targets += @{ Path = (Join-Path $userAddinsRoot "RevitGeoSuite.addin"); IsDir = $false; Label = "Legacy user .addin manifest" }
}

$removed = $false

foreach ($target in $targets) {
    if (Test-Path -LiteralPath $target.Path) {
        if ($target.IsDir) {
            Remove-Item -LiteralPath $target.Path -Recurse -Force
        }
        else {
            Remove-Item -LiteralPath $target.Path -Force
        }
        Write-Host "Removed $($target.Label): $($target.Path)" -ForegroundColor Cyan
        $removed = $true
    }
}

Write-Host ""
if ($removed) {
    Write-Host "Uninstall complete." -ForegroundColor Green
    Write-Host "Please restart Revit $RevitYear to fully unload the add-in." -ForegroundColor Yellow
}
else {
    Write-Host "Nothing to uninstall. Kanizu Revit does not appear to be installed." -ForegroundColor Yellow
}
