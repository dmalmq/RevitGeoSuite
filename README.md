<div align="center">

# RevitGeoSuite

### Modular Revit Add-in for Georeferencing, Mesh Inspection & Geospatial Export

<p>
A suite of independent tools built on a shared geospatial core,<br />
designed to close the gap between BIM authoring in Revit and downstream spatial data workflows.<br />
From coordinate system setup to CityGML, 3D Tiles, and indoor floor-plan GIS export — without the legacy conversion chains.
</p>

<p>
  <img src="https://img.shields.io/badge/Platform-Revit_2024-0f766e?style=for-the-badge&logo=autodesk&logoColor=ffffff" />
  <img src="https://img.shields.io/badge/.NET_Framework-4.8-68217a?style=for-the-badge&logo=dotnet&logoColor=ffffff" />
  <img src="https://img.shields.io/badge/Language-C%23_12-3178c6?style=for-the-badge&logo=csharp&logoColor=ffffff" />
</p>

<p>
  <img src="https://img.shields.io/badge/UI-WebView2_+_Svelte_5-393552?style=flat-square" />
  <img src="https://img.shields.io/badge/Web-TypeScript_+_Vite-31748f?style=flat-square" />
  <img src="https://img.shields.io/badge/Maps-Leaflet_+_OSM-9ccfd8?style=flat-square" />
  <img src="https://img.shields.io/badge/CRS-ProjNet-907aa9?style=flat-square" />
  <img src="https://img.shields.io/badge/Languages-EN_/_JA-f6c177?style=flat-square" />
  <img src="https://img.shields.io/badge/Focus-Japanese_CRS_&_PLATEAU-eb6f92?style=flat-square" />
</p>

</div>

---

## About

Revit handles geometry well but leaves georeferencing, coordinate systems, and spatial export as an exercise for the user. Setup mistakes propagate silently into every downstream deliverable. RevitGeoSuite provides a visual, guided workflow for coordinate reference system selection, map-based placement, and export — with validation at every step.

