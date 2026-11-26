using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Showcase;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Showcase;

public sealed class ShowcaseSnapshotReaderTests : IDisposable
{
    private readonly string _tempDir;

    public ShowcaseSnapshotReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RevitGeoSuite", "showcase-reader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public void ParseValidFullSnapshotPreservesAllLayerKindsHolesAndLosslessAttributes()
    {
        string json = BuildValidFullSnapshotJson();

        ShowcaseSnapshot snapshot = ShowcaseSnapshotReader.Parse(json);

        Assert.Equal("kanizu.gis-showcase", snapshot.Schema);
        Assert.Equal(1, snapshot.Version);
        Assert.Equal("shinjuku_station", snapshot.Scope);
        Assert.Equal("published/v3", snapshot.BasedOn);
        Assert.Equal(6677, snapshot.Epsg);
        Assert.Equal(2, snapshot.Levels.Count);
        Assert.Equal(9, snapshot.Layers.Count);
        Assert.Single(snapshot.Quarantined);
        Assert.Equal(snapshot.ContentHash, ShowcaseSnapshotReader.ComputeContentHash(snapshot));

        // Verify multi-hole unit polygon
        ShowcaseLayer unitLayer = snapshot.Layers.Single(l => l.Name == "1F_unit");
        Assert.Equal(ShowcaseLayerKind.Unit, unitLayer.Kind);
        ShowcaseFeature concourse = Assert.Single(unitLayer.Features);
        Assert.Equal("unit-001", concourse.Key);
        ShowcasePolygonGeometry unitPoly = Assert.IsType<ShowcasePolygonGeometry>(concourse.Geometry);
        Assert.Equal(3, unitPoly.Rings.Count);
        Assert.Equal(2, unitPoly.InteriorRings.Count);

        // Verify F4-ready collections (pointFacility, pointFacilityNetwork, networkLink, networkConnector, networkNode)
        ShowcaseLayer poiLayer = snapshot.Layers.Single(l => l.Kind == ShowcaseLayerKind.PointFacility);
        Assert.Single(poiLayer.Features);
        ShowcasePointGeometry poiPt = Assert.IsType<ShowcasePointGeometry>(poiLayer.Features[0].Geometry);
        Assert.Equal(3.3d, poiPt.Z);

        ShowcaseLayer poiNetLayer = snapshot.Layers.Single(l => l.Kind == ShowcaseLayerKind.PointFacilityNetwork);
        Assert.Single(poiNetLayer.Features);
        Assert.Equal("SYM-001", poiNetLayer.Features[0].Key);

        ShowcaseLayer connLayer = snapshot.Layers.Single(l => l.Kind == ShowcaseLayerKind.NetworkConnector);
        ShowcaseConnector3DGeometry connector = Assert.IsType<ShowcaseConnector3DGeometry>(connLayer.Features[0].Geometry);
        Assert.Equal(ShowcaseConnectorGeometryOrigin.SurveyedPath, connector.GeometryOrigin);
        Assert.Equal("level:0", connector.From.LevelId);
        Assert.Equal("level:1", connector.To.LevelId);
        Assert.Equal(3.3d, connector.From.TpM);
        Assert.Equal(7.8d, connector.To.TpM);

        // Lossless attribute preflight: 64-bit integer ID, leading zeros, null vs ""
        IReadOnlyList<NormalizedShowcaseAttribute> normalized =
            ShowcaseAttributeNormalizer.NormalizeFeatureAttributes(unitLayer, concourse);

        NormalizedShowcaseAttribute bigId = normalized.Single(a => a.FieldName == "big_id");
        Assert.True(bigId.PreserveAsText);
        Assert.Equal(ShowcaseFieldType.Text, bigId.EffectiveFieldType);
        Assert.Equal("9007199254740993", bigId.DisplayText);
        Assert.False(bigId.IsNull);
        Assert.False(bigId.IsEmptyString);

        NormalizedShowcaseAttribute maxInt64 = normalized.Single(a => a.FieldName == "raw_int64_id");
        Assert.True(maxInt64.PreserveAsText);
        Assert.Equal("9223372036854775807", maxInt64.DisplayText);

        NormalizedShowcaseAttribute leadingZero = normalized.Single(a => a.FieldName == "station_code");
        Assert.True(leadingZero.PreserveAsText);
        Assert.Equal("0042", leadingZero.DisplayText);

        NormalizedShowcaseAttribute nullAttr = normalized.Single(a => a.FieldName == "nullable_note");
        Assert.True(nullAttr.IsNull);
        Assert.False(nullAttr.IsEmptyString);
        Assert.Null(nullAttr.DisplayText);

        NormalizedShowcaseAttribute emptyAttr = normalized.Single(a => a.FieldName == "empty_note");
        Assert.False(emptyAttr.IsNull);
        Assert.True(emptyAttr.IsEmptyString);
        Assert.Equal(string.Empty, emptyAttr.DisplayText);

        // Date strings are NOT mangled by DateParseHandling
        NormalizedShowcaseAttribute dateAttr = normalized.Single(a => a.FieldName == "survey_date");
        Assert.Equal("2026-05-01", dateAttr.DisplayText);

        // Lossless JSON serialization keeps null vs "", exact 64-bit IDs, and leading zeros distinct
        string serialized = ShowcaseAttributeNormalizer.SerializeLosslessAttributesJson(normalized);
        JObject parsedBack = JObject.Parse(serialized);
        Assert.Equal(JTokenType.Null, parsedBack["nullable_note"]!.Type);
        Assert.Equal(JTokenType.String, parsedBack["empty_note"]!.Type);
        Assert.Equal(string.Empty, parsedBack["empty_note"]!.Value<string>());
        Assert.Equal("9007199254740993", parsedBack["big_id"]!.Value<string>());
        Assert.Equal("9223372036854775807", parsedBack["raw_int64_id"]!.Value<string>());
        Assert.Equal("0042", parsedBack["station_code"]!.Value<string>());
    }

