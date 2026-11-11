# Kanizu GIS standard

The files a Revit model needs so that its GDB export comes out right the first time. RevitGeoSuite embeds them in `RevitGeoSuite.FloorPlanExport.Core.dll`, and the pyRevit tools read them from this folder.

| File | What it is | Edited by hand |
|---|---|---|
| `color_theme.json` | Kanizu's area colour table (StaNavi map design), copied unchanged from Kanizu `crates/kanizu-core/src/recolor/color_theme.json` (dev `de67c3c`) | No, copy it again from Kanizu |
| `floor-types.json` | The 12 standard floor types with `gis_category`, `gis_restriction`, a `GIS_<area>` material and its theme colour, plus one material per theme area, `GIS_unlisted` (#E5E5E5) and the list of unit categories the export accepts | No, generated |
| `KanizuGIS.SharedParameters.txt` | The shared parameter file, with fixed GUIDs | No, generated |
| `shared-parameter-bindings.json` | The same parameters with their binding (type or instance) and categories, for the pyRevit tools | No, generated |

## Shared parameters

| Name | Binding | Bound to |
|---|---|---|
| `id` | instance | floors, rooms, stairs, generic models, specialty equipment, doors, windows, columns |
| `level_id` | instance | levels, floors, rooms |
| `name` | instance | floors, rooms, stairs, generic models, specialty equipment |
| `short_name` | instance | the same, and levels |
| `category` | instance | floors, rooms, stairs, generic models, specialty equipment; overrides the type's `gis_category` |
| `ordinal` | instance (integer) | levels, floors, rooms |
| `gis_category` | type | floors, generic models, specialty equipment |
| `gis_restriction` | type | floors, generic models, specialty equipment; empty means by location |
| `gis_kind` | type | generic models, specialty equipment (escalator, moving_walkway, elevator, ticket_gate) |
| `gis_direction` | instance | stairs, generic models, specialty equipment (up, down, both) |
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

## Changing the standard

1. Edit `GisSharedParameters.cs` or `FloorTypeStandard.cs` under `src/RevitGeoSuite.FloorPlanExport.Core/Standards/`, or copy a new `color_theme.json` from Kanizu.
2. Set `REVITGEOSUITE_REGENERATE_STANDARDS=1` and run `GisStandardTests` once. It rewrites the generated files.
3. Raise `GisStandard.Version` when existing models have to be set up again.

Never change the GUID of a released parameter: models bound to it would no longer match.
