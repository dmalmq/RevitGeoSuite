<#
.SYNOPSIS
  Installs the RevitGeoSuite UI test host into Revit on the build PC (user scope, no admin).

.DESCRIPTION
  Builds tests\RevitUiTests\RevitGeoSuite.TestHost, copies the DLL to
  %LOCALAPPDATA%\RevitGeoSuite.TestHost\<year> (so later builds are not blocked by Revit holding the
  file), and writes RevitGeoSuite.TestHost.addin into %APPDATA%\Autodesk\Revit\Addins\<year>.
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

$project = Join-Path $PSScriptRoot 'RevitGeoSuite.TestHost\RevitGeoSuite.TestHost.csproj'
dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)." }

$built = Join-Path $repo 'bin\Tests\RevitGeoSuite.TestHost\RevitGeoSuite.TestHost.dll'
New-Item -ItemType Directory -Force -Path $target, $addins | Out-Null
Copy-Item $built $target -Force

$assembly = Join-Path $target 'RevitGeoSuite.TestHost.dll'
$template = Get-Content (Join-Path $PSScriptRoot 'RevitGeoSuite.TestHost\RevitGeoSuite.TestHost.addin.template') -Raw
[IO.File]::WriteAllText($manifest, $template.Replace('{ASSEMBLY}', [Security.SecurityElement]::Escape($assembly)), [Text.UTF8Encoding]::new($false))
Write-Host "Installed: $manifest -> $assembly"
Write-Host 'Start Revit once by hand and choose Always Load for "RevitGeoSuite test host". It stays inert until Run-UiTests.ps1 -StartRevit starts Revit for a run.'
