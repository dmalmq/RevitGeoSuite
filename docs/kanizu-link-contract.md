# Kanizu link contract

RevitGeoSuite exports Revit models into a station folder that Kanizu reads. This file is the one description of what each side writes and reads. The sample station folder in `tests/Fixtures/Conformance/station/` is written by RevitGeoSuite's real writers (`StationFolderConformanceTests`), and Kanizu's tests read a copy of it (`crates/kanizu-3d/tests/fixtures/revit/station/` in Kanizu, by `crates/kanizu-core/tests/revit_station.rs`). When either side changes what it writes or reads, it changes this file, the fixture and both test suites together.

## The station folder

```text
<station>/
  kanizu-link.json              Kanizu writes; RevitGeoSuite never reads it
  gis-models/                   RevitGeoSuite writes and reads; Kanizu does not read it yet
    <model id>.gis-model.json     one model's GIS export settings
  gis-drift/                    Kanizu writes; RevitGeoSuite's pyRevit tools read
    <model id>.gis-drift.json     GDB edits to fields this model owns
  revit/                        RevitGeoSuite writes; Kanizu reads, and removes old versions
    <model folder>/
      <yyyyMMddTHHmmssZ>-<hash8>/   one version per export
        cesium-package.json         the manifest
        tiles/  tileset.json, content.glb, levels.json, elements.json, rooms.json
        gis/    floor plans (GeoPackage), gis-fields.json
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
| `export` | What the 3D Tiles export covered and how, below. Absent on packages from before it was written, and on packages that "Send to Cesium viewer" (`cesium.push`) wraps around an existing export folder later, because that step cannot know which settings made the folder. A reader treats a missing `export` as unknown settings. |

### `export`

Two versions compare element by element only when their `export` sections match. A reader that finds them different still lists the changes, and says that some may come from the different settings rather than from edits.

| Field | Meaning |
|---|---|
| `scope` | `whole` for the whole model, `view` for the elements one 3D view shows. |
| `view` | For `view`: the view's `uniqueId` and `name`. |
| `links` | The link instances whose elements were exported, each its `uniqueId` (the UniqueId of the link instance in the host) and `name` (the instance's name, such as `Platform.rvt : 1`, for display). Empty when only the host was exported. |
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

## `gis-models/<model id>.gis-model.json` (version 1)

One model's GIS export settings, kept on the share so every PC exports the model the same way. The file name is the model id (`GeoProjectInfo.ModelId`) in lower case, so a building rename or Save As keeps the file. The model remembers its station folder (`GeoProjectInfo.GisStationFolder`) from the first Cesium export into it, and from then on every floor plan export of the model, from the dialog or the Cesium export, reads the file before it starts and writes it when it ends. A model with no station folder or model id has no file and exports with the standard's settings. The format is [`GisModelFile`](../src/RevitGeoSuite.FloorPlanExport.Core/Standards/GisModelFile.cs), and a new file starts from `standards/gis-model.template.json`.

| Field | Meaning |
|---|---|
| `schema`, `version` | `revitgeosuite.gis-model` and `1` |
| `identity` | `modelId`, `station` (the station folder's name), `building` (for display), `crsEpsg` of the last GIS export |
| `levels` | The levels with a GIS `level_id`, and those of the listed views: `uniqueId`, `name`, `levelId`, `ordinal`, `shortName`. Their pairing with Kanizu floors stays in `kanizu-link.json`. |
| `views` | The plan views of the last Cesium export, `uniqueId` and `name`. Only that export writes them, so a one-off dialog export does not change them. The Cesium export takes these before the profile's own views, and warns when some no longer exist; the export dialog preselects them when its profile stores none. Without a list, both take the `GIS_` views. |
| `drawingRules.stairs` | `stepSpacingMeters` (0.60), `stopBeforeMiddleMeters` (1.10), `overlapMeters` (1.80, 0 tiles the two floors exactly). A missing number takes the standard's. Edited by hand on the share; the export never changes them. |
| `fields` | Which field list the model exports with: `standard` for the one shipped in `standards/fields.json`, or the path of a station's own list (relative to the station folder, such as `fields.json`, or absolute). |
| `versions` | `standard` (the model's `gis_standard_version` at the last export), `lastExportRevitGeoSuite`, `lastExportUtc` |

RevitGeoSuite writes it through a temporary file and a rename, and keeps fields it does not know, so a file written by a newer RevitGeoSuite keeps them through an export from an older one.

## `gis/gis-fields.json` (version 1)

The field list the export used, written beside the GeoPackage: schema `revitgeosuite.gis-fields`. One row per field: `layer` (`unit`, `detail`, `opening`, `fixture`, `level`), `name` (the GDB field name), `aliasJa`, `type` (`text`, `integer`, `real`), `length` (absent: 255), `values` (the pick list for a coded-value domain; empty means free text), `source` and `owner`.

- `source.kind` is `core` for a field the export always writes, `parameter` for a Revit parameter (`parameter`, `guid`, `binding`, `categories`), `derived` for a value the export works out, and `none` for a field with no Revit source.
- `owner` is `revit` (every export writes the value) or `gdb` (the export never writes it; Kanizu keeps the GDB's value and carries it across by `revit_key`).

Unit categories reach the GDB in the station's codes. The export writes a unit's category in IMDF terms (`walkway`, `retail`, `ticketing`) and its side of the ticket gates in `gis_restriction` (`rachi_nai` inside, `rachi_gai` outside), which `restrict` leaves out. `gis_restriction` is the side the export resolves, the same one that picks `restrict` and the preview fill: the type's `gis_restriction` parameter, or, for a type without one, the side its name gives (`ラチ内店舗`, `改札内コンコース`). Kanizu's station code table turns the two into the GDB's one code (`walkway_sta`, `walkway`, `store_sta`, `store`, `ticket office`) and drops `gis_restriction`. A pair with no code is written as exported and listed when the apply is reviewed. A blank `restrict` never replaces a GDB value such as `employeesonly`.

Kanizu creates a missing field with its alias and pick list and fills the Revit-owned ones. A station adds a field by copying `standards/fields.json` to the share, adding a row, and naming the copy in the model's `gis-model.json` `fields`. RevitGeoSuite then binds the row's parameter and exports its column, with no code change. The standard's `core` rows always win over a station copy.

## `gis-drift/<model id>.gis-drift.json` (version 1)

Kanizu's record of the GDB values that differ from a model's export, written whole every time Kanizu applies that model's GIS export to the GDB (through a temporary file and a rename). The file name is the model id in lower case (or the document key without one), with characters other than ASCII letters, digits and `-` turned into `_`. Kanizu keeps the GDB's value when an apply meets such an edit; the pyRevit **Review GDB edits** button reads the file, and accepting a row writes the GDB's value into the Revit parameter so the next export agrees.

| Field | Meaning |
|---|---|
| `schema`, `version` | `kanizu.gis-drift` and `1` |
| `model`, `appliedVersion` | The model key, and the version applied (relative to the station folder) |
| `rows[]` | `revitKey`, `uniqueId` (from the key), `layer`, `field` (the GDB field), `parameter`, `guid` and `binding` (from `gis-fields.json`; empty for a core field), `label` (the feature's name), `revit` (this export's value), `gdb` (the GDB's), `base` (the last applied export's) and `conflict` (both sides changed since the last apply). A unit `category` row also has `gdbCategory` and `gdbRestriction`: the GDB's station code as the Revit category it comes from and the floor type restriction it needs (absent when the code says nothing about one). |

A core field maps to its parameter by name: unit and fixture `name` to `name`, `alt_name` to `short_name`, unit `category` to `category`, `direction` to `gis_direction`, and level `ordinal` to `ordinal`. Unit `restrict` has no parameter: the GDB holds IMDF restrictions there (`employeesonly`), while the type's `gis_restriction` is the side of the ticket gates and picks the category code. A unit `category` row writes `gdbCategory`, not the station code; when the element's floor type has another restriction than `gdbRestriction`, it is listed for a floor type change by hand. Other core fields have no parameter and are listed for a change by hand. The button leaves out rows the model already agrees with and leaves conflicts unticked.

## GIS exports: `revit_key`

The floor plan export (GeoPackage, and the GDB built from it) carries `revit_key` on every feature, `parent_key` and `category` on detail lines. `revit_key` reads `model|UniqueId|part`, with `model@LinkUniqueId` for a feature read through a link. The rules are defined once, in the remarks on [`RevitKey`](../src/RevitGeoSuite.FloorPlanExport.Core/Models/RevitKey.cs); a reader follows those rather than a copy here. It is written only in the GIS export, not in the Cesium package's tiles.

## The walking network: who owns what

Revit owns everything the network is generated from, Kanizu owns the links, and the network GDB only receives. Nobody draws or types a network value in ArcGIS Pro as the normal path.

| Part | Owner | How |
|---|---|---|
| Links on one floor | Kanizu | Generated from the units and openings of the GIS export, edited by hand in Kanizu, checked there. Revit has no element for a walking link. |
| Connections between floors | Revit | One level connector per stair, escalator, elevator or ramp element. Kanizu pairs the element's units on each floor by their shared `revit_key` and stores the key on the connector. A wrong connector is fixed by fixing the model and generating again. |
| The delivered network | The network GDB | Kanizu writes it, floor links and connectors together, and is its only writer. |

In Kanizu a connector that carries a `revit_key` is locked: it can be inspected and its cost changed, but not removed, reversed, rejoined or changed to another kind. Generating again keeps its id and cost while the element still exports, and removes it once the element is gone. Units without a key, such as data from before the GIS export, still pair by distance on adjacent floors, and a connector drawn by hand stays possible for a station with no Revit model.

What crosses each way: Revit sends Kanizu the units, openings and connector elements in the GIS export. Kanizu sends the network GDB the whole network. Whatever Kanizu shows back in Revit is a read-only view of the network, never ownership.

## Versions

| File | Version | Raised when |
|---|---|---|
| `cesium-package.json` | 1 | an older reader would misread it |
| `levels.json` | 2 | as above (2 added `linkLevels`) |
| `elements.json` | 1 | as above |
| `rooms.json` | 1 | as above |
| `kanizu-link.json` | 1 | Kanizu changes it; RevitGeoSuite does not read it |
| `gis-model.json` | 1 | an older RevitGeoSuite would misread it |
| `gis-fields.json`, `fields.json` | 1 | an older reader would misread it |

A reader refuses a file newer than it knows, naming the file and both versions, and reads a file without a version as it is.
