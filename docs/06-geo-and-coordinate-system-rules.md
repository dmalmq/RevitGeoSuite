# Geo and Coordinate System Rules

## Purpose

This document defines how the suite handles CRS selection, coordinate conversion, persistence, precision, mesh usage, and map-based reference behavior.

## General Principles

- CRS handling must be explicit
- The selected CRS should always be visible to the user
- Persist a stable CRS reference, not the full CRS definition
- Axis order should be normalized consistently in the application
- Unit behavior must be documented and predictable
- OSM is a visual context source, not a guaranteed survey source

## Supported Coordinate Types

- Geographic coordinates (latitude/longitude)
- Projected coordinates (easting/northing or equivalent)
- Revit internal coordinate-related values as needed by placement workflows
- Mesh/grid identifiers derived from project location

## CRS Selection Rules

- Users choose a CRS before applying placement operations
- CRS definitions should include code, name, and relevant unit/datum details
- Common Japanese CRS values should be easy to find
- The application should persist a stable CRS reference, such as an EPSG code
- Full CRS definitions should be resolved from the registry at runtime rather than stored directly in shared project metadata

## Japan-Specific Notes

Initial support should include commonly used Japanese systems relevant to the user’s workflow, including examples like EPSG:6677. Additional Japan-specific concerns may include:

- local projected systems
- expected units in meters
- local plane rectangular coordinate systems
- mesh/grid systems used for data indexing or PLATEAU-related workflows

## Axis-Order Rules

Some CRS definitions and libraries may use different axis-order conventions. The application should define one normalized internal convention and consistently convert into or out of that convention. This must be documented and tested.

## Unit Rules

- Projected coordinates should be treated consistently in expected CRS units
- Revit-side operations must clearly define how those coordinates are transformed or interpreted
- Any unit conversion must be explicit and testable

## OSM Usage Rules

OpenStreetMap is used in this product primarily for:

- site discovery
- visual context
- rough alignment support
- user confidence during placement

OpenStreetMap is not treated as:

- legal survey truth
- guaranteed authoritative building edge geometry
- final control point data

## Conflict Rule

If map context conflicts with official drawings, survey data, or trusted control points, authoritative project data should win.

## Persistence Rules

Shared project metadata should persist:

- CRS reference
- project origin
- true north angle
- primary mesh code if used as a stable convenience key
- confidence level
- setup source
- timestamps

Shared project metadata should not persist:

- full CRS definitions
- validation snapshots
- preview objects
- export readiness summaries
- neighbor mesh lists

## Source of Truth Rule

The canonical source of location truth is:

1. CRS reference
2. origin
3. orientation

Primary mesh code may be persisted for convenience across modules, but it is conceptually derived from the canonical location state. If origin or CRS changes, mesh should be recomputed or invalidated.

## Precision Expectations

V1 should target practical placement assistance, not survey-grade certification. Precision expectations should be documented honestly. The suite should avoid overstating confidence.

## Validation Rules

The suite should flag suspicious conditions such as:

- very large offsets
- unexpected coordinate ranges
- invalid or missing CRS references
- impossible or highly suspicious projected values
- operations based on incomplete input
- stale derived values after canonical state changes

## Future Extensions

Future versions may add:

- mesh/grid inspector workflows
- PLATEAU tile lookup based on mesh
- shapefile / GeoPackage overlays
- control point workflows
- official base map integration
- site boundary import
- footprint-to-map overlay comparison
