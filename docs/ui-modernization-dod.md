# UI Modernization — Definition of Done (Parity Checklist)

**Status:** Living document, updated per phase
**Date:** May 2026
**Related:** `docs/plan-ui-modernization.md`

---

## Purpose

This checklist defines the per-screen parity requirements for the WebView2/Svelte
rewrite. Each old WPF window must be verified against this list before its
replacement is considered complete. Use this during code review and manual testing.

---

## Global Requirements (All Screens)

### Shell & Chrome

- [ ] Custom title bar renders with RGS logo, document name, language toggle, min/max/close
- [ ] Window is resizable (min 1024x600)
- [ ] Dark mode is default; light mode toggle persists to localStorage
- [ ] Status footer shows: active document name, current CRS (EPSG), primary mesh code, ready/blocked pill
- [ ] Left nav rail has 3 items (GEO/IMP/EXP) with active state highlighting
- [ ] Rail navigation is SPA (no window teardown)
- [ ] WebView2 runtime detection shows actionable fallback with download link if missing

### Localization

- [ ] All visible strings come from `UiLocalizer` via RPC (no hardcoded strings)
- [ ] EN ↔ JP toggle updates all strings without reload
- [ ] Language choice persists across sessions
- [ ] Auto-detect Revit UI language on first launch

### RPC Bridge

- [ ] All requests use typed envelope: `{ kind, id, method, payload, error }`
- [ ] Errors return `{ code, message }` and reject with `BridgeError` in JS
- [ ] Long-running ops use job protocol: `job.started`, `job.progress`, `job.completed`/`job.failed`
- [ ] Job cancellation works via `job.cancel` RPC
- [ ] Progress events stream correctly (percent, message, phase)

### Accessibility

- [ ] All interactive elements have `aria-label` or visible text
- [ ] Focus management: Tab order is logical, focus visible
- [ ] Keyboard shortcuts: Esc closes, Enter applies, Tab navigates
- [ ] Color contrast meets WCAG AA (4.5:1 for text, 3:1 for large text)

---

## Georeference Setup (`/georeference`)

**Replaces:** `GeoreferenceWindow.xaml`, `MeshInspectorWindow.xaml`, `ValidationWindow.xaml`

### Workflow Steps

- [ ] Stepper shows 6 steps: Review → CRS → Survey Point → PBP → Preview → Apply
- [ ] Each step gates the next (cannot skip ahead without completing prerequisites)
- [ ] Stepper shows completed state (checkmark) for finished steps
- [ ] Clicking a completed step returns to it

### Step 1: Review Current Setup

- [ ] Loads current georeference state from Revit document
- [ ] Shows readiness banners (error/warning/info) from `ProjectHealthChecker`
- [ ] Blocking errors prevent progression
- [ ] "Continue to CRS Selection" button advances to step 2

### Step 2: Select CRS

- [ ] CRS picker shows searchable dropdown with Japanese zones (EPSG:6669–6679)
- [ ] Selected CRS persists in route state
- [ ] "Confirm CRS" button advances to step 3

### Step 3: Place Survey Point

- [ ] Map shows contextual prompt: "Click on the map to place the survey point"
- [ ] Click places marker and records lat/lon
- [ ] Side panel shows selected coordinates
- [ ] "Continue to Project Base Point" button advances to step 4

### Step 4: Place Project Base Point

- [ ] Map shows contextual prompt: "Click on the map to place the project base point"
- [ ] Click places marker and records lat/lon
- [ ] Side panel shows selected coordinates
- [ ] "Continue to Preview" button advances to step 5

### Step 5: Preview Changes

- [ ] Visual diff component shows current vs proposed side-by-side
- [ ] Highlights changed values in amber
- [ ] Shows CRS, Survey Point, Project Base Point
- [ ] "Apply Changes" button advances to step 6
- [ ] "Back to Review" button returns to step 1

### Step 6: Apply

