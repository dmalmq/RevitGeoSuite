# RevitGeoSuite Installer

This folder supports two installation flows for a selected Revit year. The default target is Revit 2024.

1. `build-installer.ps1` (recommended): builds a real installer `.exe` with Inno Setup.
2. `install.ps1`: direct admin copy install for local/dev use.

## Build a setup EXE

Prerequisites:
- A built RevitGeoSuite payload in `bin/Deploy/` or another output folder
- Inno Setup 6 (`ISCC.exe`)

Command:

```powershell
pwsh ./install/build-installer.ps1 -RevitYear 2024
```

Optional parameters:

```powershell
pwsh ./install/build-installer.ps1 -RevitYear <year> -SourceDirectory "C:\path\to\build\output" -Version <version> -IsccPath "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

Output:
- `install/output/RevitGeoSuite-Setup-<year>-<version>.exe`

## Stage release files

To regenerate `install/dist` from an existing build output:

```powershell
pwsh ./install/build-release.ps1 -RevitYear 2024
```

## Installer behavior

- Installs add-in payload to:
  - `C:\ProgramData\Autodesk\Revit\Addins\<year>\RevitGeoSuite\`
- Installs manifest to:
  - `C:\ProgramData\Autodesk\Revit\Addins\<year>\RevitGeoSuite.addin`
- Registers a normal uninstall entry in Windows Apps/Programs.
- Requires admin rights.

## Notes

- Revit must be restarted after install or uninstall.
- If the selected Revit year is not detected, the installer prompts for confirmation before continuing.
