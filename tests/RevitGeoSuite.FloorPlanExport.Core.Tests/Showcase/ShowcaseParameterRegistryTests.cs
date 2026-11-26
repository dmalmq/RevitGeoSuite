using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Showcase;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Showcase;

public sealed class ShowcaseParameterRegistryTests
{
    [Fact]
    public void UuidV5MatchesAuthoritativeRfc4122TestVectors()
    {
        // Verified against Python stdlib uuid.uuid5 / RFC 4122 SHA-1 specification
        Assert.Equal(
            new Guid("886313e1-3b8a-5372-9b90-0c9aee199e5d"),
            UuidV5.Create(UuidV5.DnsNamespace, "python.org"));

        Assert.Equal(
            new Guid("2ed6657d-e927-568b-95e1-2665a8aea6a2"),
            UuidV5.Create(UuidV5.DnsNamespace, "www.example.com"));

        Assert.Equal(
            new Guid("94245bd3-fd87-5e0d-b8ff-3b7dcc1c1b32"),
            UuidV5.Create(UuidV5.UrlNamespace, "https://kanizu.app/"));

        // Version nibble == 5 and variant == RFC 4122 (8, 9, a, or b)
        Guid generated = ShowcaseParameterRegistry.ComputeParameterGuid("unit", "category", ShowcaseParameterDataType.Text);
        string text = generated.ToString("D");
        Assert.Equal('5', text[14]);
        Assert.Contains(text[19], new[] { '8', '9', 'a', 'b' });
    }

    [Fact]
    public void NamespaceGuidIsFrozenAndParameterGuidsAreDeterministicAcrossRuns()
    {
        Assert.Equal(new Guid("8b3f6c9e-4c72-4a2b-9d1e-5f8a3c6b2d40"), ShowcaseParameterRegistry.NamespaceGuid);

        Guid a1 = ShowcaseParameterRegistry.ComputeParameterGuid("unit", "category", ShowcaseParameterDataType.Text);
        Guid a2 = ShowcaseParameterRegistry.ComputeParameterGuid("UNIT", "Category", ShowcaseParameterDataType.Text);
        Guid b = ShowcaseParameterRegistry.ComputeParameterGuid("unit", "category", ShowcaseParameterDataType.Integer);
        Guid c = ShowcaseParameterRegistry.ComputeParameterGuid("fixture", "category", ShowcaseParameterDataType.Text);

        Assert.Equal(a1, a2);
        Assert.NotEqual(a1, b);
        Assert.NotEqual(a1, c);
        Assert.NotEqual(b, c);
    }

    [Fact]
    public void Enforces80ParameterCapAndSerializesOverflowLosslesslyIntoKsAttributesJson()
    {
        // Create 95 attributes on one layer, including overflow 64-bit IDs, leading zeros, null, and ""
        List<ShowcaseField> fields = new();
        SortedDictionary<string, ShowcaseAttributeValue> attrs = new(StringComparer.Ordinal);

        for (int i = 0; i < 95; i++)
        {
            string fieldName = $"z_extra_{i:D3}";
            if (i == 90)
            {
                fields.Add(new ShowcaseField(fieldName, ShowcaseFieldType.Integer, preserveAsText: false));
                attrs[fieldName] = ShowcaseAttributeValue.FromInteger(9007199254740993L);
            }
            else if (i == 91)
            {
                fields.Add(new ShowcaseField(fieldName, ShowcaseFieldType.Text, preserveAsText: false));
                attrs[fieldName] = ShowcaseAttributeValue.FromText("00123");
            }
            else if (i == 92)
            {
                fields.Add(new ShowcaseField(fieldName, ShowcaseFieldType.Text));
                attrs[fieldName] = ShowcaseAttributeValue.Null;
            }
            else if (i == 93)
            {
                fields.Add(new ShowcaseField(fieldName, ShowcaseFieldType.Text));
                attrs[fieldName] = ShowcaseAttributeValue.FromText(string.Empty);
            }
            else
            {
                fields.Add(new ShowcaseField(fieldName, ShowcaseFieldType.Text));
                attrs[fieldName] = ShowcaseAttributeValue.FromText($"val-{i:D3}");
            }
        }

        ShowcaseLayer wideLayer = new(
            "1F_unit",
            ShowcaseLayerKind.Unit,
            fields,
            new[]
            {
                new ShowcaseFeature(
                    "wide-001",
                    "level:0",
                    "0",
                    new ShowcasePolygonGeometry(new IReadOnlyList<Point2D>[]
                    {
                        new[] { new Point2D(0, 0), new Point2D(10, 0), new Point2D(10, 10), new Point2D(0, 10), new Point2D(0, 0) },
                    }),
                    attrs),
            });

        ShowcaseSnapshot snapshot = ShowcaseSnapshotReader.Parse(
            ShowcaseSnapshotReaderTests.SerializeSnapshotWithValidHash(
                scope: "wide_test",
                basedOn: null,
                writtenUtc: "2026-05-01T00:00:00Z",
                kanizu: "0.9.0",
                epsg: 6677,
                levels: new[] { new ShowcaseLevel("level:0", "0", 0, "1F", 0.0d, ShowcaseElevationSource.Surveyed) },
                layers: new[] { wideLayer },
                quarantined: Array.Empty<QuarantinedShowcaseFeature>()));

        ShowcaseParameterRegistry registry = ShowcaseParameterRegistry.Build(snapshot);

        // Strictly capped at 80 direct parameters (5 built-ins + 75 attribute columns) + 1 ks_attributes_json overflow container
        Assert.Equal(ShowcaseParameterRegistry.MaxDirectParameters, registry.DirectParameters.Count);
        Assert.Equal(ShowcaseParameterRegistry.MaxDirectParameters + 1, registry.AllParameters.Count);
        Assert.Equal(20, registry.OverflowFieldCount);
        Assert.Equal(ShowcaseParameterRegistry.OverflowParameterName, registry.OverflowParameter.Name);

        // Every single parameter name passes F2's ShowcaseExportGuard.IsShowcaseParameterName
        Assert.All(registry.AllParameters, p => Assert.True(ShowcaseExportGuard.IsShowcaseParameterName(p.Name)));

        // Bind feature and inspect overflow JSON
        (IReadOnlyList<ShowcaseParameterAssignment> direct, string? overflowJson) =
            registry.BindFeatureAttributes(wideLayer, wideLayer.Features[0]);

        Assert.Equal(ShowcaseParameterRegistry.MaxDirectParameters, direct.Count);
        Assert.False(string.IsNullOrWhiteSpace(overflowJson));

        JObject overflow = JObject.Parse(overflowJson!);
        Assert.Equal(20, overflow.Properties().Count());
        Assert.Equal("9007199254740993", overflow["z_extra_090"]!.Value<string>());
        Assert.Equal("00123", overflow["z_extra_091"]!.Value<string>());
        Assert.Equal(JTokenType.Null, overflow["z_extra_092"]!.Type);
        Assert.Equal(JTokenType.String, overflow["z_extra_093"]!.Type);
        Assert.Equal(string.Empty, overflow["z_extra_093"]!.Value<string>());
    }

