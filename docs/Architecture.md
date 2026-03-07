# Revit Geo Suite — Architecture & Module Structure (v3)

**Companion to:** Revit Geo Suite Vision Document  \
**Date:** March 2026  \
**Status:** Planning  \
**Version:** 3.0 — Refined after architecture review

---

## The Key Architecture Decision

**Don't build a chain. Build a shared foundation with independent modules on top.**

A strict chain (Georeferencing → Mesh Inspector → PLATEAU Import → 3D Tiles Export → CityGML Export) creates rigid dependencies where you cannot install Module 3 without Module 2 and Module 1. That is fragile and limits adoption.

Instead:

```
┌─────────────────────────────────────────────────┐
│ Revit Ribbon UI                                 │
│ (Shell / Host Add-in)                           │
├─────────┬──────────┬──────────┬────────┬────────┤
│ Geo     │ Mesh     │ PLATEAU  │ 3D     │ CityGML│
│Reference│ Inspector│ Context  │ Tiles  │ Export │
│ Module  │ Module   │ Import   │ Export │        │
├─────────┴──────────┴──────────┴────────┴────────┤
│ Shared UI (WPF controls, styles)                │
├─────────────────────────────────────────────────┤
│ Revit Interop Layer                             │
│ Document access · Transactions · Element utils  │
│ GeoPlacement execution · Extensible Storage     │
├──────────────────────┬──────────────────────────┤
│ Core                 │ Core.Plateau             │
│ CRS · Transforms     │ Codelists · Tile Index   │
│ Mesh · ProjectMeta   │ Schema helpers           │
│ Validation · Log     │                          │
└──────────────────────┴──────────────────────────┘
```

Every module depends **down** on the layers beneath it. Modules **never** depend on each other directly. They communicate through Core's shared services and the `GeoProjectInfo` contract.

---

## Why This Structure

| Concern | Chain Architecture | Shared Core Architecture |
|---|---|---|
| Install Module 3 alone? | No — need 1 and 2 | Yes — just needs Core |
| Ship Module 1 first? | Yes | Yes |
| Module 2 uses data from Module 1? | Direct dependency | Both read/write to Core's shared project metadata |
| One module breaks? | Everything above it breaks | Only that module breaks |
| Testing? | Must test the whole chain | Test each module in isolation against Core |
| Open-source one module? | Hard — tangled dependencies | Easy — one module + Core |

---

## Architecture Review: Key Design Principles

The following principles should be maintained throughout development.

### 1. GeoProjectInfo Stays Small and Stable

`GeoProjectInfo` is the shared contract between all modules. It contains **only truly cross-module canonical geo facts**.

Module-specific state such as:
- import history
- export settings
- cache metadata
- mapping overrides
- recent output paths

belongs in a separate per-module state pattern, not in `GeoProjectInfo`.

**Persist:** CRS reference, origin, orientation, primary mesh code, confidence, timestamps, provenance  \
**Derive at runtime:** neighbor meshes, validation results, export readiness, previews

---

### 2. Core Stays Generic — PLATEAU Logic Gets Its Own Library

Core should remain a general-purpose geospatial foundation, not tied to one ecosystem.

PLATEAU-specific logic lives in `Core.Plateau`, which can hold:
- codelist parsing
- tile index logic
- PLATEAU constants
- schema helpers

Modules that do not need PLATEAU should depend only on Core.

---

### 3. Service Interfaces Defined Early

Core exposes key services through interfaces for clean boundaries and testability.

Examples:
- `ICrsRegistry`
- `ICoordinateTransformer`
- `IMeshCalculator`
- `IGeoProjectInfoStore`
- `IModuleStateStore`

Not every class needs an interface, but the most important shared services should have one early.

---

### 4. Revit Interop Is API Only — UI Is Separate

The Revit interop layer handles:
- document access
- transactions
- Extensible Storage
- coordinate-related Revit reads/writes
- element helpers

Shared WPF controls live in `SharedUI`.

This keeps the Revit layer focused and avoids mixing API concerns with presentation.

---

### 5. Georeferencing Logic Is a Real Service, But It Is the Execution Boundary

Survey point / project base point / orientation logic is critical and subtle.

It should be modeled as a proper domain-facing service:
- `IRevitGeoPlacementService`
- `ProjectLocationReader`
- `ProjectLocationWriter`

Important boundary:
- **Modules / application services decide what should happen**
- **RevitGeoPlacementService safely reads/applies it in Revit**

