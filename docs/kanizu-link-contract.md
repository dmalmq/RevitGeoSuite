# Kanizu link contract

RevitGeoSuite exports Revit models into a station folder that Kanizu reads. This file is the one description of what each side writes and reads. The sample station folder in `tests/Fixtures/Conformance/station/` is written by RevitGeoSuite's real writers (`StationFolderConformanceTests`), and Kanizu's tests read a copy of it (`crates/kanizu-3d/tests/fixtures/revit/station/` in Kanizu, by `crates/kanizu-core/tests/revit_station.rs`). When either side changes what it writes or reads, it changes this file, the fixture and both test suites together.

## The station folder

```text
<station>/
  kanizu-link.json              Kanizu writes; RevitGeoSuite never reads it
  revit/                        RevitGeoSuite writes; Kanizu reads, and removes old versions
    <model folder>/
      <yyyyMMddTHHmmssZ>-<hash8>/   one version per export
        cesium-package.json         the manifest
        tiles/  tileset.json, content.glb, levels.json, elements.json, rooms.json
        gis/    floor plans (GeoPackage)
```

Every file has one writer, so two people on the share never overwrite each other.

- RevitGeoSuite builds a version in a hidden `revit/.tmp-<id>/` folder, writes the manifest last and renames the folder into place (`CesiumStationFolder.Publish`). A reader skips folders whose names start with `.`, and treats a visible folder without `cesium-package.json` as not RevitGeoSuite's: Kanizu lists it as skipped and says why.
- The model folder is the building name's slug plus the first 8 hex digits of the model id (`CesiumStationFolder.ModelFolderName`), or the building id without a model id. It is for people reading the share. A reader groups versions by model id, not by folder: renaming the building in Revit starts a new folder for the same model. A version without a model id is counted with the model id other versions in its folder carry, or else by its folder.
- The version folder is the UTC time and the first 8 hex digits of the content hash. A reader orders versions by the manifest's `createdUtc`.
- Kanizu removes old versions only after asking, and keeps the newest three of each model and the version applied last. It renames a version to a hidden name before deleting it, so a removal cut short leaves a folder readers skip.

## `cesium-package.json`

`schema` is `revitgeosuite.cesium-package` and `version` is `1`. RevitGeoSuite adds optional fields within version 1 and raises the version only when an older reader would misread the file. A reader refuses a version it does not know and ignores fields it does not know.

| Field | Meaning |
|---|---|
| `source.modelId` | The model's stable id (`GeoProjectInfo.ModelId`), a GUID kept in the model. Same for every user, survives Save As and moves. Absent when the model was read-only on its first export. Compared without case. |
| `source.documentKey` | Hashes the file path, so it differs per PC and on Save As. A reader uses it as the model's key only when `modelId` is absent. |
| `source.model` | The file name, for display. |
| `building.id`, `building.name` | The building's id (hashes the path) and name, for display. |
| `crs.projectEpsg` | The project CRS of every plane-grid coordinate in the package. |
| `anchor` | Where the tileset's east-north-up frame sits: `lat`, `lon`, `elevationMeters` (ellipsoidal), `geoidOffsetMeters`, `geoidModel` (`EGM2008` or `typed`), `orthometricHeightMeters`, `heightDatum` (`TP` or `unknown`), and the plane `easting`, `northing`. A reader places the model from these source numbers, with its own geoid if it has a better one, rather than trusting the tileset's transform. |
| `tiles.levels`, `tiles.elements`, `tiles.rooms` | Paths of the index files below, relative to the version folder. Absent when the export did not write them. A path never leaves the version folder. |
| `export` | What the 3D Tiles export covered and how, below. Absent on packages from before it was written. |

### `export`

Two versions compare element by element only when their `export` sections match. A reader that finds them different still lists the changes, and says that some may come from the different settings rather than from edits.

