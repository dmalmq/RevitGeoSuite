# Implementation Phases

## Phase 1A — Skeleton and Core Contracts

### Goal

Establish the solution structure, core contracts, shell, and initial test setup.

### Tasks

- Create solution and project structure
- Create Core, RevitInterop, SharedUI, and Shell projects
- Define `GeoProjectInfo`
- Define `CrsReference`
- Define `ICrsRegistry` / `CrsRegistry`
- Define `IRevitGeoModule`
- Set up shell with empty ribbon
- Set up test projects

### Deliverables

- buildable solution skeleton
- shell add-in visible in Revit
- empty ribbon tab
- core contracts committed to repo

### Acceptance Criteria

- solution builds cleanly
- add-in loads in target Revit version
- shell ribbon appears
- core contracts and test projects exist

---

## Phase 1B — Storage, Mesh, and Versioning Basics

### Goal

Add versioned storage, module state contracts, mesh calculation, and initial validation primitives.

### Tasks

- define `IGeoProjectInfoStore`
- define `IModuleStateStore`
- implement `GeoProjectInfoStorage`
- add schema versioning
- implement `IMeshCalculator`
- add initial validation primitives
- write core tests
- document storage schema v1

### Deliverables

- versioned project metadata storage
- module state store contract
- mesh calculator foundation
- initial validation primitives
- core tests

### Acceptance Criteria

- shared project metadata saves and loads correctly
- schema version is persisted
- basic mesh calculation works
- tests pass for core storage/mesh/validation basics

---

## Phase 2 — Georeference Module

### Goal

Deliver the first real user-facing module for CRS selection, map-based point picking, preview, and safe placement application.

### Tasks

- build `IRevitGeoPlacementService`
- implement reader/writer for Revit location state
- build CRS picker UI
- build map control
- implement point selection
- implement placement preview generation
- implement survey/project base workflow
- save `GeoProjectInfo`

### Deliverables

- georeference module
- placement preview workflow
- versioned shared geo metadata persistence

### Acceptance Criteria

- user can choose and save a CRS reference
- user can select a point from the map
- user sees current vs proposed values
- no change is committed without confirmation
- shared geo metadata reloads correctly

---

## Phase 3 — Mesh Inspector + Validation

### Goal

Add mesh or grid inspection and a lightweight project health or QA module early in the roadmap.

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

## Phase 4 — PLATEAU Context Import

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

## Phase 5 — 3D Tiles Export

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

## Phase 6 — CityGML Export

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
