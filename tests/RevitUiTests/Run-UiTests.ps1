<#
.SYNOPSIS
  Runs the RevitGeoSuite UI tests against Revit on the build PC and prints where the report is.

.DESCRIPTION
  Builds the runner and runs it. The test host only wakes up in a Revit started with
  REVITGEOSUITE_UITEST=1, so -StartRevit starts Revit with that variable set for that Revit process
  alone. Close any other Revit first. The runner waits up to five minutes for the test host to
  answer. Every other argument goes to the runner (see README.md), for example:

  .\Run-UiTests.ps1 -StartRevit --model C:\dev\revit-test\auto\lumine-pristine.rvt --export-view 1F --excel
#>
param(
    [switch]$StartRevit,
    [string]$RevitYear = '2024',
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$RunnerArgs
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

if ($StartRevit) {
    if (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
        Write-Warning 'Revit is already running. The tests only reach a Revit that this script started; close it and run again if the runner cannot connect.'
    }
    else {
        $revit = Join-Path $env:ProgramFiles "Autodesk\Revit $RevitYear\Revit.exe"
        if (-not (Test-Path $revit)) { throw "Revit is not at $revit." }
        $env:REVITGEOSUITE_UITEST = '1'   # inherited by this Revit only; removed again below
        try { Start-Process -FilePath $revit } finally { Remove-Item Env:REVITGEOSUITE_UITEST }
        Write-Host "Started $revit for UI tests."
    }
}

. (Join-Path $PSScriptRoot 'Find-DotnetSdk.ps1')
$dotnet = Find-DotnetSdk
Write-Host "Using $dotnet"
& $dotnet build (Join-Path $PSScriptRoot 'RevitGeoSuite.UiTestRunner\RevitGeoSuite.UiTestRunner.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)." }

& $dotnet (Join-Path $repo 'bin\Tests\RevitGeoSuite.UiTestRunner\RevitGeoSuite.UiTestRunner.dll') --revit-year $RevitYear @RunnerArgs
exit $LASTEXITCODE
