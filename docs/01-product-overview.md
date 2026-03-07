# Revit Geo Suite

## Summary

A modular Revit add-in suite for georeferencing, mesh/grid lookup, PLATEAU context workflows, and future export pipelines such as CityGML and 3D Tiles. The suite is built around a shared geospatial core so that independent tools can work together through common project metadata without depending directly on one another.

## Problem Statement

Revit workflows are often weak when it comes to georeferencing, Japanese coordinate systems, GIS interoperability, and city-model-connected workflows. Users commonly struggle with survey point vs project base point behavior, true north vs project north, coordinate reference systems, and how to align a model accurately to map or GIS context. These issues create downstream problems for BIM, GIS, PLATEAU, digital twin, web mapping, CityGML, 3D Tiles, IMDF, and export workflows.

## Vision

The suite should make geospatial workflows in Revit visual, understandable, repeatable, and safer. Instead of one large monolithic add-in, the product should provide a shared geo foundation with multiple focused modules on top, beginning with georeferencing and expanding later into mesh/grid tools, PLATEAU integration, validation, and export tools.

## Target Users

- BIM modelers working with real-world site placement
- GIS/BIM coordinators
- Digital twin teams
- Internal teams creating models for map-based or coordinate-sensitive downstream workflows
- Teams working with Japanese CRS, PLATEAU, or MLIT-related requirements
- Less experienced Revit users who need help setting coordinates correctly

## Key Value

- Reduces coordinate setup mistakes
- Makes model placement easier to understand
- Adds visual context through a map
- Standardizes coordinate workflows across teams
- Creates reusable geo metadata that later modules can build on
- Improves downstream export consistency and geo alignment

## Success Criteria

The product is successful if:

- A user can choose a CRS and place a model in a predictable, understandable way
- Coordinate setup requires less manual guesswork than current workflows
- Placement decisions are documented and reproducible
- Shared geo project metadata can be reused by other modules
- Downstream GIS/export alignment problems are reduced
- Additional modules can be installed without breaking existing functionality

## Product Principles

- Visual before numerical where possible
- Safe by default
- Explicit terminology and clear user guidance
- No silent coordinate changes
- Every major operation should be previewed before being applied
- Prefer repeatable workflows over clever shortcuts
- One shared geo foundation, many focused tools
- Modules should never depend directly on one another
