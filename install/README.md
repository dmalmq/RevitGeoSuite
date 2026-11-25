# Kanizu Revit Installer

This folder supports two installation flows for a selected Revit year. The default target is Revit 2024. Revit 2027 is also supported.

Build the payload for the same year you install: `dotnet build -c Release -p:RevitYear=<year>` (see `Directory.Build.props`). Revit 2024 loads a .NET Framework 4.8 build and Revit 2027 loads a .NET 10 build, so one payload never serves both years.

1. `build-installer.ps1` (recommended): builds a real installer `.exe` with Inno Setup.
2. `install.ps1`: direct admin copy install for local/dev use.

## Build a setup EXE

Prerequisites:
- A built Kanizu Revit payload in `bin/Deploy/<year>/` or another output folder
- Inno Setup 6 (`ISCC.exe`)

Command:

```powershell
pwsh ./install/build-installer.ps1 -RevitYear 2024
pwsh ./install/build-installer.ps1 -RevitYear 2027
```

Optional parameters:

```powershell
pwsh ./install/build-installer.ps1 -RevitYear <year> -SourceDirectory "C:\path\to\build\output" -Version <version> -IsccPath "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

Output:
- `install/output/KanizuRevit-Setup-<year>-<version>.exe`

## Stage release files

To regenerate `install/dist` from an existing build output:

```powershell
pwsh ./install/build-release.ps1 -RevitYear 2024
```

## Installer behavior

Revit 2027 moved the machine-wide add-ins folder from ProgramData to Program Files, and no longer reads ProgramData. The installers follow the selected year (`RevitAddinPaths.ps1`, mirrored in `RevitGeoSuite.iss`):

| Revit year | Add-ins folder (`<root>`) |
|---|---|
| 2026 and earlier (2024) | `C:\ProgramData\Autodesk\Revit\Addins\<year>\` |
| 2027 and later | `C:\Program Files\Autodesk\Revit\Addins\<year>\` |

- Installs the add-in payload to `<root>\KanizuRevit\` and the manifest to `<root>\KanizuRevit.addin`.
- Cleanly removes any legacy `RevitGeoSuite.addin` and `RevitGeoSuite\` folder during install/upgrade so both add-ins never load side-by-side.
- Removes any `KanizuRevit` or `RevitGeoSuite` copy for the same year from the other machine-wide folder, such as a Revit 2027 copy left in ProgramData.
- `uninstall.ps1` removes the add-in from both folders.
- Registers a normal uninstall entry in Windows Apps/Programs.
- Requires admin rights.

## Notes

- Revit must be restarted after install or uninstall.
- If the selected Revit year is not detected, the installer prompts for confirmation before continuing.
