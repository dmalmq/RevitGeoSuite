# Unit category export filter — design

Date: 2026-07-06
Status: approved

## Problem

The floor-plan export always writes every unit feature. Users sometimes need only a subset — e.g. export only `category="column"` units for downstream tooling — and currently have to post-process the output.

## Decision summary

- **Filter model:** multi-select include list of unit categories. Empty selection = no filtering (default, backward compatible).
- **Scope:** applies to both the map preview and the written shapefile/GeoPackage output (WYSIWYG).
- **UI placement:** new "Unit filter" sub-disclosure inside the existing Advanced options section of the export dialog.

## Design

### Settings & contracts

- New setting `unitCategories: string[]` (empty array default) on:
  - the Svelte settings object in `src/RevitGeoSuite.SharedUI.Web/src/routes/export/FloorPlanExportRoute.svelte`;
  - C# `ExportDialogSettings` and `ExportProfile` (`src/RevitGeoSuite.FloorPlanExport/UI/`), so the filter persists in saved profiles;
  - RPC contracts (`src/RevitGeoSuite.SharedUI.Web.Contracts/ExportContracts.cs`) with `contracts.generated.ts` regenerated.

### UI

- "Unit filter" sub-disclosure in Advanced options, using the existing chip-group pattern (same as the Features chips). One chip per known category; checked = included in export. All unchecked = export everything.
- Category list is supplied by the backend state payload — union of `ImdfUnitCategoryCatalog` official categories and any custom categories in `ZoneCatalog.CreateDefault()` — not hardcoded in the frontend.
- Section is visually disabled when the `unit` feature type toggle is off.
- Localized strings via `UiLocalizer` (`Exporter.UnitFilter`, etc.).

### Backend filtering point

- `unitCategories` threads through `FloorExportPreparationOptions` into `FloorExportDataPreparer.PrepareViews`.
- Filtering happens **after** `NormalizeUnitFeatures`, only when populating the final `unitLayer` (the loop at the end of the unit block). The unfiltered `unitFeatures` list continues to feed opening extraction and level boundaries, so filtering does not degrade those outputs. Carving (columns punched out of rooms) is computed with full context before non-matching units are dropped.
- Because `ExportPreviewService` and the file writers both consume `prepared.UnitLayer`, preview and output stay consistent with no additional plumbing.

### Matching semantics

- Case-insensitive, trimmed comparison of the feature's `category` attribute against the include list.
- Implemented as a small pure helper in `RevitGeoSuite.FloorPlanExport.Core` (e.g. `UnitCategoryFilter.ShouldInclude(category, includeList)`), unit-testable without Revit.

## Alternatives considered

- Filter in the file writers: preview would not match output — rejected.
- Filter in the frontend preview only: written files would be unfiltered — rejected.

## Testing

- Core tests for the filter helper: empty list includes all; non-empty list includes only matches; case/whitespace insensitivity; missing/null category excluded when a filter is active.
- Manual: export with only `column` checked → unit layer contains only column features; preview matches; openings/levels unchanged versus unfiltered run.
