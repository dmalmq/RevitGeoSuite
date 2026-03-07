# Scope — Version 1

## Goal of V1

Deliver a usable first version of the suite foundation and first module so users can choose a coordinate system, visualize site context on a map, pick a reference point, and safely apply coordinate-related placement settings in Revit while saving stable shared geo metadata to the document.

## V1 Release Shape

V1 is not the entire suite. It is the first shippable combination of:

- Shared Core
- Revit Interop layer
- Shared UI layer
- Shell / Host add-in
- Georeference module

## Must-Have Features

### Shared Foundation

- Shared geo core with CRS, transforms, mesh logic foundations, validation primitives, and stable project metadata model
- Revit interop layer for document access, transactions, storage, and placement execution
- Shell add-in that hosts the ribbon and module registration
- Versioned project-linked storage for shared geo metadata

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

- User can review the chosen point and coordinates before applying changes
- User can choose whether to use the picked point for survey point and/or project base point related workflows
- User can optionally specify model rotation or north alignment as part of setup
- The tool explains what each operation means before the user applies it

### Preview and Safety

- Show a before/after preview summary before applying changes
- Warn the user if values look suspicious
- Require explicit confirmation before modifying coordinates or alignment-related settings

### Persistence and Logging

- Save CRS reference and shared setup metadata to the Revit document
- Save origin, orientation, provenance, and confidence level
- Save a summary of what was applied

## Nice-to-Have Features for V1 (only if low effort)

- Address or place search in the map
- Recent CRS list
- Basic reset function
- Exportable setup summary as text or JSON
- Initial mesh code calculation hook if it does not expand scope too much

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
- Leave room for future modules without redesigning the foundation

## Open Scope Decisions

These still need confirmation:

- whether map UI is embedded in Revit or opened in a companion window
- how much rotation support should be included in V1
- exact minimum supported Revit version