Preview generation, intent planning, and workflow decisions should stay above the Revit interop layer.

---

### 6. Modules and Storage Are Versioned

Every module declares:
- module version
- minimum required Core version
- minimum required Revit version

Extensible Storage schemas are versioned from day one, with migration utilities planned early.

---

### 7. Validation Ships Early

A lightweight **Check Project** QA tool should ship early.

Even basic checks such as:
- has CRS?
- has origin?
- has confidence level?
- suspicious coordinates?
- mesh assigned?

provide immediate value and help build trust in the suite.

---

### 8. SharedUI Stays Presentational

`SharedUI` should contain:
- reusable controls
- styles/themes
- simple display converters

It should **not** become a hidden business logic layer.

Workflow logic belongs in modules or application services, not in reusable controls.

---

## Solution Structure (C# / Visual Studio)

```
RevitGeoSuite/
│
├── RevitGeoSuite.sln
│
├── src/
│   │
│   ├── RevitGeoSuite.Core/                ← Generic geo foundation (no Revit, no PLATEAU)
│   │   ├── Coordinates/
│   │   │   ├── ICrsRegistry.cs
│   │   │   ├── CrsRegistry.cs
│   │   │   ├── CrsDefinition.cs
│   │   │   ├── CrsReference.cs            ← Small persisted reference (EPSG code + optional name snapshot)
│   │   │   ├── ICoordinateTransformer.cs
│   │   │   ├── CoordinateTransformer.cs
│   │   │   └── JapanCrsPresets.cs
│   │   ├── Mesh/
│   │   │   ├── IMeshCalculator.cs
│   │   │   ├── JapanMeshCalculator.cs
│   │   │   ├── MeshCode.cs
│   │   │   └── MeshNeighborResolver.cs
│   │   ├── ProjectMetadata/
│   │   │   ├── GeoProjectInfo.cs
│   │   │   ├── GeoConfidenceLevel.cs
│   │   │   ├── ProjectOrigin.cs
│   │   │   ├── IGeoProjectInfoStore.cs
│   │   │   └── GeoProjectInfoExtensions.cs
│   │   ├── ModuleState/
│   │   │   ├── IModuleStateStore.cs
│   │   │   ├── ModuleStateKey.cs
│   │   │   └── ModuleStateEnvelope.cs
│   │   ├── Validation/
│   │   │   ├── ValidationResult.cs
│   │   │   ├── CoordinateValidator.cs
│   │   │   └── IExportReadinessChecker.cs
│   │   ├── Versioning/
│   │   │   ├── SchemaVersion.cs
│   │   │   └── MigrationRunner.cs
│   │   ├── Settings/
│   │   │   └── SuiteSettings.cs
│   │   └── Logging/
│   │       └── AuditLogger.cs
│   │
│   ├── RevitGeoSuite.Core.Plateau/         ← PLATEAU-specific shared logic (no Revit)
│   │   ├── Codelists/
│   │   │   ├── CodelistReader.cs
│   │   │   └── CodelistRegistry.cs
│   │   ├── Tiles/
│   │   │   └── PlateauTileIndex.cs
│   │   └── Schema/
│   │       ├── PlateauSchemaHelper.cs
│   │       └── PlateauConstants.cs
│   │
│   ├── RevitGeoSuite.RevitInterop/         ← Revit API wrappers (no UI)
│   │   ├── Documents/
│   │   │   └── RevitDocumentHelper.cs
│   │   ├── Transactions/
│   │   │   └── TransactionHelper.cs
│   │   ├── Elements/
│   │   │   └── ElementExtensions.cs
│   │   ├── GeoPlacement/
│   │   │   ├── IRevitGeoPlacementService.cs
│   │   │   ├── RevitGeoPlacementService.cs
│   │   │   ├── ProjectLocationReader.cs
│   │   │   └── ProjectLocationWriter.cs
│   │   └── Storage/
│   │       ├── ExtensibleStorageHelper.cs
│   │       ├── GeoProjectInfoStorage.cs
│   │       ├── ModuleStateStorage.cs
│   │       └── StorageMigrator.cs
│   │
│   ├── RevitGeoSuite.SharedUI/             ← Reusable WPF controls (presentation only)
│   │   ├── Controls/
│   │   │   ├── MapControl.xaml
│   │   │   ├── CrsPickerControl.xaml
│   │   │   └── StatusBarControl.xaml
│   │   ├── Styles/
│   │   │   └── SuiteTheme.xaml
│   │   └── Converters/
│   │       └── CrsDisplayConverter.cs
│   │
│   ├── RevitGeoSuite.Shell/                ← Main add-in entry point
│   │   ├── App.cs
│   │   ├── RibbonBuilder.cs
│   │   ├── ModuleRegistry.cs
│   │   └── RevitGeoSuite.addin
│   │
│   ├── RevitGeoSuite.Georeference/         ← MODULE 1
│   │   ├── GeoreferenceCommand.cs
│   │   ├── GeoreferenceViewModel.cs
│   │   ├── GeoreferenceWindow.xaml
│   │   ├── SiteSelectionService.cs
│   │   ├── PlacementPreviewService.cs
│   │   └── GeoreferenceModuleInfo.cs
│   │
│   ├── RevitGeoSuite.MeshInspector/        ← MODULE 2
│   │   ├── MeshInspectorCommand.cs
│   │   ├── MeshInspectorViewModel.cs
│   │   ├── MeshInspectorWindow.xaml
│   │   ├── MeshOverlayService.cs
│   │   └── MeshInspectorModuleInfo.cs
│   │
│   ├── RevitGeoSuite.Validation/           ← MODULE 3
│   │   ├── ValidationCommand.cs
│   │   ├── ValidationViewModel.cs
│   │   ├── ValidationWindow.xaml
│   │   ├── ProjectHealthChecker.cs
│   │   ├── ExportReadinessChecker.cs
│   │   └── ValidationModuleInfo.cs
│   │
│   ├── RevitGeoSuite.PlateauImport/        ← MODULE 4
│   │   ├── PlateauImportCommand.cs
│   │   ├── PlateauImportViewModel.cs
│   │   ├── PlateauImportWindow.xaml
│   │   ├── CityGmlParser.cs
│   │   ├── ContextGeometryBuilder.cs
│   │   ├── TileDownloadService.cs
│   │   ├── PlateauImportState.cs
│   │   └── PlateauImportModuleInfo.cs
│   │
│   ├── RevitGeoSuite.Tiles3DExport/        ← MODULE 5
│   │   ├── Tiles3DExportCommand.cs
│   │   ├── Tiles3DExportViewModel.cs
│   │   ├── Tiles3DExportWindow.xaml
│   │   ├── GeometryExtractor.cs
│   │   ├── GltfWriter.cs
│   │   ├── TilesetJsonBuilder.cs
│   │   ├── LodGenerator.cs
│   │   ├── Tiles3DExportState.cs
│   │   └── Tiles3DExportModuleInfo.cs
│   │
│   └── RevitGeoSuite.CityGmlExport/        ← MODULE 6
│       ├── CityGmlExportCommand.cs
│       ├── CityGmlExportViewModel.cs
│       ├── CityGmlExportWindow.xaml
│       ├── SemanticMapper.cs
│       ├── AttributeMapper.cs
│       ├── CodelistMapper.cs
│       ├── CityGmlWriter.cs
│       ├── ExportValidator.cs
│       ├── CityGmlExportState.cs
│       └── CityGmlExportModuleInfo.cs
│
├── tests/
│   ├── RevitGeoSuite.Core.Tests/
│   ├── RevitGeoSuite.Core.Plateau.Tests/
│   ├── RevitGeoSuite.Georeference.Tests/
│   ├── RevitGeoSuite.MeshInspector.Tests/
│   ├── RevitGeoSuite.Validation.Tests/
│   └── ...
│
└── docs/
    ├── architecture.md
    ├── codelist-mapping.md
    ├── japanese-crs-reference.md
    └── storage-schema-versioning.md
```

