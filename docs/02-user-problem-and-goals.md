# User Problem and Goals

## Current Problems

### 1. Coordinate setup in Revit is hard to understand

Many users do not clearly understand the relationship between:

- Survey Point
- Project Base Point
- Shared Coordinates
- Internal Origin
- True North
- Project North

This causes confusion and inconsistent office workflows.

### 2. Users lack visual context during setup

Coordinate setup is often performed using typed coordinates, imported CAD data, or incomplete instructions. Without map context, it is hard to confirm whether the model is placed and rotated correctly.

### 3. Errors propagate downstream

A small georeferencing error in Revit can cause larger problems in:

- GIS exports
- IMDF generation
- web maps
- Cesium / 3D Tiles workflows
- PLATEAU alignment
- CityGML export workflows
- site planning and digital twin platforms

### 4. Japanese coordinate systems add extra complexity

Projects using Japanese CRS values such as EPSG:6677 and related systems require additional care, especially if users are unfamiliar with projected systems, units, axis order, or how Revit expects coordinate-related behavior.

### 5. Teams do not always document setup decisions

When a model is misaligned later, it is often hard to know:

- which coordinate system was used
- what point was chosen as a reference
- whether the model was rotated
- who performed the setup
- whether the placement was based on OSM, survey drawings, or another source
- which confidence level should be assigned to the setup

### 6. Revit tools are usually isolated

Standalone tools often solve one problem but do not share location, coordinate, validation, or provenance information. This leads to repeated setup work and inconsistent results across import, validation, and export workflows.

## User Goals

Users want to:

- Choose a project CRS safely and clearly
- See map context while placing a model
- Select a location visually instead of relying only on numbers
- Set survey point and project base point with confidence
- Align a building rotation or orientation to real-world context
- Avoid accidental coordinate mistakes
- Save and repeat a consistent workflow
- Produce better downstream exports and map alignment
- Reuse project geo metadata across later tools such as mesh lookup, PLATEAU import, and export modules

## Business / Team Goals

The organization wants to:

- Reduce time spent troubleshooting placement issues
- Make coordinate setup less dependent on a few experts
- Create a repeatable team standard
- Improve confidence in BIM-to-GIS, BIM-to-PLATEAU, and BIM-to-web workflows
- Record setup decisions for QA and handover
- Build a platform that can expand into additional workflows without rewriting earlier tools

## Non-Goals

This suite is not intended to:

- Replace official survey workflows where legal/survey-grade accuracy is required
- Serve as a full GIS platform inside Revit
- Replace authoritative government base maps or survey control points
- Automatically solve all alignment problems without user review
- Guarantee perfect map accuracy from OpenStreetMap alone
- Require all modules to be installed before any value is delivered

## Assumptions

- Users have a Revit model open and want to georeference it or perform geo-related workflows
- Users may have little or moderate understanding of coordinate systems
- OSM is primarily used for visual context and approximate alignment
- Official drawings or survey data may still override OSM-based placement
- Later modules should be able to read shared project geo metadata created by earlier workflows