- [ ] Confirmation panel lists all changes to be applied
- [ ] "Confirm Apply" button executes `georeference.apply` RPC
- [ ] Apply uses Revit transaction (via `RevitContext.InvokeWithDocumentAsync`)
- [ ] Success shows completion message and reloads readiness status
- [ ] Failure shows error banner and allows retry

### Mesh Overlay

- [ ] When setup is complete (CRS + SP + PBP), mesh overlay auto-loads
- [ ] Primary mesh tile renders as blue polygon with mesh code label
- [ ] 8 neighbor tiles render as gray dashed polygons
- [ ] Click on mesh tile highlights it (orange)
- [ ] When setup is incomplete, placeholder card shows on map with hint text

### Onboarding Checklist

- [ ] When no points are set and setup is incomplete, checklist card appears on map
- [ ] Shows 3 items: Select CRS, Place Survey Point, Place Project Base Point
- [ ] Items tick off as completed
- [ ] Card auto-hides once all 3 are done

### Readiness Banners

- [ ] Banners load from `readiness.getStatus` RPC on mount
- [ ] Error banners are red, blocking, non-dismissible
- [ ] Warning banners are amber, dismissible
- [ ] Info banners are blue, dismissible
- [ ] At most one blocking banner visible at a time

---

## Import (`/import`)

**Replaces:** `PlateauImportWindow.xaml`, `PlateauOnlineImportWindow.xaml`

### Source Picker

- [ ] Entry screen shows two cards: "Local PLATEAU Folder" and "PLATEAU Online"
- [ ] Selection persists to localStorage (`import.lastSource`)
- [ ] Clicking a card advances to preflight check

### Preflight Check

- [ ] `ReadinessPreflight` component loads `readiness.getStatus`
- [ ] Shows checklist of readiness items (CRS, origin, confidence, mesh)
- [ ] Blocked status shows red error and prevents progression
- [ ] Ready status shows green check and enables "Scan Folder" button
- [ ] "Back" button returns to source picker

### Local Folder Scan

- [ ] Folder picker dialog opens via `dialog.openFolder` RPC
- [ ] Selected path displays in input field
- [ ] "Scan Folder" button starts `plateau.scanFolder` job
- [ ] Scan progress shows: files scanned, tiles found, percent, current file
- [ ] Cancel button stops scan via `job.cancel`
- [ ] Completion shows tile list and advances to select state

### Tile Selection

- [ ] Map shows tile overlay (GeoJSON polygons) from scan result
- [ ] Click on tile toggles selection (orange highlight)
- [ ] Shift+drag rectangle selects all tiles inside bounds
- [ ] Side panel shows selected tiles with metadata (LOD, feature count, file size)
- [ ] "All" / "None" buttons bulk toggle selection
- [ ] Footer shows: N tiles selected, total size, estimated import time
- [ ] "Import Selected Tiles" button starts `plateau.importTiles` job

### Import Execution

- [ ] Import runs as job via `plateau.importTiles` RPC
- [ ] Progress shows: current tile, percent complete, tiles imported
- [ ] Cancel button stops import via `job.cancel`
- [ ] Completion shows success message with tile count
- [ ] Import uses Revit transaction (via `RevitContext.InvokeWithDocumentAsync`)
- [ ] Import state persists to document (via `PlateauImportStateService`)

### Online Import (Future)

- [ ] Placeholder message: "Online import coming soon. Please use local folder import for now."
- [ ] (Phase 5 does not implement online; this is a stub)

---

## Export (`/export`)

**Replaces:** `Tiles3DExportWindow.xaml`, `CityGmlExportWindow.xaml`, export-mode `PlateauImportWindow.xaml`

### Format Picker

- [ ] Entry screen shows three cards: "PLATEAU Context", "3D Tiles", "CityGML"
- [ ] Selection persists to localStorage (`export.lastFormat`)
- [ ] Clicking a card advances to preflight check

### Preflight Check

- [ ] `ReadinessPreflight` component loads `readiness.getStatus`
- [ ] Shows checklist of readiness items
- [ ] Blocked status prevents progression
- [ ] Ready status enables "Continue" button

