# KanizuGIS pyRevit extension

Three buttons that bring a Revit model to the Kanizu GIS standard and check it, so the GDB export needs less hand work in ArcGIS Pro. The standard itself (shared parameters with fixed GUIDs, floor types and theme colours) is in [`../standards`](../standards/README.md); RevitGeoSuite and these tools read the same files.

| Button | What it does | Changes the model |
|---|---|---|
| Set up model | Binds the 12 shared parameters from the shipped file, creates or recolours the `GIS_<area>` materials, creates or updates the 12 standard floor types (`gis_category`, `gis_restriction`, material on the top layer) and writes `gis_standard_version`. When it first stamps the version, it also clears a floor's own `category` where that only repeats the category its type gives (older exports wrote it back into every floor; from the stamped version on, RevitGeoSuite counts every floor `category` as a deliberate override). Shows the full list of changes first and changes nothing until you accept; everything happens in one transaction. | Yes, after you accept |
| Audit | Lists what would need fixing by hand after an export: standard version, unbound or legacy parameters, floor types in use without `gis_category` or with an unknown one, floors with an unknown `category`, levels with floors but no `ordinal` or `short_name`, escalator-like families without `gis_kind`. Each row links to the elements. | No |
| Fix floor types | For each floor type in use that is not a standard type and has no usable `gis_category`, proposes the standard type it matches by name (`j 改札内コンコース_床` is `j ラチ内コンコース_床`; `ラチ内店舗` keeps `rachi_nai`). Accepted rows get the standard `gis_category`, `gis_restriction` and material written onto their own type. Types that match nothing are listed for a person to decide. | Yes, the rows you accept |

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

The plan's view template, `GIS_<level>` views and scope boxes, review and presentation views, schedules, the welcome sheet, A1 sheets and the legend come in a follow-up, as do the Export and Parameters buttons (P3). "By the type under it" matching in Fix floor types is not done; it matches by name only.
