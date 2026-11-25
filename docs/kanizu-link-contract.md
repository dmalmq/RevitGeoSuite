# Kanizu link contract

RevitGeoSuite exports Revit models into a station folder that Kanizu reads. This file is the one description of what each side writes and reads. The sample station folder in `tests/Fixtures/Conformance/station/` is written by RevitGeoSuite's real writers (`StationFolderConformanceTests`), and Kanizu's tests read a copy of it (`crates/kanizu-3d/tests/fixtures/revit/station/` in Kanizu, by `crates/kanizu-core/tests/revit_station.rs`). When either side changes what it writes or reads, it changes this file, the fixture and both test suites together.

## The station folder

```text
<station>/
  kanizu-link.json              Kanizu writes; RevitGeoSuite never reads it
  gis-models/                   RevitGeoSuite writes and reads; Kanizu does not read it yet
    <model id>.gis-model.json     one model's GIS export settings
  gis-rules.json                RevitGeoSuite writes (any PC); Kanizu reads. The station's category rules and profiles
  gis-rules.lock                RevitGeoSuite, only while a PC saves gis-rules.json; readers ignore it
  gis-drift/                    Kanizu writes drift files; RevitGeoSuite writes keep files and reads drift files
    <model id>.gis-drift.json     GDB edits to fields this model owns (Kanizu writes)
    <model id>.gis-keep.json      "Keep Revit's" answers (RevitGeoSuite adds; Kanizu drops once applied or stale)
  gis-network/                  Kanizu writes; RevitGeoSuite reads and draws it read-only
    <model id>.gis-network.json   the walking network as this model sees it
  revit/                        RevitGeoSuite writes; Kanizu reads, and removes old versions
    <model folder>/
      <yyyyMMddTHHmmssZ>-<hash8>/   one version per export
        cesium-package.json         the manifest
        tiles/  tileset.json, content.glb, levels.json, elements.json, rooms.json
                (a Send to Kanizu version has levels.json and rooms.json only)
        gis/    floor plans (GeoPackage), gis-fields.json
```

Every file has one writer, so two people on the share never overwrite each other.

Two files have more than one writer: `gis-rules.json` (written from any PC as per-key operations behind `gis-rules.lock`, see its section below) and `gis-drift/<model id>.gis-keep.json` (RevitGeoSuite adds answers, Kanizu drops applied/stale answers or restores them on backup restore; both keep unknown fields and entries verbatim and refuse to replace a file that changed since it was read).

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
| `tiles.tileset` | `tiles/tileset.json`. Absent on a Send to Kanizu version, which has no 3D Tiles (below). |
| `tiles.levels`, `tiles.elements`, `tiles.rooms` | Paths of the index files below, relative to the version folder. Absent when the export did not write them. A path never leaves the version folder. |
| `gis.rules` | Which category rules and profile the floor plan export used, below. Absent when the package has no `gis` section. |
| `export` | What the 3D Tiles export covered and how, below. Absent on packages from before it was written, and on packages that "Send to Cesium viewer" (`cesium.push`) wraps around an existing export folder later, because that step cannot know which settings made the folder. A reader treats a missing `export` as unknown settings. |

### Send to Kanizu versions

Send to Kanizu writes the GIS package without 3D Tiles, so a model's weekly send takes seconds. Its version has `gis/`, `tiles/levels.json` and `tiles/rooms.json`, and no `tileset.json`, GLB or `elements.json`. Its manifest has `tiles` without `tileset` and no `export`. Its `source`, `crs`, `levelMap` and `gis` are the same as a Cesium export of the model would write; `anchor` differs as below.

- `levels.json` lists the host model's levels that carry a GIS `level_id`, under `linkLevels[""]` and `levels`. `elementCount` is the number of GIS features on the level, and `minZMeters` and `maxZMeters` equal `levelElevationMeters`.
- `anchor` is the georeference origin, not moved to the building's centroid as a precise-CRS tiles export moves it. A level's height above the anchor's orthometric height is therefore a different number from a Cesium export's, but the sum is the same height.
- A reader orders a send among the model's versions like any other and pairs and compares its levels and GIS as usual. Kanizu's Revit element change list compares `elements.json` files, so when either version is a send it says there is nothing to list and the GIS compares as usual. Kanizu has no 3D view of Revit tiles today, so nothing else reads `tileset.json`.

