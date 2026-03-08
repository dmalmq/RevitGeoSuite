# Open Questions

Items marked **RESOLVED** have binding decisions. Items marked **DEFERRED** are intentionally postponed past V1.

---

## Product / UX

- **What should the final suite product name be?**
  **RESOLVED:** Revit Geo Suite. Internal namespace: `RevitGeoSuite`.

- **Should the georeferencing workflow be wizard-style or a single advanced dialog?**
  **RESOLVED:** Wizard-style. Steps: CRS selection → map point selection → preview → confirm → apply.

- **How much explanation should be visible by default for beginner users?**
  Open. Decide during UI implementation. Default to more guidance; consider a "compact mode" later.

- **Should the first module support "review existing setup" separately from "create new setup" in V1?**
  **RESOLVED:** No separate "review" mode in V1. The workflow always starts by reading current state and showing it in the preview step. If existing setup exists, a warning is displayed before apply.

---

## Technical

- **Should the map be embedded in a WPF WebView2 control or opened in a separate companion window?**
  **RESOLVED:** Embedded WebView2 control in SharedUI (`MapControl.xaml`). Uses Leaflet.js + OSM tiles. See `docs/DECISIONS.md`.

- **Which CRS library or EPSG data source should be used?**
  **RESOLVED:** ProjNet4GeoAPI (NuGet package: `ProjNet`). Japanese CRS presets (EPSG:6669–6687) hardcoded in `JapanCrsPresets.cs`.

- **What is the exact persisted CRS reference shape?**
  **RESOLVED:** `CrsReference` with `EpsgCode` (int) and optional `NameSnapshot` (string). Full CRS definitions are resolved at runtime from `ICrsRegistry`. See `Architecture.md`.

- **What Revit versions should be supported first?**
  **RESOLVED:** Revit 2024 only for V1. .NET Framework 4.8.

- **How should the shell perform module discovery in the simplest reliable way?**
  **RESOLVED:** Shell scans a known modules folder for assemblies matching `RevitGeoSuite.*.dll`. Each module exposes one `IRevitGeoModule` implementation. Incompatible modules are skipped and logged.

---

## Revit API

- **What is the safest exact API strategy for survey point or project base point related operations?**
  **RESOLVED:** Use `BasePoint.GetSurveyPoint(doc)` and `BasePoint.GetProjectBasePoint(doc)` for reading. Use `ProjectLocation.SetProjectPosition()` for writing. See `docs/05-revit-api-notes.md` for full details.

- **How should the tool detect meaningful pre-existing coordinate setup?**
  **RESOLVED:** Read `ProjectPosition` from `doc.ActiveProjectLocation`. If any of EastWest, NorthSouth, or Angle are non-zero, or if `GeoProjectInfo` already exists in Extensible Storage, warn the user.

- **What type of rotation should V1 actually perform?**
  **RESOLVED:** V1 sets `ProjectPosition.Angle` (true north rotation). This rotates the coordinate system reference, not the building geometry. The UI must clearly explain this.

- **Should linked model behavior be ignored, detected, or partially supported in V1?**
  **RESOLVED:** Ignored in V1. Linked model support is deferred. The tool should not crash if linked models exist but should not attempt to modify their placement.

- **What should happen when canonical location data changes but persisted convenience values such as mesh codes become stale?**
  **RESOLVED:** When `Origin` or `ProjectCrs` changes, `PrimaryMeshCode` is invalidated (set to null). The Mesh Inspector module recomputes it on next use. A warning is shown if mesh code appears stale.

---

## Data / Accuracy

- **What confidence language should be shown when placement is based only on OSM?**
  **RESOLVED:** `GeoConfidenceLevel.Approximate` with display text: "Approximate (map-based selection)". The UI must show this clearly in the preview and after apply.

- **Should the tool explicitly label OSM-based placement as approximate?**
  **RESOLVED:** Yes. Always. The confidence level is persisted in `GeoProjectInfo.Confidence`.

- **What validation thresholds count as suspicious for offsets and coordinate ranges?**
  **RESOLVED for V1:**
  - Coordinates outside Japan bounding box (lat 20–46, lon 122–154): warning
  - Offset from origin > 100km: warning
  - Elevation outside -500m to +5000m: warning
  - These are soft warnings, not hard blocks

---

## Future Expansion (DEFERRED past V1)

- When should PLATEAU import move from lightweight context to more advanced semantics? **DEFERRED**
- When should parcel, shapefile, or GeoPackage overlays be introduced? **DEFERRED**
- Should official base maps or aerial imagery be supported later? **DEFERRED** (likely yes)
- Should footprint extraction from Revit geometry become a later phase? **DEFERRED**
- Should the GeoPackage exporter be migrated into the suite after the first modules are stable? **DEFERRED** (likely yes, as Phase 7+)
- Should IMDF-related tooling eventually become a suite module? **DEFERRED** (likely yes, as Phase 7+)
