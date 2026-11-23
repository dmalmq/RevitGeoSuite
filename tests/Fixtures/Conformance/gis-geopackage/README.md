# Sample GIS export as a real GeoPackage

`tokyo-station.gpkg` is the GIS export of the link contract's sample station, written by the real writer (`GpkgWriter`) with the five layers of `LayerDefinition`: `unit`, `detail`, `opening`, `fixture` and `level`. It holds one host unit and one unit of a linked model, so both forms of `revit_key` are in it.

The file is not committed yet. To write it, run the test on a PC with the .NET SDK:

    $env:REVITGEOSUITE_WRITE_FIXTURES = "1"
    dotnet test tests/RevitGeoSuite.FloorPlanExport.Core.Tests --filter GeoPackageConformanceTests

Commit the file and copy it to Kanizu, so that Kanizu's tests read a GeoPackage the real writer made.

`GeoPackageConformanceTests` writes the same sample on every run and reads it back through SQLite. Once the file is here, the test holds it to the same checks. The writer stamps the clock into `gpkg_contents.last_change`, so the file is checked by content, not byte for byte.

The station fixture's `gis/tokyo-station.gpkg` is a different file: a line of text that only gives the sample manifest an artifact to name. Its bytes are part of that manifest's `contentHash` and of the version folder's name, so it cannot be a file whose bytes change on every write.
