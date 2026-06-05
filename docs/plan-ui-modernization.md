# UI Modernization & Ribbon Consolidation

**Status:** Approved for implementation
**Date:** May 2026
**Supersedes:** Ribbon and window design decisions in existing docs for V2 UI

---

## Summary

Full WebView2 rewrite of every dialog. Each module becomes an HTML/CSS/JS page
hosted by a thin WPF shell. Ribbon shrinks from 8 buttons across 3 panels to
3 buttons. Mesh Inspector and Check Project disappear as standalone destinations
and fold into Georeference Setup as a map overlay and inline validation banners.

---

## Design Decisions

### Web stack

**Svelte 5 + Vite + TypeScript + Tailwind.**

- Compiler-first, no runtime framework shipped
- Small bundles (~10–30 KB app code + Leaflet ~140 KB bundled locally)
- Vite emits fully static `dist/` for embedding
- No internet at runtime — Leaflet CSS/JS bundled as local npm dependency, not CDN

Rejected: Plain HTML/JS (reinvents components for 5 screens), React/Preact
(heavier runtime), Lit (Tailwind across shadow DOM is awkward).

### Single long-lived window

A single `WebShellWindow` instance persists across route navigations. The 3
ribbon buttons navigate to routes within the same window. The window is owned by
`App.cs` and reused between command invocations.

- Ribbon click → if window exists, send `evt navigate { route }`; if not, create and show
- Rail click → same SPA navigation, no window teardown
- Window close → explicit user action (X button, Esc, or `window.close` RPC)
- Revit `IExternalCommand.Execute()` shows the window non-modally on first click,
  subsequent clicks bring the existing window to front

This replaces the original per-command `ShowDialog()` pattern. Tradeoff: the
window is not modal, so the user can interact with Revit while it's open. This
is acceptable because the apply step already uses its own Revit transaction.

### Mesh auto-preview

When georeference setup is already applied (CRS + Survey Point + PBP all set),
the Georeference route skips the stepper and lands on a "setup complete" state
showing the mesh overlay on the map. Users who just want to check mesh codes
don't need to walk through the stepper. A "Review setup" link returns to the
stepper if needed.

### Contract generator CI guard

- Pin the generator tool version (TypeGen or Reinforced.Typings) in the project file
- Add `tools/VerifyContracts.ps1` that runs the generator and diffs output against
  the committed `contracts.generated.ts`
- CI fails if the diff is non-empty, catching silent drift
- The generator runs as an MSBuild target (`GenerateTsContracts`) before `BuildWebAssets`

### MSBuild Node integration

- `BuildWebAssets` target checks `node --version >= 20` and fails with a clear
  message including a download link (not just "npm not found")
- `WebBuild.cache` file tracks input hashes; incremental builds skip `npm run build`
  when inputs haven't changed (`npm ci` is the slow step)
- `SkipWebBuild=true` escape hatch for partial rebuilds

---

## New Project Structure

```
src/RevitGeoSuite.SharedUI.Web/          (new, Node-only, NOT in .sln)
  package.json
  vite.config.ts
  tailwind.config.ts
  tsconfig.json
  index.html
  src/
    main.ts
    App.svelte
    lib/
      bridge/
        rpc.ts
        contracts.generated.ts           (emitted by TypeGen MSBuild target)
      i18n/
        index.ts                          (consumes UiLocalizer via RPC)
        store.ts                          (Svelte store caching dictionary)
      ui/
        Rail.svelte
        Header.svelte
        CrsPicker.svelte
        LeafletMap.svelte
        MeshOverlay.svelte
        ReadinessBanner.svelte
        ReadinessPreflight.svelte
        Stepper.svelte
        StatusFooter.svelte
        CommandPalette.svelte
        ToastHost.svelte
        BackgroundTasksPanel.svelte
    routes/
      georeference/
        GeoreferenceRoute.svelte
      import/
        ImportRoute.svelte
      export/
        ExportRoute.svelte

src/RevitGeoSuite.SharedUI.Web.Contracts/ (new, .NET classlib, IN .sln)
  Dtos marked [TsExport]
  Method contracts (request/response pairs per RPC method)
```

Vite outputs into `src/RevitGeoSuite.SharedUI/Resources/Web/dist/**`.

---

## C# ↔ JS Interop — Typed RPC Bridge

### Wire envelope

```json
{ "kind": "req"|"res"|"evt", "id": "string", "method": "domain.action", "payload": {}, "error": null }
```

### Commands (JS → C#)

