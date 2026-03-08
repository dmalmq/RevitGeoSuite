# Codex Task Index - Revit Geo Suite

**Purpose:** A compact execution guide for Codex. Each task is tightly scoped, points at the relevant docs, and ends in a visible artifact. The V1 sequence is intentionally split into `foundation`, `read + preview`, and `apply` so document mutation is not mixed into early setup work.

---

## Global Constraints

- No module depends on another module.
- `RevitGeoSuite.Core` has zero dependencies.
- `RevitGeoSuite.Core.Plateau` depends only on Core.
- `RevitGeoSuite.RevitInterop` is API-only (no UI).
- `RevitGeoSuite.SharedUI` is presentational only (no workflow logic).
- `GeoProjectInfo` is small and stable (canonical facts only).
- `PlacementPreview` is never persisted.
- All storage is versioned.
- The shell uses static Georeference module registration in V1.
- Dynamic assembly scanning is a post-V1 infrastructure task.

---

## Milestone Map

- **Milestone A - Foundation:** Tasks 0-4
- **Milestone B - Read + Preview:** Tasks 5-9
- **Milestone C - Apply + Persist:** Task 10
- **Phase 2+:** Tasks 11 and later

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

## Task 0 - Solution Skeleton

Title: Create solution and project shells

Background:
See `docs/04-technical-architecture.md`, `docs/Architecture.md`, and `docs/DECISIONS.md`.

Scope:
- Create `RevitGeoSuite.sln` and project `.csproj` files
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

Outputs:
- Solution builds with zero errors using empty shells

Acceptance Criteria:
- `msbuild` or `dotnet build` succeeds for the skeleton
- References match the dependency graph exactly
- No module-to-module references exist

Tests:
- Build succeeds with no missing reference errors

---

## Task 1 - Fixture Pack

Title: Create coordinate, CRS, and migration fixtures

Background:
See `docs/06-geo-and-coordinate-system-rules.md` and `docs/09-test-plan.md`.

Scope:
- Add known CRS samples for Japanese presets
- Add known lat/lon to projected coordinate samples
- Add mesh calculation samples
- Add serialized metadata and migration fixtures
- Document tolerance expectations in fixture README or comments

Non-goals:
- No Revit write behavior yet

Constraints:
- Fixtures should be stable and human-readable where practical
- Each fixture should map to at least one test case

Outputs:
- Fixture files committed under `tests/` or a dedicated fixtures folder

Acceptance Criteria:
- Fixtures cover CRS lookup, transforms, mesh, and migration basics

Tests:
- Fixture loading tests pass

---

## Task 2 - Core Contracts

Title: Implement core contracts and interfaces

Background:
See `docs/04-technical-architecture.md` and `docs/06-geo-and-coordinate-system-rules.md`.

Scope:
- `GeoProjectInfo`, `CrsReference`, `ProjectOrigin`, `GeoConfidenceLevel`
- `PlacementIntent` and `PlacementPreview`
- Interfaces: `ICrsRegistry`, `ICoordinateTransformer`, `IMeshCalculator`, `IGeoProjectInfoStore`, `IModuleStateStore`

Non-goals:
- No Revit or UI code

Constraints:
- Persist only canonical geo facts
- Do not persist full CRS definitions
- Do not persist `PlacementPreview`

Outputs:
- Core contract types and interfaces

Acceptance Criteria:
- `GeoProjectInfo` contains canonical facts only
- `PlacementIntent` and `PlacementPreview` are explicitly non-persisted workflow models

Tests:
- Serialization tests for `GeoProjectInfo`
- Contract construction/default tests for preview and intent models

---

## Task 3 - Storage Versioning

Title: Add schema versioning and migration runner

Background:
See `docs/04-technical-architecture.md` and `docs/09-test-plan.md`.

Scope:
- Schema version field
- Migration runner
- Storage envelope shape for versioned objects

Constraints:
- All stored objects include a version
- Migration behavior is test-backed from the first revision

Outputs:
- Versioning utilities and storage contracts

