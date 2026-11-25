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

function Get-RevitAddinsRoot {
    param([Parameter(Mandatory = $true)][int]$RevitYear)

    if ($RevitYear -ge $FirstProgramFilesAddinsYear) {
        return "C:\Program Files\Autodesk\Revit\Addins\$RevitYear"
    }

    return "C:\ProgramData\Autodesk\Revit\Addins\$RevitYear"
}

function Get-StaleRevitAddinsRoot {
    <# The machine-wide folder the selected year does not read, where an earlier install may have left a copy. #>
    param([Parameter(Mandatory = $true)][int]$RevitYear)

    if ($RevitYear -ge $FirstProgramFilesAddinsYear) {
        return "C:\ProgramData\Autodesk\Revit\Addins\$RevitYear"
    }

    return "C:\Program Files\Autodesk\Revit\Addins\$RevitYear"
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
