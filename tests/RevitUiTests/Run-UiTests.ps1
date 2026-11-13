<#
.SYNOPSIS
  Runs the RevitGeoSuite UI tests against Revit on the build PC and prints where the report is.

.DESCRIPTION
  Builds the runner and runs it. The test host only wakes up in a Revit started with
  REVITGEOSUITE_UITEST=1, so -StartRevit starts Revit with that variable set for that Revit process
  alone. Close any other Revit first. The runner waits up to five minutes for the test host to
  answer. Every other argument goes to the runner (see README.md), for example:

  .\Run-UiTests.ps1 -StartRevit --model C:\dev\revit-test\auto\lumine-pristine.rvt --export-view 1F --excel

  Nothing of the tests stays where Revit or a roaming profile looks. -StartRevit copies the test
  host's manifest (from Install-TestHost.ps1's folder) into %APPDATA%\Autodesk\Revit\Addins\<year>,
  starts Revit, and deletes the manifest again as soon as the test host's pipe is up, or when Revit
  exits or 15 minutes pass. -RgsBuild <folder> does the same for a RevitGeoSuite build that is not
  installed (a PR build): it copies the folder to -RgsTarget (C:\dev\revit-test\rgs\<year>) and hands Revit a
  manifest for it for this one start. It refuses when another RevitGeoSuite manifest is installed,
  because Revit cannot load two add-ins with the same id.
#>
param(
    [switch]$StartRevit,
    [string]$RevitYear = '2024',
    [string]$TestHost = (Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitYear\RevitGeoSuite.TestHost"),
    [string]$RgsTarget = "C:\dev\revit-test\rgs\$RevitYear",
    [string]$RgsBuild = '',
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$RunnerArgs
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$addins = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitYear"
$hostManifest = Join-Path $addins 'RevitGeoSuite.TestHost.addin'
$rgsManifest = Join-Path $addins 'RevitGeoSuite.UiTest.addin'
$pipe = 'RevitGeoSuite.TestHost'

function Test-HostPipe {
    try { return [IO.Directory]::GetFiles('\\.\pipe\') -contains "\\.\pipe\$pipe" } catch { return $false }
}

# A run that was stopped half way may have left a manifest behind; never let one stay.
foreach ($stale in $hostManifest, $rgsManifest) {
    if (Test-Path $stale) { Remove-Item $stale -Force; Write-Host "Removed a manifest an earlier run left: $stale" }
}

if ($RgsBuild -and -not $StartRevit) { throw '-RgsBuild needs -StartRevit: the build is handed to the Revit this script starts.' }

if ($StartRevit) {
    if (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
        Write-Warning 'Revit is already running. The tests only reach a Revit that this script started; close it and run again if the runner cannot connect.'
    }
    else {
        $revit = Join-Path $env:ProgramFiles "Autodesk\Revit $RevitYear\Revit.exe"
        if (-not (Test-Path $revit)) { throw "Revit is not at $revit." }
        $staged = Join-Path $TestHost 'RevitGeoSuite.TestHost.addin'
        if (-not (Test-Path $staged)) { throw "No test host at $TestHost. Run Install-TestHost.ps1 first." }
        $handed = @($hostManifest)
        New-Item -ItemType Directory -Force -Path $addins | Out-Null
        if ($RgsBuild) {
            if (-not (Test-Path (Join-Path $RgsBuild 'RevitGeoSuite.Shell.dll'))) { throw "No RevitGeoSuite.Shell.dll in $RgsBuild." }
            $installed = @((Join-Path $env:ProgramData "Autodesk\Revit\Addins\$RevitYear\RevitGeoSuite.addin"), (Join-Path $addins 'RevitGeoSuite.addin')) | Where-Object { Test-Path $_ }
            if ($installed) { throw "RevitGeoSuite is installed ($($installed -join ', ')). Remove it before running a build with -RgsBuild: Revit cannot load two RevitGeoSuite add-ins." }
            $rgs = $RgsTarget
            if (Test-Path $rgs) { Remove-Item $rgs -Recurse -Force }
            New-Item -ItemType Directory -Force -Path $rgs | Out-Null
            Copy-Item (Join-Path $RgsBuild '*') $rgs -Recurse -Force
            $template = Get-Content (Join-Path $repo 'install\RevitGeoSuite.addin.template') -Raw
            [IO.File]::WriteAllText($rgsManifest, $template.Replace('C:\ProgramData\Autodesk\Revit\Addins\__REVIT_YEAR__\RevitGeoSuite\RevitGeoSuite.Shell.dll', [Security.SecurityElement]::Escape((Join-Path $rgs 'RevitGeoSuite.Shell.dll'))), [Text.UTF8Encoding]::new($false))
            if (-not (Select-String -Path $rgsManifest -SimpleMatch $rgs -Quiet)) { throw "The RevitGeoSuite manifest template changed; $rgsManifest does not name $rgs." }
            $handed += $rgsManifest
            Write-Host "RevitGeoSuite for this run: $RgsBuild, copied to $rgs."
        }

        Copy-Item $staged $hostManifest -Force
        try {
            $env:REVITGEOSUITE_UITEST = '1'   # inherited by this Revit only; removed again below
            try { $process = Start-Process -FilePath $revit -PassThru } finally { Remove-Item Env:REVITGEOSUITE_UITEST }
            Write-Host "Started $revit for UI tests. Waiting for the test host before taking the manifest away again..."
            $deadline = (Get-Date).AddMinutes(15)
            while (-not (Test-HostPipe) -and -not $process.HasExited -and (Get-Date) -lt $deadline) {
                Start-Sleep -Seconds 2
            }
        }
        finally {
            Remove-Item $handed -Force -ErrorAction SilentlyContinue
            Write-Host "Removed $($handed -join ', '): Revit has read it, and nothing is left for a roaming profile or a Revit started by hand."
        }
    }
}

. (Join-Path $PSScriptRoot 'Find-DotnetSdk.ps1')
$dotnet = Find-DotnetSdk
Write-Host "Using $dotnet"
& $dotnet build (Join-Path $PSScriptRoot 'RevitGeoSuite.UiTestRunner\RevitGeoSuite.UiTestRunner.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)." }

& $dotnet (Join-Path $repo 'bin\Tests\RevitGeoSuite.UiTestRunner\RevitGeoSuite.UiTestRunner.dll') --revit-year $RevitYear @RunnerArgs
exit $LASTEXITCODE
