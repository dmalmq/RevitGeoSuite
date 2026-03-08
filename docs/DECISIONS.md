# Technical Decisions

**Status:** Locked for V1 development
**Date:** March 2026

This document captures all binding technical decisions for the Revit Geo Suite. Codex and all contributors should treat these as settled unless explicitly reopened.

---

## Target Platform

| Decision | Value |
|---|---|
| Revit version | **Revit 2024** |
| .NET target | **.NET Framework 4.8** |
| Project style | **SDK-style `.csproj`** |
| Language | **C# 12** (via `<LangVersion>latest</LangVersion>`) |

---

## NuGet Packages

| Package | Purpose | Used By |
|---|---|---|
| `ProjNet` (ProjNet4GeoAPI) | CRS definitions, coordinate transforms | Core |
| `Microsoft.Web.WebView2` | Embedded browser for Leaflet map | SharedUI |
| `Newtonsoft.Json` | JSON serialization (.NET 4.8 standard) | Core, RevitInterop |
| `xunit` | Unit test framework | All test projects |
| `xunit.runner.visualstudio` | VS test runner | All test projects |
| `Moq` | Mocking framework | All test projects |

### Revit API References

Use **local DLL references** from the Revit 2024 install directory, not NuGet:

```xml
<Reference Include="RevitAPI">
  <HintPath>$(RevitInstallDir)\RevitAPI.dll</HintPath>
  <Private>false</Private>
</Reference>
<Reference Include="RevitAPIUI">
  <HintPath>$(RevitInstallDir)\RevitAPIUI.dll</HintPath>
  <Private>false</Private>
</Reference>
```

Define `RevitInstallDir` in `Directory.Build.props`:

```xml
<PropertyGroup>
  <RevitInstallDir Condition="'$(RevitInstallDir)' == ''">C:\Program Files\Autodesk\Revit 2024</RevitInstallDir>
</PropertyGroup>
```

---

## Dependency Injection

**Manual composition root** in `RevitGeoSuite.Shell/App.cs`. No DI container (no Autofac, no Microsoft.Extensions.DependencyInjection).

The Shell's `OnStartup` method wires up all services:

```csharp
// Example composition root (Shell/App.cs OnStartup)
var crsRegistry = new CrsRegistry();
var transformer = new CoordinateTransformer(crsRegistry);
var meshCalculator = new JapanMeshCalculator();
// ... pass into modules via constructor injection
```

---

## Map UI

- **WebView2** embedded in a WPF `UserControl` (`MapControl.xaml`)
- **Leaflet.js** with OpenStreetMap tiles
- Communication: C# ↔ JavaScript via `WebView2.CoreWebView2.PostWebMessageAsJson` and `WebMessageReceived`
- Map HTML/JS/CSS bundled as embedded resources in SharedUI

---

## JSON Serialization

**Newtonsoft.Json** everywhere. Do not mix with `System.Text.Json`.

---

## Output and Deployment

- All module DLLs output to a single deploy folder: `bin/Deploy/`
- Use `Directory.Build.props` to set a common output path:

```xml
<PropertyGroup>
  <OutputPath>$(SolutionDir)bin\Deploy\</OutputPath>
  <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
</PropertyGroup>
```

- The Shell project produces the `.addin` manifest that Revit reads

---

## Addin Manifest

Example `RevitGeoSuite.addin`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>Revit Geo Suite</Name>
    <Assembly>RevitGeoSuite.Shell.dll</Assembly>
    <AddInId>A1B2C3D4-E5F6-7890-ABCD-EF1234567890</AddInId>
    <FullClassName>RevitGeoSuite.Shell.App</FullClassName>
    <VendorId>RevitGeoSuite</VendorId>
    <VendorDescription>Revit Geo Suite</VendorDescription>
  </AddIn>
</RevitAddIns>
```

---

## Project Reference Graph

Matches `Architecture.md` dependency graph:

```
Core                    → (none)
Core.Plateau            → Core
RevitInterop            → Core, Revit API DLLs
SharedUI                → Core
Shell                   → Core, RevitInterop, SharedUI
Georeference            → Core, RevitInterop, SharedUI
MeshInspector           → Core, RevitInterop, SharedUI
Validation              → Core, RevitInterop, SharedUI
PlateauImport           → Core, Core.Plateau, RevitInterop, SharedUI
Tiles3DExport           → Core, RevitInterop, SharedUI
CityGmlExport           → Core, Core.Plateau, RevitInterop, SharedUI
```

Test projects reference their corresponding source project plus `xunit` and `Moq`.

---

## Mesh Invalidation Strategy

When `Origin` or `ProjectCrs` changes in `GeoProjectInfo`:
- `PrimaryMeshCode` is set to `null` (invalidated)
- The Mesh Inspector module recomputes it on next use
- A warning is shown if mesh code appears stale relative to the current origin/CRS

This keeps `GeoProjectInfo` consistent without requiring modules to depend on each other.

---

## Mesh Grid Display

How mesh boundaries are rendered on the Leaflet map for the Mesh Inspector (Task 7) and PLATEAU Import (Task 9) modules.

| Decision | Value |
|---|---|
| Format | GeoJSON `FeatureCollection` with `Polygon` features (one per mesh cell) |
| Scale | Primary mesh cell + 8 neighbors = **9 cells max** |
| Rendering | `L.geoJSON()` layer on the shared Leaflet `MapControl` in SharedUI |
| C# → JS communication | Mesh boundaries computed by `JapanMeshCalculator` + `MeshNeighborResolver`, serialized as GeoJSON, sent via `PostWebMessageAsJson` |
| JS message type | `showMeshGrid` — MapControl JS receives this and adds/replaces the GeoJSON layer |
| Primary cell style | Blue fill, semi-transparent (`fillOpacity: 0.25`), solid border |
| Neighbor cell style | Grey fill, more transparent (`fillOpacity: 0.10`), dashed border |
| Labels | Mesh code displayed at each cell center via Leaflet marker or tooltip |
| Integration | Optional overlay layer on SharedUI `MapControl`, toggled by modules that need it |
| Performance | No concerns at this scale (~9 simple polygons) |

The GeoJSON payload includes the mesh code as a feature property:

```json
{
  "type": "FeatureCollection",
  "features": [
    {
      "type": "Feature",
      "properties": { "meshCode": "53394611", "isPrimary": true },
      "geometry": {
        "type": "Polygon",
        "coordinates": [[[lon_sw, lat_sw], [lon_se, lat_se], [lon_ne, lat_ne], [lon_nw, lat_nw], [lon_sw, lat_sw]]]
      }
    }
  ]
}
```

This reuses the same `PostWebMessageAsJson` pattern already established for map click events and marker placement.

---

## Coding Conventions

- MVVM pattern inside modules (ViewModel + Window)
- `INotifyPropertyChanged` via a shared base class or manual implementation
- Async operations use `Task`-based patterns where possible
- All public service interfaces defined in Core
- Revit API calls only in RevitInterop
- No `dynamic` keyword usage
- Nullable reference types enabled where supported by .NET 4.8 tooling

---

## Version Control

- `.gitignore`: standard Visual Studio + `bin/`, `obj/`, `*.user`, `*.suo`, `.vs/`, `packages/`
- Branch strategy: `main` for stable, feature branches for development