---

## The Shared State: GeoProjectInfo

`GeoProjectInfo` is the shared contract between all modules. It is **deliberately small and stable**.

### What Gets Persisted (Extensible Storage, versioned)

```csharp
public class GeoProjectInfo
{
    // Schema version — persisted for migration
    public int SchemaVersion { get; set; }

    // CRS reference — persist only the stable identifier
    public CrsReference ProjectCrs { get; set; }   // e.g. EPSG:6677

    // Canonical project geo facts
    public ProjectOrigin Origin { get; set; }      // Lat, Lng, Elevation
    public double TrueNorthAngle { get; set; }

    // Persisted convenience key used by multiple modules
    public MeshCode PrimaryMeshCode { get; set; }

    // Provenance
    public GeoConfidenceLevel Confidence { get; set; }
    public string SetupSource { get; set; }        // e.g. "Georef Module v1.2"
    public DateTime? GeoSetupDate { get; set; }
}
```

### CRS Persistence Rule

Persist a **stable CRS reference**, not the full `CrsDefinition`.

Example:

```csharp
public class CrsReference
{
    public int EpsgCode { get; set; }              // e.g. 6677
    public string NameSnapshot { get; set; }       // optional, for display/debugging only
}
```

At runtime:
- `ProjectCrs.EpsgCode` is resolved through `ICrsRegistry`
- if the registry changes later, storage remains stable
- the persisted schema stays small and easier to migrate

