using System.Collections.Generic;
using System.IO;
using RevitGeoSuite.Core.Plateau.Tiles3D;
using RevitGeoSuite.PlateauImport.Online;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests.Online;

public sealed class PlateauFootprintDxfWriterTests
{
    [Fact]
    public void Write_emits_one_closed_R12_polyline_per_feature_and_a_marker()
    {
        // Two square buildings, axis-aligned in project metres.
        PlateauTilesetFeature first = BuildSquareFeature("bldg-a", 0, 0, 10);
        PlateauTilesetFeature second = BuildSquareFeature("bldg-b", 50, 0, 10);
        PlateauTilesetModel model = new PlateauTilesetModel(
            "https://example.test/tileset.json",
            "bldg",
            "2",
            texture: false,
            areaCode: "13101",
            features: new[] { first, second });

        Vector3d marker = new Vector3d(0, 0, 0);
        StringWriter writer = new StringWriter();
        PlateauFootprintDxfWriter.WriteResult result = PlateauFootprintDxfWriter.Write(writer, model, marker, Vector3d.Zero);

        string dxf = writer.ToString();

        Assert.Equal(2, result.FeatureCount);
        Assert.Equal(2, result.PolylineCount);
        Assert.Empty(result.Warnings);

        // R12 file claims AC1009, never AC1015. LWPOLYLINE was added in R14 so it must
        // be absent.
        Assert.Contains("\nAC1009\n", dxf);
        Assert.DoesNotContain("AC1015", dxf);
        Assert.DoesNotContain("LWPOLYLINE", dxf);

        // Two POLYLINE entities, each with the closed flag and a SEQEND terminator.
        Assert.Equal(2, CountOccurrences(dxf, "\nPOLYLINE\n"));
        Assert.Equal(2, CountOccurrences(dxf, "\nSEQEND\n"));
        // Each square produces 4 VERTEX entities (convex hull of a square = 4 points).
        Assert.Equal(8, CountOccurrences(dxf, "\nVERTEX\n"));
        // Two "70\n1" closed-polyline flags, one per POLYLINE.
        Assert.Equal(2, CountOccurrences(dxf, "\n70\n1\n"));

        // Marker: standard survey-control symbol = one CIRCLE + two LINE entities.
        // No legacy POINT / TEXT label entities anymore.
        Assert.Contains("\nCIRCLE\n", dxf);
        Assert.Equal(2, CountOccurrences(dxf, "\nLINE\n"));
        Assert.DoesNotContain("Project Base Point", dxf);
        Assert.DoesNotContain("\nPOINT\n", dxf);
        Assert.DoesNotContain("\nTEXT\n", dxf);

        // Three SECTION/ENDSEC pairs: HEADER, TABLES, ENTITIES.
        Assert.Equal(3, CountOccurrences(dxf, "\nSECTION\n"));
        Assert.Equal(3, CountOccurrences(dxf, "\nENDSEC\n"));
        Assert.EndsWith("EOF\n", dxf);
    }

    [Fact]
    public void Write_declares_required_layers_in_LAYER_table()
    {
        PlateauTilesetFeature feature = BuildSquareFeature("bldg-a", 0, 0, 10);
        PlateauTilesetModel model = new PlateauTilesetModel(
            "https://example.test/tileset.json", "bldg", "2", texture: false, areaCode: "13101",
            features: new[] { feature });

        StringWriter writer = new StringWriter();
        PlateauFootprintDxfWriter.Write(writer, model, new Vector3d(0, 0, 0), Vector3d.Zero);
        string dxf = writer.ToString();

        // The LAYER table opens immediately after "TABLES" / "TABLE" / "LAYER" markers.
        int tablesIdx = dxf.IndexOf("\nTABLES\n", System.StringComparison.Ordinal);
        int entitiesIdx = dxf.IndexOf("\nENTITIES\n", System.StringComparison.Ordinal);
        Assert.True(tablesIdx >= 0);
        Assert.True(entitiesIdx > tablesIdx);
        string tablesSection = dxf.Substring(tablesIdx, entitiesIdx - tablesIdx);

        // Each layer is declared with `0\nLAYER\n2\n<name>\n` in the LAYER table.
        Assert.Contains("\nLAYER\n2\n0\n", tablesSection);
        Assert.Contains("\nLAYER\n2\nPLATEAU_BUILDINGS\n", tablesSection);
        Assert.Contains("\nLAYER\n2\nPROJECT_BASE_POINT\n", tablesSection);
        Assert.Contains("\nENDTAB\n", tablesSection);
    }

