# Codex Task Index — Revit Geo Suite

**Purpose:** A single, compact execution guide for Codex. Each task is issue‑style, tightly scoped, and links back to the detailed spec docs. For phase-specific tests, see `docs/09-test-plan.md` → “Tests by Phase.”

---

## Global Constraints

- No module depends on another module.
- `RevitGeoSuite.Core` has zero dependencies.
- `RevitGeoSuite.Core.Plateau` depends only on Core.
- `RevitGeoSuite.RevitInterop` is API-only (no UI).
- `RevitGeoSuite.SharedUI` is presentational only (no workflow logic).
- `GeoProjectInfo` is small and stable (canonical facts only).
- All storage is versioned.
- Modules declare Core/Revit compatibility.

---

## Task Format (Copy for Each Task)

Title:

Background:

Scope:

Non-goals:

Constraints:

Files/Paths:

Inputs:

Outputs:

Acceptance Criteria:

Tests:

---

## Task 0 — Solution Skeleton

Title: Create solution and project shells

Background:
See `docs/04-technical-architecture.md`, `docs/Architecture.md`, and `docs/DECISIONS.md`.

Scope:
- Create `RevitGeoSuite.sln` and all project `.csproj` files
- Set up `Directory.Build.props` with shared settings
- Create `.gitignore` for build artifacts
- Create `.addin` manifest in Shell project
- Create empty marker classes so solution compiles

Non-goals:
- No implementation logic. Only project files and empty shells.

Constraints:
- All projects target `net48` (SDK-style `.csproj`)
- No cross-module references
- Revit API references use local DLL paths via `$(RevitInstallDir)`
- Common output path: `bin/Deploy/`

Files/Paths:

```
RevitGeoSuite.sln
Directory.Build.props
.gitignore
src/RevitGeoSuite.Core/RevitGeoSuite.Core.csproj
src/RevitGeoSuite.Core.Plateau/RevitGeoSuite.Core.Plateau.csproj
src/RevitGeoSuite.RevitInterop/RevitGeoSuite.RevitInterop.csproj
src/RevitGeoSuite.SharedUI/RevitGeoSuite.SharedUI.csproj
src/RevitGeoSuite.Shell/RevitGeoSuite.Shell.csproj
src/RevitGeoSuite.Shell/RevitGeoSuite.addin
src/RevitGeoSuite.Georeference/RevitGeoSuite.Georeference.csproj
src/RevitGeoSuite.MeshInspector/RevitGeoSuite.MeshInspector.csproj
src/RevitGeoSuite.Validation/RevitGeoSuite.Validation.csproj
src/RevitGeoSuite.PlateauImport/RevitGeoSuite.PlateauImport.csproj
src/RevitGeoSuite.Tiles3DExport/RevitGeoSuite.Tiles3DExport.csproj
src/RevitGeoSuite.CityGmlExport/RevitGeoSuite.CityGmlExport.csproj
tests/RevitGeoSuite.Core.Tests/RevitGeoSuite.Core.Tests.csproj
tests/RevitGeoSuite.Core.Plateau.Tests/RevitGeoSuite.Core.Plateau.Tests.csproj
tests/RevitGeoSuite.Georeference.Tests/RevitGeoSuite.Georeference.Tests.csproj
```

NuGet packages per project:

| Project | NuGet Packages |
|---|---|
| Core | `ProjNet`, `Newtonsoft.Json` |
| Core.Plateau | `Newtonsoft.Json` |
| RevitInterop | `Newtonsoft.Json` (Revit API via local DLLs) |
| SharedUI | `Microsoft.Web.WebView2` |
| Shell | (Revit API via local DLLs) |
| All modules | (none beyond project refs) |
| All test projects | `xunit`, `xunit.runner.visualstudio`, `Moq`, `Microsoft.NET.Test.Sdk` |

Project reference graph:

```
Core                    → (none)
Core.Plateau            → Core
RevitInterop            → Core, Revit API DLLs
SharedUI                → Core
Shell                   → Core, RevitInterop, SharedUI
Georeference            → Core, RevitInterop, SharedUI
MeshInspector           → Core, RevitInterop, SharedUI
Validation              → Core, RevitInterop, SharedUI
PlateauImport           → Core, Core.Plateau, RevitInterop, SharedUI
Tiles3DExport           → Core, RevitInterop, SharedUI
CityGmlExport           → Core, Core.Plateau, RevitInterop, SharedUI
```

`Directory.Build.props` must include:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>latest</LangVersion>
    <OutputPath>$(SolutionDir)bin\Deploy\</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <RevitInstallDir Condition="'$(RevitInstallDir)' == ''">C:\Program Files\Autodesk\Revit 2024</RevitInstallDir>
  </PropertyGroup>