---

### Source of Truth Rule

The **true source of truth** is:

1. `ProjectCrs`
2. `Origin`
3. `TrueNorthAngle`

`PrimaryMeshCode` is persisted because it is useful across modules, but conceptually it is a **derived convenience key**, not the foundational source of location truth.

If origin or CRS changes:
- mesh should be recomputed
- stale mesh values should be invalidated or refreshed

---

### What Gets Derived at Runtime (never persisted)

- `List<MeshCode> NeighborMeshCodes`
- `List<ValidationResult> LastValidation`
- `ExportReadiness ExportReadiness`

These are calculated from canonical project state and do not need persistence.

---

### Module-Specific State (owned by each module, persisted separately)

```csharp
public class PlateauImportState
{
    public List<string> ImportedTileIds { get; set; }
    public DateTime? LastImportDate { get; set; }
    public string CacheDirectory { get; set; }
}

public class Tiles3DExportState
{
    public string LastExportPath { get; set; }
    public string LastLodSetting { get; set; }
    public DateTime? LastExportDate { get; set; }
}

public class CityGmlExportState
{
    public Dictionary<string, string> CategoryMappingOverrides { get; set; }
    public string TargetSchemaVersion { get; set; }
    public string LastExportPath { get; set; }
}
```

Persisted through a consistent pattern:

```csharp
public interface IModuleStateStore
{
    void Save<T>(Document doc, string moduleId, T state);
    T Load<T>(Document doc, string moduleId);
    bool HasState(Document doc, string moduleId);
}
```

---

### How Modules Interact Through GeoProjectInfo

1. **Georeference Module** sets CRS, origin, and orientation → saves to document
2. **Mesh Inspector** reads origin/CRS, calculates mesh → updates `PrimaryMeshCode`
3. **Validation Module** reads shared state → reports project health
4. **PLATEAU Import** reads mesh code → finds/downloads tiles → saves import history to `PlateauImportState`
5. **3D Tiles Export** reads CRS and origin → positions output correctly → saves export preferences to `Tiles3DExportState`
6. **CityGML Export** reads shared geo facts + `Core.Plateau` codelists/schema helpers → saves mapping choices to `CityGmlExportState`

No module depends on another module.

---

## Module Discovery & Version Compatibility

The Shell is the only thing registered with Revit through one `.addin` file.

### Discovery Strategy

Keep module discovery simple at first:
- Shell scans a known modules folder
- load assemblies matching a predictable naming convention
- each module exposes one `IRevitGeoModule`
- incompatible modules are skipped and logged

Avoid overcomplicated plugin/discovery infrastructure early.

---

### Module Contract

```csharp
public interface IRevitGeoModule
{
    string ModuleName { get; }
    string ModuleVersion { get; }

    string RequiredCoreVersion { get; }
    string RequiredRevitVersion { get; }

    string PanelName { get; }
    int SortOrder { get; }

    void RegisterCommands(RibbonPanel panel);

    bool IsAvailable(GeoProjectInfo info);
    string UnavailableReason { get; }
}
```

### Version Handshake

On startup, the Shell checks:
- installed Core version
- installed Revit version
- module’s declared requirements

Incompatible modules:
- do not load
- are logged clearly
- do not crash the suite

### Graceful Degradation

If georeferencing has not been set up yet, dependent workflows remain visible but unavailable with a clear reason such as:

> Please set up project georeferencing first.

---

## Dependency Graph

```
RevitGeoSuite.Core              → (no dependencies, pure C#)
RevitGeoSuite.Core.Plateau      → Core
RevitGeoSuite.RevitInterop      → Core, Revit API
RevitGeoSuite.SharedUI          → Core
RevitGeoSuite.Shell             → Core, RevitInterop, SharedUI
RevitGeoSuite.Georeference      → Core, RevitInterop, SharedUI
RevitGeoSuite.MeshInspector     → Core, RevitInterop, SharedUI
RevitGeoSuite.Validation        → Core, RevitInterop, SharedUI
RevitGeoSuite.PlateauImport     → Core, Core.Plateau, RevitInterop, SharedUI
RevitGeoSuite.Tiles3DExport     → Core, RevitInterop, SharedUI
RevitGeoSuite.CityGmlExport     → Core, Core.Plateau, RevitInterop, SharedUI
```

