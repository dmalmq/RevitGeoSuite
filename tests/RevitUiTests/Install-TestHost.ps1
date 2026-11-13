<#
.SYNOPSIS
  Installs the RevitGeoSuite UI test host on the build PC (no admin), where no other PC or account sees it.

.DESCRIPTION
  Builds the test host and its loader and copies them, with their manifest, to
  %APPDATA%\Autodesk\Revit\Addins\<year>\RevitGeoSuite.TestHost (or -Target). On the build PC
  Revit does not find add-in DLLs under AppData\Local, while it loads those under Roaming. Revit reads
  manifests only at the top of Addins\<year>, so the manifest in the subfolder is invisible to it.
  Run-UiTests.ps1 -StartRevit copies the manifest up just before it starts Revit and deletes it as
  soon as that Revit has loaded the test host. A roaming profile therefore carries only inert DLLs to
  another PC, never a manifest, and a Revit started by hand never loads the test host.

  The manifest names the loader, which is the DLL Revit's "Always Load" answer covers. The loader is
  copied only when it is missing or its FileVersion changed, so a test host rebuild never brings the
  prompt back. Even when loaded, it stays inert (no pipe, no event hooks, no DevTools port) unless
  that Revit was started with REVITGEOSUITE_UITEST=1. -Uninstall removes it.

  Earlier versions left the manifest at the top of Addins\<year> for good and the DLLs in
  %LOCALAPPDATA%; this script deletes both.

.EXAMPLE
  .\Install-TestHost.ps1
  .\Install-TestHost.ps1 -Uninstall
#>
param(
    [string]$RevitYear = '2024',
    [string]$Target = (Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitYear\RevitGeoSuite.TestHost"),
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$addins = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitYear"
$liveManifest = Join-Path $addins 'RevitGeoSuite.TestHost.addin'
$oldTarget = Join-Path $env:LOCALAPPDATA "RevitGeoSuite.TestHost\$RevitYear"
$manifest = Join-Path $Target 'RevitGeoSuite.TestHost.addin'

if (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
    throw 'Close Revit first: it holds the test host DLL while it runs.'
}

foreach ($old in $liveManifest, $oldTarget) {
    if (Test-Path $old) {
        Remove-Item $old -Recurse -Force
        Write-Host "Removed $old (the manifest no longer stays where Revit and the roaming profile see it)."
    }
}
$oldParent = Split-Path $oldTarget
if ((Test-Path $oldParent) -and -not (Get-ChildItem $oldParent -Force)) { Remove-Item $oldParent -Force }

if ($Uninstall) {
    Remove-Item $Target -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Removed the test host for Revit $RevitYear."
    return
}

. (Join-Path $PSScriptRoot 'Find-DotnetSdk.ps1')
$dotnet = Find-DotnetSdk
Write-Host "Using $dotnet"
foreach ($name in 'RevitGeoSuite.TestHost', 'RevitGeoSuite.TestHost.Loader') {
    & $dotnet build (Join-Path $PSScriptRoot "$name\$name.csproj") -c Release
    if ($LASTEXITCODE -ne 0) { throw "dotnet build of $name failed ($LASTEXITCODE)." }
}

New-Item -ItemType Directory -Force -Path $Target | Out-Null
Copy-Item (Join-Path $repo 'bin\Tests\RevitGeoSuite.TestHost\RevitGeoSuite.TestHost.dll') $Target -Force

# Revit remembers "Always Load" for the DLL the manifest names. Leave an installed loader of the
# same FileVersion untouched, byte for byte, so the answer Daniel gave once keeps holding.
$builtLoader = Join-Path $repo 'bin\Tests\RevitGeoSuite.TestHost.Loader\RevitGeoSuite.TestHost.Loader.dll'
$loader = Join-Path $Target 'RevitGeoSuite.TestHost.Loader.dll'
$newVersion = (Get-Item $builtLoader).VersionInfo.FileVersion
$oldVersion = if (Test-Path $loader) { (Get-Item $loader).VersionInfo.FileVersion } else { $null }
if ($oldVersion -eq $newVersion) {
    Write-Host "Loader $newVersion already installed and left as it is: Revit will not ask again."
} else {
    Copy-Item $builtLoader $Target -Force
    Write-Host "Installed loader $newVersion (was $(if ($oldVersion) { $oldVersion } else { 'none' })). At the next Run-UiTests.ps1 -StartRevit, choose Always Load for ""RevitGeoSuite test host"" once."
}

$template = Get-Content (Join-Path $PSScriptRoot 'RevitGeoSuite.TestHost\RevitGeoSuite.TestHost.addin.template') -Raw
[IO.File]::WriteAllText($manifest, $template.Replace('{ASSEMBLY}', [Security.SecurityElement]::Escape($loader)), [Text.UTF8Encoding]::new($false))
Write-Host "Installed $Target (loader, test host, and the manifest that Run-UiTests.ps1 -StartRevit hands to Revit for one start)."