Acceptance Criteria:
- v0 -> v1 migration path exists

Tests:
- Migration unit test using fixtures

---

## Task 4 - Shell and Static Georeference Registration

Title: Implement shell composition root and static Georeference command wiring

Background:
See `docs/04-technical-architecture.md` and `docs/08-implementation-phases.md`.

Scope:
- `IRevitGeoModule`
- shell composition root in `App.cs`
- ribbon layout
- static registration of the Georeference module

Non-goals:
- No assembly scanning yet

Constraints:
- Preserve the future module boundary
- Do not add dynamic plugin infrastructure in V1

Outputs:
- Shell loads and exposes one Georeference command

Acceptance Criteria:
- shell composes the Georeference module without scanning directories
- ribbon command opens the module entry point

Tests:
- build test
- shell composition test where practical

---

## Task 5 - CRS Registry and Transformer

Title: Build CRS selection backend with ProjNet

Background:
See `docs/03-scope-v1.md`, `docs/06-geo-and-coordinate-system-rules.md`, and `docs/DECISIONS.md`.

Scope:
- Implement `CrsRegistry` using ProjNet with Japanese presets (EPSG:6669-6687)
- Implement `CoordinateTransformer`
- expose search/filter-friendly CRS models for UI use

Non-goals:
- No map or Revit interaction yet

Constraints:
- ProjNet is the only CRS library
- axis normalization rules must be documented in code/tests

Outputs:
- Working CRS lookup and transform services

Acceptance Criteria:
- known EPSG presets resolve correctly
- known coordinate samples transform within tolerance

Tests:
- unit tests for lookup, presets, invalid input, and transform tolerances

---

## Task 6 - SharedUI Controls

Title: Implement CRS picker and shared map host controls

Background:
See `docs/07-ui-flow.md` and `docs/DECISIONS.md`.

Scope:
- `CrsPickerControl`
- `MapControl` using WebView2 + Leaflet + OSM
- shared styles and display converters
- C# <-> JS bridge for click events and marker placement

Non-goals:
- No workflow logic in SharedUI
- No apply behavior

Constraints:
- map is hosted as a reusable control
- communication uses `PostWebMessageAsJson` and `WebMessageReceived`

Outputs:
- reusable CRS picker and map control

Acceptance Criteria:
- map loads and returns clicked coordinates to C#
- picker can search and select Japanese presets

Tests:
- manual smoke test for map load and click behavior
- unit test coverage for UI-independent helper code only

---

## Task 7 - Current State Reader

Title: Implement Revit current-state reading and existing-setup detection

Background:
See `docs/05-revit-api-notes.md`.

Scope:
- `ProjectLocationReader`
- current project location summary model
- detection of existing non-default setup
- read-only mapping from Revit state into preview input models

Non-goals:
- No write behavior

Constraints:
- Revit API access stays inside RevitInterop
- reader must be safe on unsupported states and return understandable failures

Outputs:
- current-state read service for the Georeference workflow

Acceptance Criteria:
- current state can be shown before the user starts preview
- existing setup warning appears when current state is non-default or metadata exists

Tests:
- mock-based tests for workflow consumption of current-state data

---

## Task 8 - Preview Models and Services

Title: Build `PlacementIntent` -> `PlacementPreview` generation

Background:
See `docs/04-technical-architecture.md`, `docs/05-revit-api-notes.md`, and `docs/07-ui-flow.md`.

Scope:
- `SiteSelectionService`
- `PlacementPreviewService`
- warning generation for suspicious coordinate ranges
- persistence summary generation

Non-goals:
- No document mutation

Constraints:
- preview logic stays above RevitInterop where possible
- use transaction rollback preview only if pure-math preview proves insufficient

Outputs:
- before/after preview model for the wizard

Acceptance Criteria:
- preview shows current vs proposed origin, CRS, and angle
- preview states exactly what apply will and will not change
- preview does not modify the Revit document

Tests:
- unit tests for preview diff generation and warning logic using fixtures

---

