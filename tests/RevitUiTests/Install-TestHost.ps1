<#
.SYNOPSIS
  Installs the RevitGeoSuite UI test host into Revit on the build PC (user scope, no admin).

.DESCRIPTION
  Builds the test host and its loader, copies them to %LOCALAPPDATA%\RevitGeoSuite.TestHost\<year>,
  and writes RevitGeoSuite.TestHost.addin into %APPDATA%\Autodesk\Revit\Addins\<year>. The manifest
  names the loader, which is the DLL Revit's "Always Load" answer covers. The loader is copied only
  when it is missing or its FileVersion changed, so a test host rebuild never brings the prompt back.
  Revit loads it at every start, but it stays inert (no pipe, no event hooks, no DevTools port)
  unless that Revit was started with REVITGEOSUITE_UITEST=1, which Run-UiTests.ps1 -StartRevit sets
  for that one Revit. Build PC only. -Uninstall removes it.

.EXAMPLE
  .\Install-TestHost.ps1
  .\Install-TestHost.ps1 -Uninstall
#>
param(
    [string]$RevitYear = '2024',
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$addins = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitYear"
$target = Join-Path $env:LOCALAPPDATA "RevitGeoSuite.TestHost\$RevitYear"
$manifest = Join-Path $addins 'RevitGeoSuite.TestHost.addin'

if (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
    throw 'Close Revit first: it holds the test host DLL while it runs.'
}

if ($Uninstall) {
    Remove-Item $manifest -ErrorAction SilentlyContinue
    Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Removed the test host from Revit $RevitYear."
    return
}

. (Join-Path $PSScriptRoot 'Find-DotnetSdk.ps1')
$dotnet = Find-DotnetSdk
Write-Host "Using $dotnet"
foreach ($name in 'RevitGeoSuite.TestHost', 'RevitGeoSuite.TestHost.Loader') {
    & $dotnet build (Join-Path $PSScriptRoot "$name\$name.csproj") -c Release
    if ($LASTEXITCODE -ne 0) { throw "dotnet build of $name failed ($LASTEXITCODE)." }
}

New-Item -ItemType Directory -Force -Path $target, $addins | Out-Null
Copy-Item (Join-Path $repo 'bin\Tests\RevitGeoSuite.TestHost\RevitGeoSuite.TestHost.dll') $target -Force

# Revit remembers "Always Load" for the DLL the manifest names. Leave an installed loader of the
# same FileVersion untouched, byte for byte, so the answer Daniel gave once keeps holding.
$builtLoader = Join-Path $repo 'bin\Tests\RevitGeoSuite.TestHost.Loader\RevitGeoSuite.TestHost.Loader.dll'
$loader = Join-Path $target 'RevitGeoSuite.TestHost.Loader.dll'
$newVersion = (Get-Item $builtLoader).VersionInfo.FileVersion
$oldVersion = if (Test-Path $loader) { (Get-Item $loader).VersionInfo.FileVersion } else { $null }
if ($oldVersion -eq $newVersion) {
    Write-Host "Loader $newVersion already installed and left as it is: Revit will not ask again."
} else {
    Copy-Item $builtLoader $target -Force
    Write-Host "Installed loader $newVersion (was $(if ($oldVersion) { $oldVersion } else { 'none' })). Start Revit once by hand and choose Always Load for ""RevitGeoSuite test host""."
}

$template = Get-Content (Join-Path $PSScriptRoot 'RevitGeoSuite.TestHost\RevitGeoSuite.TestHost.addin.template') -Raw
[IO.File]::WriteAllText($manifest, $template.Replace('{ASSEMBLY}', [Security.SecurityElement]::Escape($loader)), [Text.UTF8Encoding]::new($false))
Write-Host "Installed: $manifest -> $loader, test host $(Join-Path $target 'RevitGeoSuite.TestHost.dll')"
Write-Host 'The test host stays inert until Run-UiTests.ps1 -StartRevit starts Revit for a run.'
