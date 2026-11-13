<#
.SYNOPSIS
  Runs the RevitGeoSuite UI tests against Revit on the build PC and prints where the report is.

.DESCRIPTION
  Builds the runner and runs it. With -StartRevit, starts Revit first when it is not running; the
  runner waits up to five minutes for the test host to answer. Every other argument goes to the
  runner (see README.md), for example:

  .\Run-UiTests.ps1 -StartRevit --model C:\dev\revit-test\auto\lumine-pristine.rvt --export-view 1F --excel
#>
param(
    [switch]$StartRevit,
    [string]$RevitYear = '2024',
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$RunnerArgs
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

if ($StartRevit -and -not (Get-Process -Name Revit -ErrorAction SilentlyContinue)) {
    $revit = Join-Path $env:ProgramFiles "Autodesk\Revit $RevitYear\Revit.exe"
    if (-not (Test-Path $revit)) { throw "Revit is not at $revit." }
    Start-Process -FilePath $revit
    Write-Host "Started $revit."
}

dotnet build (Join-Path $PSScriptRoot 'RevitGeoSuite.UiTestRunner\RevitGeoSuite.UiTestRunner.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)." }

& dotnet (Join-Path $repo 'bin\Tests\RevitGeoSuite.UiTestRunner\RevitGeoSuite.UiTestRunner.dll') --revit-year $RevitYear @RunnerArgs
exit $LASTEXITCODE
