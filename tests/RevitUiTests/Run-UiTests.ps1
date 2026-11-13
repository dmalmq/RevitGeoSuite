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
  exits or -StartMinutes (5) pass. If the test host is not up by then, or Revit exits first (a modal
  start-up prompt is the usual cause), it writes a FAIL report with the journal's dialog lines to a run
  folder of its own, closes Revit and exits 1.

  -RgsBuild <folder> runs a RevitGeoSuite build that is not installed (a PR build) with no prompt: it
  copies the folder to -RgsTarget (C:\dev\revit-test\rgs\<year>) and the test host starts
  RevitGeoSuite.Shell.App from there inside Revit (REVITGEOSUITE_UITEST_RGS, this Revit only). Revit
  sees no manifest for it, so it never asks Always Load. It refuses when RevitGeoSuite is installed,
  because two copies would build the same ribbon.

  The export page's remembered settings (%APPDATA%\RevitGeoSuite\FloorPlanExport\settings.json) are
  empty for the run and go back as they were on exit, so every run sees the page's defaults.

  -PyRevitExtension <folder> adds a pyRevit extension folder to userextensions in
  %APPDATA%\pyRevit\pyRevit_config.ini for the same window and takes it out again. pyRevit reads the
  list at Revit start; a reload in that Revit drops the folder. Every run also takes entries under
  C:\dev\rgs-uitests and C:\dev\revit-test out of that roaming file, since those exist only here.
