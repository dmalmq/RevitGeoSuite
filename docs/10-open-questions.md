# Open Questions

## Product / UX

- What should the final suite product name be?
- Should the georeferencing workflow be wizard-style or a single advanced dialog?
- How much explanation should be visible by default for beginner users?
- Should the first module support “review existing setup” separately from “create new setup” in V1?

## Technical

- Should the map be embedded in a WPF WebView2 control or opened in a separate companion window?
- Which CRS library or EPSG data source should be used?
- What is the exact persisted CRS reference shape?
- What Revit versions should be supported first?
- How should the shell perform module discovery in the simplest reliable way?

## Revit API

- What is the safest exact API strategy for survey point or project base point related operations?
- How should the tool detect meaningful pre-existing coordinate setup?
- What type of rotation should V1 actually perform?
- Should linked model behavior be ignored, detected, or partially supported in V1?
- What should happen when canonical location data changes but persisted convenience values such as mesh codes become stale?

## Data / Accuracy

- What confidence language should be shown when placement is based only on OSM?
- Should the tool explicitly label OSM-based placement as approximate?
- What validation thresholds count as suspicious for offsets and coordinate ranges?

## Future Expansion

- When should PLATEAU import move from lightweight context to more advanced semantics?
- When should parcel, shapefile, or GeoPackage overlays be introduced?
- Should official base maps or aerial imagery be supported later?
- Should footprint extraction from Revit geometry become a later phase?
- Should the GeoPackage exporter be migrated into the suite after the first modules are stable?
- Should IMDF-related tooling eventually become a suite module?

## Recommended First Answers

For initial development, a practical starting position is:

- wizard-style workflow
- one primary target Revit version
- OSM-based map in embedded WebView2 if feasible
- explicit “approximate map context” wording
- rotation support limited and clearly defined in V1
- keep module discovery simple and predictable in the first implementation