`bridge.request<T>(method, payload)` returns `Promise<T>`. C# handlers implement
`IRpcHandler` and are registered per-route.

### Events (C# → JS)

`kind: "evt"`, no id. JS uses `bridge.on(method, cb)`.

### Progress

Long-running ops (PLATEAU scan, export prepare) return `{ jobId }` immediately.
Subsequent `evt <domain>.progress` carries `{ jobId, percent, message }`.
`cancel.<jobId>` cancels via `CancellationToken`. Mirrors existing
`PlateauScanProgress.cs` shape.

### Errors

C# exceptions → `res.error { code, message }` → JS rejects with `BridgeError`.

### Type sync

Source generation via TypeGen (pinned version). A new
`RevitGeoSuite.SharedUI.Web.Contracts` classlib holds DTOs marked `[TsExport]`.
MSBuild target `GenerateTsContracts` emits `contracts.generated.ts` before
`BuildWebAssets`. The generator produces a typed union of method names → req/res
pairs so `bridge.request("plateau.scanFolder", ...)` is fully typed.

---

## WebShellWindow

Located at `src/RevitGeoSuite.SharedUI/Shell/WebShellWindow.xaml(.cs)`.

```csharp
public sealed class WebShellWindow : Window
{
    public WebShellWindow(WebShellOptions options);
    public void NavigateTo(string route);
    public bool IsRouteActive { get; }
}

public sealed class WebShellOptions
{
    public string InitialRoute { get; init; }
    public string TitleKey { get; init; }
    public IntPtr OwnerHandle { get; init; }
    public IReadOnlyList<IRpcHandler> Handlers { get; init; }
    public IRevitDocumentHandle Doc { get; init; }
}
```

### Responsibilities

1. Init `CoreWebView2Environment` via `WebShellEnvironment` (generalizes
   `MapHostEnvironment.cs` — same `LocalAppData\RevitGeoSuite\WebView2`
   userDataFolder, same virtual-host-to-folder mapping using
   `ui.revitgeosuite.local`)
2. Extract embedded `dist/**` to disk on first launch per hash; skip on hash match
3. Navigate to `https://ui.revitgeosuite.local/index.html#<Route>`
4. Wire `WebRpcBridge`, register per-route handlers plus always-on handlers:
   - `localization.getAll`, `localization.setLanguage`
   - `window.close`, `window.navigate`
   - `dialog.openFolder`
   - `recents.get`, `recents.set`
5. Set Revit owner via `WindowInteropHelper`
6. Use WPF `WindowChrome` for borderless window with custom title bar drawn in HTML
7. Window is owned by `Shell/App.cs`, reused across command invocations

### Window lifecycle

```
Ribbon click (first time):
  App.cs → new WebShellWindow(options) → window.Show() → window.NavigateTo(route)

Ribbon click (window exists):
  App.cs → window.NavigateTo(route) → window.Activate()

Rail click (same window):
  JS bridge.request("window.navigate", { route }) →
    C# swaps handlers → sends evt "navigate" → Svelte SPA navigation

User closes window:
  JS bridge.request("window.close") → C# window.Hide() (keep alive for reuse)
  OR user clicks X → window.Hide()

Revit shutdown:
  App.cs OnShutdown → window.Close() → dispose WebView2
```

---

## Ribbon Consolidation

`src/RevitGeoSuite.Shell/ModuleRegistry.cs` shrinks from 7 module entries to 3
ribbon commands. `IRevitGeoModule` stays for handler grouping but no longer
drives the ribbon.

| Ribbon button        | Command                  | Route         | Handlers registered                                                     |
|----------------------|--------------------------|---------------|-------------------------------------------------------------------------|
| Georeference Setup   | GeoreferenceSetupCommand | /georeference | Georeference + Mesh (map overlay) + Readiness (banners)                 |
| Import               | ImportCommand            | /import       | PlateauImport (local) + PlateauOnlineImport + Readiness preflight       |
| Export               | ExportCommand            | /export       | PlateauContextExport + Tiles3DExport + CityGmlExport + Readiness preflight |

### Command migration

The existing per-module commands (`GeoreferenceCommand`, `PlateauImportCommand`,
`PlateauOnlineImportCommand`, `PlateauContextExportCommand`,
`Tiles3DExportCommand`, `CityGmlExportCommand`) stay as internal entry points
but no longer register on the ribbon. Their service composition moves into the
per-route handler constructors.

### Deletions

