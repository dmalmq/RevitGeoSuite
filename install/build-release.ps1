<#
.SYNOPSIS
    Stages a Kanizu Revit release payload from build output into install/dist/.
.DESCRIPTION
    Run this script after building the solution or shell project.
    The resulting dist/ folder contains only the production payload required by the installer.
#>
param(
    [string]$RevitYear = "2024",
    [string]$SourceDirectory = ""
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

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Split-Path -Parent $scriptDir
$distDir = Join-Path $scriptDir "dist"
$addinTemplate = Join-Path $scriptDir "KanizuRevit.addin.template"
$generatedAddinManifest = Join-Path $distDir "KanizuRevit.addin"

if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $SourceDirectory = Get-RevitDeployDirectory -RepoRoot $repoRoot -RevitYear $RevitYear
}

$sourceDirectoryFull = [System.IO.Path]::GetFullPath($SourceDirectory)
Assert-RevitPayloadMatchesYear -SourceDirectory $sourceDirectoryFull -RevitYear $RevitYear

if (Test-Path -LiteralPath $distDir) {
    Remove-Item -LiteralPath $distDir -Recurse -Force
}
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

$copiedCount = Copy-PayloadFiles -SourceRoot $sourceDirectoryFull -DestinationRoot $distDir
Set-Content -LiteralPath $generatedAddinManifest -Value (Get-AddinManifestContents -TemplatePath $addinTemplate -RevitYear $RevitYear) -Encoding UTF8
$yearMarkerPath = Write-RevitYearStamp -Directory $distDir -RevitYear $RevitYear

$fileCount = (Get-ChildItem -LiteralPath $distDir -Recurse -File).Count
Write-Host ""
Write-Host "Release staging complete. $fileCount files copied to:" -ForegroundColor Green
Write-Host "  Source: $sourceDirectoryFull" -ForegroundColor Green
Write-Host "  $distDir" -ForegroundColor Green
Write-Host "  Generated manifest: $generatedAddinManifest" -ForegroundColor Green
Write-Host "  Year marker: $yearMarkerPath" -ForegroundColor Green
Write-Host "  Payload files copied: $copiedCount" -ForegroundColor Green
Write-Host ""
Write-Host "Next step options:" -ForegroundColor Yellow
Write-Host "  1) End-user installer EXE: run build-installer.ps1 -RevitYear $RevitYear" -ForegroundColor Yellow
Write-Host "  2) Direct admin install:  run install.ps1 -RevitYear $RevitYear" -ForegroundColor Yellow
