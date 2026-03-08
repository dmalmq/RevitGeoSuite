# Technical Architecture

## Overview

The suite separates generic geo logic, Revit API interactions, shared UI, and module-specific workflows so that most business logic can be tested independently of Revit and each feature module can be developed without direct dependency on the others.

## Architectural Goals

- Keep coordinate conversion logic isolated and testable
- Keep Revit API calls in a dedicated interop layer
- Keep shared UI separate from Revit interop
- Keep module workflows independent from one another
- Make it possible to expand from georeferencing into mesh, PLATEAU, validation, and export workflows later
- Allow logging, preview, validation, and metadata reuse to be added consistently
- Make the first implementation small enough for Codex to execute safely

## Target Architecture vs Initial Implementation

### Target Architecture

Do not build a chain of dependent modules. Build a shared foundation with independent modules on top.

Modules should:

- depend on Core and RevitInterop
- optionally depend on Core.Plateau if needed
- never depend directly on one another
- communicate through shared project metadata and shared services

### Initial Codex-First Implementation

The shell should preserve the future module boundary but keep runtime composition simple:

- define `IRevitGeoModule` from the start
- statically register the Georeference module in the shell for V1
- defer assembly scanning and dynamic module discovery until the georeference workflow is stable
- keep later modules behind the same contracts so the shell can evolve without redesigning Core

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
- preview and intent contracts that do not depend on Revit

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
- project location reads and writes in Revit
- storage migration helpers
- wrapping direct Revit API behavior behind testable interfaces

This is the only layer that should encapsulate direct Revit API behavior.

### 4. SharedUI

Responsible for:

- reusable WPF controls
- styles/themes
- display converters
- WebView2 host for the shared map control

This layer should remain presentational and should not absorb business workflow logic.

### 5. Shell

Responsible for:

- add-in entry point
- ribbon setup
- manual composition root
- static V1 registration of the Georeference module
- later module discovery and compatibility checks when that infrastructure is introduced

### 6. Modules

Responsible for:

- user-facing workflows
- windows and view models
- module-specific services
- module-specific state
- calling shared services and Revit interop safely

## Shared Contracts

### GeoProjectInfo

The central shared state object should remain small and stable. It should contain only cross-module canonical geo facts such as:

- schema version
- CRS reference
- project origin
- true north angle
- primary mesh code
- confidence level
- setup source
- setup date

It should not contain module-specific state such as import histories, export settings, mapping overrides, preview objects, or validation snapshots.

### PlacementIntent

`PlacementIntent` is a workflow contract owned by the georeference flow. It represents what the user is asking the system to do before any document mutation happens.

It should contain only the planned input to the operation, such as:

- selected CRS reference
- selected map point
- optional elevation input
- optional true north angle input
- confidence and provenance
- selected apply mode (`metadata only`, `project location`, `project location + angle`)

### PlacementPreview

`PlacementPreview` is a read-only model rendered by the wizard before commit.

It should include:

- current known values
- proposed values
- warnings and suspicious conditions
- persistence summary
- explicit statement of what the apply step will and will not change

`PlacementPreview` must never be persisted as shared project metadata.

## Module State Pattern

Module-specific state should be stored separately through a consistent module state storage pattern. Examples:

- `PlateauImportState`
- `Tiles3DExportState`
- `CityGmlExportState`

## Suggested Technical Decisions

- Language: C#
- UI: WPF
- Architecture: MVVM-style separation inside modules
- Tests: xUnit
- Serialization: structured storage and JSON where appropriate
- Logging: structured text or JSON-based logs

## Dependency Considerations

Potential external dependencies may include:

- a coordinate transformation library or EPSG definition source
- map hosting support, likely WebView2 or a related browser-based approach
- JSON serialization library

## Service Boundary Rules

- `IRevitGeoPlacementService` is an execution boundary, not a workflow owner
- preview generation should stay above RevitInterop where possible
- RevitInterop should expose explicit read/apply operations rather than hidden UI-driven side effects
- Core and module tests should validate preview math without requiring Revit runtime

## Design Rules

- No direct UI-to-Revit API shortcuts
- No coordinate changes without preview data
- Any transformation applied to the model must be represented as a reviewable object first
- SharedUI stays presentational
- Modules never depend on each other directly
- Core remains generic and not PLATEAU-specific
- Anything uncertain should be logged or surfaced to the user instead of silently assumed
- Dynamic plugin infrastructure is deferred until it creates value beyond static registration
