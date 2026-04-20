<div align="center">

# RevitGeoSuite

### Modular Revit Add-in for Georeferencing, Mesh Inspection & Geospatial Export

<p>
A suite of independent tools built on a shared geospatial core,<br />
designed to close the gap between BIM authoring in Revit and downstream spatial data workflows.<br />
From coordinate system setup to CityGML and 3D Tiles export — without the legacy conversion chains.
</p>

<p>
  <img src="https://img.shields.io/badge/Platform-Revit_2024-0f766e?style=for-the-badge&logo=autodesk&logoColor=ffffff" />
  <img src="https://img.shields.io/badge/.NET_Framework-4.8-68217a?style=for-the-badge&logo=dotnet&logoColor=ffffff" />
  <img src="https://img.shields.io/badge/Language-C%23_12-3178c6?style=for-the-badge&logo=csharp&logoColor=ffffff" />
</p>

<p>
  <img src="https://img.shields.io/badge/UI-WPF_+_WebView2-393552?style=flat-square" />
  <img src="https://img.shields.io/badge/Maps-Leaflet_+_OSM-9ccfd8?style=flat-square" />
  <img src="https://img.shields.io/badge/CRS-ProjNet-907aa9?style=flat-square" />
  <img src="https://img.shields.io/badge/Focus-Japanese_CRS_&_PLATEAU-f6c177?style=flat-square" />
</p>

</div>

---

## About

Revit handles geometry well but leaves georeferencing, coordinate systems, and spatial export as an exercise for the user. Setup mistakes propagate silently into every downstream deliverable. RevitGeoSuite provides a visual, guided workflow for coordinate reference system selection, map-based placement, and export — with validation at every step.

