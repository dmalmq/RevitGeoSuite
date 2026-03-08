# Scope - Version 1

## Goal of V1

Deliver a usable first version of the suite foundation and the georeference workflow so users can choose a coordinate system, visualize site context on a map, pick a reference point, preview the effect of that choice, and then safely apply project location settings in Revit while saving stable shared geo metadata to the document.

## Codex-First Release Strategy

V1 is intentionally staged so the highest-risk behavior is implemented last.

### Milestone A - Foundation

Deliver the minimum buildable foundation:

- Shared Core contracts for CRS, transforms, project metadata, validation primitives, and mesh foundations
- RevitInterop contracts for document access, storage, and project location services
- SharedUI shell controls and styles
- Shell add-in with static registration of the Georeference module
- Fixture-backed tests for CRS, transforms, metadata, and migration behavior

### Milestone B - Read + Preview

Deliver the full user workflow up to the confirmation gate, but without modifying the document:

- searchable CRS picker with Japanese presets
- embedded OSM map for point selection
- current project location summary and existing setup warning
- selected point summary with latitude/longitude and projected coordinates
- `PlacementIntent` and `PlacementPreview` models for current vs proposed values
- suspicious-value warnings and explicit confidence/provenance labeling

This milestone is a valid internal release for workflow validation.

### Milestone C - Apply + Persist

Enable the actual write path:

- apply project location changes through `ProjectLocation.SetProjectPosition()`
- optional true north adjustment through `ProjectPosition.Angle`
- persist `GeoProjectInfo` and audit summary
- require explicit confirmation before commit
- fail atomically if the apply operation cannot complete

This milestone is the first external V1 candidate.

## V1 Release Shape

V1 is not the entire suite. The first public release is the combination of:

- Shared Core
- Revit Interop layer
- Shared UI layer
- Shell / Host add-in
- Georeference module

The shell keeps a future-ready module contract, but V1 wiring is static for the Georeference module.

## Must-Have Features

### Shared Foundation

- Shared geo core with CRS, transforms, mesh foundations, validation primitives, and stable project metadata contracts
- Revit interop layer for document access, transactions, storage, and project location execution
- Shell add-in that hosts the ribbon and statically registers the Georeference workflow
- Versioned project-linked storage for shared geo metadata from day one
- Fixture set for known CRS and coordinate samples

### Coordinate System Selection

- User can choose a CRS/EPSG code from a list
- User can search CRS values
- Common Japanese CRS presets are available
- The selected CRS is stored as a stable CRS reference in project-linked metadata

### Map-Based Site Context

- User can open a map view based on OpenStreetMap
- User can pan and zoom the map
- User can click a point on the map
- The clicked point returns latitude/longitude and projected coordinates in the chosen CRS

### Guided Placement Workflow

- User can review the chosen point and coordinates before any change is applied
- User can choose whether to apply project location, apply true north rotation, or save canonical geo metadata only
- The tool explains what each operation means before the user applies it
- V1 does not rotate building geometry and does not promise direct base point manipulation beyond the project location workflow Revit exposes safely

### Preview and Safety

- Show a before/after preview summary before applying changes
- Warn the user if values look suspicious
- Require explicit confirmation before modifying coordinates or orientation-related settings
- Detect potentially meaningful pre-existing setup before overwrite

### Persistence and Logging

- Save CRS reference and shared setup metadata to the Revit document
- Save origin, orientation, provenance, confidence level, and timestamps
- Save a summary of what was applied

## Nice-to-Have Features for V1 (Only If Low Effort)

- Address or place search in the map
- Recent CRS list
- Basic reset function after apply exists and is well understood
- Exportable setup summary as text or JSON

## Out of Scope for V1

- Full mesh inspector UI
- PLATEAU context import
- Shapefile / GeoPackage overlay
- Control-point adjustment workflows
- High-precision survey-grade validation
- Automatic footprint extraction from arbitrary geometry
- Multi-building campus orchestration
- Collaboration or cloud syncing
- Bidirectional GIS synchronization
- Dynamic assembly-scanning module discovery
- CityGML export
- 3D Tiles export

## V1 Quality Bar

A V1 release should:

- Build reliably
- Load in Revit successfully
- Work on sample test projects
- Have a clear user workflow
- Avoid dangerous silent changes
- Be understandable by a non-expert user
- Persist shared geo metadata in a stable, versioned way
- Keep preview logic testable without Revit where possible
- Leave room for future modules without redesigning the foundation

## Resolved V1 Boundaries

The following decisions are treated as fixed for Codex-first V1:

- map UI is embedded in Revit via WebView2
- target platform is Revit 2024 on .NET Framework 4.8
- beginner guidance defaults to verbose, not compact
- there is no separate review-only mode in the wizard
- V1 rotation support is limited to `ProjectPosition.Angle`
- the first implementation uses static shell wiring for the Georeference module