    [Fact]
    public void Write_skips_features_with_no_triangles_and_reports_warning()
    {
        PlateauTilesetFeature empty = new PlateauTilesetFeature(
            "bldg-empty",
            new Dictionary<string, object?>(),
            new List<PlateauTilesetTriangle>());
        PlateauTilesetModel model = new PlateauTilesetModel(
            "https://example.test/tileset.json", "bldg", "2", texture: false, areaCode: "13101",
            features: new[] { empty });

        StringWriter writer = new StringWriter();
        PlateauFootprintDxfWriter.WriteResult result = PlateauFootprintDxfWriter.Write(writer, model, new Vector3d(0, 0, 0), Vector3d.Zero);

        Assert.Equal(0, result.PolylineCount);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void Write_places_marker_at_supplied_coordinates()
    {
        PlateauTilesetFeature feature = BuildSquareFeature("bldg-a", 0, 0, 10);
        PlateauTilesetModel model = new PlateauTilesetModel(
            "https://example.test/tileset.json", "bldg", "2", texture: false, areaCode: "13101",
            features: new[] { feature });

        Vector3d marker = new Vector3d(123.5, 456.25, 0);
        StringWriter writer = new StringWriter();
        PlateauFootprintDxfWriter.Write(writer, model, marker, Vector3d.Zero);

        string dxf = writer.ToString();
        // The marker is now a CIRCLE entity centred on the supplied coordinates,
        // followed by layer + X(10)/Y(20)/Z(30)/radius(40) group codes.
        int circleIdx = dxf.IndexOf("\nCIRCLE\n", System.StringComparison.Ordinal);
        Assert.True(circleIdx >= 0);
        string afterCircle = dxf.Substring(circleIdx);
        Assert.Contains("\n10\n123.5\n", afterCircle);
        Assert.Contains("\n20\n456.25\n", afterCircle);
    }

    [Fact]
    public void Write_shifts_vertices_and_marker_by_origin_offset()
    {
        // Square at (100..110, 100..110), marker at (105, 105). With an origin offset
        // of (50, 50, 0), the writer must emit vertices at (50..60, 50..60) and the
        // marker at (55, 55).
        PlateauTilesetFeature feature = BuildSquareFeature("bldg-a", 100, 100, 10);
        PlateauTilesetModel model = new PlateauTilesetModel(
            "https://example.test/tileset.json", "bldg", "2", texture: false, areaCode: "13101",
            features: new[] { feature });

        Vector3d marker = new Vector3d(105, 105, 0);
        Vector3d originOffset = new Vector3d(50, 50, 0);
        StringWriter writer = new StringWriter();
        PlateauFootprintDxfWriter.Write(writer, model, marker, originOffset);

        string dxf = writer.ToString();

        // Polyline corners after shift: 50 and 60 on both axes.
        Assert.Contains("\n10\n50.0\n", dxf);
        Assert.Contains("\n20\n50.0\n", dxf);
        Assert.Contains("\n10\n60.0\n", dxf);
        Assert.Contains("\n20\n60.0\n", dxf);
        // None of the un-shifted coordinates should leak through.
        Assert.DoesNotContain("\n10\n100.0\n", dxf);
        Assert.DoesNotContain("\n10\n110.0\n", dxf);

        // Marker (now a CIRCLE) after shift: centred at (55, 55).
        int circleIdx = dxf.IndexOf("\nCIRCLE\n", System.StringComparison.Ordinal);
        Assert.True(circleIdx >= 0);
        string afterCircle = dxf.Substring(circleIdx);
        Assert.Contains("\n10\n55.0\n", afterCircle);
        Assert.Contains("\n20\n55.0\n", afterCircle);
    }

    private static PlateauTilesetFeature BuildSquareFeature(string id, double originX, double originY, double size)
    {
        List<PlateauTilesetTriangle> triangles = new List<PlateauTilesetTriangle>
        {
            new PlateauTilesetTriangle(
                new Vector3d(originX, originY, 0),
                new Vector3d(originX + size, originY, 0),
                new Vector3d(originX + size, originY + size, 0)),
            new PlateauTilesetTriangle(
                new Vector3d(originX, originY, 0),
                new Vector3d(originX + size, originY + size, 0),
                new Vector3d(originX, originY + size, 0)),
        };
        return new PlateauTilesetFeature(id, new Dictionary<string, object?>(), triangles);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = haystack.IndexOf(needle, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