### Shared Scope Step

- [ ] Scope picker: "Whole model" / "Active 3D view" / "Selection"
- [ ] Selection shows element count
- [ ] Scope applies to all three formats

### Format-Specific Options

- [ ] PLATEAU Context: feature type checkboxes (Building, Road, etc.)
- [ ] 3D Tiles: LOD selector, geometry mode (Lightweight/Detailed)
- [ ] CityGML: schema version, semantic mapping options

### Output Preview

- [ ] Live preview pane shows: estimated file count, total size, included feature types, CRS
- [ ] Updates as user changes scope/options
- [ ] "Export" button starts export job

### Export Execution

- [ ] Export runs as job via format-specific RPC (`plateau.exportContext`, `tiles3d.export`, `citygml.export`)
- [ ] Progress shows: current stage, percent complete, elements exported
- [ ] Cancel button stops export via `job.cancel`
- [ ] Completion shows success message with file path
- [ ] Export uses Revit transaction (via `RevitContext.InvokeWithDocumentAsync`)
- [ ] Export state persists to document

---

## Testing Checklist

### Build Verification

- [ ] `dotnet build` succeeds with web assets embedded
- [ ] `*.resources` contains `WebUI/index.html` and hashed assets
- [ ] `npm run build` produces `dist/` with index.html, CSS, JS
- [ ] `npm test` passes (Vitest for JS bridge + Svelte components)
- [ ] `dotnet test` passes (xUnit for handlers + bridge dispatch)
- [ ] `tools/VerifyContracts.ps1` passes (generator output matches committed file)

### Revit Integration

- [ ] Add-in loads in Revit 2024 without errors
- [ ] Ribbon shows 3 new buttons: Georeference Setup, Import, Export
- [ ] Each button opens the web shell to the correct route
- [ ] Rail navigation works between all 3 routes
- [ ] Window close hides (not destroys); reopen reuses same instance
- [ ] WebView2 runtime missing → shows fallback with download button

### Bridge Round-Trip

- [ ] Trigger an action (e.g., CRS search) and confirm JS request → C# response completes
- [ ] Verify error path by throwing in a handler → JS rejects with `BridgeError`
- [ ] Localization toggle EN ↔ JP updates all strings without reload
- [ ] Long-running op (PLATEAU scan) streams progress events correctly
- [ ] Cancel button stops job and cleans up

### Map + Mesh Overlay

- [ ] In Georeference, pick CRS, place SP and PBP
- [ ] Confirm primary mesh tile + neighbors render on same Leaflet map
- [ ] Mesh code is reported back to C# and shown in status footer

### Cross-Window Handoff

- [ ] Open Import, click GEO in rail
- [ ] Confirm in-place navigation to Georeference route without window teardown
- [ ] Confirm handlers swap correctly (Import handlers removed, Georeference handlers added)

---

## Known Gaps (Deferred to Phase 8)

These items are **not** required for phase completion but are tracked for future polish:

- Command palette (Ctrl+K)
- Recents (last 5 CRS, last 5 folders)
- Keyboard cheatsheet overlay (`?`)
- Toast notifications (instead of modal alerts)
- Background tasks panel (footer pill with running jobs)
- Skeleton loaders (for tile lists, CRS results)
- Responsive collapse (rail to icons at narrow widths)
- Export presets ("Same as last time")

---

## Sign-Off

Each phase requires:

1. **Code review** — all checklist items for that phase are marked `[x]`
2. **Manual testing** — tester verifies in Revit 2024 with a real project
3. **Automated tests** — `dotnet test` and `npm test` pass
4. **Contract drift check** — `tools/VerifyContracts.ps1` passes
5. **Documentation** — this DoD is updated if new requirements emerge

---

## Revision History

| Date | Phase | Changes |
|------|-------|---------|
| 2026-05-29 | Initial | Created DoD checklist for Phases 3–6 |