    [Fact]
    public void ReadFileLoadsSnapshotFromDiskAndIgnoresWrittenUtcInContentHash()
    {
        string json1 = BuildValidFullSnapshotJson(writtenUtc: "2026-05-01T00:00:00Z");
        string json2 = BuildValidFullSnapshotJson(writtenUtc: "2026-06-15T12:34:56Z");

        string path = Path.Combine(_tempDir, "shinjuku_station.gis-showcase.json");
        File.WriteAllText(path, json1);

        ShowcaseSnapshot fromFile = ShowcaseSnapshotReader.ReadFile(path);
        ShowcaseSnapshot fromText2 = ShowcaseSnapshotReader.Parse(json2);

        Assert.Equal(fromFile.ContentHash, fromText2.ContentHash);
        Assert.NotEqual(fromFile.WrittenUtc, fromText2.WrittenUtc);
    }

    [Fact]
    public void RejectsInvalidSchemaAndUnsupportedVersions()
    {
        JObject badSchema = JObject.Parse(BuildValidFullSnapshotJson());
        badSchema["schema"] = "kanizu.other-schema";
        Assert.Throws<ShowcaseSnapshotException>(() =>
            ShowcaseSnapshotReader.Parse(badSchema.ToString(), new ShowcaseSnapshotReadOptions { VerifyContentHash = false }));

        JObject badVersion = JObject.Parse(BuildValidFullSnapshotJson());
        badVersion["version"] = 2;
        Assert.Throws<ShowcaseSnapshotException>(() =>
            ShowcaseSnapshotReader.Parse(badVersion.ToString(), new ShowcaseSnapshotReadOptions { VerifyContentHash = false }));

        JObject zeroVersion = JObject.Parse(BuildValidFullSnapshotJson());
        zeroVersion["version"] = 0;
        Assert.Throws<ShowcaseSnapshotException>(() =>
            ShowcaseSnapshotReader.Parse(zeroVersion.ToString(), new ShowcaseSnapshotReadOptions { VerifyContentHash = false }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4326)]  // WGS84 geographic (degrees)
    [InlineData(6668)]  // JGD2011 geographic (degrees)
    [InlineData(4612)]  // JGD2000 geographic (degrees)
    [InlineData(2229)]  // US Survey Feet State Plane
    [InlineData(999999)]
    public void RejectsMissingGeographicOrNonMetricEpsg(int invalidEpsg)
    {
        JObject doc = JObject.Parse(BuildValidFullSnapshotJson());
        doc["epsg"] = invalidEpsg;

        Assert.Throws<ShowcaseSnapshotException>(() =>
            ShowcaseSnapshotReader.Parse(doc.ToString(), new ShowcaseSnapshotReadOptions { VerifyContentHash = false }));
    }

    [Fact]
    public void RejectsContentHashMismatchWhenVerificationEnabled()
    {
        JObject doc = JObject.Parse(BuildValidFullSnapshotJson());
        doc["contentHash"] = "0000000000000000000000000000000000000000000000000000000000000000";

        ShowcaseSnapshotException ex = Assert.Throws<ShowcaseSnapshotException>(() =>
            ShowcaseSnapshotReader.Parse(doc.ToString()));
        Assert.Contains("contentHash mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsMalformedUnclosedAndDegeneratePolygonRings()
    {
        // Unclosed ring
        JObject unclosed = JObject.Parse(BuildValidFullSnapshotJson());
        unclosed["layers"]![0]!["features"]![0]!["geometry"] = JObject.Parse(
            """{"type":"polygon","rings":[[[0.0,0.0],[10.0,0.0],[10.0,10.0],[0.0,10.0]]]}""");
        Assert.Throws<ShowcaseSnapshotException>(() =>
            ShowcaseSnapshotReader.Parse(unclosed.ToString(), new ShowcaseSnapshotReadOptions { VerifyContentHash = false }));

        // Fewer than 4 vertices
        JObject tooFew = JObject.Parse(BuildValidFullSnapshotJson());
        tooFew["layers"]![0]!["features"]![0]!["geometry"] = JObject.Parse(
            """{"type":"polygon","rings":[[[0.0,0.0],[10.0,0.0],[0.0,0.0]]]}""");
        Assert.Throws<ShowcaseSnapshotException>(() =>
            ShowcaseSnapshotReader.Parse(tooFew.ToString(), new ShowcaseSnapshotReadOptions { VerifyContentHash = false }));

        // Degenerate zero-area collinear ring
        JObject degenerate = JObject.Parse(BuildValidFullSnapshotJson());
        degenerate["layers"]![0]!["features"]![0]!["geometry"] = JObject.Parse(
            """{"type":"polygon","rings":[[[0.0,0.0],[5.0,0.0],[10.0,0.0],[0.0,0.0]]]}""");
        Assert.Throws<ShowcaseSnapshotException>(() =>
            ShowcaseSnapshotReader.Parse(degenerate.ToString(), new ShowcaseSnapshotReadOptions { VerifyContentHash = false }));
    }

    [Fact]
    public void RejectsNonFiniteCoordinatesAndDuplicateFeatureKeys()
    {
        string nanJson = BuildValidFullSnapshotJson().Replace("3.3", "NaN");
        Assert.Throws<ShowcaseSnapshotException>(() =>
            ShowcaseSnapshotReader.Parse(nanJson, new ShowcaseSnapshotReadOptions { VerifyContentHash = false }));

        // Duplicate feature keys within the same layer
        JObject dupDoc = JObject.Parse(BuildValidFullSnapshotJson());
        JArray features = (JArray)dupDoc["layers"]![0]!["features"]!;
        features.Add(features[0]!.DeepClone());
        dupDoc["summary"]!["totalFeatures"] = dupDoc["summary"]!["totalFeatures"]!.Value<int>() + 1;
        Assert.Throws<ShowcaseSnapshotException>(() =>
            ShowcaseSnapshotReader.Parse(dupDoc.ToString(), new ShowcaseSnapshotReadOptions { VerifyContentHash = false }));
    }

    internal static string BuildValidFullSnapshotJson(string writtenUtc = "2026-05-01T00:00:00Z")
    {
        ShowcaseLevel[] levels =
        {
            new("level:0", "0", 0, "1F", 3.3d, ShowcaseElevationSource.Surveyed),
            new("level:1", "1", 1, "2F", 7.8d, ShowcaseElevationSource.Model),
        };

        ShowcaseLayer unitLayer = new(
            "1F_unit",
            ShowcaseLayerKind.Unit,
            new[]
            {
                new ShowcaseField("id", ShowcaseFieldType.Text),
                new ShowcaseField("category", ShowcaseFieldType.Text, aliasJa: "種別", domain: "UnitCategory"),
                new ShowcaseField("big_id", ShowcaseFieldType.Integer, preserveAsText: true),
                new ShowcaseField("raw_int64_id", ShowcaseFieldType.Integer, preserveAsText: false),
                new ShowcaseField("station_code", ShowcaseFieldType.Text, preserveAsText: true),
                new ShowcaseField("nullable_note", ShowcaseFieldType.Text),
                new ShowcaseField("empty_note", ShowcaseFieldType.Text),
                new ShowcaseField("survey_date", ShowcaseFieldType.Date),
                new ShowcaseField("ordinal", ShowcaseFieldType.Integer),
                new ShowcaseField("width_m", ShowcaseFieldType.Real),
                new ShowcaseField("is_paid", ShowcaseFieldType.Boolean),
            },
            new[]
            {
                new ShowcaseFeature(
                    "unit-001",
                    "level:0",
                    "0",
                    new ShowcasePolygonGeometry(new IReadOnlyList<Point2D>[]
                    {
                        // Exterior 20m x 12m (CW FileGDB style to also exercise winding normalization downstream)
                        new[] { new Point2D(0, 0), new Point2D(0, 12), new Point2D(20, 12), new Point2D(20, 0), new Point2D(0, 0) },
                        // Hole 1: 2m x 2m column void
                        new[] { new Point2D(3, 3), new Point2D(5, 3), new Point2D(5, 5), new Point2D(3, 5), new Point2D(3, 3) },
                        // Hole 2: 3m x 2m escalator void
                        new[] { new Point2D(10, 4), new Point2D(13, 4), new Point2D(13, 6), new Point2D(10, 6), new Point2D(10, 4) },
                    }),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["id"] = ShowcaseAttributeValue.FromText("unit-001"),
                        ["category"] = ShowcaseAttributeValue.FromText("walkway"),
                        ["big_id"] = ShowcaseAttributeValue.FromText("9007199254740993"),
                        ["raw_int64_id"] = ShowcaseAttributeValue.FromInteger(9223372036854775807L),
                        ["station_code"] = ShowcaseAttributeValue.FromText("0042"),
                        ["nullable_note"] = ShowcaseAttributeValue.Null,
                        ["empty_note"] = ShowcaseAttributeValue.FromText(string.Empty),
                        ["survey_date"] = ShowcaseAttributeValue.FromText("2026-05-01"),
                        ["ordinal"] = ShowcaseAttributeValue.FromInteger(0),
                        ["width_m"] = ShowcaseAttributeValue.FromReal(12.5d),
                        ["is_paid"] = ShowcaseAttributeValue.FromBoolean(true),
                    }),
            });

        ShowcaseLayer openingLayer = new(
            "1F_opening",
            ShowcaseLayerKind.Opening,
            new[]
            {
                new ShowcaseField("id", ShowcaseFieldType.Text),
                new ShowcaseField("category", ShowcaseFieldType.Text),
                new ShowcaseField("door", ShowcaseFieldType.Text),
            },
            new[]
            {
                new ShowcaseFeature(
                    "op-001",
                    "level:0",
                    "0",
                    new ShowcaseLineStringGeometry(new[] { new Point2D(20, 4), new Point2D(20, 6) }),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["id"] = ShowcaseAttributeValue.FromText("op-001"),
                        ["category"] = ShowcaseAttributeValue.FromText("pedestrian"),
                        ["door"] = ShowcaseAttributeValue.FromText("automatic"),
                    }),
            });

        ShowcaseLayer fixtureLayer = new(
            "1F_fixture",
            ShowcaseLayerKind.Fixture,
            new[]
            {
                new ShowcaseField("id", ShowcaseFieldType.Text),
                new ShowcaseField("category", ShowcaseFieldType.Text),
                new ShowcaseField("fixture_type", ShowcaseFieldType.Text),
            },
            new[]
            {
                new ShowcaseFeature(
                    "fxt-001",
                    "level:0",
                    "0",
                    new ShowcasePolygonGeometry(new IReadOnlyList<Point2D>[]
                    {
                        new[] { new Point2D(7, 1), new Point2D(9, 1), new Point2D(9, 2), new Point2D(7, 2), new Point2D(7, 1) },
                    }),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["id"] = ShowcaseAttributeValue.FromText("fxt-001"),
                        ["category"] = ShowcaseAttributeValue.FromText("ticket_gate"),
                        ["fixture_type"] = ShowcaseAttributeValue.FromText("turnstile"),
                    }),
            });

        ShowcaseLayer levelLayer = new(
            "1F_level",
            ShowcaseLayerKind.Level,
            new[]
            {
                new ShowcaseField("id", ShowcaseFieldType.Text),
                new ShowcaseField("short_name", ShowcaseFieldType.Text),
                new ShowcaseField("ordinal", ShowcaseFieldType.Integer),
            },
            new[]
            {
                new ShowcaseFeature(
                    "level-0",
                    "level:0",
                    "0",
                    new ShowcasePolygonGeometry(new IReadOnlyList<Point2D>[]
                    {
                        new[] { new Point2D(0, 0), new Point2D(20, 0), new Point2D(20, 12), new Point2D(0, 12), new Point2D(0, 0) },
                    }),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["id"] = ShowcaseAttributeValue.FromText("level:0"),
                        ["short_name"] = ShowcaseAttributeValue.FromText("1F"),
                        ["ordinal"] = ShowcaseAttributeValue.FromInteger(0),
                    }),
            });

