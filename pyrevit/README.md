# KanizuGIS pyRevit extension

Five buttons that bring a Revit model to the Kanizu GIS standard, check it and export it, so the GDB export needs less hand work in ArcGIS Pro. The standard itself (shared parameters with fixed GUIDs, floor types and theme colours) is in [`../standards`](../standards/README.md); RevitGeoSuite and these tools read the same files.

| Button | What it does | Changes the model |
|---|---|---|
| Set up model | Binds the 12 shared parameters from the shipped file, creates or recolours the `GIS_<area>` materials, creates or updates the 12 standard floor types (`gis_category`, `gis_restriction`, material on the top layer) and writes `gis_standard_version`. When it first stamps the version, it also clears a floor's own `category` where that only repeats the category its type gives (older exports wrote it back into every floor; from the stamped version on, RevitGeoSuite counts every floor `category` as a deliberate override). Shows the full list of changes first and changes nothing until you accept; everything happens in one transaction. | Yes, after you accept |
| Audit | Lists what would need fixing by hand after an export: standard version, unbound or legacy parameters, floor types in use without `gis_category` or with an unknown one, floors with an unknown `category`, levels with floors but no `ordinal` or `short_name`, escalator-like families without `gis_kind`. Each row links to the elements. | No |
| Fix floor types | For each floor type in use that is not a standard type and has no usable `gis_category`, proposes the standard type it matches by name (`j 改札内コンコース_床` is `j ラチ内コンコース_床`; `ラチ内店舗` keeps `rachi_nai`). Accepted rows get the standard `gis_category`, `gis_restriction` and material written onto their own type. Types that match nothing are listed for a person to decide. | Yes, the rows you accept |
| Export | Runs Audit, asks before going on when it finds errors, then opens RevitGeoSuite's Export. The export reads the model's `gis-model.json` in the station folder and preselects its views (else the `GIS_` views), so every PC exports the same. | No |
| Parameters | Writes the GIS parameters of every floor, placed room, stair, escalator-like family and level to a CSV for Excel (UTF-8 with a byte order mark), and reads an edited CSV back (UTF-8 or the CP932 that Japanese Excel saves). Rows are tied to elements by `element_id`, so they can be sorted, filtered or deleted. Only the cells that changed are written, only in the columns that kind of element owns (`name`, `short_name`, `category`, `gis_direction` on floors, rooms, stairs and families; `short_name` and `ordinal` on levels), and only values the standard accepts. Each change is listed first and nothing is written until you accept. | Yes, the changes you accept |

## Rules the tools keep

- Nothing is deleted, and no floor changes type, so every element keeps its UniqueId and its `revit_key`.
- A parameter bound as the wrong kind (type instead of instance, or the reverse) is left as it is and reported, whatever its GUID.
- A parameter the model already holds under another GUID (older RevitGeoSuite created one file per PC) is kept. It only gains the categories the standard adds, and Audit reports it.
- Run Set up model on a detached copy of a model the first time.

## Install

In pyRevit, Settings, Custom Extension Directories, add the `pyrevit` folder of this repository (or of an installed copy that keeps `standards` beside it), then reload. The tools find `standards/` by walking up from the extension; set `KANIZU_GIS_STANDARDS` to point elsewhere.

Runs on pyRevit's default IronPython 2.7 engine with Revit 2024, and is written for the CPython engine and Revit 2027 too (element ids are read with `ElementId.Value`).

## Tests

Everything except `lib/kanizu_gis/revit_io.py` and the button scripts is plain Python:

    python -m unittest discover -s pyrevit/KanizuGIS.extension/tests

CI runs the same command. The Revit side can only be checked in Revit, on a detached copy of a real model.

## Not in this version

The plan's view template, `GIS_<level>` views and scope boxes, review and presentation views, schedules, the welcome sheet, A1 sheets and the legend come in a follow-up. "By the type under it" matching in Fix floor types is not done; it matches by name only.