</Project>
```

`.gitignore` must include:

```
bin/
obj/
*.user
*.suo
.vs/
packages/
*.nupkg
TestResults/
```

`.addin` manifest template — see `docs/DECISIONS.md`.

Inputs:
- `docs/DECISIONS.md`
- `docs/Architecture.md`

Outputs:
- Solution builds with zero errors (no implementation, just empty shells)

Acceptance Criteria:
- `dotnet build` succeeds (or `msbuild` for .NET Framework)
- References match the dependency graph above exactly
- No cross-module project references
- All test projects reference xunit and their corresponding source project

Tests:
- Build succeeds with no errors or warnings about missing references

---

## Task 1 — Core Contracts

Title: Implement Core contracts and interfaces

Background:
See `docs/04-technical-architecture.md`, `docs/06-geo-and-coordinate-system-rules.md`.

Scope:
- `GeoProjectInfo`, `CrsReference`, `ProjectOrigin`, `GeoConfidenceLevel`.
- Interfaces: `ICrsRegistry`, `ICoordinateTransformer`, `IMeshCalculator`, `IGeoProjectInfoStore`, `IModuleStateStore`.

Non-goals:
- No Revit or UI code.

Constraints:
- Persist only canonical geo facts.
- Do not persist full CRS definitions.

Files/Paths:
- `src/RevitGeoSuite.Core/`

Inputs:
- `docs/06-geo-and-coordinate-system-rules.md`

Outputs:
- Core contract types and interfaces.

Acceptance Criteria:
- `GeoProjectInfo` contains only canonical facts.

Tests:
- Serialization/deserialization tests for `GeoProjectInfo`.

---

## Task 2 — Storage Versioning

Title: Add schema versioning and migration runner

Background:
See `docs/04-technical-architecture.md` and `docs/09-test-plan.md`.

Scope:
- Schema version field
- Migration runner

Constraints:
- All stored objects include a version.

Files/Paths:
- `src/RevitGeoSuite.Core/Versioning/`

Outputs:
- Versioning utilities.

Acceptance Criteria:
- v0 → v1 migration path exists.

Tests:
- Migration unit test.

---

## Task 3 — RevitInterop Boundary

Title: Implement Revit interop services

Background:
See `docs/05-revit-api-notes.md`.

Scope:
- `IRevitGeoPlacementService`
- Project location reader/writer
- Storage helpers

Constraints:
- No UI references.

Files/Paths:
- `src/RevitGeoSuite.RevitInterop/`

Acceptance Criteria:
- Interop compiles without WPF references.

---

## Task 4 — SharedUI Controls

Title: Implement SharedUI controls and styles

Background:
See `docs/07-ui-flow.md`.

Scope:
- `MapControl`, `CrsPickerControl`, `StatusBarControl`, styles.

Constraints:
- No workflow logic.

Files/Paths:
- `src/RevitGeoSuite.SharedUI/`

Acceptance Criteria:
- SharedUI contains no storage or workflow logic.

---

## Task 5 — Shell and Module Discovery

Title: Implement module discovery and ribbon

Background:
See `docs/04-technical-architecture.md` and `docs/08-implementation-phases.md`.

Scope:
- `IRevitGeoModule`
- Module registry and version checks
- Ribbon layout

Constraints:
- Incompatible modules are skipped and logged.

Files/Paths:
- `src/RevitGeoSuite.Shell/`

Acceptance Criteria:
- Module compatibility checks work as defined.

---

## Task 6A — CRS Picker UI + CrsRegistry Implementation

Title: Build CRS selection with ProjNet backend

Background:
See `docs/03-scope-v1.md`, `docs/07-ui-flow.md`, and `docs/06-geo-and-coordinate-system-rules.md`.

Scope:
- Implement `CrsRegistry` using ProjNet4GeoAPI with Japanese CRS presets (EPSG:6669–6687)
- Implement `CoordinateTransformer` using ProjNet
- Build `CrsPickerControl` UI (search/filter EPSG codes, show CRS details)
- Wire CRS picker into `GeoreferenceViewModel`

Non-goals:
- No map, no placement, no Revit interaction yet.

Constraints:
- CRS picker is a SharedUI control, reusable by other modules
- ProjNet is the only CRS library

Files/Paths:
- `src/RevitGeoSuite.Core/Coordinates/CrsRegistry.cs`
- `src/RevitGeoSuite.Core/Coordinates/CoordinateTransformer.cs`
- `src/RevitGeoSuite.Core/Coordinates/JapanCrsPresets.cs`
- `src/RevitGeoSuite.SharedUI/Controls/CrsPickerControl.xaml`
- `src/RevitGeoSuite.Georeference/GeoreferenceViewModel.cs` (CRS selection portion)

Acceptance Criteria:
- User can search and select a CRS from the picker
- Selected CRS resolves to a valid ProjNet coordinate system
- Japanese presets appear prominently

Tests:
- Unit: `CrsRegistry` resolves known EPSG codes
- Unit: `CoordinateTransformer` converts known sample points within tolerance
- Unit: Japanese presets are all loadable

---

## Task 6B — Map Control (WebView2 + Leaflet) + Point Selection

Title: Build embedded map with point selection

Background:
See `docs/07-ui-flow.md` and `docs/DECISIONS.md`.

Scope:
- Implement `MapControl` using WebView2 + Leaflet.js + OSM tiles
- Bundle HTML/JS/CSS as embedded resources
- C# ↔ JS bridge for click events and marker placement
- `SiteSelectionService` to manage selected point state
- Wire into `GeoreferenceViewModel`

Non-goals:
- No placement preview or Revit interaction yet.

Constraints:
- Map is a SharedUI control
- Communication via `PostWebMessageAsJson` / `WebMessageReceived`

Files/Paths:
- `src/RevitGeoSuite.SharedUI/Controls/MapControl.xaml`
- `src/RevitGeoSuite.SharedUI/Controls/MapControl.xaml.cs`
- `src/RevitGeoSuite.SharedUI/Resources/map.html`
- `src/RevitGeoSuite.Georeference/SiteSelectionService.cs`

Acceptance Criteria:
- Map loads and displays OSM tiles
- User can click to place a marker
- Selected lat/lon coordinates are returned to C#
- Coordinates are converted to the selected CRS and displayed

Tests:
- Manual: map loads, click returns coordinates
- Unit: `SiteSelectionService` stores and converts selected point

---

## Task 6C — Placement Preview Service

Title: Build preview calculations and before/after comparison UI

Background:
See `docs/07-ui-flow.md` and `docs/05-revit-api-notes.md`.

Scope:
- `PlacementPreviewService`: calculate proposed changes without modifying the document
- Read current project location via `ProjectLocationReader`
- Generate before/after comparison data
- Build preview panel in `GeoreferenceWindow`

Non-goals:
- Does not apply changes to the Revit document.

Constraints:
- Preview logic stays above the Revit interop layer
- Uses `TransactionGroup` with rollback for read-only preview if needed

Files/Paths:
- `src/RevitGeoSuite.Georeference/PlacementPreviewService.cs`
- `src/RevitGeoSuite.Georeference/GeoreferenceWindow.xaml` (preview panel)
- `src/RevitGeoSuite.RevitInterop/GeoPlacement/ProjectLocationReader.cs`

Acceptance Criteria:
- Preview shows current vs. proposed: origin, CRS, true north angle
- Preview does not modify the Revit document
- Existing coordinate setup triggers a warning

Tests:
- Unit: `PlacementPreviewService` generates correct diff from mock inputs
- Manual: preview panel shows before/after values

---

## Task 6D — Apply Placement Workflow + Persist GeoProjectInfo

Title: Apply geo placement and save project metadata

Background:
See `docs/05-revit-api-notes.md`, `docs/06-geo-and-coordinate-system-rules.md`.

Scope:
- `ProjectLocationWriter`: apply survey point / project base point changes in a Revit `Transaction`
- Wire Apply button in `GeoreferenceWindow` with confirmation dialog
- Save `GeoProjectInfo` to Extensible Storage via `GeoProjectInfoStorage`
- Log the operation via `AuditLogger`
- Detect and warn about existing coordinate setup before overwriting

Non-goals:
- No linked model support in V1.

Constraints:
- All document modifications in a single `Transaction`
- Failed changes must not leave the model partially updated
- Confirmation required before apply

Files/Paths:
- `src/RevitGeoSuite.RevitInterop/GeoPlacement/ProjectLocationWriter.cs`
- `src/RevitGeoSuite.RevitInterop/GeoPlacement/RevitGeoPlacementService.cs`
- `src/RevitGeoSuite.RevitInterop/Storage/GeoProjectInfoStorage.cs`
- `src/RevitGeoSuite.Georeference/GeoreferenceCommand.cs`

Acceptance Criteria:
- User confirms before apply
- Survey point / project base point are updated correctly
- `GeoProjectInfo` is persisted with correct schema version
- Operation is logged
- Existing setup warning appears when metadata already exists

Tests:
- Unit: `RevitGeoPlacementService` calls correct writer methods (mocked)
- Manual: full workflow in Revit — CRS → map → preview → apply → verify

---

## Task 7 — Mesh Inspector

Title: Implement mesh inspection

Background:
See `docs/08-implementation-phases.md`, `docs/06-geo-and-coordinate-system-rules.md` (mesh boundary formulas), and `docs/DECISIONS.md` (Mesh Grid Display section).

Scope:
- Compute primary mesh code from project origin via `JapanMeshCalculator`
- Compute 8 neighbor codes via `MeshNeighborResolver`
- Convert each mesh code to a lat/lon bounding box (see boundary formulas in `docs/06-geo-and-coordinate-system-rules.md`)
- Build a GeoJSON `FeatureCollection` (9 `Polygon` features with `meshCode` and `isPrimary` properties)
- Send GeoJSON to SharedUI `MapControl` via `PostWebMessageAsJson` with message type `showMeshGrid`
- MapControl JS receives `showMeshGrid`, renders the GeoJSON layer using `L.geoJSON()`, styles primary vs. neighbor cells, and adds mesh code labels
- Click interaction: clicking a mesh cell shows its code and (future) PLATEAU data availability
- Display primary and neighbor mesh info in the MeshInspector UI panel
- Update `PrimaryMeshCode` in `GeoProjectInfo`

Non-goals:
- No PLATEAU data download or import (that is Task 9).
- No modification of other canonical geo facts.

Constraints:
- Do not modify other canonical geo facts.
- Mesh grid is an optional overlay on the shared `MapControl`, not a separate map instance.
- GeoJSON construction happens in C# (MeshInspector module), not in JavaScript.

Files/Paths:
- `src/RevitGeoSuite.Core/Mesh/JapanMeshCalculator.cs`
- `src/RevitGeoSuite.Core/Mesh/MeshNeighborResolver.cs`
- `src/RevitGeoSuite.MeshInspector/MeshInspectorViewModel.cs`
- `src/RevitGeoSuite.MeshInspector/MeshInspectorWindow.xaml`
- `src/RevitGeoSuite.SharedUI/Resources/map.html` (add `showMeshGrid` handler)

Acceptance Criteria:
- Only `PrimaryMeshCode` is persisted.
- Map shows 9 mesh cells (1 primary + 8 neighbors) as styled polygons.
- Primary cell is visually distinct from neighbors.
- Each cell displays its mesh code label.
- Clicking a cell shows mesh code details.

Tests:
- Unit: `JapanMeshCalculator` returns correct bounding box for known mesh codes
- Unit: `MeshNeighborResolver` returns 8 valid neighbors for interior and edge cases
- Unit: GeoJSON builder produces valid FeatureCollection with correct properties
- Manual: mesh grid displays correctly on the map centered on project origin

---

## Task 8 — Validation Module

Title: Implement project health checks

Background:
See `docs/09-test-plan.md`.

Scope:
- Basic checks: CRS, origin, confidence, suspicious coords.

Constraints:
- No persistence of validation results.

Files/Paths:
- `src/RevitGeoSuite.Validation/`

Acceptance Criteria:
- Validation results are derived only.

---

## Task 9 — Core.Plateau + PLATEAU Import

Title: Implement PLATEAU shared logic and import

Background:
See `docs/08-implementation-phases.md`.

Scope:
- Core.Plateau: codelists, tile index, schema helpers
- Plateau import state

Constraints:
- Core.Plateau depends only on Core.

Files/Paths:
- `src/RevitGeoSuite.Core.Plateau/`
- `src/RevitGeoSuite.PlateauImport/`

Acceptance Criteria:
- No Revit references in Core.Plateau.

---

## Task 10 — 3D Tiles Export

Title: Implement 3D Tiles export

Background:
See `docs/08-implementation-phases.md`.

Scope:
- Geometry extraction
- glTF/GLB
- Tileset generation

Constraints:
- No cross-module dependencies.

Files/Paths:
- `src/RevitGeoSuite.Tiles3DExport/`

---

## Task 11 — CityGML Export

Title: Implement CityGML export

Background:
See `docs/08-implementation-phases.md`.

Scope:
- Semantic mapping
- Codelist mapping
- Writer

Constraints:
- Use Core.Plateau only.

Files/Paths:
- `src/RevitGeoSuite.CityGmlExport/`

---

## Cross-Cutting Checks (Run After Each Task)

Title: Dependency graph check

Scope:
- List project references and flag violations.

Acceptance Criteria:
- No module depends on another module.

---

Title: Storage schema check

Scope:
- Enumerate persisted fields and confirm only canonical facts exist in `GeoProjectInfo`.

Acceptance Criteria:
- No module-specific fields in `GeoProjectInfo`.