## Task 9 - Georeference Wizard (Preview-Only Build)

Title: Wire the full georeference wizard through the preview step

Background:
See `docs/07-ui-flow.md` and `docs/08-implementation-phases.md`.

Scope:
- current-state screen
- CRS step
- map point step
- setup intent step
- preview screen
- command gating and navigation

Non-goals:
- No apply behavior yet

Constraints:
- preview is mandatory before any future apply action
- workflow copy must explain that V1 does not rotate building geometry directly

Outputs:
- working internal build that ends at preview

Acceptance Criteria:
- user can complete the workflow up to preview without changing the document
- apply is absent or explicitly disabled in this milestone

Tests:
- view model tests for state transitions
- manual preview-only workflow test in Revit

---

## Task 10 - Apply Placement + Persist GeoProjectInfo

Title: Apply project location changes and save shared metadata

Background:
See `docs/05-revit-api-notes.md`, `docs/06-geo-and-coordinate-system-rules.md`, and `docs/07-ui-flow.md`.

Scope:
- `ProjectLocationWriter`
- `RevitGeoPlacementService.ApplyPlacement(...)`
- `GeoProjectInfo` persistence through versioned storage
- audit summary logging
- confirmation dialog and failure handling

Non-goals:
- No linked model support in V1
- No geometry rotation
- No separate arbitrary direct base point editing tool

Constraints:
- apply via `ProjectLocation.SetProjectPosition()`
- `ProjectPosition.Angle` is the only rotation behavior in V1
- all document modifications happen in a single transaction
- failed changes must not leave the model partially updated

Outputs:
- full V1 georeference workflow with confirmation and persistence

Acceptance Criteria:
- user confirms before apply
- project location changes are applied safely
- `GeoProjectInfo` is persisted with correct schema version
- operation is logged
- existing setup warning appears before overwrite

Tests:
- mock-based unit tests for service orchestration
- manual end-to-end Revit workflow: CRS -> map -> preview -> apply -> verify

---

## Task 11 - Mesh Inspector

Title: Implement mesh inspection

Background:
See `docs/08-implementation-phases.md`, `docs/06-geo-and-coordinate-system-rules.md`, and `docs/DECISIONS.md`.

Scope:
- compute primary mesh code from project origin
- compute 8 neighbor codes
- build GeoJSON for shared map overlay
- display primary and neighbor mesh info in the Mesh Inspector UI
- update `PrimaryMeshCode` in `GeoProjectInfo`

Constraints:
- only `PrimaryMeshCode` is persisted
- mesh overlay reuses the shared map control

Acceptance Criteria:
- map shows 9 mesh cells
- primary cell is visually distinct
- mesh details can be inspected without changing other canonical facts

Tests:
- mesh calculation and GeoJSON builder tests
- manual overlay test on the shared map

---

## Task 12 - Validation Module

Title: Implement project health checks

Background:
See `docs/09-test-plan.md`.

Scope:
- basic checks: CRS, origin, confidence, suspicious coordinates, stale mesh state

Constraints:
- validation results are derived only
- no persistence of validation snapshots

Acceptance Criteria:
- validation warnings are understandable and non-destructive

Tests:
- unit tests for validation rules
- manual test for warning display

---

## Task 13 - Optional Post-V1 Shell Discovery Infrastructure

Title: Add assembly-scanning module discovery after V1 is stable

Background:
See `docs/04-technical-architecture.md` and `docs/Architecture.md`.

Scope:
- scan known module directories
- load assemblies matching naming convention
- perform module compatibility checks
- skip and log incompatible modules

Constraints:
- must preserve behavior of statically registered modules during migration
- should be added only after V1 workflow is working end-to-end

Acceptance Criteria:
- shell can discover compatible modules without destabilizing the georeference workflow

Tests:
- compatibility handshake tests
- manual smoke test with multiple modules present

---

## Later Phases

- `Core.Plateau` + PLATEAU import
- `Tiles3DExport`
- `CityGmlExport`
- optional advanced overlays and official base maps