The suite is especially focused on Japanese coordinate reference systems and [PLATEAU](https://www.mlit.go.jp/plateau/) digital twin interoperability, including JIS X 0410 mesh code inspection and CityGML tile alignment.

---

## Modules

| Module | Description |
|--------|-------------|
| **Georeference** | CRS selection with Japanese presets, OSM map-based point picking, placement preview, and atomic apply |
| **Mesh Inspector** | Japanese mesh code lookup, boundary calculation, and 8-neighbor display as GeoJSON overlay |
| **Validation** | Project health checks for coordinate setup, export readiness, and suspicious value warnings |
| **PLATEAU Import** | PLATEAU context data import with codelist parsing and tile indexing |
| **3D Tiles Export** | Export pipeline for 3D Tiles format |
| **CityGML Export** | CityGML export with semantic mapping |

---

## Architecture

Modules are independent over a shared foundation. No module depends on another — they communicate through a small, stable shared state contract (`GeoProjectInfo`).

```text
┌─────────────────────────────────────────────────┐
│ Shell (Ribbon UI + Module Registration)         │
├─────────┬──────────┬──────────┬────────┬────────┤
│ Geo     │ Mesh     │ PLATEAU  │ 3D     │ CityGML│
│Reference│ Inspector│ Import   │ Tiles  │ Export │
├─────────┴──────────┴──────────┴────────┴────────┤
│  SharedUI (WPF)  │  RevitInterop (API wrappers) │
├──────────────────┴──────────────────────────────┤
│ Core (CRS, Transforms, Mesh, Metadata)          │
└─────────────────────────────────────────────────┘
```

---

## Project Structure

```text
RevitGeoSuite/
├── src/
│   ├── RevitGeoSuite.Core/              # Generic geo foundation (no Revit dependency)
│   ├── RevitGeoSuite.Core.Plateau/      # PLATEAU-specific shared logic
│   ├── RevitGeoSuite.RevitInterop/      # Revit API wrappers (no UI)
│   ├── RevitGeoSuite.SharedUI/          # Reusable WPF controls
│   ├── RevitGeoSuite.Shell/             # Add-in entry point and ribbon setup
│   ├── RevitGeoSuite.Georeference/      # Georeferencing module
│   ├── RevitGeoSuite.MeshInspector/     # Mesh code inspector module
│   ├── RevitGeoSuite.Validation/        # Project validation module
│   ├── RevitGeoSuite.PlateauImport/     # PLATEAU context import
│   ├── RevitGeoSuite.Tiles3DExport/     # 3D Tiles export
│   └── RevitGeoSuite.CityGmlExport/     # CityGML export
├── tests/
│   ├── RevitGeoSuite.Core.Tests/
│   └── ...
└── docs/                                # Architecture and design documentation
```

---

## Prerequisites

- **Revit 2024**
- **.NET Framework 4.8 SDK**
- **Visual Studio 2022** (or later) with the ".NET desktop development" workload
- **WebView2 Runtime** (typically pre-installed on Windows 10/11)

## Build

```bash
git clone <repo-url>
cd RevitGeoSuite

# MSBuild (from VS Developer Command Prompt)
msbuild RevitGeoSuite.sln /p:Configuration=Release

# Or open RevitGeoSuite.sln in Visual Studio and build
```

All output DLLs are written to `bin/Deploy/`.

## Install

### Installer EXE

After building the solution, create a distributable installer `.exe` with:

```powershell
powershell -ExecutionPolicy Bypass -File .\install\build-installer.ps1
```

The installer EXE is written to `install/output/` and installs the add-in payload under `C:\ProgramData\Autodesk\Revit\Addins\2024\RevitGeoSuite\` with `RevitGeoSuite.addin` placed in `C:\ProgramData\Autodesk\Revit\Addins\2024\`.

### Manual Install

1. Build the solution
2. Copy `bin/Deploy/` contents to a folder (e.g. `C:\RevitGeoSuite\`)
3. Copy `RevitGeoSuite.addin` to `%AppData%\Autodesk\Revit\Addins\2024\`
4. Update the `<Assembly>` path in the `.addin` file to point to `RevitGeoSuite.Shell.dll`
5. Launch Revit 2024

## Tests

```bash
dotnet test
```

---

## Documentation

See the [`docs/`](docs/) folder for detailed design documentation:

- [Product Overview](docs/01-product-overview.md) — Vision and goals
- [User Problems & Goals](docs/02-user-problem-and-goals.md) — Pain points and desired workflows
- [Scope V1](docs/03-scope-v1.md) — Milestones and boundaries
- [Technical Architecture](docs/04-technical-architecture.md) — Layer responsibilities and contracts
- [Revit API Notes](docs/05-revit-api-notes.md) — Revit 2024 API patterns
- [Geo & CRS Rules](docs/06-geo-and-coordinate-system-rules.md) — Coordinate system handling
- [UI Flow](docs/07-ui-flow.md) — User workflow design
- [Implementation Phases](docs/08-implementation-phases.md) — Build order
- [Test Plan](docs/09-test-plan.md) — Testing strategy
- [Architecture](docs/Architecture.md) — Module structure and dependency graph
- [Decisions](docs/DECISIONS.md) — Locked technical decisions

---

## Tools & Technologies

### Architecture / BIM
![Revit](https://img.shields.io/badge/Revit_2024-0f766e?style=for-the-badge&logo=autodesk&logoColor=ffffff)
![IFC](https://img.shields.io/badge/IFC-475569?style=for-the-badge)
![CityGML](https://img.shields.io/badge/CityGML-0ea5e9?style=for-the-badge)
![3D Tiles](https://img.shields.io/badge/3D_Tiles-0284c7?style=for-the-badge)

### Spatial Data
![GeoPackage](https://img.shields.io/badge/GeoPackage-0369a1?style=for-the-badge)
![PLATEAU](https://img.shields.io/badge/PLATEAU-0891b2?style=for-the-badge)
![JIS Mesh](https://img.shields.io/badge/JIS_X_0410_Mesh-56949f?style=for-the-badge)
![IMDF](https://img.shields.io/badge/IMDF-0ea5e9?style=for-the-badge)

### Programming
![C Sharp](https://img.shields.io/badge/C%23_12-68217a?style=for-the-badge&logo=csharp&logoColor=ffffff)
![WPF](https://img.shields.io/badge/WPF-3178c6?style=for-the-badge)
![Leaflet](https://img.shields.io/badge/Leaflet.js-199900?style=for-the-badge&logo=leaflet&logoColor=ffffff)
![xUnit](https://img.shields.io/badge/xUnit-393552?style=for-the-badge)

---

<div align="center">

Revit model → georeferenced placement → validated coordinates → spatial data export

</div>

## License

TBD
