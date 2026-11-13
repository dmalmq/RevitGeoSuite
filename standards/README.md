# Kanizu GIS standard

The files a Revit model needs so that its GDB export comes out right the first time. RevitGeoSuite embeds them in `RevitGeoSuite.FloorPlanExport.Core.dll`, and the pyRevit tools read them from this folder.

| File | What it is | Edited by hand |
|---|---|---|
| `color_theme.json` | Kanizu's area colour table (StaNavi map design), copied unchanged from Kanizu `crates/kanizu-core/src/recolor/color_theme.json` (dev `de67c3c`) | No, copy it again from Kanizu |
| `floor-types.json` | The 21 standard floor types with `gis_category`, `gis_restriction`, a `GIS_<area>` material and its theme colour, the `former_names` they replace, plus one material per theme area, `GIS_unlisted` (#E5E5E5) and the list of unit categories the export accepts | No, generated |
| `KanizuGIS.SharedParameters.txt` | The shared parameter file, with fixed GUIDs | No, generated |
| `fields.json` | The field list: one row per GIS field with its layer, GDB name, Japanese alias, type, pick list, Revit source and owner ([contract](../docs/kanizu-link-contract.md)) | No, generated; a station copy gets rows added by hand |
| `gis-model.template.json` | The start of a model's `gis-model.json` in the station folder: the standard's stair drawing numbers and empty identity, levels and views ([contract](../docs/kanizu-link-contract.md)) | No, generated |
| `shared-parameter-bindings.json` | The same parameters with their binding (type or instance) and categories, for the pyRevit tools | No, generated |

## Shared parameters

| Name | Binding | Bound to |
|---|---|---|
| `id` | instance | floors, rooms, stairs, generic models, specialty equipment, doors, windows, columns |
| `level_id` | instance | levels, floors, rooms |
| `name` | instance | floors, rooms, stairs, generic models, specialty equipment, doors |
| `short_name` | instance | the same, and levels |
| `category` | instance | floors, rooms, stairs, generic models, specialty equipment; overrides the type's `gis_category` |
| `ordinal` | instance (integer) | levels, floors, rooms |
| `gis_category` | type | floors, generic models, specialty equipment |
| `gis_restriction` | type | floors, generic models, specialty equipment; empty means by location |
| `gis_kind` | type | generic models, specialty equipment (escalator, moving_walkway, elevator, ticket_gate) |
| `gis_direction` | instance | stairs, generic models, specialty equipment (up, down, both) |
| `gis_levels_served` | type | generic models, specialty equipment; the levels an elevator stops at, by level name or `short_name`, separated by commas; empty means the levels the car spans |
| `gis_door` | type | doors; the opening's `door` field |
| `gis_access_con` | type | doors; the opening's `access_con` field |
| `gis_key` | instance, not user editable | floors, rooms, stairs, generic models, specialty equipment, columns; the export writes the element's first `revit_key` (part 0) here |
| `gis_standard_version` | instance, not user editable | project information; the version of this standard the model was set up with |

The first six names are the ones RevitGeoSuite has always used. Earlier versions created them from a separate file on each PC, so an existing model may hold them under other GUIDs. The export keeps those definitions, adds any missing categories to their binding, and lists them in a warning. It never rebinds a parameter that already holds values.

## How a floor gets its category

1. The floor's own `category`, when it is a known unit category.
2. The floor type's `gis_category`, with `gis_restriction` (or, when that is empty, the restriction the type name gives).
3. A per-PC override chosen in the export dialog.
4. The floor type name, `j <zone>_床`, looked up in the zone catalog.

The export no longer writes the resolved category back into a floor's `category` (rooms still get it), so a value there is always one somebody set. Exports before this standard wrote it into every floor, so until a model is set up with the standard (Project Information has `gis_standard_version`), a floor's `category` that only repeats what steps 3 and 4 give is read as that old echo and does not override a type that has its own `gis_category`. Set up model clears those echoes when it stamps the version; from then on every floor `category` counts.

A generic model or specialty equipment family gets its unit category the same way: `gis_kind` first, then its own `category`, then its type's `gis_category` with `gis_restriction`, then the per-PC override and the family name catalog. A family with a category from these parameters is exported as a unit even when its name is not in the catalog.

## Floor type names

Each standard name, `j <zone>_床`, means exactly one GDB code, and the gate side (`ラチ内`, `ラチ外`) is in the name wherever the code depends on it. Version 2 split `j 店舗_床` into `j ラチ外店舗_床` (`store`) and `j ラチ内店舗_床` (`store_sta`) and added `j ラチ外スロープ_床`, `j ラチ内スロープ_床`, `j 案内所_床`, `j ベビー休憩室_床`, `j 喫煙所_床`, `j その他_床`, `j 道路_床` and `j 屋外_床` (`unenclosedarea`, which also covers roofs).

`former_names` in `floor-types.json` lists the names models still use and the standard names each can now be: `j 店舗_床` (either shop type), `smoking room`, `Baby room`, `Roof` and `Other`. A floor type is an old name when its zone name equals a former name's, ignoring case, and it is not a standard name.

- **Audit** warns once per old-named type in use, and says which name to rename it to.
- **Fix floor types** offers one row per current name, narrowed to one when the type's own `gis_restriction` or its name says inside or outside. Accepting a row writes `gis_category`, `gis_restriction` and the material and renames the type. Several rows for one type cannot be accepted together. A name that another floor type already has is not a rename target, so that type only gets its parameters. The type becomes a standard type, so the check stops reporting it.
- **Set up model** does not create a standard type while an old-named type in use lists that name, so the name stays free for the rename.
- Renaming never changes a floor's type and never deletes a type.

## Changing the standard

1. Edit `GisSharedParameters.cs` or `FloorTypeStandard.cs` under `src/RevitGeoSuite.FloorPlanExport.Core/Standards/`, or copy a new `color_theme.json` from Kanizu.
2. Set `REVITGEOSUITE_REGENERATE_STANDARDS=1` and run `GisStandardTests` once. It rewrites the generated files.
3. Raise `GisStandard.Version` when existing models have to be set up again.

Never change the GUID of a released parameter: models bound to it would no longer match.

## Standards check

RevitGeoSuite checks a model against this standard itself (`GisStandardCheck`), so nobody has to remember to run Audit and the check also works without pyRevit. It reads `gis_standard_version`, the bound parameters, the `GIS_` plan views and the floor types in use. The current version is `GisStandard.Version` and the oldest one the export still takes is `GisStandard.OldestSupportedVersion`.

- **When a model opens.** One notice per model per Revit session, only for a model that holds the version stamp or any of the six parameters RevitGeoSuite has always used, and only when it is behind the standard, newer than the add-in, or would be blocked at export. It offers Update now (how to run Set up model), Later, and Skip this version (remembered on this PC until a newer standard).
- **On the ribbon.** The GIS standard button on the Geo Suite panel shows the active model's state (Current, Update available, Update needed, Update add-in, Not set up, Needs fixing). Clicking it lists every issue.
- **At export.** The export and the preview carry the state line and the warnings into the report. The export is blocked, and says why, only when the output would be wrong: a model below the oldest supported version, or a `gis_` parameter bound under a GUID other than the shipped one (or as a project parameter). The six older parameters keep their own GUIDs with a warning, because the export reads them by name and Set up model never rebinds them. The Cesium export applies the same rule.
- **In pyRevit.** Audit prints RevitGeoSuite's verdict at the top when RevitGeoSuite is loaded, and Export stops before opening RevitGeoSuite's export when that verdict blocks it.

## Field list

`fields.json` lists every field the export writes, with the alias and pick list Kanizu gives the GDB field. Most rows are `core`. The opening's `door`, `access_con` and `name` are `parameter` rows that read the standard parameters `gis_door`, `gis_access_con` (both from the door's type) and `name`, so a GDB edit to them can be taken back into Revit. Level `address_id` and `build_id` and fixture `anchor_id` are GDB-owned rows: the export never writes them, and Kanizu keeps the GDB's values. To add a field for a station, copy it to the station folder, add a row, and set `fields` in the model's `gis-model.json` to the copy's name. For example, an operator name kept in a new instance parameter:

```json
{
  "layer": "unit", "name": "operator", "aliasJa": "事業者", "type": "text", "length": 50,
  "values": ["JR東日本", "東京メトロ"], "owner": "revit",
  "source": { "kind": "parameter", "parameter": "gis_operator", "guid": "<a new GUID>",
              "binding": "instance", "categories": ["OST_Floors", "OST_Rooms"] }
}
```

The next export binds `gis_operator` with that GUID to floors and rooms, and writes an `operator` column on units. A row with `"owner": "gdb"` and `"source": { "kind": "none" }` is a GDB-only field, which the export never writes. A row with `"kind": "derived"` exports a value the export works out, such as `ViewName`. Rows that break a rule (unknown layer, bad field name, a new parameter without a GUID or categories, a GUID used twice) are left out with a warning in the export report. A station copy cannot change the standard's `core` rows.

