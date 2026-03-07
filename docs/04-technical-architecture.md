# Technical Architecture

## Overview

The suite should separate generic geo logic, Revit API interactions, shared UI, and module-specific workflows so that most business logic can be tested independently of Revit and each feature module can be developed without direct dependency on the others.

## Architectural Goals

- Keep coordinate conversion logic isolated and testable
- Keep Revit API calls in a dedicated interop layer
- Keep shared UI separate from Revit interop
- Keep module workflows independent from one another
- Make it possible to expand from georeferencing into mesh, PLATEAU, validation, and export workflows later
- Allow logging, preview, validation, and metadata reuse to be added consistently

## Key Architecture Decision

Do not build a chain of dependent modules. Build a shared foundation with independent modules on top.

Modules should:

- depend on Core and RevitInterop
- optionally depend on Core.Plateau if needed
- never depend directly on one another
- communicate through shared project metadata and shared services

## Proposed Solution Structure

```text
/src
  /RevitGeoSuite.Core
  /RevitGeoSuite.Core.Plateau
  /RevitGeoSuite.RevitInterop
  /RevitGeoSuite.SharedUI
  /RevitGeoSuite.Shell
  /RevitGeoSuite.Georeference
  /RevitGeoSuite.MeshInspector
  /RevitGeoSuite.Validation
  /RevitGeoSuite.PlateauImport
  /RevitGeoSuite.Tiles3DExport
  /RevitGeoSuite.CityGmlExport
/tests
  /RevitGeoSuite.Core.Tests
  /RevitGeoSuite.Core.Plateau.Tests
  /RevitGeoSuite.Georeference.Tests
  /RevitGeoSuite.MeshInspector.Tests
  /RevitGeoSuite.Validation.Tests
/docs
```

## Main Layers

### 1. Core

Responsible for:

- CRS definitions and lookup
- CRS references for persistence
- coordinate transforms
- mesh logic
- shared project metadata contract
- validation primitives
- logging abstractions
- versioning abstractions

This layer must have no Revit dependency.

### 2. Core.Plateau

Responsible for:

- PLATEAU codelists
- tile indexing
- schema helpers
- PLATEAU-specific constants

This layer must have no Revit dependency and should be referenced only by PLATEAU-related modules.

### 3. RevitInterop

Responsible for:

- document access
- transactions
- Extensible Storage
- placement execution in Revit
- project location reader/writer
- element utilities

This is the only layer that should encapsulate direct Revit API behavior.

### 4. SharedUI

Responsible for:

- reusable WPF controls
- styles/themes
- display converters

This layer should remain presentational and should not absorb business workflow logic.

### 5. Shell

Responsible for:

- add-in entry point
- ribbon setup
- module discovery and registration
- version compatibility checks

### 6. Modules

Responsible for:

- user-facing workflows
- windows and view models
- module-specific services
- module-specific state
- calling shared services and Revit interop safely

## Shared Contract: GeoProjectInfo

The central shared state object should remain small and stable. It should contain only cross-module canonical geo facts such as:

- schema version
- CRS reference
- project origin
- true north angle
- primary mesh code
- confidence level
- setup source
- setup date

It should not contain module-specific state such as import histories, export settings, or mapping overrides.

## Module State Pattern

Module-specific state should be stored separately through a consistent module state storage pattern. Examples:

- `PlateauImportState`
- `Tiles3DExportState`
- `CityGmlExportState`

## Suggested Technical Decisions

- Language: C#
- UI: WPF
- Architecture: MVVM-style separation inside modules
- Tests: xUnit or NUnit
- Serialization: structured storage and JSON where appropriate
- Logging: structured text or JSON-based logs

## Dependency Considerations

Potential external dependencies may include:

- a coordinate transformation library or EPSG definition source
- map hosting support, likely WebView2 or a related browser-based approach
- JSON serialization library

## Design Rules

- No direct UI-to-Revit API shortcuts
- No coordinate changes without preview data
- Any transformation applied to the model must be represented as a reviewable object first
- SharedUI stays presentational
- Modules never depend on each other directly
- Core remains generic and not PLATEAU-specific
- Anything uncertain should be logged or surfaced to the user instead of silently assumed
