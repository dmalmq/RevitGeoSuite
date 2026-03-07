# Test Plan

## Purpose

This document defines how the suite foundation and modules will be tested during development and before release.

## Test Categories

- unit tests
- integration tests
- manual workflow tests
- regression tests
- storage or versioning tests

## Tests by Phase

### Phase 1A — Skeleton and Core Contracts

- Build or compile test: solution builds cleanly
- Dependency graph check: no cross-module references
- Core contract unit tests: construction, defaults, and serialization

### Phase 1B — Storage, Mesh, and Versioning Basics

- `GeoProjectInfo` save or load round-trip
- Schema version persisted and migration v0 → v1 succeeds
- Mesh code calculation for known sample points
- Validation primitives flag obvious invalid states

### Phase 2 — Georeference Module

- Integration: preview model generation given a mock project state
- Manual Revit workflow: CRS selection, map point selection, preview, apply
- Smoke test: existing setup warning appears when metadata exists

### Phase 3 — Mesh Inspector + Validation

- Unit: neighbor mesh list for sample points
- Manual: UI displays primary and neighbor mesh codes
- Manual: validation warnings appear without modifying the document

### Phase 4 — PLATEAU Context Import

- Unit: codelist parsing and tile index lookups with fixtures
- Integration: import state persistence separate from `GeoProjectInfo`
- Manual: import a small PLATEAU sample and confirm elements exist

### Phase 5 — 3D Tiles Export

- Unit: geometry extraction on a sample model
- Golden file: tileset JSON and GLB shape checks (not bit-exact)
- Manual: load output in a viewer and confirm alignment

### Phase 6 — CityGML Export

- Unit: semantic and attribute mapping for sample categories
- Schema validation: generated CityGML passes XML schema checks
- Manual: spot-check a small export for geometry and attribute integrity

## Unit Tests

Focus on logic that does not require Revit UI execution.

### Core / CRS / Conversion Tests

- known CRS selection loads correctly
- EPSG lookup returns expected metadata
- CRS references resolve correctly through the registry
- axis-order normalization behaves correctly
- coordinate conversion matches known sample points within acceptable tolerance
- invalid CRS input is rejected safely

### Mesh Tests

- mesh code calculation returns expected values for known sample points
- neighboring mesh logic behaves correctly
- stale mesh logic or invalidation rules can be validated where applicable

### Validation Tests

- suspicious coordinate values are flagged
- missing required inputs are flagged
- unsupported operations return understandable errors
- derived export readiness behaves correctly for common states

### Persistence Tests

- CRS reference is saved and loaded correctly
- shared project metadata is saved with schema version
- module-specific state is saved and loaded separately
- migrations preserve valid data
- audit record serialization works

## Integration Tests

Integration tests should focus on boundaries between modules, shared services, and Revit interop where practical.

Examples:

- placement preview model can be created using sample project state
- apply workflow calls the correct Revit interop operations
- shared geo metadata can be read by later modules
- failed operations are reported cleanly
- version incompatibility handling in module registration behaves as expected

## Manual Test Scenarios

### Scenario 1 — Basic CRS Selection

1. Open suite
2. Choose EPSG:6677
3. Save and reopen workflow
4. Confirm CRS reference persists

Expected:

- CRS remains selected
- details shown correctly

### Scenario 2 — Map Point Selection

1. Open map view
2. Navigate to target site
3. Click a location
4. Confirm lat or lon and projected values are shown

Expected:

- selected point data is valid and visible

### Scenario 3 — Preview Before Apply

1. Select point
2. choose placement-related operation
3. review preview screen

Expected:

- current and proposed values are both shown
- apply is not automatic

### Scenario 4 — Apply Placement

1. confirm preview
2. apply operation
3. inspect result in project

Expected:

- operation succeeds or fails cleanly
- summary is logged
- shared geo metadata is updated

### Scenario 5 — Existing Setup Warning

1. Open a project with existing coordinate data
2. start workflow

Expected:

- tool warns that existing setup may already be present

### Scenario 6 — Suspicious Input Warning

1. choose invalid or clearly unrealistic point or data
2. attempt preview or apply

Expected:

- warning is shown
- operation is blocked or strongly gated depending on severity

### Scenario 7 — Module Reuse of Shared Metadata

1. Complete georeference workflow
2. Open mesh inspector or validation module
3. Confirm shared metadata is reused correctly

Expected:

- later module reads shared metadata without requiring manual re-entry

## Test Data

The project should maintain:

- sample CRS test cases
- one or more sample Revit test files
- known coordinate samples for Japanese CRS values
- mesh calculation samples
- expected output snapshots for selected operations where practical
- storage migration test fixtures as versions evolve

## Regression Checklist

Before release or major refactor:

- shell add-in loads in target Revit version
- CRS selection still works
- map point selection still works
- preview still appears before apply
- logging still records operations
- warnings still trigger for suspicious conditions
- shared project metadata still loads correctly
- module-specific state remains isolated
- version checks still behave correctly

## Tolerance / Precision Note

Tests should document acceptable tolerance rather than assuming exact equality for all coordinate conversions.
