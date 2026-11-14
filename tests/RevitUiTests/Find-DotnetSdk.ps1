# Dot-sourced by Install-TestHost.ps1 and Run-UiTests.ps1. Returns the path of a dotnet.exe that has
# a .NET 8 or later SDK. A bare `dotnet` on PATH can be a runtime-only install (Program Files) while
# the SDK lives in %USERPROFILE%\.dotnet, so each candidate is asked for its SDKs.
function Find-DotnetSdk {
    $candidates = @()
    if ($env:DOTNET_ROOT) { $candidates += Join-Path $env:DOTNET_ROOT 'dotnet.exe' }
    $candidates += Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    $candidates += @(Get-Command dotnet -All -ErrorAction SilentlyContinue | ForEach-Object { $_.Source })
    $candidates += Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'

    $tried = @()
    foreach ($exe in ($candidates | Where-Object { $_ } | Select-Object -Unique)) {
        if (-not (Test-Path $exe)) { continue }
        $sdks = @(& $exe --list-sdks 2>$null)
        $tried += "$exe ($(if ($sdks.Count) { ($sdks | ForEach-Object { ($_ -split ' ')[0] }) -join ', ' } else { 'no SDK' }))"
        if ($sdks | Where-Object { [int](($_ -split '\.')[0]) -ge 8 }) { return $exe }
    }

    throw ("No .NET 8 or later SDK found. Tried: " + ($(if ($tried.Count) { $tried -join '; ' } else { 'no dotnet.exe' })) +
        ". Install the .NET 8 SDK (https://dot.net), or set DOTNET_ROOT to the folder that holds it.")
}