        ShowcaseLayer poiLayer = new(
            "point_facility",
            ShowcaseLayerKind.PointFacility,
            new[]
            {
                new ShowcaseField("symbol_id", ShowcaseFieldType.Text),
                new ShowcaseField("name_ja", ShowcaseFieldType.Text),
            },
            new[]
            {
                new ShowcaseFeature(
                    "SYM-001",
                    "level:0",
                    "0",
                    new ShowcasePointGeometry(new Point2D(8, 6), 3.3d),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["symbol_id"] = ShowcaseAttributeValue.FromText("SYM-001"),
                        ["name_ja"] = ShowcaseAttributeValue.FromText("多機能トイレ"),
                    }),
            });

        ShowcaseLayer poiNetLayer = new(
            "point_facility_network",
            ShowcaseLayerKind.PointFacilityNetwork,
            new[] { new ShowcaseField("symbol_id", ShowcaseFieldType.Text) },
            new[]
            {
                new ShowcaseFeature(
                    "SYM-001",
                    "level:0",
                    "0",
                    new ShowcasePointGeometry(new Point2D(8.5d, 6), 3.3d),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["symbol_id"] = ShowcaseAttributeValue.FromText("SYM-001"),
                    }),
            });

        ShowcaseLayer netLinkLayer = new(
            "network_link",
            ShowcaseLayerKind.NetworkLink,
            new[] { new ShowcaseField("pathid", ShowcaseFieldType.Text) },
            new[]
            {
                new ShowcaseFeature(
                    "L-001",
                    "level:0",
                    "0",
                    new ShowcaseLineStringGeometry(new[] { new Point2D(1, 6), new Point2D(19, 6) }),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["pathid"] = ShowcaseAttributeValue.FromText("L-001"),
                    }),
            });

        ShowcaseLayer netConnectorLayer = new(
            "network_connector",
            ShowcaseLayerKind.NetworkConnector,
            new[] { new ShowcaseField("pathid", ShowcaseFieldType.Text) },
            new[]
            {
                new ShowcaseFeature(
                    "C-001",
                    null,
                    null,
                    new ShowcaseConnector3DGeometry(
                        new ShowcaseEndpoint("level:0", "0", new Point2D(10, 5), 3.3d),
                        new ShowcaseEndpoint("level:1", "1", new Point2D(15, 5), 7.8d),
                        new[] { new Point2D(10, 5), new Point2D(15, 5) },
                        ShowcaseConnectorGeometryOrigin.SurveyedPath),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["pathid"] = ShowcaseAttributeValue.FromText("C-001"),
                    }),
            });

        ShowcaseLayer netNodeLayer = new(
            "network_node",
            ShowcaseLayerKind.NetworkNode,
            new[] { new ShowcaseField("node_id", ShowcaseFieldType.Text) },
            new[]
            {
                new ShowcaseFeature(
                    "N-001",
                    "level:0",
                    "0",
                    new ShowcasePointGeometry(new Point2D(10, 6), 3.3d),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["node_id"] = ShowcaseAttributeValue.FromText("N-001"),
                    }),
            });

        ShowcaseLayer[] layers =
        {
            unitLayer, openingLayer, fixtureLayer, levelLayer,
            poiLayer, poiNetLayer, netLinkLayer, netConnectorLayer, netNodeLayer,
        };
        QuarantinedShowcaseFeature[] quarantined =
        {
            new("1F_unit", "bad-row", "missing geometry"),
        };

        return SerializeSnapshotWithValidHash(
            scope: "shinjuku_station",
            basedOn: "published/v3",
            writtenUtc: writtenUtc,
            kanizu: "0.9.0",
            epsg: 6677,
            levels: levels,
            layers: layers,
            quarantined: quarantined);
    }

    internal static string SerializeSnapshotWithValidHash(
        string scope,
        string? basedOn,
        string writtenUtc,
        string kanizu,
        int epsg,
        IReadOnlyList<ShowcaseLevel> levels,
        IReadOnlyList<ShowcaseLayer> layers,
        IReadOnlyList<QuarantinedShowcaseFeature> quarantined)
    {
        ShowcaseSummary summary = ShowcaseSnapshotReader.ComputeSummary(layers, quarantined);
        ShowcaseSnapshot draft = new(
            ShowcaseContract.Schema,
            ShowcaseContract.Version,
            scope,
            basedOn,
            writtenUtc,
            kanizu,
            epsg,
            contentHash: string.Empty,
            levels,
            layers,
            quarantined,
            summary);

        string contentHash = ShowcaseSnapshotReader.ComputeContentHash(draft);

        JObject root = new()
        {
            ["schema"] = draft.Schema,
            ["version"] = draft.Version,
            ["scope"] = draft.Scope,
        };
        if (draft.BasedOn != null)
        {
            root["basedOn"] = draft.BasedOn;
        }

        root["writtenUtc"] = draft.WrittenUtc;
        root["kanizu"] = draft.Kanizu;
        root["epsg"] = draft.Epsg;
        root["contentHash"] = contentHash;
        root["levels"] = new JArray(levels.Select(l =>
        {
            JObject o = new()
            {
                ["levelId"] = l.LevelId,
                ["floorKey"] = l.FloorKey,
                ["ordinal"] = l.Ordinal,
                ["shortName"] = l.ShortName,
            };
            if (l.TpM.HasValue)
            {
                o["tpM"] = l.TpM.Value;
            }

            o["elevationSource"] = ShowcaseSnapshotReader.ToContractString(l.ElevationSource);
            return o;
        }));
        root["layers"] = new JArray(layers.Select(layer => new JObject
        {
            ["name"] = layer.Name,
            ["kind"] = ShowcaseSnapshotReader.ToContractString(layer.Kind),
            ["fields"] = new JArray(layer.Fields.Select(f =>
            {
                JObject fo = new()
                {
                    ["name"] = f.Name,
                    ["fieldType"] = ShowcaseSnapshotReader.ToContractString(f.FieldType),
                };
                if (f.AliasJa != null)
                {
                    fo["aliasJa"] = f.AliasJa;
                }

                if (f.Domain != null)
                {
                    fo["domain"] = f.Domain;
                }

                fo["preserveAsText"] = f.PreserveAsText;
                return fo;
            })),
            ["features"] = new JArray(layer.Features.Select(feat =>
            {
                JObject fo = new()
                {
                    ["key"] = feat.Key,
                };
                if (feat.LevelId != null)
                {
                    fo["levelId"] = feat.LevelId;
                }

                if (feat.FloorKey != null)
                {
                    fo["floorKey"] = feat.FloorKey;
                }

                fo["geometry"] = SerializeGeometry(feat.Geometry);
                JObject attrs = new();
                foreach (KeyValuePair<string, ShowcaseAttributeValue> kvp in feat.Attributes.OrderBy(a => a.Key, StringComparer.Ordinal))
                {
                    attrs[kvp.Key] = kvp.Value.Kind switch
                    {
                        ShowcaseAttributeKind.Null => JValue.CreateNull(),
                        ShowcaseAttributeKind.Text => new JValue(kvp.Value.TextValue),
                        ShowcaseAttributeKind.Integer => new JValue(kvp.Value.Int64Value!.Value),
                        ShowcaseAttributeKind.Real => new JValue(kvp.Value.RealValue!.Value),
                        ShowcaseAttributeKind.Boolean => new JValue(kvp.Value.BoolValue!.Value),
                        ShowcaseAttributeKind.Json => JToken.Parse(kvp.Value.RawJsonText!),
                        _ => JValue.CreateNull(),
                    };
                }

                fo["attributes"] = attrs;
                return fo;
            })),
        }));
        root["quarantined"] = new JArray(quarantined.Select(q => new JObject
        {
            ["layer"] = q.Layer,
            ["sourceKey"] = q.SourceKey,
            ["reason"] = q.Reason,
        }));

        JObject countsByKind = new();
        foreach (KeyValuePair<ShowcaseLayerKind, int> kvp in summary.CountsByKind)
        {
            countsByKind[ShowcaseSnapshotReader.ToContractString(kvp.Key)] = kvp.Value;
        }

        root["summary"] = new JObject
        {
            ["countsByKind"] = countsByKind,
            ["totalLayers"] = summary.TotalLayers,
            ["totalFeatures"] = summary.TotalFeatures,
            ["totalQuarantined"] = summary.TotalQuarantined,
        };

        return root.ToString(Formatting.Indented);
    }

    private static JObject SerializeGeometry(ShowcaseGeometry geom)
    {
        switch (geom)
        {
            case ShowcasePointGeometry pt:
            {
                JObject o = new()
                {
                    ["type"] = "point",
                    ["xy"] = new JArray(pt.Xy.X, pt.Xy.Y),
                };
                if (pt.Z.HasValue)
                {
                    o["z"] = pt.Z.Value;
                }

                return o;
            }

            case ShowcaseLineStringGeometry line:
                return new JObject
                {
                    ["type"] = "lineString",
                    ["coords"] = new JArray(line.Coords.Select(c => new JArray(c.X, c.Y))),
                };

            case ShowcaseMultiLineStringGeometry mline:
                return new JObject
                {
                    ["type"] = "multiLineString",
                    ["lines"] = new JArray(mline.Lines.Select(l => new JArray(l.Select(c => new JArray(c.X, c.Y))))),
                };

            case ShowcasePolygonGeometry poly:
                return new JObject
                {
                    ["type"] = "polygon",
                    ["rings"] = new JArray(poly.Rings.Select(r => new JArray(r.Select(c => new JArray(c.X, c.Y))))),
                };

            case ShowcaseMultiPolygonGeometry mpoly:
                return new JObject
                {
                    ["type"] = "multiPolygon",
                    ["polygons"] = new JArray(mpoly.Polygons.Select(p =>
                        new JArray(p.Rings.Select(r => new JArray(r.Select(c => new JArray(c.X, c.Y))))))),
                };

            case ShowcaseConnector3DGeometry conn:
                return new JObject
                {
                    ["type"] = "connector3D",
                    ["from"] = SerializeEndpoint(conn.From),
                    ["to"] = SerializeEndpoint(conn.To),
                    ["path"] = new JArray(conn.Path.Select(c => new JArray(c.X, c.Y))),
                    ["geometryOrigin"] = ShowcaseSnapshotReader.ToContractString(conn.GeometryOrigin),
                };

            default:
                throw new InvalidOperationException($"Unsupported test geometry {geom.GetType().Name}");
        }
    }

    private static JObject SerializeEndpoint(ShowcaseEndpoint ep)
    {
        JObject o = new();
        if (ep.LevelId != null)
        {
            o["levelId"] = ep.LevelId;
        }

        if (ep.FloorKey != null)
        {
            o["floorKey"] = ep.FloorKey;
        }

        o["xy"] = new JArray(ep.Xy.X, ep.Xy.Y);
        if (ep.TpM.HasValue)
        {
            o["tpM"] = ep.TpM.Value;
        }

        return o;
    }
}