| Field | Meaning |
|---|---|
| `scope` | `whole` for the whole model, `view` for the elements one 3D view shows. |
| `view` | For `view`: the view's `uniqueId` and `name`. |
| `links` | The link instances whose elements were exported, each its `uniqueId` (the UniqueId of the link instance in the host) and `name`. Empty when only the host was exported. |
| `levelOfDetail` | `coarse`, `medium` or `fine`. |
| `exactPlacement` | `true` when every vertex was unprojected from the project CRS into the anchor's frame, `false` when plane-grid metres were used as east-north-up (a twist of about 0.04° at Tokyo). |

## `tiles/levels.json` (version 2)

`linkLevels` maps each model's link name (`""` for the host) to its levels, and `levels` merges them by level key. Each level has `levelKey`, `levelName`, `levelUniqueIds` (the Revit `Level.UniqueId`s), `levelElevationMeters` (relative to the anchor's orthometric height), `elementCount`, `minZMeters` and `maxZMeters`. Kanizu pairs levels with its floors by `Level.UniqueId` and stores the pairs in `kanizu-link.json`; nothing Kanizu owns is written into the Revit model.

## `tiles/elements.json` (version 1)

One entry per exported element: `uniqueId`, `elementId`, `sourceDocument`, `sourceLinkName`, `sourceLinkInstanceUniqueId`, `levelUniqueId`, `levelKey`, `category`, `hash`, `triangleCount`, `min`, `max`, `volumeCubicMeters`. `frame` is `projected-grid-orthometric` (x and y in the project CRS, z in the anchor's height datum) and `hashMethod` is `sha256-mm-sorted-triangles`.

An element's key is **`sourceLinkInstanceUniqueId` and `uniqueId`**. The link instance is part of it because one linked file placed twice repeats its UniqueIds; it is empty for host elements. `sourceDocument` is the file's title: it changes on Save As, so it is for display only and no part of the key. The one exception is a linked element from an index written before `sourceLinkInstanceUniqueId` existed: its title is the only thing telling two linked files apart, so it stays in that element's key. The GLB's `EXT_structural_metadata` table carries the same three fields, so a pick in the 3D view finds its entry.

## `tiles/rooms.json` (version 1)

Every placed room's finish-face footprint, for pairing the GDB's units with Revit rooms: `uniqueId`, `elementId`, `sourceDocument`, `sourceLinkName`, `sourceLinkInstanceUniqueId`, `levelUniqueId`, `levelName`, `name`, `number`, `elevation`, `areaSquareMeters`, `hash`, and `footprint` (closed rings, outer first, in the project CRS). A room is keyed like an element: link instance and UniqueId.

## `kanizu-link.json` (format 1)

Kanizu's own file: for each model (by `modelId`, or `documentKey` without one) the Kanizu building it is, the version applied last, its level pairs (`Level.UniqueId` to Kanizu floor key) and its unit-to-room pairs. RevitGeoSuite never reads or writes it.

## GIS exports: `revit_key`

The floor plan export (GeoPackage, and the GDB built from it) carries `revit_key` on every feature, `parent_key` and `category` on detail lines. `revit_key` reads `model|UniqueId|part`, with `model@LinkUniqueId` for a feature read through a link. The rules are defined once, in the remarks on [`RevitKey`](../src/RevitGeoSuite.FloorPlanExport.Core/Models/RevitKey.cs); a reader follows those rather than a copy here. It is written only in the GIS export, not in the Cesium package's tiles.

## Versions

| File | Version | Raised when |
|---|---|---|
| `cesium-package.json` | 1 | an older reader would misread it |
| `levels.json` | 2 | as above (2 added `linkLevels`) |
| `elements.json` | 1 | as above |
| `rooms.json` | 1 | as above |
| `kanizu-link.json` | 1 | Kanizu changes it; RevitGeoSuite does not read it |

A reader refuses a file newer than it knows, naming the file and both versions, and reads a file without a version as it is.
