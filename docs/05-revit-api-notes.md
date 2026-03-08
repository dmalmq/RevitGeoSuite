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

## Questions to Investigate in Implementation — Resolved for Revit 2024

### Which Revit API classes best represent project location and coordinate-related operations?

**Answer (Revit 2024):**

- `BasePoint` class (in `Autodesk.Revit.DB`): use `BasePoint.GetProjectBasePoint(doc)` and `BasePoint.GetSurveyPoint(doc)` to access the two key base points. These are `Element` subclasses with parameters for position and angle.
- `ProjectLocation`: accessed via `doc.ActiveProjectLocation`. Contains `ProjectPosition` data.
- `ProjectPosition`: holds `EastWest`, `NorthSouth`, `Elevation`, and `Angle` (true north rotation in radians).
- `SiteLocation`: accessed via `doc.SiteLocation`. Holds `Latitude`, `Longitude`, and `TimeZone`.

**Reading current state:**

```csharp
// Survey Point
var surveyPoint = BasePoint.GetSurveyPoint(doc);
var spPosition = surveyPoint.Position; // XYZ in internal coordinates

// Project Base Point
var projectBasePoint = BasePoint.GetProjectBasePoint(doc);
var pbpPosition = projectBasePoint.Position;

// Project Location (shared coordinates origin)
var projectLocation = doc.ActiveProjectLocation;
var projectPosition = projectLocation.GetProjectPosition(XYZ.Zero);
// projectPosition.EastWest, .NorthSouth, .Elevation, .Angle
```

**Writing placement:**

```csharp
using (var tx = new Transaction(doc, "Set Project Location"))
{
    tx.Start();

    var projectLocation = doc.ActiveProjectLocation;
    var newPosition = new ProjectPosition(
        eastWest,    // in feet (Revit internal units)
        northSouth,  // in feet
        elevation,   // in feet
        angle        // true north angle in radians
    );
    projectLocation.SetProjectPosition(XYZ.Zero, newPosition);

    tx.Commit();
}
```

### What is the safest way to read and update survey point-related data?

**Answer:** Use `BasePoint.GetSurveyPoint(doc)` for reading. For writing, modify `ProjectPosition` via `ProjectLocation.SetProjectPosition()`. Do not move the survey point element directly unless you specifically need to offset it from the internal origin. Prefer working through `ProjectLocation` + `ProjectPosition` for the standard georeferencing workflow.

### What API differences exist across supported Revit versions?

**Answer:** V1 targets Revit 2024 only. The `BasePoint.GetProjectBasePoint()` and `BasePoint.GetSurveyPoint()` static methods are available from Revit 2021+. For Revit 2020 and earlier, you would need to use `FilteredElementCollector` to find base points by category. Since we target 2024 only, use the modern static methods.

### How should undo/rollback be handled?

**Answer:**
- Use a single `Transaction` for the apply operation. If any step fails, call `tx.RollBack()` to revert all changes atomically.
- Use `TransactionGroup` when you need to preview changes: start a `TransactionGroup`, run a `Transaction` inside it, read the results for preview, then `RollBack()` the group to undo everything.

```csharp
// Preview pattern using TransactionGroup
using (var tg = new TransactionGroup(doc, "Preview Placement"))
{
    tg.Start();

    using (var tx = new Transaction(doc, "Temp Apply"))
    {
        tx.Start();
        // Apply changes temporarily
        tx.Commit();
    }

    // Read the resulting state for preview display
    var previewData = ReadCurrentState(doc);

    tg.RollBack(); // Undo everything — document unchanged
    return previewData;
}
```

### What project state conditions should block an operation?

**Answer:**
- Document is read-only or workshared and not editable
- Another user owns the project location elements (in workshared models)
- Active transaction already in progress
- Document is a family document (not a project)

Check with:

```csharp
if (doc.IsFamilyDocument) // block
if (doc.IsReadOnly) // block
```

### How can preview values be calculated before transaction commit?

**Answer:** Use the `TransactionGroup` rollback pattern shown above. Alternatively, for pure math previews (converting coordinates, computing angles), do the math in Core without touching the Revit document at all. Only use `TransactionGroup` when you need to see what Revit would actually do (e.g., how linked models would move).

### How should versioned Extensible Storage be migrated across schema revisions?

**Answer:** Extensible Storage schemas are identified by GUID. For migration:

1. Keep old schema GUIDs registered so you can read legacy data
2. On load, check `SchemaVersion` field in the stored data
3. If version is old, read with the old schema, migrate in memory, write with the new schema
4. Delete the old schema entity after successful migration

```csharp
// Migration pattern
var entity = element.GetEntity(oldSchema);
if (entity.IsValid())
{
    var oldData = DeserializeV0(entity);
    var newData = MigrateV0ToV1(oldData);

    using (var tx = new Transaction(doc, "Migrate Storage"))
    {
        tx.Start();
        element.DeleteEntity(oldSchema);
        WriteWithNewSchema(element, newData);
        tx.Commit();
    }
}
```

### Mock strategy for testing

- **Core tests:** Pure unit tests. No Revit dependency. Test `CrsRegistry`, `CoordinateTransformer`, `MeshCalculator`, `GeoProjectInfo` serialization directly.
- **RevitInterop tests:** Define interfaces (`IRevitGeoPlacementService`, `IGeoProjectInfoStore`). Test modules against mocked interfaces. Do not require Revit runtime in CI.
- **Module tests:** Test ViewModels and services against mocked interop interfaces. ViewModels receive interfaces via constructor injection.
- **Manual tests:** Require Revit 2024 installation. Follow scenarios in `docs/09-test-plan.md`.

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
