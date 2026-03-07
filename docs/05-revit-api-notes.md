# Revit API Notes

## Purpose

This document captures Revit-specific concerns, terminology, risks, and API investigation areas for the suite, especially the georeferencing foundation and future geo-aware modules.

## Key Revit Concepts to Handle Carefully

- Internal Origin
- Survey Point
- Project Base Point
- Shared Coordinates
- Project Location
- Site Location
- True North
- Project North
- Linked model placement implications
- Extensible Storage versioning and migration

## Risks

### Survey Point / Project Base Point semantics are often misunderstood

The suite must not assume users understand these concepts. The UI and documentation should explain them clearly.

### Revit coordinate behavior may differ from user expectations

Some coordinate-related operations can appear simple conceptually but behave differently in Revit depending on project state, shared coordinates, or existing site/project settings.

### Rotation can be ambiguous

Users may mean:

- rotate the building geometry
- rotate project north
- align true north
- change only reference/orientation metadata

The module must define exactly what type of rotation it performs.

### Existing project conditions may already contain coordinate data

The tool should detect and report whether the model already appears to have coordinate setup. It should avoid overwriting meaningful existing values without a warning.

### Placement execution needs a strong boundary

The Revit-facing placement service should be treated as an execution boundary. Workflow planning and preview generation should stay above it; actual document modification should remain encapsulated inside the Revit interop layer.

## Questions to Investigate in Implementation

- Which Revit API classes best represent project location and coordinate-related operations?
- What is the safest way to read and update survey point-related data?
- What API differences exist across supported Revit versions?
- How should undo/rollback be handled?
- What project state conditions should block an operation?
- How can preview values be calculated before transaction commit?
- How should versioned Extensible Storage be migrated across schema revisions?

## Transaction Rules

- All modifying operations must be wrapped in a proper Revit transaction
- Read-only preview logic should remain outside modifying transactions when possible
- Failed coordinate changes should not leave the model in a partially updated state
- Storage migrations must not silently corrupt project metadata

## Safety Rules

- Detect existing coordinate setup before applying a new one
- Show current values before changing them
- Show proposed values before changing them
- Ask for confirmation before commit
- Log each applied operation
- Version persisted geo metadata from day one

## Compatibility Target

Initial target should be one or two specific Revit versions first, then expand later. Recommended approach:

- choose one primary supported version for development
- keep version-specific behavior documented as discovered
- make module compatibility explicit in the shell/module registration process

## Related Future Concerns

- linked models and shared coordinate propagation
- export workflows dependent on project location
- IFC / GIS / CityGML / 3D Tiles export interaction
- PLATEAU context import and external reference tagging
- multi-site or campus workflows
