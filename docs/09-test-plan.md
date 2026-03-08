# Test Plan

## Purpose

This document defines how the suite foundation and modules will be tested during development and before release.

## Test Categories

- fixture tests
- unit tests
- integration tests
- manual workflow tests
- regression tests
- storage and versioning tests

## Tests by Milestone

### V1 Milestone A - Foundation and Fixtures

- Build or compile test: solution builds cleanly
- Dependency graph check: no cross-module references
- Core contract unit tests: construction, defaults, and serialization
- Fixture verification: known CRS, transform, and migration fixtures load correctly

### V1 Milestone B - Read + Preview Workflow

- Integration: preview model generation given a mock project state
- Unit: current-state read model is mapped correctly
- Unit: preview warnings appear for suspicious input
- Manual Revit workflow: CRS selection, map point selection, preview-only flow

### V1 Milestone C - Apply + Persist

- Integration: apply workflow calls correct Revit interop operations
- Manual Revit workflow: CRS selection, map point selection, preview, apply
- Smoke test: existing setup warning appears when metadata exists
- Persistence test: saved `GeoProjectInfo` reloads correctly

### Phase 2 - Mesh Inspector + Validation

- Unit: neighbor mesh list for sample points
- Manual: UI displays primary and neighbor mesh codes
- Manual: validation warnings appear without modifying the document

### Phase 3 - PLATEAU Context Import

- Unit: codelist parsing and tile index lookups with fixtures
- Integration: import state persistence separate from `GeoProjectInfo`
- Manual: import a small PLATEAU sample and confirm elements exist

### Phase 4 - 3D Tiles Export

- Unit: geometry extraction on a sample model
- Golden file: tileset JSON and GLB shape checks (not bit-exact)
- Manual: load output in a viewer and confirm alignment

### Phase 5 - CityGML Export

- Unit: semantic and attribute mapping for sample categories
- Schema validation: generated CityGML passes XML schema checks
- Manual: spot-check a small export for geometry and attribute integrity

## Fixture Requirements

The project should maintain fixture data early, before the apply workflow is enabled.

Required fixtures:

- sample CRS lookup cases for Japanese presets
- known lat/lon to projected coordinate samples with tolerance targets
- known mesh calculation samples
- serialized `GeoProjectInfo` examples for round-trip tests
- migration fixtures for version upgrades
- one or more sample Revit test files for manual validation

## Unit Tests

Focus on logic that does not require Revit UI execution.

### Core / CRS / Conversion Tests

- known CRS selection loads correctly
- EPSG lookup returns expected metadata
- CRS references resolve correctly through the registry
- axis-order normalization behaves correctly
- coordinate conversion matches known sample points within acceptable tolerance
- invalid CRS input is rejected safely

### Preview / Workflow Tests

- `PlacementIntent` validates required inputs
- `PlacementPreview` shows current vs proposed values correctly
- preview warning generation behaves correctly for suspicious ranges
- command availability is gated until preview is complete

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
- static shell registration composes the Georeference module correctly

## Manual Test Scenarios

### Scenario 1 - Basic CRS Selection

1. Open suite
2. Choose EPSG:6677
3. Save and reopen workflow
4. Confirm CRS reference persists in workflow state or document after apply

Expected:

- CRS remains selected
- details shown correctly

### Scenario 2 - Map Point Selection

1. Open map view
2. Navigate to target site
3. Click a location
4. Confirm lat/lon and projected values are shown

Expected:

- selected point data is valid and visible

### Scenario 3 - Preview Before Apply

1. Select point
2. choose setup intent
3. review preview screen

Expected:

- current and proposed values are both shown
- apply is not automatic
- preview clearly states what will and will not change

### Scenario 4 - Apply Placement

1. confirm preview
2. apply operation
3. inspect result in project

Expected:

- operation succeeds or fails cleanly
- summary is logged
- shared geo metadata is updated

### Scenario 5 - Existing Setup Warning

1. Open a project with existing coordinate data
2. start workflow

Expected:

- tool warns that existing setup may already be present

### Scenario 6 - Suspicious Input Warning

1. choose invalid or clearly unrealistic point or data
2. attempt preview or apply

Expected:

- warning is shown
- operation is blocked or strongly gated depending on severity

### Scenario 7 - Module Reuse of Shared Metadata

1. Complete georeference workflow
2. Open mesh inspector or validation module
3. Confirm shared metadata is reused correctly

Expected:

- later module reads shared metadata without requiring manual re-entry

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
- static shell composition still works for V1

## Mock and Test Strategy by Layer

### Core Tests - Pure Unit Tests

Core has zero external dependencies. Tests run without Revit, without UI, and without file system access beyond fixtures.

What to test:

- `CrsRegistry`: EPSG lookup, Japanese presets, invalid code handling
- `CoordinateTransformer`: known sample point conversions with tolerance assertions
- `JapanMeshCalculator`: mesh code calculation for known lat/lon pairs
- `MeshNeighborResolver`: neighbor mesh list for sample codes
- `GeoProjectInfo`: serialization round-trip with Newtonsoft.Json
- `MigrationRunner`: version migration produces valid output
- `CoordinateValidator`: suspicious coordinate detection
- `PlacementPreview` builders: current/proposed diff behavior

### RevitInterop Tests - Interface Mocks Only

RevitInterop wraps Revit API calls behind interfaces. Tests for code that depends on RevitInterop should mock these interfaces. No Revit runtime is needed in CI.

Key interfaces to mock:

- `IRevitGeoPlacementService`
- `IGeoProjectInfoStore`
- `IModuleStateStore`

What not to test in CI:

- actual Revit API behavior
- Extensible Storage read/write against a live Revit document

### Module Tests - ViewModel and Service Tests

Test module logic against mocked Core and RevitInterop interfaces.

What to test:

- ViewModel state transitions (`CRS selected -> point selected -> preview ready -> applied`)
- `PlacementPreviewService` generates correct before/after data from mock inputs
- `SiteSelectionService` stores and converts coordinates
- command availability (`Apply` disabled until preview confirmed)

### Manual Tests - Require Revit 2024

Manual tests follow the scenarios defined earlier in this document. They require:

- Revit 2024 installed
- a sample `.rvt` project file
- human verification of UI behavior and Revit document state

When to run:

- before each release
- after changes to RevitInterop layer
- after changes to placement workflow

## Tolerance / Precision Note

Tests should document acceptable tolerance rather than assuming exact equality for all coordinate conversions.

Recommended tolerances:

- coordinate transforms: 0.001 meters (1 mm)
- angle comparisons: 0.0001 radians
- mesh code calculations: exact match
