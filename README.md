# Revit Geo Suite

A modular Revit add-in suite for georeferencing, mesh/grid lookup, PLATEAU context workflows, and export pipelines (CityGML, 3D Tiles). Built around a shared geospatial core so that independent tools work together through common project metadata without depending on one another.

## Features (V1)

- **Georeference Module:** Visual CRS selection, map-based site placement, preview before apply, persisted geo metadata
- **Mesh Inspector:** Japanese mesh code lookup and neighbor display
- **Validation:** Project health checks for coordinate setup

## Prerequisites

- **Revit 2024**
- **.NET Framework 4.8 SDK**
- **Visual Studio 2022** (or later) with the ".NET desktop development" workload
- **WebView2 Runtime** (typically pre-installed on Windows 10/11)

## Build

```bash
# Clone the repository
git clone <repo-url>
cd RevitGeoSuite

# Build with MSBuild (from VS Developer Command Prompt)
msbuild RevitGeoSuite.sln /p:Configuration=Release

# Or open RevitGeoSuite.sln in Visual Studio and build
```

All output DLLs are written to `bin/Deploy/`.

## Install

1. Build the solution
2. Copy `bin/Deploy/` contents to a folder (e.g., `C:\RevitGeoSuite\`)
3. Copy `RevitGeoSuite.addin` to `%AppData%\Autodesk\Revit\Addins\2024\`
4. Update the `<Assembly>` path in the `.addin` file to point to `RevitGeoSuite.Shell.dll`
5. Launch Revit 2024

## Project Structure

```
RevitGeoSuite/
├── src/
│   ├── RevitGeoSuite.Core/              # Generic geo foundation (no Revit)
│   ├── RevitGeoSuite.Core.Plateau/      # PLATEAU-specific shared logic
│   ├── RevitGeoSuite.RevitInterop/      # Revit API wrappers (no UI)
│   ├── RevitGeoSuite.SharedUI/          # Reusable WPF controls
│   ├── RevitGeoSuite.Shell/             # Main add-in entry point
│   ├── RevitGeoSuite.Georeference/      # Georeferencing module
│   ├── RevitGeoSuite.MeshInspector/     # Mesh code inspector module
│   ├── RevitGeoSuite.Validation/        # Project validation module
│   ├── RevitGeoSuite.PlateauImport/     # PLATEAU context import
│   ├── RevitGeoSuite.Tiles3DExport/     # 3D Tiles export
│   └── RevitGeoSuite.CityGmlExport/    # CityGML export
├── tests/
│   ├── RevitGeoSuite.Core.Tests/
│   └── ...
└── docs/                                # Architecture and design docs
```

## Architecture

Modules are independent. They depend on Core and RevitInterop but never on each other. Communication happens through shared `GeoProjectInfo` metadata.

```
┌─────────────────────────────────────────────────┐
│ Shell (Ribbon UI + Module Discovery)            │
├─────────┬──────────┬──────────┬────────┬────────┤
│ Geo     │ Mesh     │ PLATEAU  │ 3D     │ CityGML│
│Reference│ Inspector│ Import   │ Tiles  │ Export │
├─────────┴──────────┴──────────┴────────┴────────┤
│ SharedUI (WPF)  │  RevitInterop (API wrappers)  │
├──────────────────┴──────────────────────────────┤
│ Core (CRS, Mesh, Metadata, Validation)          │
└─────────────────────────────────────────────────┘
```

## Documentation

See the `docs/` folder for detailed documentation:

- [Architecture](docs/Architecture.md) — Module structure and dependency graph
- [Technical Decisions](docs/DECISIONS.md) — Locked decisions for development
- [Product Overview](docs/01-product-overview.md) — Vision and goals
- [Scope V1](docs/03-scope-v1.md) — What ships in V1
- [Technical Architecture](docs/04-technical-architecture.md) — Layer responsibilities
- [Revit API Notes](docs/05-revit-api-notes.md) — Revit 2024 API patterns
- [Geo/CRS Rules](docs/06-geo-and-coordinate-system-rules.md) — Coordinate system handling
- [UI Flow](docs/07-ui-flow.md) — User workflow design
- [Implementation Phases](docs/08-implementation-phases.md) — Build order
- [Test Plan](docs/09-test-plan.md) — Testing strategy
- [Codex Task Index](docs/Codex-Task-Index.md) — Task breakdown for development

## Running Tests

```bash
dotnet test
```

## License

TBD