### `export`

Two versions compare element by element only when their `export` sections match. A reader that finds them different still lists the changes, and says that some may come from the different settings rather than from edits.

| Field | Meaning |
|---|---|
| `scope` | `whole` for the whole model, `view` for the elements one 3D view shows. |
| `view` | For `view`: the view's `uniqueId` and `name`. |
| `links` | The link instances whose elements were exported, each its `uniqueId` (the UniqueId of the link instance in the host) and `name` (the instance's name, such as `Platform.rvt : 1`, for display). Empty when only the host was exported. |
| `levelOfDetail` | `coarse`, `medium` or `fine`. |
| `exactPlacement` | `true` when every vertex was unprojected from the project CRS into the anchor's frame, `false` when plane-grid metres were used as east-north-up (a twist of about 0.04° at Tokyo). |

### `gis.rules`

| Field | Meaning |
|---|---|
| `source` | `station` when the rules came from the station's `gis-rules.json`, `local` when they came from the exporting PC's own files: the model has no station folder, or the station folder could not be reached or written to during this export (the export result then carries a warning). |
| `profile` | The profile the export used. With `source` `station` it is the model's `gis-model.json` `profile`, `Station` when absent. |
| `hash` | `sha256`, lower-case hex, of the canonical JSON (below) of the category rules plus that profile. |

When the version being applied has another `hash` than the version applied last, the review says the station rules changed between the two exports, so some changes may come from the rules and not from model edits.

**Canonical JSON.** The hashed text is the UTF-8 encoding, with no byte order mark, of one object with two members, `categoryRules` and `profile`, written like this:

- No whitespace outside strings. Object members are sorted by key, ordinal comparison of the NFC-normalised key; a member whose value is `null` is left out. Array order stays, except `categoryRules`.
- Strings are NFC-normalised. `"` is written `\"` and `\` is written `\\`; a character below U+0020 is written `\u` and four lower-case hex digits; every other character is written as itself (no `\n`-style escapes, no escaping of `/` or of non-ASCII).
- `categoryRules` holds the rules as the file writes them (`type`, `match` and, when it has one, `value`), sorted by `type` and then `match`, both compared ordinally after NFC normalisation.
- `profile` is the profile object as the file holds it, `name` included. RevitGeoSuite always writes a profile with every key, so the file's own JSON gives the same bytes.
- `true` and `false` as such; an integer in decimal. Any other number is the shortest digit string that reads back as the same double (shortest round trip), written positionally, never with an exponent: `1.25E-05` is `0.0000125`, `1.0` is `1`, `0.0` is `0`.
- `changed` and the file's `schema` and `version` are not part of it.

`tests/Fixtures/Conformance/gis-rules/` holds a rules file and `expected-hash.txt`, the hash of its rules and its profile `Station`; RevitGeoSuite and Kanizu both assert it.

## `tiles/levels.json` (version 2)

`linkLevels` maps each model's link name (`""` for the host) to its levels, and `levels` merges them by level key. Each level has `levelKey`, `levelName`, `levelUniqueIds` (the Revit `Level.UniqueId`s), `levelElevationMeters` (relative to the anchor's orthometric height), `elementCount`, `minZMeters` and `maxZMeters`. Kanizu pairs levels with its floors by `Level.UniqueId` and stores the pairs in `kanizu-link.json`; nothing Kanizu owns is written into the Revit model.

## `tiles/elements.json` (version 1)

One entry per exported element: `uniqueId`, `elementId`, `sourceDocument`, `sourceLinkName`, `sourceLinkInstanceUniqueId`, `levelUniqueId`, `levelKey`, `category`, `hash`, `triangleCount`, `min`, `max`, `volumeCubicMeters`. `frame` is `projected-grid-orthometric` (x and y in the project CRS, z in the anchor's height datum) and `hashMethod` is `sha256-mm-sorted-triangles`.

An element's key is **`sourceLinkInstanceUniqueId` and `uniqueId`**. The link instance is part of it because one linked file placed twice repeats its UniqueIds; it is empty for host elements. `sourceDocument` is the file's title: it changes on Save As, so it is for display only and no part of the key. The one exception is a linked element from an index written before `sourceLinkInstanceUniqueId` existed: its title is the only thing telling two linked files apart, so it stays in that element's key. The GLB's `EXT_structural_metadata` table carries the same three fields, so a pick in the 3D view finds its entry.

An entry may also carry the element's Kanizu GIS join values, each only when it is not blank: `gisId` (`id`), `gisRevitKey` (`gis_key`), `gisKind` (`gis_kind`, instance then type), `gisCategory` (`category` on the instance, else `gis_category` of the type) and `gisLevelId` (`level_id` of the element's level, else of the element). They are read by their shared-parameter GUIDs only. The version stays 1: a reader ignores fields it does not know, and a model without the GIS parameters writes the same file as before. When at least one element has a value, the GLB's `EXT_structural_metadata` table gets the same five string columns (empty for elements without one); otherwise it has none of them.

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
| `links` | The linked models the last export into the station selected, `uniqueId` (the link instance's `UniqueId` in the host model) and `name`. Every export into the station writes them, the dialog's included, because the dialog is where they are picked. The Cesium export takes these in place of its profile's own selection and warns about, and skips, those no longer in the model; whether linked models are exported at all is the station profile's `includeLinkedModels`. Absent until an export records them; `[]` means that export selected none. |
| `drawingRules.stairs` | `stepSpacingMeters` (0.60), `stopBeforeMiddleMeters` (1.10), `overlapMeters` (1.80, 0 tiles the two floors exactly). A missing number takes the standard's. Edited by hand on the share; the export never changes them. |
| `profile` | The profile of the station's `gis-rules.json` the model exports with. Absent means `Station`. When the station has no `Station` profile, the first export into it creates one from that export's own settings (the dialog's, or the Cesium export's profile), and if two PCs create it at once the first one stays. An export always uses the station's profile; where the export's own settings differ, its result carries one warning that lists each as `setting: yours -> station`. A name the station's file does not have stops the export, with a message that lists the profiles it has. |
| `fields` | Which field list the model exports with: `standard` for the one shipped in `standards/fields.json`, or the path of a station's own list (relative to the station folder, such as `fields.json`, or absolute). |
| `versions` | `standard` (the model's `gis_standard_version` at the last export), `lastExportRevitGeoSuite`, `lastExportUtc` |

RevitGeoSuite writes it through a temporary file and a rename, and keeps fields it does not know, so a file written by a newer RevitGeoSuite keeps them through an export from an older one.

## `gis-rules.json` (version 1)

The category rules and export profiles one station shares, so two PCs exporting the same model into the same station folder give the same GIS package. RevitGeoSuite writes it from any PC; Kanizu reads it and never writes it. A model with no station folder keeps its rules and profiles on the PC (`%APPDATA%\RevitGeoSuite\FloorPlanExport`) and its package says `gis.rules.source` is `local`. The format is [`GisRulesFile`](../src/RevitGeoSuite.FloorPlanExport.Core/Standards/GisRulesFile.cs); a reader refuses a version it does not know and ignores fields it does not know.

| Field | Meaning |
|---|---|
| `schema`, `version` | `revitgeosuite.gis-rules` and `1` |
| `categoryRules` | One set for the station, every model. Each row is `type` (`floorCategory`, `roomCategory`, `familyCategory`, `acceptedOpeningFamily`), `match` and `value` (the category; absent for `acceptedOpeningFamily`). They sit below the type parameter `gis_category`: an element's `category`, then its type's `gis_category`, then these rules, then the type-name catalog. |
| `profiles` | Named export profiles with the settings that shape the output: `targetEpsg`, `coordinateMode`, `featureTypes`, `unitGeometrySource`, `unitAttributeSource`, `roomCategoryParameter`, `includeLinkedModels`, `unitCategories`, `simplifyStairUnits`, `simplifyEscalatorUnits`, `use3DSectionBox` with its two offsets, `geometryRepair`, `schemaProfiles` with `activeSchemaProfile`, and `validationPolicies` with `activeValidationPolicy`. Which linked models an export takes is the host model's choice, so it is in the model's `gis-model.json` `links`, not here. The output folder, language, diagnostics and QGIS side outputs, post-export actions and debug views stay on each PC. A station export always exports all of the views it selects, with no incremental baseline. |
| `changed` | `utc`, `user` (the Windows user name) and `revitGeoSuite` (the version) of the last write, for display. |

Several PCs write the file, each save as a set of operations on single keys: set or delete one rule (by `type` and `match`) or one profile (by `name`). A writer first creates `gis-rules.lock` beside the file, which fails when the file exists, so only one PC holds it. The lock holds the writer's `machine`, `user` and `utc`. Holding it, the writer applies its operations to a fresh read, writes a temporary file beside it (`.gis-rules.<id>.tmp`) and swaps it in, retrying the swap up to five times with 100 to 800 ms between tries when the share refuses it, then reads the file again. It deletes the lock when it is done, also when the save fails. A writer that finds the lock waits on the same five tries and, if the lock is still there, fails the save with the name of the user and PC holding it and leaves the file as it was. A lock older than 30 seconds is left over from a crashed save: the next writer deletes it and takes the lock. The age is measured on the share's clock, not the writer's, because PCs' clocks drift: the writer creates an empty temporary file beside the lock, takes its write time as the share's current time, compares that with the lock's write time and deletes the temporary file. So two PCs changing different keys both land, whatever the timing. Two PCs changing the same key end with the last write. A reader never sees half a file and never takes the lock. Temporary files older than an hour on the share's clock are removed by the next writer.

The first export, or the first explicit save of a rule or profile, of a model on a PC adds that PC's rules and project profiles for the model to the file where the station has no entry for the same key or name, and renames the PC's file to `<name>.moved-to-station` once the station's write is read back. A station value that differs is kept and listed as a warning in the export result, and a rule deleted from the station does not come back. Until then, a read on that PC (the preview, the audit, the export dialog) counts the PC's rules that have not moved in where the station has no entry for the same key, as the move will add them, so it resolves the categories the export will; it writes and renames nothing.

## `gis/gis-fields.json` (version 1)

The field list the export used, written beside the GeoPackage: schema `revitgeosuite.gis-fields`. One row per field: `layer` (`unit`, `detail`, `opening`, `fixture`, `level`), `name` (the GDB field name, but for fixture `type` and level `level_name`, below), `aliasJa`, `type` (`text`, `integer`, `real`), `length` (absent: 255), `values` (the pick list for a coded-value domain; empty means free text), `source` and `owner`.

- `source.kind` is `core` for a field the export always writes, `parameter` for a Revit parameter (`parameter`, `guid`, `binding`, `categories`), `derived` for a value the export works out, and `none` for a field with no Revit source.
- `owner` is `revit` (every export writes the value) or `gdb` (the export never writes it; Kanizu keeps the GDB's value and carries it across by `revit_key`).
- A `parameter` row with `binding` `type` is read from the element's type. A blank or absent parameter value exports as no value.

The standard list has these rows beyond the core ones:

| Row | Source | Owner |
|---|---|---|
| `unit.levels_served` | core: the levels an elevator stops at, as level ids lowest first and separated by commas. From `gis_levels_served` (level names or `short_name`s) on any elevator object in the shaft, an instance value before a type value, else the levels the shaft's objects stand on or span. A shaft is the elevator objects whose plan footprints overlap by at least half of the smaller one, so a car drawn once per floor is one elevator and cars side by side in a bank stay apart. Only levels this export writes count, so a level with a `level_id` but no exported view never appears. Empty when neither gives two levels, and for every unit that is not an elevator family. Kanizu reads it for the network and does not write it to the GDB, whose levels have ids of their own. | revit |
| `opening.door`, `opening.access_con` | parameter `gis_door`, `gis_access_con` on the door type | revit |
| `opening.name` | parameter `name` on the door | revit |
| `level.address_id`, `level.build_id`, `fixture.anchor_id` | none | gdb |

Unit categories reach the GDB in the station's codes. The export writes a unit's category in IMDF terms (`walkway`, `retail`, `ticketing`) and its side of the ticket gates in `gis_restriction` (`rachi_nai` inside, `rachi_gai` outside), which `restrict` leaves out. `gis_restriction` is the side the export resolves, the same one that picks `restrict` and the preview fill: the type's `gis_restriction` parameter, or, for a type without one, the side its name gives (`ラチ内店舗`, `改札内コンコース`). Kanizu's station code table turns the two into the GDB's one code (`walkway_sta`, `walkway`, `store_sta`, `store`, `ticket office`) and drops `gis_restriction`. A pair with no code is written as exported and listed when the apply is reviewed. A blank `restrict` never replaces a GDB value such as `employeesonly`. The same holds for an opening's `door`, `access_con` and `name`: models leave those parameters empty today while the delivered GDBs fill them by hand. The cost is that clearing one of these values in Revit does not clear it in the GDB; clear it there too.

Two fields reach the GDB under another name. Kanizu reads a fixture's `type` as the GDB's `category` and a level's `level_name` as its `name`; when an export writes both names, the GDB's one wins. A fixture's category is always `fixture` (the export's fixtures are ticket gates), which Kanizu writes as the station code `checkin.kiosk`. A level's `name` only fills a GDB level `name` that is blank: a station's level names are hand data (`B1ラチ内`, `B1ラチ外`, one per polygon) that one Revit level cannot reproduce, so a GDB name that differs stays and Kanizu lists it in Review changes. A level's `short_name` is the level's `short_name` parameter; a blank one never replaces the GDB's (`1F`, `B2`). `elevation_m` is for Kanizu's own use and does not reach the GDB.

Kanizu creates a missing field with its alias and pick list and fills the Revit-owned ones. A station adds a field by copying `standards/fields.json` to the share, adding a row, and naming the copy in the model's `gis-model.json` `fields`. RevitGeoSuite then binds the row's parameter and exports its column, with no code change. The standard's `core` rows always win over a station copy.

## `gis-drift/<model id>.gis-drift.json` (version 1)

Kanizu's record of the GDB values and shapes that differ from a model's export, written whole every time Kanizu applies that model's GIS export to the GDB and again after every save into the GDB or restore of a GDB backup (through a temporary file and a rename). The file name is the model id in lower case (or the document key without one), with characters other than ASCII letters, digits and `-` turned into `_`. Kanizu keeps the GDB's value when an apply meets such an edit; the pyRevit **Review GDB edits** button reads the file, and accepting a row writes the GDB's value into the Revit parameter so the next export agrees.

| Field | Meaning |
|---|---|
| `schema`, `version` | `kanizu.gis-drift` and `1` |
| `model`, `appliedVersion` | The model key, and the version applied (relative to the station folder) |
| `rows[]` | `revitKey`, `uniqueId` (from the key), `layer`, `field` (the GDB field), `parameter`, `guid` and `binding` (from `gis-fields.json`; empty for a core field), `label` (the feature's name), `revit` (this export's value), `gdb` (the GDB's), `base` (the last applied export's) and `conflict` (both sides changed since the last apply). A unit `category` row also has `gdbCategory` and `gdbRestriction`: the GDB's station code as the Revit category it comes from and the floor type restriction it needs (absent when the code says nothing about one). |
| `notPlaced` | Keys of the applied version's features the apply did not put in the GDB (no layer of their kind on their floor, or a level paired with no floor); absent when none |
| `epsg` | The EPSG code of every `shapes[].outline`: the export's CRS, which is the model's project CRS. Absent when the export named none; no shape then has an outline |
| `shapes[]` | Units whose shape the GDB changed since the applied version, which Kanizu kept: `edit`, `revitKey`, `uniqueId` (from the key), `layer` (`unit`), `label`, `levelId` (the level paired with the floor the GDB row is on; empty for a deleted unit or a floor no level of the model is paired with), `group` and `keeps` (split and merge only), and `outline` (the GDB's outline as a GeoJSON Polygon or MultiPolygon in `epsg`, each position easting then northing whatever axis order the EPSG code names, exterior rings counter-clockwise; `null` for a deleted unit and a unit a merge took in). Absent when none |

`shapes[].edit` says what the GDB did:

| `edit` | In the GDB | `keeps` |
|---|---|---|
| `reshaped` | The unit has another outline. | |
| `deleted` | The unit is gone. | |
| `moved` | The unit's row is on another floor (or layer) while the element stayed on its level. | |
| `handDrawn` | A unit without `revit_key` that no element matches. `revitKey` is empty. | |
| `split` | The unit was cut into pieces, one row each. Every piece still carries the unit's `revitKey`, because a split row starts as a copy of the original; `group` is that key. | `true` on the piece in the unit's own row, which keeps the element; the others become new floors |
| `merged` | Units were merged into one. The one that keeps its row has the merged outline; the others are gone and have no outline. `group` is the key of the one that keeps its row. | `true` on that one; the others are deleted in Revit |

RevitGeoSuite's **Review GDB edits** previews each shape edit as an outline over the floor's plan. It applies `reshaped` by redrawing the floor's sketch, so the floor keeps its UniqueId and key. It applies `split` by redrawing the floor of the piece with `keeps` to that piece, and adding a floor for each other piece on the floor's level with its height offset, all as one undo step. A new floor takes the piece's `gdbName`, `gdbAltName` and `gdbCategory` as `name`, `short_name` and `category`, and the standard floor type of `gdbCategory` and `gdbRestriction`; a field the piece does not carry keeps the split floor's value or type (never `id` or `gis_key`); a piece a floor on the same level already matches is not added again. It applies `merged` by deleting the floors of the units the merge took in (only floors; another element is listed for a change by hand) and redrawing the floor of the unit with `keeps` to the merged outline, as one undo step; a floor already gone counts as done. It applies `handDrawn` by drawing a floor on the Revit level whose `level_id` equals the shape's `levelId`, of the standard floor type that `gdbCategory` and `gdbRestriction` name, or of the type the user picks when they are absent, with `gdbName` and `gdbAltName` as `name` and `short_name` when present; a floor on that level that already has the outline counts as done, and the next apply pairs the unit with the floor. The outline returns to model coordinates through the inverse of the export's shared-coordinate transform. The other edits are listed for a change by hand until they are applied too.

Two rows that carry one `revit_key` read as a split that waits for Revit, not as an error. Once Revit has made a floor for a piece, the next apply matches the piece's row with that floor by overlap and writes the floor's key onto it.

A core field maps to its parameter by name: unit and fixture `name` to `name`, `alt_name` to `short_name`, unit `category` to `category`, `direction` to `gis_direction`, and level `ordinal` to `ordinal` and `short_name` to `short_name`. Fixture `category` and level `name` have no parameter: a fixture's category only marks it a fixture, and a level's name is the Revit level's own. Unit `restrict` has no parameter: the GDB holds IMDF restrictions there (`employeesonly`), while the type's `gis_restriction` is the side of the ticket gates and picks the category code. A unit `category` row writes `gdbCategory`, not the station code; when the element's floor type has another restriction than `gdbRestriction`, it is listed for a floor type change by hand. Other core fields have no parameter and are listed for a change by hand. The button leaves out rows the model already agrees with and leaves conflicts unticked.

## `gis-drift/<model id>.gis-keep.json` (version 1)

RevitGeoSuite's record of drift rows answered **"Keep Revit's"** in **Review GDB edits** (`gisDrift.keep`), stored beside the model's drift file under the same sanitized file stem (ASCII letters, digits and `-` kept; everything else `_`). Kanizu remains the sole writer of the GDB: it reads the file, lists each entry in its Revit models review, and writes Revit's value into the GDB only during an apply when the entry is ticked and both sides still match its recorded values exactly. The two implementations are [`GisKeep`](../src/RevitGeoSuite.FloorPlanExport.Core/Standards/GisKeep.cs) and Kanizu's `kanizu-core/src/revit/keep.rs`.

| Field | Meaning |
|---|---|
| `schema`, `version` | `kanizu.gis-keep` and integer `1`. Both sides refuse any other schema, any version other than integer `1` (including `1.0` or `"1"`), and a file whose `model` does not match the expected model key. |
| `model` | The model key, matching `<model id>.gis-drift.json`'s `model`. |
| `entries[]` | One object per answered `(revitKey, layer, field, gdb)` conflict: `revitKey`, `layer` (`unit`, `opening`, `fixture`, `level`, ...), `field` (the GDB field), `revit` (Revit's value when kept), `gdb` (the GDB value being overridden), and `keptUtc` (ISO 8601 UTC to the second, e.g. `2026-11-21T09:30:00Z`). |

- **Value comparison and identity.** Values compare as exact strings (no trimming or case folding). When reading, JSON `null` or an omitted value reads as `""`, and numbers or booleans coerce to their text representation; entries missing `revitKey` or `field`, or with array/object values, are ignored on read and preserved verbatim on rewrite. An entry's conflict identity is **`(revitKey, layer, field, gdb)`** — a split feature with several GDB values for one `(revitKey, layer, field)` gets one entry per distinct `gdb` value. Re-answering the same `(revitKey, layer, field, gdb)` conflict in RevitGeoSuite updates that entry's `revit` and `keptUtc` in place.
- **Empty Revit values.** Kanizu never overwrites a non-empty GDB value with an empty `revit` value, even if a file contains such an entry; RevitGeoSuite skips recording entries where `revit` is empty over a non-empty `gdb`, or where `revit == gdb`.
- **Two-writer merge and lifecycle.** Files are written as UTF-8 JSON without a byte order mark (readers strip an optional UTF-8 BOM). Both sides preserve unknown top-level fields, unknown per-entry fields, and unrecognized entries verbatim, write through a temporary file beside the target, and refuse to replace the file if its on-disk bytes changed since it was read (RevitGeoSuite retries up to 3 times; Kanizu fails and asks to compare again). After saving an apply, Kanizu drops entries that were **taken** (matching by identity `(revitKey, layer, field, gdb)`), **done** (the GDB already holds `revit`), or **stale** (compared on an export newer than `keptUtc` and either side moved on); both **done** and **stale** entries are dropped only when the on-disk entry still matches all fields verbatim, so a newer answer recorded meanwhile stays. Entries left unticked or not checked stay. Dropping the last entry removes the file. Restoring a GDB backup in Kanizu merges the backup's keep entries back beside any entries RevitGeoSuite added since. Non-interactive apply tools inspect `gis-drift/*.gis-keep.json` and refuse to overwrite values answered there.

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

## `gis-network/<model id>.gis-network.json` (version 1)

Kanizu writes the whole file again each time it saves the network: when Save writes the network GDB, and when the project is saved after the network changed since the file was last written. It never writes on Generate alone. It writes through a temporary file and a rename, names the file like `gis-drift`, and deletes it when the model's network is cleared. RevitGeoSuite's GIS standard page reads it on its Network tab and draws it with DirectContext3D in the `GIS_` plans (each plan its own level's links, connector ends and findings) and in `GIS_` 3D views (all of it, connectors running between levels), plus the view that was active when it was shown. It reads the file again when a view opens and the file was written since. Nothing of it is an element, an undo step or part of any export, and no link or finding id is stored in the model. The sample is [tests/Fixtures/Conformance/gis-network](../tests/Fixtures/Conformance/gis-network/).

| Field | Meaning |
|---|---|
| `schema`, `version` | `kanizu.gis-network` and `1`. A file without a version is version 1. |
| `model`, `basedOn` | The model key, as in `gis-drift`, and the export version (relative to the station folder) whose CRS and level ids the file uses. |
| `writtenUtc`, `kanizu` | For display only. |
| `epsg` | The CRS of every position, `[easting, northing]` in metres, rounded to 1 mm. RevitGeoSuite inverts the export's shared-coordinate affine, which holds only for the model's project CRS: when `epsg` is another CRS it draws nothing and says why. |
| `levels[]` | `levelId`, `floorKey`, `ordinal`, `shortName`, for labels. |
| `links[]` | `id`, `levelId`, `line` (two or more positions), `oneWay` (first to last), `stepFree`, `passageType`. |
| `connectors[]` | `id`, `kind` (`elevator`, `escalator`, `stairs`, `ramp`, `movingwalkway`; another kind is drawn generically), `revitKey` and `uniqueId` (null when drawn by hand or untied), `from` and `to` (`levelId`, `at`), `oneWay`, `stepFree`, `passageType`. |
| `findings[]` | Only findings that need a model change: `kind`, `levelId` and `at` (either can be null; the finding then goes to the audit only), `revitKey`, `uniqueId`, `linkId`, `detail` (an English sentence). The audit words each kind itself: `transitWithoutLink`, `connectorWithoutElement`, `connectorOutsideUnit`, `floorWithoutConnector`, `rampUnresolved`, `rampUnjoined` and `linkThroughWall` are warnings, `doorWithoutLink` is a note, and an unknown kind is a note showing `detail`. |
| `notChecked` | The finding kinds Kanizu did not look for, because it wrote the file with no Generate output at hand (then only `floorWithoutConnector` is checked). Empty, or missing, when every kind was checked. The audit adds one note naming the kinds not checked, so a clean list does not read as none found. |
| `notShown` | `links`, `connectors`, `findings` that Kanizu left out because their floor is paired with no level of this model. |

Each point sits at its level's `Level.ProjectElevation`, found by the level's `level_id` parameter, lifted 100 mm so the floor's top face does not hide it. Items on a `levelId` no level of the model has are counted, not drawn. In a host model only the host's own file is read; drawing linked models' files through the link transforms is not built yet.

## Round-trip state

Both apps say where each model stands in the round trip with the same five words, read from the station folder alone, so they agree on the same folder. RevitGeoSuite shows the station's state on the GIS standard ribbon button and Kanizu beside its window title.

Per model, take the model's newest export under `revit/`: versions are grouped by `source.modelId` in lower case; a version without one belongs to the model id seen in its model folder (from that folder's newest version that has one), else to its folder; the newest is the one with the latest `createdUtc` (then the version folder's path). Then read the model's drift file, `gis-drift/<model>.gis-drift.json`, named by that version's model id in lower case, or its `source.documentKey` without one.

| Newest export applied | Drift `rows` or `shapes` | State |
|---|---|---|
| yes | none | In sync |
| no, or no drift file, or empty `appliedVersion` | none | Revit ahead |
| yes | some | GDB ahead |
| no | some | Both changed |

"Applied" means the drift file's `appliedVersion` equals the newest export's version folder relative to the station (`revit/<model folder>/<version folder>`). `notPlaced` does not count as waiting. A model is **Blocked** when its drift file cannot be read, has a `version` newer than the reader knows, or names an `appliedVersion` folder that is not in the station folder. A station whose `revit/` folder cannot be listed is Blocked.

A station's state is its models' worst: Blocked if any model is; else Both changed if a model is Both changed, or one model is Revit ahead while another is GDB ahead; else Revit ahead or GDB ahead if a model is; else In sync. A station with no export has no state.

| State | 日本語 |
|---|---|
| In sync | 同期済み |
| Revit ahead | Revit が先行 |
| GDB ahead | GDB が先行 |
| Both changed | 両方で変更あり |
| Blocked | 停止中 |

Kanizu's own new-export notice judges a pending export against the GDB, so it can say Revit ahead where the folder still says Both changed (the export already agrees with the GDB's edits); the folder state above is the one both apps show.

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
| `gis-drift/*.gis-drift.json` | 1 | as above (`epsg` and `shapes` were added in version 1: a reader that ignores them still reads every value row right) |
| `gis-drift/*.gis-keep.json` | 1 | an older reader or writer would misread it; unknown top-level/entry fields and unkeyed entries are preserved verbatim |
| `gis-network/*.gis-network.json` | 1 | an older reader would misread it; new connector kinds, passage codes and finding kinds need no new version |

A reader refuses a file newer than it knows, naming the file and both versions, and reads a file without a version as it is.
