<#
.SYNOPSIS
    Where Revit loads machine-wide (all users) add-ins, per Revit year.
.DESCRIPTION
    Dot-source this file. install.ps1, uninstall.ps1 and build-release.ps1 share it, and
    RevitGeoSuite.iss mirrors the same rule for the setup EXE.

    Revit 2026 and earlier read C:\ProgramData\Autodesk\Revit\Addins\<year>.
    Revit 2027 and later read C:\Program Files\Autodesk\Revit\Addins\<year> and no longer read
    ProgramData, so an add-in left there for 2027 silently never loads.
#>

$FirstProgramFilesAddinsYear = 2027

function Get-ProgramFilesRoot {
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        return $env:ProgramFiles
    }

    return "C:\Program Files"
}

function Get-ProgramDataRoot {
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramData)) {
        return $env:ProgramData
    }

    return "C:\ProgramData"
}

function Get-RevitAddinsRoot {
    param([Parameter(Mandatory = $true)][int]$RevitYear)

    if ($RevitYear -ge $FirstProgramFilesAddinsYear) {
        return Join-Path (Get-ProgramFilesRoot) "Autodesk\Revit\Addins\$RevitYear"
    }

    return Join-Path (Get-ProgramDataRoot) "Autodesk\Revit\Addins\$RevitYear"
}

function Get-StaleRevitAddinsRoot {
    <# The machine-wide folder the selected year does not read, where an earlier install may have left a copy. #>
    param([Parameter(Mandatory = $true)][int]$RevitYear)

    if ($RevitYear -ge $FirstProgramFilesAddinsYear) {
        return Join-Path (Get-ProgramDataRoot) "Autodesk\Revit\Addins\$RevitYear"
    }

    return Join-Path (Get-ProgramFilesRoot) "Autodesk\Revit\Addins\$RevitYear"
}

function Get-RevitDeployDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][int]$RevitYear
    )

    return Join-Path $RepoRoot "bin\Deploy\$RevitYear"
}

function Get-ExpectedTargetFramework {
    param([Parameter(Mandatory = $true)][int]$RevitYear)

    if ($RevitYear -le 2024) {
        return ".NETFramework,Version=v4.8"
    }
    if ($RevitYear -eq 2025 -or $RevitYear -eq 2026) {
        return ".NETCoreApp,Version=v8.0"
    }
    if ($RevitYear -eq 2027) {
        return ".NETCoreApp,Version=v10.0"
    }

    throw "Unsupported RevitYear '$RevitYear'. Add its expected target framework in install/RevitAddinPaths.ps1."
}

function Get-AssemblyTargetFramework {
    <# Reads TargetFrameworkAttribute from a .NET PE assembly without loading it into the current runtime. #>
    param([Parameter(Mandatory = $true)][string]$AssemblyPath)

    if (-not (Test-Path -LiteralPath $AssemblyPath)) {
        throw "Assembly not found: $AssemblyPath"
    }

    $bytes = [System.IO.File]::ReadAllBytes($AssemblyPath)
    $ascii = [System.Text.Encoding]::ASCII.GetString($bytes)
    $regexMatches = [System.Text.RegularExpressions.Regex]::Matches(
        $ascii,
        '\x01\x00[\s\S](\.(?:NETFramework|NETCoreApp|NETStandard),Version=v\d+\.\d+(?:\.\d+)?)'
    )

    foreach ($match in $regexMatches) {
        $index = $match.Index
        $tfm = $match.Groups[1].Value
        if ($bytes[$index + 2] -ne $tfm.Length) {
            continue
        }

        $after = $index + 3 + $tfm.Length
        $hasNoNamedArgs = ($after + 1 -lt $bytes.Length) -and ($bytes[$after] -eq 0x00) -and ($bytes[$after + 1] -eq 0x00)
        $hasNamedStringArg = ($after + 3 -lt $bytes.Length) -and ($bytes[$after] -eq 0x01) -and ($bytes[$after + 1] -eq 0x00) -and ($bytes[$after + 2] -eq 0x54) -and ($bytes[$after + 3] -eq 0x0E)
        if ($hasNoNamedArgs -or $hasNamedStringArg) {
            return $tfm
        }
    }

    return $null
}

function Write-RevitYearStamp {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][int]$RevitYear
    )

    $stampPath = Join-Path $Directory "RevitYear.txt"
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($stampPath, "$RevitYear", $utf8NoBom)
    return $stampPath
}

function Assert-RevitPayloadMatchesYear {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDirectory,
        [Parameter(Mandatory = $true)][int]$RevitYear,
        [switch]$RequireYearStamp
    )

    $shellDll = Join-Path $SourceDirectory "RevitGeoSuite.Shell.dll"
    if (-not (Test-Path -LiteralPath $shellDll)) {
        throw "Expected build output was not found at $shellDll. Build with -p:RevitYear=$RevitYear first, or pass -SourceDirectory to a matching build output folder."
    }

    $stampPath = Join-Path $SourceDirectory "RevitYear.txt"
    if (Test-Path -LiteralPath $stampPath) {
        $rawStamp = Get-Content -LiteralPath $stampPath -Raw
        $stampedYear = if ($null -ne $rawStamp) { $rawStamp.Trim() } else { "" }
        if ($stampedYear -ne "$RevitYear") {
            throw "Payload in '$SourceDirectory' is stamped for Revit $stampedYear (RevitYear.txt), not Revit $RevitYear. Build or stage the payload for Revit $RevitYear first."
        }
    }
    elseif ($RequireYearStamp) {
        throw "Payload in '$SourceDirectory' is missing RevitYear.txt. Run build-release.ps1 -RevitYear $RevitYear first."
    }

    $expectedTfm = Get-ExpectedTargetFramework -RevitYear $RevitYear
    $actualTfm = Get-AssemblyTargetFramework -AssemblyPath $shellDll
    if ([string]::IsNullOrWhiteSpace($actualTfm)) {
        throw "Could not read TargetFrameworkAttribute from '$shellDll'. Refusing to package or install an unverified payload for Revit $RevitYear."
    }
    if ($actualTfm -ne $expectedTfm) {
        throw "Payload target framework mismatch in '$shellDll': found '$actualTfm', but Revit $RevitYear requires '$expectedTfm'. Build with -p:RevitYear=$RevitYear."
    }
}

function Get-AddinManifestContents {
    param(
        [Parameter(Mandatory = $true)][string]$TemplatePath,
        [Parameter(Mandatory = $true)][int]$RevitYear
    )

    if (-not (Test-Path -LiteralPath $TemplatePath)) {
        throw "Cannot find .addin template at $TemplatePath."
    }

    return (Get-Content -LiteralPath $TemplatePath -Raw).Replace("__ADDINS_ROOT__", (Get-RevitAddinsRoot -RevitYear $RevitYear))
}
