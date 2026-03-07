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
See `docs/04-technical-architecture.md` and `docs/08-implementation-phases.md`.

Scope:
- Create solution and project shells for Core, Core.Plateau, RevitInterop, SharedUI, Shell, and modules.

Non-goals:
- No implementation logic.

Constraints:
- No cross-module references.

Files/Paths:
- `src/`
- `tests/`

Inputs:
- `docs/04-technical-architecture.md`

Outputs:
- Solution and project files.

Acceptance Criteria:
- References match the dependency graph in `Architecture.md`.

Tests:
- N/A.

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

## Task 6 — Georeference Module (V1)

Title: Build Georeference workflow

Background:
See `docs/03-scope-v1.md` and `docs/07-ui-flow.md`.

Scope:
- CRS picker
- Map point selection
- Preview
- Apply placement via interop
- Persist `GeoProjectInfo`

Constraints:
- No dependencies on other modules.

Files/Paths:
- `src/RevitGeoSuite.Georeference/`

Acceptance Criteria:
- User can choose CRS, pick point, preview, and apply with confirmation.

Tests:
- Manual scenarios from `docs/09-test-plan.md`.

---

## Task 7 — Mesh Inspector

Title: Implement mesh inspection

Background:
See `docs/08-implementation-phases.md`.

Scope:
- Compute mesh
- Display primary and neighbor mesh
- Update `PrimaryMeshCode` only

Constraints:
- Do not modify other canonical geo facts.

Files/Paths:
- `src/RevitGeoSuite.MeshInspector/`

Acceptance Criteria:
- Only `PrimaryMeshCode` is persisted.

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