- `MeshInspectorCommand` — deleted
- `ValidationCommand` — deleted
- `MeshInspectorWindow.xaml(.cs)` — deleted
- `ValidationWindow.xaml(.cs)` — deleted

The underlying services (mesh calculator, neighbor resolution, readiness rules)
survive in their module assemblies and are consumed by the Georeference handlers
and the preflight handler. No logic is lost, only the dialog shells.

---

## Left Nav Rail

3 items, identical across all routes:

| Key  | Label         | Icon |
|------|---------------|------|
| GEO  | Georeference  | Crosshair |
| IMP  | Import        | Folder |
| EXP  | Export        | Cube |

Clicking a rail item calls `bridge.request("window.navigate", { route })`.
C# swaps RPC handlers and sends `evt navigate { route }`. Svelte does in-place
SPA navigation. No window teardown.

MSH / CHK / PLT / 3DT / GML disappear from the rail. Mesh is a map overlay
inside Georeference. Readiness is a banner. PLATEAU / 3D Tiles / CityGML become
sub-states of the Import or Export source/format picker.

---

## Localization

Do not duplicate `UiLocalizer.cs` in JS. Two RPC methods:

- `localization.getAll(lang)` → full dictionary
- `localization.setLanguage(lang)` → triggers `evt localization.changed { lang, strings }`

A Svelte store caches the dictionary. EN/JP remain owned by `UiLocalizer.cs`.
Auto-detect Revit UI language for the initial EN/JP setting; user override sticks.

---

## Migration Phases

Each phase leaves the suite shippable.

### Phase 1 — Scaffolding

- New `RevitGeoSuite.SharedUI.Web` project (Node, not in .sln)
- New `RevitGeoSuite.SharedUI.Web.Contracts` classlib (in .sln)
- MSBuild targets: `BuildWebAssets`, `GenerateTsContracts`
- Node version check (>= 20) with clear error message + download link
- `WebBuild.cache` for incremental builds
- `WebShellEnvironment` (generalizes `MapHostEnvironment.cs`)
- Empty `WebShellWindow` rendering a "Hello" Svelte app
- Leaflet bundled as local npm dependency (no CDN)
- No ribbon changes

**Acceptance:** `dotnet build` succeeds, web assets embedded, WebShellWindow
opens in Revit showing a Svelte "Hello" page.

### Phase 2 — RPC Bridge + Contracts Generator

- `WebRpcBridge` with typed envelope dispatch
- TypeGen MSBuild target emitting `contracts.generated.ts`
- `tools/VerifyContracts.ps1` for CI drift detection
- Echo handler for round-trip testing
- Vitest tests for JS bridge
- xUnit tests for C# bridge dispatch + handler registration

**Acceptance:** JS can call `bridge.request("echo", ...)` and get a typed
response. Error path tested. CI catches contract drift.

### Phase 3 — Georeference (Skeleton + Map + Shell Chrome)

- First real port; biggest screen
- `LeafletMap.svelte` (porting `MapHost.html` logic, using bundled Leaflet)
- Svelte shell: `Header.svelte`, `Rail.svelte`, `StatusFooter.svelte`
- Custom dark title bar via `WindowChrome` extended client area
- Dark mode + light mode with system-default detection
- CRS picker + Survey/Project Base Point wiring
- Localization RPC + Svelte i18n store
- Embedded dist extraction with hash check
- Shipped behind a hidden ribbon button alongside the old Georeference button

**Acceptance:** Real Revit install opens the new window, map works, CRS search
round-trips through RPC, EN↔JP toggle updates all strings, custom chrome renders
correctly.

### Phase 4 — Georeference (Mesh Overlay + Readiness + Stepper)

- Stepper / progressive disclosure:
  1. Confirm or correct existing setup
  2. Pick CRS
  3. Place Survey Point
  4. Place Project Base Point
  5. Preview
  6. Apply
- Map-first layout: large Leaflet map (60–70%), contextual right panel per step
- Mesh overlay on the same Leaflet map (consume `MeshInspectorModule` services)
- Mesh auto-preview: when setup is already applied, skip stepper, show mesh on map
- Readiness rules as inline banners (consume `ValidationModule` services)
- Banners dismissible, ranked, at most one blocking banner visible at a time
- Visual diff before apply: SP↔PBP diagram showing current vs proposed
- First-launch onboarding checklist card on empty map
- Register `GeoreferenceSetupCommand` as the real ribbon button
- Delete `GeoreferenceWindow.xaml`, `MeshInspectorWindow.xaml`,
  `ValidationWindow.xaml`, `MeshInspectorCommand`, `ValidationCommand`
