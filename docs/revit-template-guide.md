# Kanizu Revit Master Template (`.rte`) & `.rfa` Authoring Guide

This guide defines how to author and maintain the single master **Revit 2024 `.rte`** station template and standard `.rfa` families for **Kanizu Revit** (`RevitGeoSuite`).

## 1. Single Master Revit 2024 `.rte` Baseline

- **Author and maintain ONE master template in Revit 2024** (`KanizuStationMaster2024.rte`).
- Revit 2027 automatically upgrades `.rte` files authored in Revit 2024 on open — do not maintain forked Revit 2024 and Revit 2027 `.rte` masters.
- All Kanizu Revit add-in features (shared parameters, floor types, QA schedules, export view template, and GeoPackage/Cesium exports) run identically on Revit 2024 (`net48`) and Revit 2027 (`net10.0-windows`).

## 2. Building or Updating the Master `.rte` with Prepare Template

1. Open the master `.rte` file (or start a clean empty Revit 2024 project to save as `.rte`).
2. Open **GIS standard** (`#/gis-standard`). Kanizu Revit auto-detects `.rte` or empty models into **Template** mode.
3. Click **Prepare Template**. In one undoable Revit transaction, Kanizu Revit idempotently:
   - Binds all **15 frozen shared parameters** from `standards/KanizuGIS.SharedParameters.txt` with their shipped GUIDs and category bindings.
   - Creates/updates all **7 `GIS_*` theme materials** matching `standards/color_theme.json`.
   - Creates/updates all **21 standard `j *_床` floor types** with `gis_category`, `gis_restriction`, and top-layer material.
   - Stamps `gis_standard_version = 2` on Project Information.
   - Creates/updates the **4 standard QA schedules**:
     - `Kanizu GIS - Floors` (`gis_category`, `gis_restriction`, `category`, `name`, `id`)
     - `Kanizu GIS - Circulation` (multi-category: `gis_kind`, `gis_direction`, `gis_levels_served`, `name`, `id`)
     - `Kanizu GIS - Doors` (`gis_door`, `gis_access_con`, `name`, `id`)
     - `Kanizu GIS - Levels` (`ordinal`, `short_name`, `level_id`)
   - Creates/repairs the **`Kanizu GIS Export Plan`** view template (coarse detail, Coordination discipline, cut plane 1200 mm / top clip 2300 mm, export-read model categories visible, only `GIS_*` line styles visible).
4. Load your approved station `.rfa` families (see Section 3).
5. Confirm **Template** mode reaches **100% Ready** (all project-only checks — Japan CRS, `ModelId`, and level ordinals — are marked `N/A` in Template mode).
6. Save the `.rte` file.

## 3. `.rfa` Family Authoring & Parameter Specification

All shared parameters added inside `.rfa` families **must** come from `standards/KanizuGIS.SharedParameters.txt` so their GUIDs match project bindings and QA schedules.

### 3.1 Vertical Circulation & Ticket Gate Families (`standards/families.json`)

Every family listed in `standards/families.json` must carry the **`gis_kind`** **Type** shared parameter (`ff63e172-c407-4265-b075-4e1d5e9822b8`) set to the exact token for its kind on **every type** inside the `.rfa`:

| `gis_kind` | Approved Standard Family Names (`families.json`) | Typical Revit Category | Optional Instance Parameters |
| --- | --- | --- | --- |
| `escalator` | `j エスカレータ-lightweight`, `j エスカレーター-lightweight` | Generic Models / Specialty Equipment | `gis_direction` (`up`, `down`, `both`) |
| `moving_walkway` | *(add approved family names to `standards/families.json`)* | Generic Models / Specialty Equipment | `gis_direction` (`up`, `down`, `both`) |
| `elevator` | `j EV` | Mechanical Equipment / Specialty Equipment | `gis_levels_served` (comma-separated level names or `short_name`s) |
| `ticket_gate` | `j 自動改札` | Specialty Equipment / Generic Models | `name`, `short_name` |

- Duplicating a type inside an approved family keeps it standard as long as `gis_kind` stays set to the family's kind.
- To approve a new family name, add it under its kind in `standards/families.json` (each family name may appear under only one kind).

### 3.2 Door Families (`OST_Doors`)

Every door family intended for IMDF opening export must expose these **Type** shared parameters (automatically bound to `OST_Doors` by **Prepare Template** / **Set up model**, and also attachable directly inside the `.rfa` from `standards/KanizuGIS.SharedParameters.txt`):

| Parameter | GUID | Binding | Purpose |
| --- | --- | --- | --- |
| `gis_door` | `e413bbb1-dab0-4acd-9b16-eeac608dca9b` | Type | Exported to opening `door` field (e.g., `sliding`, `swinging`, `automatic`, `open`) |
| `gis_access_con` | `0156f4b3-7806-4033-baa0-09838c299ddb` | Type | Exported to opening `access_con` field (e.g., `public`, `staff`, `restricted`) |

### 3.3 Validating `.rfa` Libraries

Use **Validate .rfa folder…** in `#/gis-standard` to inspect an external folder of `.rfa` files read-only against `standards/families.json` and the door parameter rules before loading them into the master `.rte`.

## 4. Template Mode vs. Project Mode Readiness

| Check Group | Template Mode (`.rte`) | Project Mode (`.rvt`) |
| --- | --- | --- |
| Shared parameters (15 frozen GUIDs) & `gis_standard_version` | Required | Required |
| 21 standard floor types & `GIS_*` materials | Required | Required |
| Loaded families (`families.json` `gis_kind` + door `gis_door`/`gis_access_con`) | Required | Required |
| 4 QA schedules & `Kanizu GIS Export Plan` view template | Required | Required |
| Japan Plane Rectangular CRS (`EPSG:6669`–`6687`) verified | **N/A** | Required |
| Host `ModelId` & all loaded Revit links have stable `ModelId` | **N/A** | Required |
| Project levels have complete, non-duplicate `ordinal` & `short_name` | **N/A** | Required |