The suite is especially focused on Japanese coordinate reference systems and [PLATEAU](https://www.mlit.go.jp/plateau/) digital twin interoperability, including JIS X 0410 mesh code inspection and CityGML tile alignment.

---

## Modules

All functionality is reached through three web-shell entry points on a single **Geo Suite** ribbon panel — **Georef**, **Import**, and **Export**. Each button opens a shared **WebView2** window hosting a Svelte single-page app, navigated by a left rail (`/georeference`, `/import`, `/export`). C# and the web UI communicate over a typed RPC bridge whose TypeScript contracts are generated from the .NET contract assembly at build time. The UI ships in English and Japanese with a runtime language toggle.

| Module | Entry | Description |
|--------|-------|-------------|
| **Georeference** | Georef | CRS selection with Japanese presets, OSM map-based point picking, PLATEAU grid-tile snapping, and a split survey / project base point apply path with placement preview |
| **Mesh Inspector** | Georef | Japanese JIS X 0410 mesh code lookup, boundary calculation, and 8-neighbor display as a GeoJSON map overlay |
| **Validation** | shell-wide | Live, read-only project health checks for coordinate setup and export readiness, surfaced in the shell status footer against the shared `GeoProjectInfo` |
| **PLATEAU Import** | Import | Folder scan with codelist parsing and tile indexing, grid-tile selection, building/feature filtering, a shape-based context geometry pipeline (lightweight extrusion, detailed DirectShape, or mass-on-Relief), road/sidewalk outlines, and DXF basemap export |
| **Ground / Terrain Import** | Import | Native Revit topography surface built from a local DEM (Kiban GML) or online Cesium Ion quantized-mesh terrain, with configurable radius/spacing and geoid-undulation offset to ellipsoidal height |
| **3D Tiles Export** | Export | Scoped export (whole model or selected 3D view) with per-object metadata, RGBA material colors, level grouping with a manifest, optional precise CRS anchor rebasing, and geoid undulation offset to convert orthometric anchors to WGS84 ellipsoidal height |
| **CityGML Export** | Export | Lightweight CityGML export with semantic and attribute mapping, codelist assignment, and a separate module export state |
| **Floor Plan Export** | Export | Indoor floor-plan GIS export to GeoPackage / Shapefile using an IMDF-style schema (units, zones, openings, levels, vertical circulation), with an interactive map preview, category/floor assignment, validation policies, reusable export profiles, and an export diagnostics manifest |

---

## Architecture

Modules are independent over a shared foundation — no module depends on another; they communicate through a small, stable shared-state contract (`GeoProjectInfo`). The C# layers expose their workflows to a single Svelte web UI over a typed RPC bridge whose TypeScript contracts are generated from the .NET contract assembly at build time.

```text
 Shell — Revit add-in · "Geo Suite" ribbon · hosts the WebView2 window
   │
 SharedUI.Web — Svelte SPA · routes: /georeference · /import · /export
   │   ↕ typed RPC bridge (TypeScript contracts generated from .NET)
   ▼
 Workflows
   Georeference · Mesh Inspector · Validation
   PLATEAU Import · Ground / Terrain Import
   3D Tiles Export · CityGML Export · Floor Plan Export
   │
   ▼
 Foundation
   SharedUI (WPF host + WebView2)      RevitInterop (Revit API)
   Core   ·   Core.Plateau   ·   FloorPlanExport.Core
```

---

## Project Structure

```text
RevitGeoSuite/
├── src/
│   ├── RevitGeoSuite.Core/                  # Generic geo foundation (no Revit dependency)
│   ├── RevitGeoSuite.Core.Plateau/          # PLATEAU + terrain logic (DEM, Cesium Ion, tiling)
│   ├── RevitGeoSuite.RevitInterop/          # Revit API wrappers (no UI)
│   ├── RevitGeoSuite.SharedUI/              # WPF shell window hosting WebView2
│   ├── RevitGeoSuite.SharedUI.Web/          # Svelte + TypeScript single-page UI (Vite)
│   ├── RevitGeoSuite.SharedUI.Web.Contracts/           # Shared RPC contract types (C#)
│   ├── RevitGeoSuite.SharedUI.Web.Contracts.Generator/ # Emits contracts.generated.ts
│   ├── RevitGeoSuite.Shell/                 # Add-in entry point and ribbon setup
│   ├── RevitGeoSuite.Georeference/          # Georeferencing workflow
│   ├── RevitGeoSuite.MeshInspector/         # Mesh code inspector
│   ├── RevitGeoSuite.Validation/            # Project validation
│   ├── RevitGeoSuite.PlateauImport/         # PLATEAU context + ground/terrain import
│   ├── RevitGeoSuite.Tiles3DExport/         # 3D Tiles export
│   ├── RevitGeoSuite.CityGmlExport/         # CityGML export
│   ├── RevitGeoSuite.FloorPlanExport/       # Floor plan GIS export (Revit-facing)
│   └── RevitGeoSuite.FloorPlanExport.Core/  # Floor plan export engine (GeoPackage/Shapefile, IMDF schema)
└── tests/                                   # xUnit test projects mirroring src/
```

---

## Prerequisites

- **Revit 2024**
- **.NET Framework 4.8 SDK**
- **Visual Studio 2022** (or later) with the ".NET desktop development" workload
- **Node.js 20+** *(builds the Svelte web UI during the solution build; pass `-p:SkipWebBuild=true` to reuse the committed bundle and skip Node)*
- **WebView2 Runtime** (typically pre-installed on Windows 10/11)
- **Inno Setup 6** *(optional — only needed to build the installer `.exe`)*

## Build

```bash
git clone <repo-url>
cd RevitGeoSuite

# MSBuild (from VS Developer Command Prompt)
msbuild RevitGeoSuite.sln /p:Configuration=Release

# Or open RevitGeoSuite.sln in Visual Studio and build
```

The solution build regenerates the TypeScript RPC contracts and runs the web build (`npm ci` + `vite build`) before compiling the WebView2 host, then embeds the bundle. Use `-p:SkipWebBuild=true` to reuse the committed `dist/` bundle when Node.js is unavailable. All output DLLs are written to `bin/Deploy/`.

## Install

### Installer EXE

After building the solution, create a distributable installer `.exe` (requires Inno Setup 6) with:

```powershell
pwsh ./install/build-installer.ps1 -RevitYear 2024
```

The installer EXE is written to `install/output/` and installs the add-in payload under `C:\ProgramData\Autodesk\Revit\Addins\2024\RevitGeoSuite\` with `RevitGeoSuite.addin` placed in `C:\ProgramData\Autodesk\Revit\Addins\2024\`. It registers a normal uninstall entry in Windows Apps. See [`install/README.md`](install/README.md) for parameters and dev-only direct copy install.

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

## Tools & Technologies

### Architecture / BIM
![Revit](https://img.shields.io/badge/Revit_2024-0f766e?style=for-the-badge&logo=autodesk&logoColor=ffffff)
![IFC](https://img.shields.io/badge/IFC-475569?style=for-the-badge)
![CityGML](https://img.shields.io/badge/CityGML-0ea5e9?style=for-the-badge)
![3D Tiles](https://img.shields.io/badge/3D_Tiles-0284c7?style=for-the-badge)

### Spatial Data
![PLATEAU](https://img.shields.io/badge/PLATEAU-0891b2?style=for-the-badge)
![JIS Mesh](https://img.shields.io/badge/JIS_X_0410_Mesh-56949f?style=for-the-badge)
![ProjNET](https://img.shields.io/badge/ProjNET-907aa9?style=for-the-badge)
![NetTopologySuite](https://img.shields.io/badge/NetTopologySuite-1f6f5c?style=for-the-badge)
![GeoPackage](https://img.shields.io/badge/GeoPackage-2563eb?style=for-the-badge)
![Shapefile](https://img.shields.io/badge/Shapefile-475569?style=for-the-badge)
![IMDF](https://img.shields.io/badge/IMDF-0f766e?style=for-the-badge)
![Cesium Terrain](https://img.shields.io/badge/Cesium_Ion_Terrain-3c8c40?style=for-the-badge)
![GeoJSON](https://img.shields.io/badge/GeoJSON-0369a1?style=for-the-badge)

### Programming
![C Sharp](https://img.shields.io/badge/C%23_12-68217a?style=for-the-badge&logo=csharp&logoColor=ffffff)
![TypeScript](https://img.shields.io/badge/TypeScript-3178c6?style=for-the-badge&logo=typescript&logoColor=ffffff)
![Svelte](https://img.shields.io/badge/Svelte_5-ff3e00?style=for-the-badge&logo=svelte&logoColor=ffffff)
![Vite](https://img.shields.io/badge/Vite-646cff?style=for-the-badge&logo=vite&logoColor=ffffff)
![Tailwind](https://img.shields.io/badge/Tailwind-38bdf8?style=for-the-badge&logo=tailwindcss&logoColor=ffffff)
![WPF](https://img.shields.io/badge/WPF-393552?style=for-the-badge)
![Leaflet](https://img.shields.io/badge/Leaflet.js-199900?style=for-the-badge&logo=leaflet&logoColor=ffffff)
![xUnit](https://img.shields.io/badge/xUnit-2b2d42?style=for-the-badge)

---

<div align="center">

Revit model → georeferenced placement → validated coordinates → spatial data export

</div>

## License

TBD