- Ribbon shrinks: Mesh Inspector and Check Project disappear

**Acceptance:** Full georeference workflow works end-to-end in the new UI.
Mesh tiles render on map after setup. Readiness banners appear inline.
Old windows and commands deleted.

### Phase 5 — Import

- Source picker on entry: Local PLATEAU folder / PLATEAU Online
- Source picker remembers last source, lands there by default
- Map-first tile selection: drag rectangle to bulk-select, click to toggle
- Tile metadata badges: LOD, file size, feature count
- Footer total: "N tiles selected · ~X MB · est. Y min"
- Readiness preflight panel (shared `ReadinessPreflight.svelte` component)
- Replace `PlateauImportWindow` + `PlateauOnlineImportWindow`
- Register `ImportCommand`, drop the two old PLATEAU ribbon buttons

**Acceptance:** Both local and online import workflows function. Tile selection
on map works. Preflight blocks incomplete setups. Old windows deleted.

### Phase 6 — Export

- Format picker on entry: PLATEAU Context / 3D Tiles / CityGML
- Shared scope step: whole model / active 3D view / selection
- Format-specific options below scope
- Output preview pane: estimated file count, total size, features, CRS
- Readiness preflight panel (same shared component)
- Replace `Tiles3DExportWindow` + `CityGmlExportWindow` + export-mode
  `PlateauImportWindow`
- Register `ExportCommand`, drop the three old export ribbon buttons

**Acceptance:** All three export formats work. Scope selection applies across
formats. Preflight blocks incomplete setups. Old windows deleted.

### Phase 7 — Cleanup

- Delete `ModuleNavRailControl.xaml(.cs)`
- Delete `MapControl.xaml(.cs)`
- Delete `MapHostEnvironment.cs`
- Delete `MapHost.html`
- Delete remaining per-module commands' ribbon registrations
- Remove `Microsoft.Web.WebView2.Wpf` XAML usage if none remain
- Update `ModuleWindowNavigator.cs` (or delete if no longer needed)
- Update `docs/04-technical-architecture.md` and `docs/DECISIONS.md`

**Acceptance:** No dead WPF code remains. `dotnet build` clean. All 8 test
projects still pass.

### Phase 8 — Polish (Deferred)

These items are additive and have no architectural dependency on the rewrite.
They extend the Svelte UI without changing the C# bridge or shell.

| Item | Description |
|------|-------------|
| Command palette | Ctrl+K: jump to actions, search EPSG, switch routes, open recent folders |
| Recents | Last 5 CRS in picker, last 5 PLATEAU folders, stored in `recents.json` |
| Keyboard cheatsheet | `?` overlay, Esc closes, Enter applies, Tab focuses next input |
| Toast notifications | Completion/error toasts instead of modal alerts |
| Background tasks panel | Footer pill showing running scans/exports with progress + cancel |
| Skeleton loaders | For tile lists, CRS results, scan progress |
| Responsive collapse | Rail collapses to icons at narrow widths, panels stack below map |
| Export presets | "Same as last time" one-click for repeat workflows |

---

## UX Improvements (Included in Core Phases)

These ship with their respective phases, not deferred:

### Phase 3 (shell-level)

- Custom dark title bar: RGS logo + active doc name left, language toggle +
  min/max/close right
- Dark mode + light mode with system-default detection
- Persistent status footer: active Revit doc · current CRS (EPSG) · primary mesh
  code · "ready / blocked" pill

### Phase 4 (georeference flow)

- Stepper / progressive disclosure (6 steps, each gates the next)
- Map-first layout (60–70% map, contextual side panel)
- Mesh auto-preview when setup is already applied
- First-launch onboarding checklist card
- Visual diff before apply (SP↔PBP diagram)

### Phase 5 (import flow)

- Map-first tile selection with rectangle drag
- Tile metadata badges and footer totals
- Source picker memory

### Phase 6 (export flow)

- Shared scope step across formats
- Output preview pane with live estimates

---

## Risks