    [Fact]
    public void GeneratesValidDeterministicRevitSharedParameterFileText()
    {
        ShowcaseSnapshot snapshot = ShowcaseSnapshotReader.Parse(ShowcaseSnapshotReaderTests.BuildValidFullSnapshotJson());
        ShowcaseParameterRegistry registry1 = ShowcaseParameterRegistry.Build(snapshot);
        ShowcaseParameterRegistry registry2 = ShowcaseParameterRegistry.Build(snapshot);

        string file1 = registry1.ToSharedParameterFileText(includeGisModelRoleParameter: true);
        string file2 = registry2.ToSharedParameterFileText(includeGisModelRoleParameter: true);

        Assert.Equal(file1, file2);
        Assert.DoesNotContain("\r\n", file1);
        Assert.Contains("*META\tVERSION\tMINVERSION\nMETA\t2\t1\n", file1);
        Assert.Contains($"*GROUP\tID\tNAME\nGROUP\t1\t{ShowcaseParameterRegistry.ParameterGroupName}\nGROUP\t2\t{GisSharedParameters.GroupName}\n", file1);
        Assert.Contains("*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOEXISTENT\n", file1);

        // Frozen gis_model_role GUID is present
        Assert.Contains($"{ShowcaseExportGuard.GisModelRoleParameterGuid:D}\tgis_model_role\tTEXT\t\t2\t1\t", file1);

        // Lossless 64-bit integer columns are emitted as TEXT, safe 32-bit integer columns as INTEGER, real as NUMBER, bool as YESNO
        Assert.Contains("\tks_big_id\tTEXT\t", file1);
        Assert.Contains("\tks_raw_int64_id\tTEXT\t", file1);
        Assert.Contains("\tks_station_code\tTEXT\t", file1);
        Assert.Contains("\tks_ordinal\tINTEGER\t", file1);
        Assert.Contains("\tks_width_m\tNUMBER\t", file1);
        Assert.Contains("\tks_is_paid\tYESNO\t", file1);
        Assert.Contains("\tks_attributes_json\tTEXT\t", file1);

        // All PARAM lines have exactly 10 tab-separated columns and unique GUIDs/names
        string[] paramLines = file1
            .Split('\n')
            .Where(line => line.StartsWith("PARAM\t", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(registry1.AllParameters.Count + 1, paramLines.Length);

        HashSet<string> guids = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in paramLines)
        {
            string[] cols = line.Split('\t');
            Assert.Equal(10, cols.Length);
            Assert.True(Guid.TryParse(cols[1], out _));
            Assert.True(guids.Add(cols[1]), $"Duplicate GUID in shared parameter file: {cols[1]}");
            Assert.True(names.Add(cols[2]), $"Duplicate parameter name in shared parameter file: {cols[2]}");
        }
    }

    [Fact]
    public void SanitizesNonAsciiFieldNamesDeterministicallyWithoutCollisions()
    {
        string a = ShowcaseParameterRegistry.SanitizeFieldToken("施設名称");
        string b = ShowcaseParameterRegistry.SanitizeFieldToken("施設区分");
        string aAgain = ShowcaseParameterRegistry.SanitizeFieldToken("施設名称");

        Assert.StartsWith("f_", a, StringComparison.Ordinal);
        Assert.StartsWith("f_", b, StringComparison.Ordinal);
        Assert.Equal(a, aAgain);
        Assert.NotEqual(a, b);
        Assert.Equal("symbol_id", ShowcaseParameterRegistry.SanitizeFieldToken("KS_Symbol-ID"));
    }
}
