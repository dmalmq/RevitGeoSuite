using System;
using System.Collections.Generic;
using System.IO;
using RevitGeoSuite.Core.Plateau.Tiles3D;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public sealed class PlateauContextOutlinesDxfWriterTests
{
    [Fact]
    public void Write_emits_one_closed_R12_polyline_per_outline_feature_and_a_marker()
    {
        IReadOnlyCollection<PlateauContextOutlinesDxfWriter.OutlineFeature> features = new[]
        {
            BuildSquare("PLATEAU_BUILDINGS", 0, 0, 10),
            BuildSquare("PLATEAU_ROADS", 30, 0, 10),
        };

        StringWriter writer = new StringWriter();
        PlateauContextOutlinesDxfWriter.WriteResult result = PlateauContextOutlinesDxfWriter.Write(
            writer, features, new Vector3d(0, 0, 0), Vector3d.Zero);

        string dxf = writer.ToString();

        Assert.Equal(2, result.FeatureCount);
        Assert.Equal(2, result.PolylineCount);
        Assert.Equal(0, result.FillCount);
        Assert.Equal(0, result.SolidCount);
        Assert.Equal(0, result.HatchCount);
        Assert.Empty(result.Warnings);

        Assert.Contains("\nAC1009\n", dxf);
        Assert.DoesNotContain("AC1015", dxf);
        Assert.DoesNotContain("LWPOLYLINE", dxf);
        Assert.DoesNotContain("\n420\n", dxf);

        Assert.Equal(2, CountOccurrences(dxf, "\nPOLYLINE\n"));
        Assert.Equal(2, CountOccurrences(dxf, "\nSEQEND\n"));
        // Two closed squares × 4 vertices each = 8 VERTEX entities.
        Assert.Equal(8, CountOccurrences(dxf, "\nVERTEX\n"));
        Assert.Equal(2, CountOccurrences(dxf, "\n70\n1\n"));

        Assert.Contains("\nCIRCLE\n", dxf);
        Assert.Equal(2, CountOccurrences(dxf, "\nLINE\n"));

        Assert.Equal(3, CountOccurrences(dxf, "\nSECTION\n"));
        Assert.Equal(3, CountOccurrences(dxf, "\nENDSEC\n"));
        Assert.EndsWith("EOF\n", dxf);
    }

    [Fact]
    public void Write_emits_R12_solid_triangles_for_area_features()
    {
        IReadOnlyCollection<PlateauContextOutlinesDxfWriter.OutlineFeature> features = new[]
        {
            BuildSquare("PLATEAU_BUILDINGS", 0, 0, 10),
        };
        IReadOnlyCollection<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas = new[]
        {
            new PlateauContextOutlinesDxfWriter.AreaFeature(
                "PLATEAU_ROADS",
                new (double X, double Y)[]
                {
                    (20d, 0d),
                    (40d, 0d),
                    (40d, 10d),
                    (20d, 10d),
                },
                sourceId: "roads-dissolved-1"),
        };

        StringWriter writer = new StringWriter();
        PlateauContextOutlinesDxfWriter.WriteResult result = PlateauContextOutlinesDxfWriter.Write(
            writer,
            features,
            roadAreas,
            new Vector3d(0, 0, 0),
            Vector3d.Zero);

        string dxf = writer.ToString();

        Assert.Equal(2, result.FeatureCount);
        Assert.Equal(1, result.PolylineCount);
        Assert.Equal(1, result.FillCount);
        Assert.Equal(2, result.SolidCount);
        Assert.Equal(0, result.HatchCount);
        Assert.Empty(result.Warnings);
        Assert.Contains("\nAC1009\n", dxf);
        Assert.DoesNotContain("\nAC1015\n", dxf);
        Assert.DoesNotContain("\nHATCH\n", dxf);
        Assert.DoesNotContain("\n420\n", dxf);
        Assert.Equal(2, CountOccurrences(dxf, "\nSOLID\n"));
        Assert.Contains("\n8\nPLATEAU_ROADS\n", dxf);
        Assert.Equal(1, CountOccurrences(dxf, "\nPOLYLINE\n"));
    }

    [Fact]
    public void Write_emits_open_polylines_for_line_features()
    {
        IReadOnlyCollection<PlateauContextOutlinesDxfWriter.LineFeature> lines = new[]
        {
            new PlateauContextOutlinesDxfWriter.LineFeature(
                "PLATEAU_ROADS",
                new (double X, double Y)[] { (0d, 0d), (10d, 0d), (10d, 10d) },
                "mvt-road-line"),
        };

        StringWriter writer = new StringWriter();
        PlateauContextOutlinesDxfWriter.WriteResult result = PlateauContextOutlinesDxfWriter.Write(
            writer,
            Array.Empty<PlateauContextOutlinesDxfWriter.OutlineFeature>(),
            Array.Empty<PlateauContextOutlinesDxfWriter.AreaFeature>(),
            lines,
            new Vector3d(0, 0, 0),
            Vector3d.Zero,
            PlateauDxfRoadFillMode.R12SolidTriangles);

        string dxf = writer.ToString();

        Assert.Equal(1, result.FeatureCount);
        Assert.Equal(1, result.PolylineCount);
        Assert.Equal(0, result.FillCount);
        Assert.Empty(result.Warnings);
        Assert.Equal(1, CountOccurrences(dxf, "\nPOLYLINE\n"));
        Assert.Equal(3, CountOccurrences(dxf, "\nVERTEX\n"));
        Assert.Contains("\n66\n1\n10\n0.0\n20\n0.0\n30\n0.0\n70\n0\n", dxf);
        Assert.DoesNotContain("\n66\n1\n10\n0.0\n20\n0.0\n30\n0.0\n70\n1\n", dxf);
    }

    [Fact]
    public void Write_uses_custom_layer_aci_color_when_feature_supplies_layer_color()
    {
        DxfLayerColor openingColor = DxfLayerColor.FromRgb(255, 0, 0);
        IReadOnlyCollection<PlateauContextOutlinesDxfWriter.OutlineFeature> features = new[]
        {
            new PlateauContextOutlinesDxfWriter.OutlineFeature(
                "OPENING",
                new (double X, double Y)[] { (0d, 0d), (5d, 0d), (5d, 5d), (0d, 5d) },
                layerColor: openingColor),
        };

        StringWriter writer = new StringWriter();
        PlateauContextOutlinesDxfWriter.WriteResult result = PlateauContextOutlinesDxfWriter.Write(
            writer, features, new Vector3d(0, 0, 0), Vector3d.Zero);

        string dxf = writer.ToString();

        Assert.Equal(1, result.PolylineCount);
        Assert.Contains("\nAC1009\n", dxf);
        Assert.DoesNotContain("\nAC1015\n", dxf);
        Assert.DoesNotContain("\n420\n", dxf);
        Assert.Contains("\nLAYER\n2\nOPENING\n70\n0\n62\n1\n6\nCONTINUOUS\n", dxf);
    }

    [Theory]
    [InlineData("#FF0000", 0xFF0000, 1)]
    [InlineData("#00B050", 0x00B050, 3)]
    [InlineData("#0070C0", 0x0070C0, 5)]
    [InlineData("#A6A6A6", 0xA6A6A6, 9)]
    public void DxfLayerColor_parses_hex_and_sets_nearest_aci(string hex, int expectedTrueColor, int expectedAci)
    {
        Assert.True(DxfLayerColor.TryParseHex(hex, out DxfLayerColor? color));
        Assert.NotNull(color);
        Assert.Equal(expectedTrueColor, color!.TrueColor);
        Assert.Equal(expectedAci, color.Aci);
    }

    [Fact]
    public void Write_emits_modern_hatch_for_area_features_when_requested()
    {
        IReadOnlyCollection<PlateauContextOutlinesDxfWriter.OutlineFeature> features = new[]
        {
            BuildSquare("PLATEAU_BUILDINGS", 0, 0, 10),
        };
        IReadOnlyCollection<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas = new[]
        {
            new PlateauContextOutlinesDxfWriter.AreaFeature(
                "PLATEAU_ROADS",
                new (double X, double Y)[]
                {
                    (20d, 0d),
                    (40d, 0d),
                    (40d, 10d),
                    (20d, 10d),
                },
                sourceId: "roads-dissolved-1"),
        };

        StringWriter writer = new StringWriter();
        PlateauContextOutlinesDxfWriter.WriteResult result = PlateauContextOutlinesDxfWriter.Write(
            writer,
            features,
            roadAreas,
            new Vector3d(0, 0, 0),
            Vector3d.Zero,
            PlateauDxfRoadFillMode.ModernHatch);

        string dxf = writer.ToString();

        Assert.Equal(2, result.FeatureCount);
        Assert.Equal(1, result.PolylineCount);
        Assert.Equal(1, result.FillCount);
        Assert.Equal(0, result.SolidCount);
        Assert.Equal(1, result.HatchCount);
        Assert.Empty(result.Warnings);
        Assert.Contains("\nAC1015\n", dxf);
        Assert.DoesNotContain("\nAC1009\n", dxf);
        Assert.Contains("\nHATCH\n", dxf);
        Assert.Contains("\n100\nAcDbHatch\n", dxf);
        Assert.Contains("\n2\nSOLID\n", dxf);
        Assert.Equal(0, CountOccurrences(dxf, "\n0\nSOLID\n"));
        Assert.Equal(1, CountOccurrences(dxf, "\n0\nHATCH\n"));
        Assert.Contains("\n91\n1\n", dxf);
        Assert.Contains("\n92\n3\n", dxf);
        Assert.Contains("\n98\n1\n", dxf);
    }

    [Fact]
    public void Write_modern_hatch_includes_interior_boundary_paths()
    {
        IReadOnlyCollection<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas = new[]
        {
            new PlateauContextOutlinesDxfWriter.AreaFeature(
                "PLATEAU_ROADS",
                new (double X, double Y)[]
                {
                    (0d, 0d),
                    (10d, 0d),
                    (10d, 10d),
                    (0d, 10d),
                },
                new[]
                {
                    new (double X, double Y)[]
                    {
                        (3d, 3d),
                        (7d, 3d),
                        (7d, 7d),
                        (3d, 7d),
                    },
                },
                "road-with-block-hole"),
        };

        StringWriter writer = new StringWriter();
        PlateauContextOutlinesDxfWriter.WriteResult result = PlateauContextOutlinesDxfWriter.Write(
            writer,
            Array.Empty<PlateauContextOutlinesDxfWriter.OutlineFeature>(),
            roadAreas,
            new Vector3d(0, 0, 0),
            Vector3d.Zero,
            PlateauDxfRoadFillMode.ModernHatch);

        string dxf = writer.ToString();

        Assert.Equal(1, result.FillCount);
        Assert.Equal(0, result.SolidCount);
        Assert.Equal(1, result.HatchCount);
        Assert.Empty(result.Warnings);
        Assert.Contains("\n91\n2\n", dxf);
        Assert.Contains("\n92\n3\n", dxf);
        Assert.Contains("\n92\n2\n", dxf);
        Assert.Equal(2, CountOccurrences(dxf, "\n92\n"));
        Assert.Equal(2, CountOccurrences(dxf, "\n97\n0\n"));
    }

    [Fact]
    public void Write_triangulates_area_feature_interior_rings_as_unfilled_holes()
    {
        IReadOnlyCollection<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas = new[]
        {
            new PlateauContextOutlinesDxfWriter.AreaFeature(
                "PLATEAU_ROADS",
                new (double X, double Y)[]
                {
                    (0d, 0d),
                    (10d, 0d),
                    (10d, 10d),
                    (0d, 10d),
                },
                new[]
                {
                    new (double X, double Y)[]
                    {
                        (3d, 3d),
                        (7d, 3d),
                        (7d, 7d),
                        (3d, 7d),
                    },
                },
                "road-with-block-hole"),
        };

        StringWriter writer = new StringWriter();
        PlateauContextOutlinesDxfWriter.WriteResult result = PlateauContextOutlinesDxfWriter.Write(
            writer,
            Array.Empty<PlateauContextOutlinesDxfWriter.OutlineFeature>(),
            roadAreas,
            new Vector3d(0, 0, 0),
            Vector3d.Zero);

        string dxf = writer.ToString();

        Assert.Equal(1, result.FillCount);
        Assert.True(result.SolidCount > 2);
        Assert.Equal(0, result.HatchCount);
        Assert.Empty(result.Warnings);
        Assert.DoesNotContain("interior ring", dxf, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(84d, SumSolidTriangleAreas(dxf), 6);
    }

    [Fact]
    public void Write_declares_only_layers_referenced_by_input_features_plus_default_and_marker()
    {
        IReadOnlyCollection<PlateauContextOutlinesDxfWriter.OutlineFeature> features = new[]
        {
            BuildSquare("PLATEAU_BUILDINGS", 0, 0, 5),
            BuildSquare("PLATEAU_ROADS", 20, 0, 5),
        };

        StringWriter writer = new StringWriter();
        PlateauContextOutlinesDxfWriter.Write(writer, features, new Vector3d(0, 0, 0), Vector3d.Zero);
        string dxf = writer.ToString();

        int tablesIdx = dxf.IndexOf("\nTABLES\n", StringComparison.Ordinal);
        int entitiesIdx = dxf.IndexOf("\nENTITIES\n", StringComparison.Ordinal);
        Assert.True(tablesIdx >= 0 && entitiesIdx > tablesIdx);
        string tablesSection = dxf.Substring(tablesIdx, entitiesIdx - tablesIdx);

        Assert.Contains("\nLAYER\n2\n0\n", tablesSection);
        Assert.Contains("\nLAYER\n2\nPLATEAU_BUILDINGS\n", tablesSection);
        Assert.Contains("\nLAYER\n2\nPLATEAU_ROADS\n", tablesSection);
        Assert.Contains("\nLAYER\n2\nPROJECT_BASE_POINT\n", tablesSection);
        // Layers we did not reference must not leak into the table.
        Assert.DoesNotContain("\nLAYER\n2\nPLATEAU_BRIDGES\n", tablesSection);
        Assert.DoesNotContain("\nLAYER\n2\nPLATEAU_VEGETATION\n", tablesSection);
        Assert.DoesNotContain("\nLAYER\n2\nPLATEAU_RELIEF\n", tablesSection);
    }

    [Fact]
    public void Write_shifts_vertices_and_marker_by_origin_offset()
    {
        PlateauContextOutlinesDxfWriter.OutlineFeature feature = BuildSquare("PLATEAU_BUILDINGS", 100, 100, 10);
        Vector3d marker = new Vector3d(105, 105, 0);
        Vector3d originOffset = new Vector3d(50, 50, 0);

        StringWriter writer = new StringWriter();
        PlateauContextOutlinesDxfWriter.Write(writer, new[] { feature }, marker, originOffset);
        string dxf = writer.ToString();

        Assert.Contains("\n10\n50.0\n", dxf);
        Assert.Contains("\n20\n50.0\n", dxf);
        Assert.Contains("\n10\n60.0\n", dxf);
        Assert.Contains("\n20\n60.0\n", dxf);
        Assert.DoesNotContain("\n10\n100.0\n", dxf);
        Assert.DoesNotContain("\n10\n110.0\n", dxf);

        int circleIdx = dxf.IndexOf("\nCIRCLE\n", StringComparison.Ordinal);
        Assert.True(circleIdx >= 0);
        string afterCircle = dxf.Substring(circleIdx);
        Assert.Contains("\n10\n55.0\n", afterCircle);
        Assert.Contains("\n20\n55.0\n", afterCircle);
    }

    [Fact]
    public void Write_skips_features_with_fewer_than_three_vertices_and_warns()
    {
        PlateauContextOutlinesDxfWriter.OutlineFeature collapsed = new PlateauContextOutlinesDxfWriter.OutlineFeature(
            "PLATEAU_BUILDINGS",
            new (double X, double Y)[] { (0d, 0d), (1d, 0d) },
            sourceId: "collapsed-feature");

        StringWriter writer = new StringWriter();
        PlateauContextOutlinesDxfWriter.WriteResult result = PlateauContextOutlinesDxfWriter.Write(
            writer, new[] { collapsed }, new Vector3d(0, 0, 0), Vector3d.Zero);

        Assert.Equal(0, result.PolylineCount);
        Assert.Single(result.Warnings);
        Assert.Contains("collapsed-feature", result.Warnings[0]);
    }

    [Fact]
    public void LayerByFeatureType_includes_every_PlateauFeatureType()
    {
        foreach (PlateauFeatureType featureType in Enum.GetValues(typeof(PlateauFeatureType)))
        {
            Assert.True(
                PlateauContextOutlinesDxfWriter.LayerByFeatureType.ContainsKey(featureType),
                $"Layer mapping missing for {featureType}.");
        }
    }

    private static PlateauContextOutlinesDxfWriter.OutlineFeature BuildSquare(string layer, double originX, double originY, double size)
    {
        (double X, double Y)[] vertices = new[]
        {
            (originX, originY),
            (originX + size, originY),
            (originX + size, originY + size),
            (originX, originY + size),
        };
        return new PlateauContextOutlinesDxfWriter.OutlineFeature(layer, vertices, sourceId: "square");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    private static double SumSolidTriangleAreas(string dxf)
    {
        string[] lines = dxf.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        double total = 0d;
        for (int index = 0; index < lines.Length; index++)
        {
            if (!string.Equals(lines[index], "SOLID", StringComparison.Ordinal))
            {
                continue;
            }

            Dictionary<int, double> values = new Dictionary<int, double>();
            for (int pairIndex = index + 1; pairIndex + 1 < lines.Length; pairIndex += 2)
            {
                if (!int.TryParse(lines[pairIndex], out int code))
                {
                    break;
                }

                if (code == 0)
                {
                    break;
                }

                if (double.TryParse(lines[pairIndex + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
                {
                    values[code] = value;
                }
            }

            if (values.TryGetValue(10, out double x1)
                && values.TryGetValue(20, out double y1)
                && values.TryGetValue(11, out double x2)
                && values.TryGetValue(21, out double y2)
                && values.TryGetValue(12, out double x3)
                && values.TryGetValue(22, out double y3))
            {
                total += Math.Abs(((x1 * (y2 - y3)) + (x2 * (y3 - y1)) + (x3 * (y1 - y2))) * 0.5d);
            }
        }

        return total;
    }
}