#>
param(
    [switch]$StartRevit,
    [string]$RevitYear = '2024',
    [string]$TestHost = (Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitYear\RevitGeoSuite.TestHost"),
    [string]$RgsTarget = "C:\dev\revit-test\rgs\$RevitYear",
    [string]$RgsBuild = '',
    [string]$PyRevitExtension = '',
    [int]$StartMinutes = 5,
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

# Revit never brought the test host up: write a FAIL report in a run folder of its own, with the
# dialogs the journal shows (a modal prompt is the usual cause), so the run is never silent.
function Write-StartFailure([string]$Why, [datetime]$Since) {
    $folder = Join-Path 'C:\dev\revit-test\auto' (Get-Date -Format 'yyyyMMdd-HHmmss')
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $journals = Join-Path $env:LOCALAPPDATA "Autodesk\Revit\Autodesk Revit $RevitYear\Journals"
    $lines = @(Get-ChildItem $journals -Filter 'journal.*.txt' -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -ge $Since.AddMinutes(-1) } |
        ForEach-Object {
            $file = $_
            $stream = [IO.FileStream]::new($file.FullName, 'Open', 'Read', 'ReadWrite, Delete')
            try { $text = [IO.StreamReader]::new($stream).ReadToEnd() } finally { $stream.Dispose() }
            $text -split "`n" | Where-Object { $_ -match 'Dialog' } | ForEach-Object { "$($file.Name): $($_.Trim())" }
        } | Select-Object -Last 20)
    $report = @(
        '# RevitGeoSuite UI test report', '',
        "RUN STOPPED: $Why.", '',
        '**FAIL**: 0 passed, 1 failed, 0 not run.', '',
        '| Step | Title | Result |', '|---|---|---|',
        '| START | Revit starts and the test host answers | **FAIL** |', '',
        '## START. Revit starts and the test host answers: **FAIL**', '',
        "- FAILED: $Why.",
        "- Dialogs in the journal since the start: $(if ($lines.Count -eq 0) { 'none' } else { '' })"
    ) + ($lines | ForEach-Object { "  - ``$_``" })
    $path = Join-Path $folder 'report.md'
    [IO.File]::WriteAllLines($path, [string[]]$report, [Text.UTF8Encoding]::new($false))
    Write-Host "Run stopped: $Why."
    Write-Host "Report: $path"
    return $path
}

$pyRevitConfig = Join-Path $env:APPDATA 'pyRevit\pyRevit_config.ini'

# pyRevit keeps userextensions as a JSON-style list on one line of its INI file.
function Set-PyRevitExtensions([scriptblock]$Change) {
    if (-not (Test-Path $pyRevitConfig)) { return }
    $text = [IO.File]::ReadAllText($pyRevitConfig)
    $match = [regex]::Match($text, '(?m)^userextensions[ \t]*=[ \t]*(\[.*\])[ \t]*(?=\r?$)')
    $list = @(if ($match.Success) { ConvertFrom-Json $match.Groups[1].Value | ForEach-Object { $_ } })
    $new = @(& $Change $list)
    if ((ConvertTo-Json -InputObject $list -Compress) -eq (ConvertTo-Json -InputObject $new -Compress)) { return }
    $line = 'userextensions = ' + $(if ($new.Count -eq 0) { '[]' } else { ConvertTo-Json -InputObject $new -Compress })
    if ($match.Success) {
        $text = $text.Remove($match.Index, $match.Length).Insert($match.Index, $line)
    } elseif ($text -match '(?m)^\[core\][ \t]*(?=\r?$)') {
        $text = [regex]::new('(?m)^\[core\][ \t]*(?=\r?$)').Replace($text, { param($m) "[core]`r`n$line" }, 1)
    } else {
        $text = $text.TrimEnd() + "`r`n[core]`r`n$line`r`n"
    }
    [IO.File]::WriteAllText($pyRevitConfig, $text, [Text.UTF8Encoding]::new($false))
    Write-Host "pyRevit userextensions: $(ConvertTo-Json -InputObject $new -Compress)"
}

# The GeoPackage export remembers its last output folder (and profiles) in the roaming %APPDATA%. A test
# export would leave a C:\dev\revit-test folder there for every PC; the files go back as they were on exit.
$exportSettings = @{}
foreach ($name in 'settings.json', 'profiles.json') {
    $file = Join-Path $env:APPDATA "RevitGeoSuite\FloorPlanExport\$name"
    $exportSettings[$file] = $null
    if (Test-Path $file) { $exportSettings[$file] = [IO.File]::ReadAllBytes($file) }
}
# A run starts the export page as a first-time user sees it: no remembered views, folder or options, so
# the default view choice (gis-model.json, else every GIS_ plan) is what the scenarios test. An empty
# object, not a missing file: without the file RevitGeoSuite would read the legacy RevitGeoExporter one.
# Profiles stay: the Cesium export uses the PC's first floor plan profile.
$runSettings = Join-Path $env:APPDATA 'RevitGeoSuite\FloorPlanExport\settings.json'
if ($StartRevit) {
    New-Item -ItemType Directory -Force -Path (Split-Path $runSettings) | Out-Null
    [IO.File]::WriteAllText($runSettings, '{}')
    Write-Host "Export settings for this run: empty ($runSettings); the old file goes back on exit."
}

# Test folders (C:\dev\rgs-uitests, C:\dev\revit-test) exist only on the build PC; never leave one in the roaming list.
Set-PyRevitExtensions { param($list) $list | Where-Object { "$_" -notmatch '^C:\\dev\\(rgs-uitests|revit-test)(\\|$)' } }

if ($PyRevitExtension -and -not $StartRevit) { throw '-PyRevitExtension needs -StartRevit: the folder is handed to the Revit this script starts.' }
if ($RgsBuild -and -not $StartRevit) { throw '-RgsBuild needs -StartRevit: the build is handed to the Revit this script starts.' }

$process = $null
$startFailure = $null
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
            Write-Host "RevitGeoSuite for this run: $RgsBuild, copied to $rgs; the test host starts it inside Revit."
        }

        if ($PyRevitExtension) {
            $extension = (Resolve-Path $PyRevitExtension).Path
            Set-PyRevitExtensions { param($list) @($list) + $extension }
        }

        Copy-Item $staged $hostManifest -Force
        $startedAt = Get-Date
        try {
            $env:REVITGEOSUITE_UITEST = '1'   # inherited by this Revit only; removed again below
            if ($RgsBuild) { $env:REVITGEOSUITE_UITEST_RGS = $rgs }
            # Revit resolves bare file names (such as a detached model's default name) against its working directory: keep that in the test folder.
            New-Item -ItemType Directory -Force -Path 'C:\dev\revit-test' | Out-Null
            try { $process = Start-Process -FilePath $revit -WorkingDirectory 'C:\dev\revit-test' -PassThru } finally { Remove-Item Env:REVITGEOSUITE_UITEST; Remove-Item Env:REVITGEOSUITE_UITEST_RGS -ErrorAction SilentlyContinue }
            Write-Host "Started $revit for UI tests. Waiting for the test host before taking the manifest away again..."
            $deadline = (Get-Date).AddMinutes($StartMinutes)
            while (-not (Test-HostPipe) -and -not $process.HasExited -and (Get-Date) -lt $deadline) {
                Start-Sleep -Seconds 2
            }
            if (-not (Test-HostPipe)) {
                $why = if ($process.HasExited) { "Revit exited (code $($process.ExitCode)) before the test host was up" } else { "the test host was not up $StartMinutes minutes after Revit started" }
                $startFailure = Write-StartFailure $why $startedAt
            }
        }
        finally {
            Remove-Item $handed -Force -ErrorAction SilentlyContinue
            if ($PyRevitExtension) { Set-PyRevitExtensions { param($list) $list | Where-Object { $_ -ne $extension } } }
            Write-Host "Removed $($handed -join ', '): Revit has read it, and nothing is left for a roaming profile or a Revit started by hand."
        }
    }
}

. (Join-Path $PSScriptRoot 'Find-DotnetSdk.ps1')
$exitCode = 1
try {
    if (-not $startFailure) {
        $dotnet = Find-DotnetSdk
        Write-Host "Using $dotnet"
        & $dotnet build (Join-Path $PSScriptRoot 'RevitGeoSuite.UiTestRunner\RevitGeoSuite.UiTestRunner.csproj') -c Release
        if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)." }

        & $dotnet (Join-Path $repo 'bin\Tests\RevitGeoSuite.UiTestRunner\RevitGeoSuite.UiTestRunner.dll') --revit-year $RevitYear @RunnerArgs
        $exitCode = $LASTEXITCODE
    }
}
finally {
    # The Revit this script started is the tests' own: close it on every way out, without saving.
    # Stopping the process is the only close that cannot meet a "save changes?" prompt.
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        Write-Host "Closed the test Revit (process $($process.Id)) without saving."
    }
    foreach ($file in @($exportSettings.Keys)) {
        if ($null -eq $exportSettings[$file]) { Remove-Item $file -Force -ErrorAction SilentlyContinue }
        else { [IO.File]::WriteAllBytes($file, $exportSettings[$file]) }
    }
    Write-Host "Put the export's remembered settings back as they were: $($exportSettings.Keys -join ', ')."
}

exit $exitCode
