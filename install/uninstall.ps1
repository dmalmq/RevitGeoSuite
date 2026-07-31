<#
.SYNOPSIS
    Uninstalls the RevitGeoSuite add-in for the selected Revit year.
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
$installDir = Join-Path $addinsRoot "RevitGeoSuite"
$addinFile = Join-Path $addinsRoot "RevitGeoSuite.addin"

$removed = $false

if (Test-Path -LiteralPath $installDir) {
    Remove-Item -LiteralPath $installDir -Recurse -Force
    Write-Host "Removed: $installDir" -ForegroundColor Cyan
    $removed = $true
}
else {
    Write-Host "Add-in folder not found: $installDir" -ForegroundColor Yellow
}

if (Test-Path -LiteralPath $addinFile) {
    Remove-Item -LiteralPath $addinFile -Force
    Write-Host "Removed: $addinFile" -ForegroundColor Cyan
    $removed = $true
}
else {
    Write-Host ".addin manifest not found: $addinFile" -ForegroundColor Yellow
}

Write-Host ""
if ($removed) {
    Write-Host "Uninstall complete." -ForegroundColor Green
    Write-Host "Please restart Revit $RevitYear to fully unload the add-in." -ForegroundColor Yellow
}
else {
    Write-Host "Nothing to uninstall. RevitGeoSuite does not appear to be installed." -ForegroundColor Yellow
}