| Risk | Mitigation |
|------|------------|
| WebView2 runtime availability | Evergreen ships on Win10 21H2+ / Win11. Bundle Evergreen Bootstrapper in installer. Detect via `CoreWebView2Environment.GetAvailableBrowserVersionString()`. Reuse MapControl fallback overlay pattern. |
| Shared userDataFolder lock | Single window eliminates multi-instance lock conflicts. Audit per-module singletons (`PlateauScanSessionCache`, `*StateService`) for single-window assumptions. |
| Non-modal window + Revit transactions | Apply step uses its own Revit transaction. If the user modifies the model while the window is open, the apply step re-reads state before committing. |
| Embedded resources at scale | Hash the manifest and extract only on hash change. .NET Framework 4.8 resource lookup is case-sensitive; use `LogicalName` glob to normalize. |
| Build environment | Requires Node 20+ on dev machines and CI. MSBuild target fails clearly with download link if npm is missing. `SkipWebBuild=true` for partial rebuilds. `WebBuild.cache` skips rebuild on unchanged inputs. |
| Mesh overlay discoverability | When setup is not yet applied, show a faint placeholder rectangle and a one-line hint on the map. When setup is applied, auto-show the mesh overlay. |
| Readiness banner noise | Dismissible, ranked. At most one blocking banner visible. Lower-severity warnings in a collapsed "N issues" pill. |
| Contract generator drift | Pin generator version. `tools/VerifyContracts.ps1` diffs generated output in CI. Non-empty diff fails the build. |
| Single window lifecycle | Window owned by `App.cs`. Hidden on close, not destroyed. Disposed on Revit shutdown. If WebView2 crashes, recreate the window on next ribbon click. |

---

## Verification

After each phase:

1. **Build:** `dotnet build` succeeds with web assets embedded; verify
   `*.resources` contains `WebUI/index.html` and hashed assets
2. **Load in Revit:** install via existing deploy path; open each ribbon button;
   confirm window opens with working WebView2 and the correct route
3. **Bridge round-trip:** trigger an action (e.g. CRS search) and confirm JS
   request and C# response complete; verify error path by throwing in a handler
4. **Map + mesh overlay:** in Georeference, pick CRS, place Survey Point and PBP,
   confirm primary mesh tile + neighbors render on the same Leaflet map
5. **Long-running op:** kick off a PLATEAU folder scan; confirm progress events
   stream and cancel works
6. **Localization:** toggle EN ↔ JP; confirm all visible strings update without
   reload
7. **SPA navigation:** open Import, click GEO in the rail; confirm in-place
   navigation to Georeference route without window teardown
8. **Tests:** `dotnet test` (xUnit for handlers + bridge dispatch) +
   `npm test` (Vitest for JS bridge + Svelte components)

---

## Critical Files

### Reuse patterns

- `src/RevitGeoSuite.SharedUI/Controls/MapHostEnvironment.cs` → generalize into `WebShellEnvironment`
- `src/RevitGeoSuite.SharedUI/Controls/MapControl.xaml.cs` → WebView2 init, message bridge, fallback overlay reference
- `src/RevitGeoSuite.SharedUI/Resources/MapHost.html` → Leaflet port reference

### Ribbon refactor

- `src/RevitGeoSuite.Shell/ModuleRegistry.cs` → shrink to 3
- `src/RevitGeoSuite.Shell/RibbonBuilder.cs` → likely no change
- New: `src/RevitGeoSuite.Shell/Commands/GeoreferenceSetupCommand.cs`
- New: `src/RevitGeoSuite.Shell/Commands/ImportCommand.cs`
- New: `src/RevitGeoSuite.Shell/Commands/ExportCommand.cs`

### Localization source of truth

- `src/RevitGeoSuite.SharedUI/Localization/UiLocalizer.cs` → surfaced to JS via RPC

### Windows replaced (rewritten into Svelte routes)

- `Georeference/GeoreferenceWindow.xaml(.cs)`
- `PlateauImport/PlateauImportWindow.xaml(.cs)`
- `PlateauImport/Online/PlateauOnlineImportWindow.xaml(.cs)`
- `Tiles3DExport/Tiles3DExportWindow.xaml(.cs)`
- `CityGmlExport/CityGmlExportWindow.xaml(.cs)`

### Windows deleted (folded inline into Georeference)

- `MeshInspector/MeshInspectorWindow.xaml(.cs)`
- `MeshInspector/MeshInspectorCommand.cs`
- `Validation/ValidationWindow.xaml(.cs)`
- `Validation/ValidationCommand.cs`

Service classes in those module assemblies survive.

### Controls deleted in Phase 7

- `SharedUI/Controls/ModuleNavRailControl.xaml(.cs)`
- `SharedUI/Controls/MapControl.xaml(.cs)`
- `SharedUI/Controls/MapHostEnvironment.cs`
- `SharedUI/Resources/MapHost.html`
