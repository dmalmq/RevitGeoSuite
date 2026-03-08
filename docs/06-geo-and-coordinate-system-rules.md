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

## Mesh Code Boundary Calculation

Japan's Standard Regional Mesh (JIS X 0410) divides the country into a hierarchy of grid cells. Each level subdivides the parent cell. The mesh code digits encode the cell's position, allowing direct computation of lat/lon boundaries without a lookup table.

### Mesh Levels

| Level | Name | Code digits | Lat span | Lon span |
|---|---|---|---|---|
| Primary (1次) | 1st mesh | 4 digits (AABB) | 40' (2/3°) | 1° |
| Secondary (2次) | 2nd mesh | 6 digits (AABBCC) | 5' (1/12°) | 7.5' (1/8°) |
| Tertiary (3次) | 3rd mesh | 8 digits (AABBCCDD) | 30" (1/120°) | 45" (1/80°) |

PLATEAU uses **tertiary mesh codes** (8 digits, ~1km squares) for CityGML tile organization.

### Decoding a Tertiary Mesh Code

Given an 8-digit code `AABBCCDD`:

- `AA` = primary lat code → lat_base = AA × 2/3 (degrees)
- `BB` = primary lon code → lon_base = BB + 100 (degrees)
- `C` (first digit of CC pair) = secondary lat index (0–7) → lat_base += C × 5/60
- `C` (second digit of CC pair) = secondary lon index (0–7) → lon_base += C × 7.5/60
- `D` (first digit of DD pair) = tertiary lat index (0–9) → lat_base += D × 30/3600
- `D` (second digit of DD pair) = tertiary lon index (0–9) → lon_base += D × 45/3600

The result is the **southwest corner**. Add the tertiary span (30" lat, 45" lon) to get the northeast corner.

### Example: Mesh Code 53394611

```
AA=53, BB=39 → lat_base = 53 × 2/3 = 35.333...°, lon_base = 39 + 100 = 139°
CC=46       → C1=4, C2=6 → lat_base += 4 × 5/60 = +0.333...°, lon_base += 6 × 7.5/60 = +0.75°
DD=11       → D1=1, D2=1 → lat_base += 1 × 30/3600 = +0.00833°, lon_base += 1 × 45/3600 = +0.0125°

SW corner: (35.675°, 139.7625°)
NE corner: (35.683333°, 139.775°)
```

### Implementation Notes

- `JapanMeshCalculator` in Core implements this formula for all three levels
- `MeshNeighborResolver` computes the 8 adjacent mesh codes by incrementing/decrementing the tertiary indices and handling rollover into secondary/primary levels
- For the mesh grid display, the calculator produces 9 bounding boxes (primary + 8 neighbors) which are converted to GeoJSON polygons (see `docs/DECISIONS.md` — Mesh Grid Display)

---

## Future Extensions

Future versions may add:

- mesh/grid inspector workflows
- PLATEAU tile lookup based on mesh
- shapefile / GeoPackage overlays
- control point workflows
- official base map integration
- site boundary import
- footprint-to-map overlay comparison
