# Implementation Phases

## V1 Milestone A - Foundation and Fixtures

### Goal

Establish the solution structure, core contracts, shell, and fixture-backed test setup.

### Tasks

- Create solution and project structure
- Create Core, RevitInterop, SharedUI, Shell, and Georeference projects
- Define `GeoProjectInfo`, `CrsReference`, `PlacementIntent`, and `PlacementPreview`
- Define `ICrsRegistry`, `ICoordinateTransformer`, `IGeoProjectInfoStore`, and `IRevitGeoPlacementService`
- Set up shell with static registration of the Georeference module
- Set up test projects and fixture files for known CRS and coordinate samples

### Deliverables

- buildable solution skeleton
- shell add-in visible in Revit
- static ribbon command for georeference workflow
- core contracts committed to repo
- initial fixtures and tests committed to repo

### Acceptance Criteria

- solution builds cleanly
- add-in loads in target Revit version
- shell ribbon appears
- core contracts and test projects exist
- fixture-backed unit tests pass for core contracts and sample data

---

## V1 Milestone B - Read + Preview Workflow

### Goal

Deliver the first full user-facing workflow for CRS selection, map-based point picking, current-state read, and safe preview generation without modifying the document.

### Tasks

- implement `CrsRegistry` and `CoordinateTransformer`
- build CRS picker UI
- build shared map control
- implement point selection service
- implement `ProjectLocationReader`
- implement placement preview generation
- implement existing setup warning logic
- wire wizard flow through preview step only

### Deliverables

- georeference wizard through preview
- read-only current vs proposed summary
- suspicious input warnings
- fixture-backed preview calculations

### Acceptance Criteria

- user can choose and save a draft CRS reference in workflow state
- user can select a point from the map
- user sees current vs proposed values
- preview does not modify the Revit document
- workflow is understandable without expert Revit knowledge

---

## V1 Milestone C - Apply + Persist

### Goal

Enable the confirmed write path and document persistence.

### Tasks

- implement `ProjectLocationWriter`
- implement `RevitGeoPlacementService.ApplyPlacement(...)`
- save `GeoProjectInfo` through versioned storage
- log the operation via audit summary
- require confirmation before apply
- handle transaction rollback on failure

### Deliverables

- apply workflow in Revit
- versioned shared geo metadata persistence
- audit summary after apply

### Acceptance Criteria

- user confirms before apply
- project location changes are applied through a single safe transaction
- `GeoProjectInfo` reloads correctly
- failed operations do not leave partial state behind

---

## Phase 2 - Mesh Inspector + Validation

### Goal

Add mesh/grid inspection and a lightweight project QA module after the georeference workflow is stable.

### Tasks

- mesh code calculation UI
- neighbor mesh display
- copy mesh code
- implement `Check Project` command
- suspicious coordinate checks
- export readiness summary

### Deliverables

- mesh inspector module
- validation module
- project health summary UI

### Acceptance Criteria

- user can view primary and neighboring mesh codes
- suspicious conditions are reported clearly
- project health can be checked without modifying the document

---

## Phase 3 - PLATEAU Context Import

### Goal

Introduce PLATEAU-specific shared logic and the first PLATEAU-facing module.

### Tasks

- build `Core.Plateau`
- implement tile lookup
- implement CityGML parsing foundation
- build lightweight context geometry workflow
- save import state separately from `GeoProjectInfo`

### Deliverables

- Core.Plateau shared library
- PLATEAU import module
- import state persistence

### Acceptance Criteria

- module can resolve relevant tiles from mesh or location inputs
- basic PLATEAU context data can be imported into Revit
- import state is stored separately from shared project geo metadata

---

## Phase 4 - 3D Tiles Export

### Goal

Add visualization-oriented export using shared geo metadata.

### Tasks

- geometry extraction
- glTF / GLB writing
- tileset generation
- LOD logic
- export state persistence

### Deliverables

- 3D Tiles export module
- export state model

### Acceptance Criteria

- export uses shared CRS or origin correctly
- export state is stored separately from shared project geo metadata
- output can be validated in a target viewer or test environment

---

## Phase 5 - CityGML Export

### Goal

Add semantic export workflows using shared geo metadata and PLATEAU-specific helpers.

### Tasks

- semantic mapping
- attribute mapping
- codelist mapping
- XML writing
- export validation
- module state persistence

### Deliverables

- CityGML export module
- validation for export readiness
- module-specific mapping state

### Acceptance Criteria

- exporter can use shared CRS or origin state correctly
- module-specific settings do not pollute `GeoProjectInfo`
- output is structurally valid for the targeted schema or profile