Key rules:
- no module depends on another module
- Core has zero dependencies
- PLATEAU logic is isolated
- RevitInterop has no UI
- SharedUI has no workflow logic

---

## Extensible Storage: Versioning & Migration Strategy

### Schema Versioning

Every stored object includes a version field.

```csharp
private const int CurrentSchemaVersion = 1;

public void Save(Document doc, GeoProjectInfo info)
{
    info.SchemaVersion = CurrentSchemaVersion;
    // write to Extensible Storage
}

public GeoProjectInfo Load(Document doc)
{
    var info = // read from Extensible Storage
    if (info.SchemaVersion < CurrentSchemaVersion)
    {
        info = StorageMigrator.Migrate(info);
    }
    return info;
}
```

### Migration Rules

```csharp
public static class StorageMigrator
{
    public static GeoProjectInfo Migrate(GeoProjectInfo old)
    {
        if (old.SchemaVersion == 0)
            old = MigrateV0ToV1(old);

        return old;
    }
}
```

### What to Persist vs. Derive

| Data | Persist? | Why |
|---|---|---|
| EPSG code / CRS reference | Yes | Canonical and stable |
| CRS full definition | No | Resolve from registry at runtime |
| Origin lat/lng/elevation | Yes | Canonical |
| True north angle | Yes | Canonical |
| Primary mesh code | Yes | Useful shared convenience key |
| Neighbor mesh codes | No | Cheap to derive |
| Confidence level | Yes | Provenance |
| Setup source | Yes | Provenance |
| Timestamp | Yes | Provenance |
| Validation results | No | Snapshot only |
| Export readiness | No | Derived |
| Previews | No | Derived |

---

## The Core Library: What Goes Where

### Core (generic geo foundation, no Revit, no PLATEAU)

- CRS definitions, registry, transforms
- persisted CRS references
- Japanese CRS presets
- mesh code calculation
- `GeoProjectInfo`
- module state contracts
- validation logic
- versioning abstractions
- settings models
- logging abstractions

### Core.Plateau (PLATEAU-specific shared logic, no Revit)

- codelist XML parsing
- codelist value registry
- PLATEAU tile index
- PLATEAU constants
- schema helpers

Note: if PLATEAU support grows substantially later, `Core.Plateau` can be split further without affecting the rest of the architecture.

### RevitInterop (Revit API wrappers, no UI)

- document access helpers
- transaction wrappers
- element utilities
- `IRevitGeoPlacementService`
- project location reader/writer
- Extensible Storage
- `GeoProjectInfoStorage`
- `ModuleStateStorage`

### SharedUI (presentation only)

- `MapControl`
- `CrsPickerControl`
- `StatusBarControl`
- shared styles/themes
- display converters

### Modules

- windows and view models
- module-specific services
- workflow logic
- module-specific state
- module metadata (`IRevitGeoModule`)

---

## Build & Ship Strategy

### Phase 1: Ship foundation + first module

Ship: Core + RevitInterop + SharedUI + Shell + Georeference Module

This is already useful on its own:
- Japanese CRS setup
- map-based placement
- persisted geo metadata
- stable base for future modules

### Phase 2: Add Mesh Inspector

Ship: Mesh Inspector Module

### Phase 3: Add Validation Early

Ship: Validation Module

### Phase 4: Add PLATEAU Import

Ship: Core.Plateau + PlateauImport Module

### Phase 5: Add 3D Tiles Export

Ship: Tiles3DExport Module

### Phase 6: Add CityGML Export

Ship: CityGmlExport Module

### Phase 7+: Continue

- migrate GeoPackage exporter into suite
- consider IMDF-related module(s)
- add more validation/export tools as needed

---

## How Existing Plugins Migrate Into the Suite

| Aspect | Current GeoPackage Plugin | As a Suite Module |
|---|---|---|
| CRS handling | Own dialog, self-contained | Reads from `GeoProjectInfo` |
| Export settings | Internal | Module-owned state via `IModuleStateStore` |
| Ribbon | Own tab | Appears in suite tab |
| Distribution | Standalone DLL | Module DLL in suite folder |

This is a good sign: existing tools can be absorbed without redesigning the whole platform.

---

## Naming Conventions

### Projects

| Project | Name | Rationale |
|---|---|---|
| Core | `RevitGeoSuite.Core` | Generic geo foundation |
| PLATEAU shared | `RevitGeoSuite.Core.Plateau` | PLATEAU-specific extension |
| Revit API layer | `RevitGeoSuite.RevitInterop` | Clear API boundary |
| Shared UI | `RevitGeoSuite.SharedUI` | Presentation only |
| Shell | `RevitGeoSuite.Shell` | Entry point |
| 3D Tiles Export | `RevitGeoSuite.Tiles3DExport` | Explicit naming |

### Namespace examples

- `RevitGeoSuite.Core.Coordinates`
- `RevitGeoSuite.Core.Mesh`
- `RevitGeoSuite.Core.ProjectMetadata`
- `RevitGeoSuite.Core.ModuleState`
- `RevitGeoSuite.Core.Plateau.Codelists`
- `RevitGeoSuite.RevitInterop.GeoPlacement`
- `RevitGeoSuite.RevitInterop.Storage`
- `RevitGeoSuite.SharedUI.Controls`
- `RevitGeoSuite.Georeference`
- `RevitGeoSuite.MeshInspector`
- `RevitGeoSuite.Validation`

---

## Ribbon Layout

```
[Revit Geo Suite] tab
├── [Project Setup] panel
│   ├── Georeference button
│   ├── Mesh Inspector button
│   └── Check Project button
├── [PLATEAU] panel
│   └── Import Context button
├── [Export] panel
│   ├── 3D Tiles Export button
│   ├── GeoPackage Export button
│   └── CityGML Export button
```

---

## Suggested Development Order

### Sprint 1A: Skeleton and Core Contracts

- Set up solution structure
- Create Core, RevitInterop, SharedUI, Shell projects
- Build `GeoProjectInfo`
- Build `CrsReference`
- Build `ICrsRegistry` / `CrsRegistry`
- Build `IRevitGeoModule`
- Build Shell with empty ribbon
- Set up test projects

### Sprint 1B: Storage, Mesh, and Versioning Basics

- Build `IGeoProjectInfoStore`
- Build `IModuleStateStore`
- Build `GeoProjectInfoStorage`
- Add schema versioning
- Build `IMeshCalculator`
- Build initial validation primitives
- Write Core tests
- Document storage schema v1

### Sprint 2: Georeference Module

- Build `IRevitGeoPlacementService`
- CRS picker UI
- Map control
- Point selection
- placement preview generation
- survey/project base workflow
- save `GeoProjectInfo`
- ship v0.1

### Sprint 3: Mesh Inspector + Validation

- mesh code calculation UI
- neighbor mesh display
- copy mesh code
- Check Project command
- suspicious coordinate checks
- export readiness summary
- ship v0.2

### Sprint 4: PLATEAU Context Import

- build `Core.Plateau`
- tile lookup
- CityGML parsing
- lightweight context geometry
- import state persistence
- ship v0.3

### Sprint 5: 3D Tiles Export

- geometry extraction
- glTF/GLB writing
- tileset generation
- LOD logic
- export state persistence
- ship v0.4

### Sprint 6: CityGML Export

- semantic mapping
- attribute mapping
- codelist mapping
- XML writing
- export validation
- module state persistence
- ship v0.5

---

## Architecture Review Summary

| Area | Status |
|---|---|
| Independent modules over chain | ✅ Confirmed |
| Shared Core without Revit dependency | ✅ Confirmed |
| GeoProjectInfo kept small and stable | ✅ Confirmed |
| Persist CRS reference instead of full definition | ✅ Revised |
| Mesh treated as convenience key, not source of truth | ✅ Revised |
| PLATEAU logic separated from generic Core | ✅ Confirmed |
| Service interfaces defined early | ✅ Confirmed |
| Shared UI separated from Revit API layer | ✅ Confirmed |
| Georeferencing as execution boundary | ✅ Clarified |
| Module version compatibility | ✅ Confirmed |
| Extensible Storage versioned with migration | ✅ Confirmed |
| Validation shipped early | ✅ Confirmed |
| Discovery kept simple at first | ✅ Revised |
| SharedUI remains presentational | ✅ Clarified |

---

## One Sentence Summary

A versioned generic geo Core provides shared intelligence, a separate PLATEAU library handles ecosystem-specific logic, Revit interop safely applies document changes, and independent modules plug into the Shell without depending on one another.
