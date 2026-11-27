using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Showcase;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Showcase;

public sealed class ShowcasePoiAndNetworkPlannerTests
{
    [Fact]
    public void PlansPoiMarkersWith22PlusAttributesCategoryMaterialsOverflowAndTightTriangleBudget()
    {
        // Build a point_facility layer with 26 columns (covering real StaNavi/IMDF POI schema + lossless edge cases)
        List<ShowcaseField> fields = new()
        {
            new("symbol_id", ShowcaseFieldType.Text),
            new("category", ShowcaseFieldType.Text),
            new("sub_category", ShowcaseFieldType.Text),
            new("name_ja", ShowcaseFieldType.Text, aliasJa: "施設名称"),
            new("name_en", ShowcaseFieldType.Text),
            new("name_zh", ShowcaseFieldType.Text),
            new("name_ko", ShowcaseFieldType.Text),
            new("floor", ShowcaseFieldType.Text),
            new("level_id", ShowcaseFieldType.Text),
            new("building_id", ShowcaseFieldType.Text),
            new("operator", ShowcaseFieldType.Text),
            new("opening_hours", ShowcaseFieldType.Text),
            new("wheelchair", ShowcaseFieldType.Boolean),
            new("ostomy", ShowcaseFieldType.Boolean),
            new("baby_chair", ShowcaseFieldType.Boolean),
            new("baby_bed", ShowcaseFieldType.Boolean),
            new("braille", ShowcaseFieldType.Boolean),
            new("audio_guide", ShowcaseFieldType.Boolean),
            new("elevator_access", ShowcaseFieldType.Boolean),
            new("paid_area", ShowcaseFieldType.Boolean),
            new("node_id", ShowcaseFieldType.Text),
            new("link_id", ShowcaseFieldType.Text),
            new("source_id", ShowcaseFieldType.Integer),
            new("station_code", ShowcaseFieldType.Text),
            new("remarks", ShowcaseFieldType.Text),
            new("updated_at", ShowcaseFieldType.Date),
        };

        SortedDictionary<string, ShowcaseAttributeValue> MakePoiAttrs(string symbolId, string category, string nameJa) =>
            new(StringComparer.Ordinal)
            {
                ["symbol_id"] = ShowcaseAttributeValue.FromText(symbolId),
                ["category"] = ShowcaseAttributeValue.FromText(category),
                ["sub_category"] = ShowcaseAttributeValue.FromText("station_core"),
                ["name_ja"] = ShowcaseAttributeValue.FromText(nameJa),
                ["name_en"] = ShowcaseAttributeValue.FromText(category),
                ["name_zh"] = ShowcaseAttributeValue.FromText("设施"),
                ["name_ko"] = ShowcaseAttributeValue.FromText("시설"),
                ["floor"] = ShowcaseAttributeValue.FromText("1F"),
                ["level_id"] = ShowcaseAttributeValue.FromText("level:0"),
                ["building_id"] = ShowcaseAttributeValue.FromText("BLD-01"),
                ["operator"] = ShowcaseAttributeValue.FromText("JR-East"),
                ["opening_hours"] = ShowcaseAttributeValue.FromText("04:30-01:00"),
                ["wheelchair"] = ShowcaseAttributeValue.FromBoolean(true),
                ["ostomy"] = ShowcaseAttributeValue.FromBoolean(true),
                ["baby_chair"] = ShowcaseAttributeValue.FromBoolean(true),
                ["baby_bed"] = ShowcaseAttributeValue.FromBoolean(false),
                ["braille"] = ShowcaseAttributeValue.FromBoolean(true),
                ["audio_guide"] = ShowcaseAttributeValue.FromBoolean(false),
                ["elevator_access"] = ShowcaseAttributeValue.FromBoolean(true),
                ["paid_area"] = ShowcaseAttributeValue.FromBoolean(true),
                ["node_id"] = ShowcaseAttributeValue.FromText("N-001"),
                ["link_id"] = ShowcaseAttributeValue.FromText("L-001"),
                ["source_id"] = ShowcaseAttributeValue.FromInteger(9007199254740995L), // 64-bit unsafe JS integer
                ["station_code"] = ShowcaseAttributeValue.FromText("0073"),            // leading-zero code
                ["remarks"] = ShowcaseAttributeValue.Null,
                ["updated_at"] = ShowcaseAttributeValue.FromText("2026-11-27"),
            };

        ShowcaseLayer poiLayer = new(
            "point_facility",
            ShowcaseLayerKind.PointFacility,
            fields,
            new[]
            {
                new ShowcaseFeature("SYM-WC", "level:0", "0", new ShowcasePointGeometry(new Point2D(10, 5), 3.3d), MakePoiAttrs("SYM-WC", "accessible_toilet", "多機能トイレ")),
                new ShowcaseFeature("SYM-GATE", "level:0", "0", new ShowcasePointGeometry(new Point2D(14, 5)), MakePoiAttrs("SYM-GATE", "ticket_gate", "中央改札")),
                new ShowcaseFeature("SYM-AED", "level:0", "0", new ShowcasePointGeometry(new Point2D(18, 5)), MakePoiAttrs("SYM-AED", "aed_emergency", "AED")),
                new ShowcaseFeature("SYM-SHOP", "level:0", "0", new ShowcasePointGeometry(new Point2D(22, 5)), MakePoiAttrs("SYM-SHOP", "retail_cafe", "カフェ")),
                new ShowcaseFeature("SYM-GEN", "level:0", "0", new ShowcasePointGeometry(new Point2D(26, 5)), MakePoiAttrs("SYM-GEN", "misc_landmark", "待ち合わせ場所")),
            });

        ShowcaseSnapshot snapshot = ParseSnapshot(
            "poi_26cols",
            new[] { new ShowcaseLevel("level:0", "0", 0, "1F", 3.3d, ShowcaseElevationSource.Surveyed) },
            new[] { poiLayer });

        ShowcasePoiAndNetworkPlanResult result = ShowcasePoiAndNetworkPlanner.Plan(snapshot, ShowcaseCoordinateTransform.Identity);

        Assert.Empty(result.Warnings);
        Assert.Equal(5, result.PoiMarkerCount);
        Assert.Equal(5, result.Elements.Count);
        Assert.Equal(5, result.SurveyedElementCount);
        Assert.Equal(0, result.SchematicElementCount);

        // Verify distinct POI material mapping
        Assert.Equal(ShowcaseMaterialCatalog.PoiAccessibility, result.Elements.Single(e => e.FeatureKey == "SYM-WC").MaterialName);
        Assert.Equal(ShowcaseMaterialCatalog.PoiTransit, result.Elements.Single(e => e.FeatureKey == "SYM-GATE").MaterialName);
        Assert.Equal(ShowcaseMaterialCatalog.PoiSafety, result.Elements.Single(e => e.FeatureKey == "SYM-AED").MaterialName);
        Assert.Equal(ShowcaseMaterialCatalog.PoiCommercial, result.Elements.Single(e => e.FeatureKey == "SYM-SHOP").MaterialName);
        Assert.Equal(ShowcaseMaterialCatalog.PoiGeneral, result.Elements.Single(e => e.FeatureKey == "SYM-GEN").MaterialName);

        // Verify all 26 columns bound cleanly via UUIDv5 ks_* parameters + built-ins + provenance
        ShowcaseElementPlan wc = result.Elements.Single(e => e.FeatureKey == "SYM-WC");
        Assert.Equal(ShowcaseElementKind.PoiMarker, wc.ElementKind);
        Assert.Equal("Kanizu.Showcase", wc.ApplicationId);
        Assert.Equal("point_facility:SYM-WC", wc.ApplicationDataId);
        Assert.StartsWith("KNZ_FRM_PoiMarker_", wc.ElementName, StringComparison.Ordinal);
        Assert.Equal(ShowcaseGeometryProvenance.Surveyed, wc.GeometryProvenance);

        // Floating cleanly above 0.15m floor/unit slab (+0.18m over 3.3m TP = 3.48m)
        ShowcaseMarkerPlacement placement = Assert.Single(wc.MarkerPlacements);
        Assert.Equal(ShowcaseMarkerShapeKind.PoiPin, placement.ShapeKind);
        Assert.InRange(placement.EstimatedTriangles, 1, ShowcaseMarkerPlacement.MaxAllowedMarkerTriangles);
        Assert.Equal(44, placement.EstimatedTriangles);
        double actualZ = ShowcaseCoordinateTransform.FeetToMeters(placement.LocationFeet.ZFeet);
        Assert.InRange(Math.Abs(actualZ - 3.48d), 0d, 1e-6d);

        // Lossless 64-bit source_id, leading-zero station_code, null remarks, and ks_geometry_provenance
        ShowcaseParameterDefinition sourceIdDef = result.ParameterRegistry.FindDirectParameter(ShowcaseLayerKind.PointFacility, "source_id")!;
        ShowcaseParameterDefinition stationCodeDef = result.ParameterRegistry.FindDirectParameter(ShowcaseLayerKind.PointFacility, "station_code")!;
        ShowcaseParameterDefinition remarksDef = result.ParameterRegistry.FindDirectParameter(ShowcaseLayerKind.PointFacility, "remarks")!;
        ShowcaseParameterDefinition provenanceDef = result.ParameterRegistry.FindByName(ShowcaseParameterRegistry.GeometryProvenanceParameterName)!;

        Assert.Equal(ShowcaseParameterDataType.Text, sourceIdDef.DataType);
        Assert.Equal("9007199254740995", wc.ParameterAssignments.Single(a => a.Parameter.Guid == sourceIdDef.Guid).TextValue);
        Assert.Equal("0073", wc.ParameterAssignments.Single(a => a.Parameter.Guid == stationCodeDef.Guid).TextValue);
        Assert.True(wc.ParameterAssignments.Single(a => a.Parameter.Guid == remarksDef.Guid).IsNull);
        Assert.Equal("surveyed", wc.ParameterAssignments.Single(a => a.Parameter.Guid == provenanceDef.Guid).TextValue);

        // Verify overflow kicks in cleanly when a tight direct parameter cap is used
        ShowcaseParameterRegistry cappedRegistry = ShowcaseParameterRegistry.Build(snapshot, includeBuiltInMetadata: true, maxDirectParameters: 12);
        ShowcasePoiAndNetworkPlanResult cappedResult = ShowcasePoiAndNetworkPlanner.Plan(snapshot, ShowcaseCoordinateTransform.Identity, cappedRegistry);
        ShowcaseElementPlan cappedWc = cappedResult.Elements.Single(e => e.FeatureKey == "SYM-WC");
        Assert.False(string.IsNullOrWhiteSpace(cappedWc.OverflowAttributesJson));
        JObject overflowObj = JObject.Parse(cappedWc.OverflowAttributesJson!);
        Assert.Equal("9007199254740995", overflowObj["source_id"]!.Value<string>());
        Assert.Equal("0073", overflowObj["station_code"]!.Value<string>());
        Assert.Equal(JTokenType.Null, overflowObj["remarks"]!.Type);
    }

    [Fact]
    public void JoinsPointFacilityNetworkBySymbolIdEmitsConnectorStemsAndSurfacesUnmatchedDiagnostics()
    {
        ShowcaseLayer poiLayer = new(
            "point_facility",
            ShowcaseLayerKind.PointFacility,
            new[]
            {
                new ShowcaseField("symbol_id", ShowcaseFieldType.Text),
                new ShowcaseField("category", ShowcaseFieldType.Text),
            },
            new[]
            {
                new ShowcaseFeature(
                    "POI-1",
                    "level:0",
                    "0",
                    new ShowcasePointGeometry(new Point2D(10, 5), 3.3d),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["symbol_id"] = ShowcaseAttributeValue.FromText("SYM-001"),
                        ["category"] = ShowcaseAttributeValue.FromText("toilet"),
                    }),
            });

        ShowcaseLayer nodeLayer = new(
            "network_node",
            ShowcaseLayerKind.NetworkNode,
            new[] { new ShowcaseField("node_id", ShowcaseFieldType.Text) },
            new[]
            {
                new ShowcaseFeature(
                    "N-100",
                    "level:0",
                    "0",
                    new ShowcasePointGeometry(new Point2D(13, 6), 3.3d),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["node_id"] = ShowcaseAttributeValue.FromText("N-100"),
                    }),
            });

        ShowcaseLayer linkLayer = new(
            "network_link",
            ShowcaseLayerKind.NetworkLink,
            new[] { new ShowcaseField("link_id", ShowcaseFieldType.Text) },
            new[]
            {
                new ShowcaseFeature(
                    "L-200",
                    "level:0",
                    "0",
                    new ShowcaseLineStringGeometry(new[] { new Point2D(5, 6), new Point2D(20, 6) }),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["link_id"] = ShowcaseAttributeValue.FromText("L-200"),
                    }),
            });

        ShowcaseLayer attachmentLayer = new(
            "point_facility_network",
            ShowcaseLayerKind.PointFacilityNetwork,
            new[]
            {
                new ShowcaseField("symbol_id", ShowcaseFieldType.Text),
                new ShowcaseField("node_id", ShowcaseFieldType.Text),
                new ShowcaseField("link_id", ShowcaseFieldType.Text),
            },
            new[]
            {
                // Matched by symbol_id + node_id + link_id
                new ShowcaseFeature(
                    "ATT-MATCHED",
                    "level:0",
                    "0",
                    new ShowcasePointGeometry(new Point2D(12, 6), 3.3d),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["symbol_id"] = ShowcaseAttributeValue.FromText("SYM-001"),
                        ["node_id"] = ShowcaseAttributeValue.FromText("N-100"),
                        ["link_id"] = ShowcaseAttributeValue.FromText("L-200"),
                    }),
                // Unmatched symbol_id + unmatched node_id -> non-fatal diagnostics, distinct material
                new ShowcaseFeature(
                    "ATT-ORPHAN",
                    "level:0",
                    "0",
                    new ShowcasePointGeometry(new Point2D(25, 6), 3.3d),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["symbol_id"] = ShowcaseAttributeValue.FromText("SYM-MISSING"),
                        ["node_id"] = ShowcaseAttributeValue.FromText("N-MISSING"),
                        ["link_id"] = ShowcaseAttributeValue.Null,
                    }),
            });

        ShowcaseSnapshot snapshot = ParseSnapshot(
            "poi_join",
            new[] { new ShowcaseLevel("level:0", "0", 0, "1F", 3.3d, ShowcaseElevationSource.Surveyed) },
            new[] { poiLayer, attachmentLayer, linkLayer, nodeLayer });

        ShowcasePoiAndNetworkPlanResult result = ShowcasePoiAndNetworkPlanner.Plan(snapshot, ShowcaseCoordinateTransform.Identity);

        Assert.Equal(2, result.AttachmentMarkerCount);
        Assert.Equal(1, result.MatchedAttachmentCount);
        Assert.Equal(1, result.UnmatchedAttachmentCount);

        // Matched join produces connector stems (POI->attachment + attachment->node) and teal material
        ShowcasePoiAttachmentJoinRecord matchedJoin = result.AttachmentJoins.Single(j => j.SymbolId == "SYM-001");
        Assert.Equal(ShowcaseAttachmentJoinStatus.Matched, matchedJoin.Status);
        Assert.Equal("point_facility:POI-1", matchedJoin.MatchedPoiApplicationDataId);
        Assert.Equal("N-100", matchedJoin.MatchedNodeId);
        Assert.Equal("L-200", matchedJoin.MatchedLinkId);
        Assert.True(matchedJoin.ConnectorStemLengthMeters > 2.0d);

        ShowcaseElementPlan matchedElem = result.Elements.Single(e => e.FeatureKey == "ATT-MATCHED");
        Assert.Equal(ShowcaseElementKind.PoiAttachment, matchedElem.ElementKind);
        Assert.Equal(ShowcaseMaterialCatalog.PoiAttachment, matchedElem.MaterialName);
        Assert.Equal(2, matchedElem.TubeSegments.Count); // POI stem + node tie stem

        // Unmatched join is preserved with warning material + non-fatal diagnostics
        ShowcasePoiAttachmentJoinRecord unmatchedJoin = result.AttachmentJoins.Single(j => j.SymbolId == "SYM-MISSING");
        Assert.Equal(ShowcaseAttachmentJoinStatus.UnmatchedPoi, unmatchedJoin.Status);
        Assert.Null(unmatchedJoin.MatchedPoiApplicationDataId);
        Assert.Null(unmatchedJoin.MatchedNodeId);

        ShowcaseElementPlan unmatchedElem = result.Elements.Single(e => e.FeatureKey == "ATT-ORPHAN");
        Assert.Equal(ShowcaseMaterialCatalog.PoiAttachmentUnmatched, unmatchedElem.MaterialName);
        Assert.Empty(unmatchedElem.TubeSegments);

        Assert.Contains(result.Warnings, w => w.IndexOf("SYM-MISSING", StringComparison.Ordinal) >= 0 && w.IndexOf("no matching point_facility", StringComparison.OrdinalIgnoreCase) >= 0);
        Assert.Contains(result.Warnings, w => w.IndexOf("N-MISSING", StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public void PlansHorizontalNetworkRibbonsDirectionalChevronsNetworkOverlayColorsAndDegenerateGuards()
    {
        ShowcaseLayer linkLayer = new(
            "network_link",
            ShowcaseLayerKind.NetworkLink,
            new[]
            {
                new ShowcaseField("link_type", ShowcaseFieldType.Text),
                new ShowcaseField("direction", ShowcaseFieldType.Text),
                new ShowcaseField("oneway", ShowcaseFieldType.Boolean),
                new ShowcaseField("step_free", ShowcaseFieldType.Boolean),
            },
            new[]
            {
                // One-way forward moving walkway (12m -> multiple chevrons)
                new ShowcaseFeature(
                    "L-FORWARD",
                    "level:0",
                    "0",
                    new ShowcaseLineStringGeometry(new[] { new Point2D(0, 0), new Point2D(12, 0) }),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["link_type"] = ShowcaseAttributeValue.FromText("moving_walkway"),
                        ["direction"] = ShowcaseAttributeValue.FromText("forward"),
                        ["oneway"] = ShowcaseAttributeValue.FromBoolean(true),
                        ["step_free"] = ShowcaseAttributeValue.FromBoolean(true),
                    }),
                // Reverse one-way ticket gate passage
                new ShowcaseFeature(
                    "L-REVERSE",
                    "level:0",
                    "0",
                    new ShowcaseLineStringGeometry(new[] { new Point2D(0, 4), new Point2D(6, 4) }),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["link_type"] = ShowcaseAttributeValue.FromText("ticket_gate"),
                        ["direction"] = ShowcaseAttributeValue.FromText("reverse"),
                        ["oneway"] = ShowcaseAttributeValue.FromBoolean(true),
                        ["step_free"] = ShowcaseAttributeValue.FromBoolean(true),
                    }),
                // Bidirectional non-step-free walkway (0 chevrons, orange WalkNotStepFree color)
                new ShowcaseFeature(
                    "L-BIDIR-STEPPED",
                    "level:0",
                    "0",
                    new ShowcaseLineStringGeometry(new[] { new Point2D(0, 8), new Point2D(10, 8) }),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["link_type"] = ShowcaseAttributeValue.Null,
                        ["direction"] = ShowcaseAttributeValue.FromText("both"),
                        ["oneway"] = ShowcaseAttributeValue.FromBoolean(false),
                        ["step_free"] = ShowcaseAttributeValue.FromBoolean(false),
                    }),
                // Degenerate tiny segment (0.01 m < 0.05 m min) -> skipped with warning
                new ShowcaseFeature(
                    "L-TINY",
                    "level:0",
                    "0",
                    new ShowcaseLineStringGeometry(new[] { new Point2D(0, 12), new Point2D(0.01d, 12) }),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["link_type"] = ShowcaseAttributeValue.FromText("walkway"),
                        ["direction"] = ShowcaseAttributeValue.FromText("both"),
                        ["oneway"] = ShowcaseAttributeValue.FromBoolean(false),
                        ["step_free"] = ShowcaseAttributeValue.FromBoolean(true),
                    }),
            });

        ShowcaseSnapshot snapshot = ParseSnapshot(
            "net_ribbons",
            new[] { new ShowcaseLevel("level:0", "0", 0, "1F", 5.0d, ShowcaseElevationSource.Surveyed) },
            new[] { linkLayer });

        ShowcasePoiAndNetworkPlanResult result = ShowcasePoiAndNetworkPlanner.Plan(snapshot, ShowcaseCoordinateTransform.Identity);

        Assert.Equal(3, result.HorizontalLinkCount);
        Assert.Equal(0, result.VerticalTubeCount);
        Assert.True(result.ChevronCount >= 4);
        Assert.Contains(result.Warnings, w => w.IndexOf("L-TINY", StringComparison.Ordinal) >= 0);

        // Ribbon elevation floats 0.40 m above 5.0 m TP (5.40 m) cleanly within 0.35-0.50 m target range
        ShowcaseElementPlan fwd = result.Elements.Single(e => e.FeatureKey == "L-FORWARD");
        Assert.Equal(ShowcaseElementKind.NetworkRibbon, fwd.ElementKind);
        Assert.Equal(ShowcaseMaterialCatalog.NetMovingWalkway, fwd.MaterialName);
        Assert.Equal(ShowcaseMaterialCatalog.NetChevron, fwd.AccentMaterialName);
        Assert.Single(fwd.Solids);
        Assert.Equal(3, fwd.AccentSolids.Count); // 12m / 4m = 3 chevrons
        double ribbonBaseM = ShowcaseCoordinateTransform.FeetToMeters(fwd.Solids[0].BaseElevationFeet);
        Assert.InRange(ribbonBaseM - 5.0d, 0.35d, 0.50d);
        Assert.True(fwd.AccentSolids[0].BaseElevationFeet > fwd.Solids[0].BaseElevationFeet + fwd.Solids[0].HeightFeet);

        // Verify forward chevron tip points in +X and reverse chevron tip points in -X!
        Point2D fwdTip = fwd.AccentSolids[0].OuterRingFeet.OrderByDescending(p => p.X).First();
        double fwdAvgX = fwd.AccentSolids[0].OuterRingFeet.Average(p => p.X);
        Assert.True(fwdTip.X > fwdAvgX);

        ShowcaseElementPlan rev = result.Elements.Single(e => e.FeatureKey == "L-REVERSE");
        Assert.Equal(ShowcaseMaterialCatalog.NetTicketGate, rev.MaterialName);
        Assert.Single(rev.AccentSolids);
        Point2D revTip = rev.AccentSolids[0].OuterRingFeet.OrderBy(p => p.X).First();
        double revAvgX = rev.AccentSolids[0].OuterRingFeet.Average(p => p.X);
        Assert.True(revTip.X < revAvgX);

        // Bidirectional non-step-free walkway: orange color, 0 chevrons
        ShowcaseElementPlan stepped = result.Elements.Single(e => e.FeatureKey == "L-BIDIR-STEPPED");
        Assert.Equal(ShowcaseMaterialCatalog.NetWalkNotStepFree, stepped.MaterialName);
        Assert.Empty(stepped.AccentSolids);
        ShowcaseColorRgb orange = ShowcaseMaterialCatalog.GetRequired(ShowcaseMaterialCatalog.NetWalkNotStepFree).Color;
        Assert.Equal(new ShowcaseColorRgb(230, 120, 0), orange);
    }

    [Fact]
    public void PlansVerticalTransitionTubesWithExplicitSurveyedVsSchematicProvenance()
    {
        ShowcaseLevel[] levels =
        {
            new("level:0", "0", 0, "1F", 3.3d, ShowcaseElevationSource.Surveyed),
            new("level:1", "1", 1, "2F", 7.8d, ShowcaseElevationSource.Surveyed),
            new("level:2", "2", 2, "3F", null, ShowcaseElevationSource.Estimated),
        };

        ShowcaseLayer connectorLayer = new(
            "network_connector",
            ShowcaseLayerKind.NetworkConnector,
            new[]
            {
                new ShowcaseField("connector_kind", ShowcaseFieldType.Text),
            },
            new[]
            {
                // 1. Pure vertical elevator shaft (identical XY, SurveyedPath)
                new ShowcaseFeature(
                    "CONN-EV",
                    null,
                    null,
                    new ShowcaseConnector3DGeometry(
                        new ShowcaseEndpoint("level:0", "0", new Point2D(10, 5), 3.3d),
                        new ShowcaseEndpoint("level:1", "1", new Point2D(10, 5), 7.8d),
                        new[] { new Point2D(10, 5), new Point2D(10, 5) },
                        ShowcaseConnectorGeometryOrigin.SurveyedPath),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["connector_kind"] = ShowcaseAttributeValue.FromText("elevator"),
                    }),
                // 2. Angled escalator with Schematic geometryOrigin -> explicit schematic provenance + warning!
                new ShowcaseFeature(
                    "CONN-ESC-SCHEM",
                    null,
                    null,
                    new ShowcaseConnector3DGeometry(
                        new ShowcaseEndpoint("level:0", "0", new Point2D(12, 5), 3.3d),
                        new ShowcaseEndpoint("level:1", "1", new Point2D(20, 5), 7.8d),
                        new[] { new Point2D(12, 5), new Point2D(20, 5) },
                        ShowcaseConnectorGeometryOrigin.Schematic),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["connector_kind"] = ShowcaseAttributeValue.FromText("escalator"),
                    }),
                // 3. Untyped schematic connector connecting to estimated 3F -> M_KS_Net_Schematic + warning
                new ShowcaseFeature(
                    "CONN-SCHEM-UNTYPED",
                    null,
                    null,
                    new ShowcaseConnector3DGeometry(
                        new ShowcaseEndpoint("level:1", "1", new Point2D(25, 5), 7.8d),
                        new ShowcaseEndpoint("level:2", "2", new Point2D(30, 5), null),
                        new[] { new Point2D(25, 5), new Point2D(30, 5) },
                        ShowcaseConnectorGeometryOrigin.Schematic),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["connector_kind"] = ShowcaseAttributeValue.Null,
                    }),
            });

        ShowcaseLayer verticalLinkLayer = new(
            "network_link",
            ShowcaseLayerKind.NetworkLink,
            new[]
            {
                new ShowcaseField("link_type", ShowcaseFieldType.Text),
                new ShowcaseField("start_level", ShowcaseFieldType.Text),
                new ShowcaseField("end_level", ShowcaseFieldType.Text),
            },
            new[]
            {
                // 4. 3D surveyed ramp link (explicit Z coordinates -> Surveyed)
                new ShowcaseFeature(
                    "LINK-RAMP-3D",
                    null,
                    null,
                    new ShowcaseLineStringGeometry(
                        new[] { new Point2D(0, 10), new Point2D(6, 10), new Point2D(12, 10) },
                        new double?[] { 3.3d, 5.55d, 7.8d }),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["link_type"] = ShowcaseAttributeValue.FromText("ramp"),
                        ["start_level"] = ShowcaseAttributeValue.Null,
                        ["end_level"] = ShowcaseAttributeValue.Null,
                    }),
                // 5. 2D stairs link spanning start_level..end_level -> synthesized Schematic + warning
                new ShowcaseFeature(
                    "LINK-STAIRS-LVL",
                    null,
                    null,
                    new ShowcaseLineStringGeometry(new[] { new Point2D(0, 14), new Point2D(8, 14) }),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["link_type"] = ShowcaseAttributeValue.FromText("stairs"),
                        ["start_level"] = ShowcaseAttributeValue.FromText("level:0"),
                        ["end_level"] = ShowcaseAttributeValue.FromText("level:1"),
                    }),
            });

        ShowcaseSnapshot snapshot = ParseSnapshot("vertical_tubes", levels, new[] { verticalLinkLayer, connectorLayer });
        ShowcasePoiAndNetworkPlanResult result = ShowcasePoiAndNetworkPlanner.Plan(snapshot, ShowcaseCoordinateTransform.Identity);

        Assert.Equal(5, result.VerticalTubeCount);
        Assert.Equal(0, result.HorizontalLinkCount);
        Assert.Equal(2, result.SurveyedElementCount);
        Assert.Equal(3, result.SchematicElementCount);

        ShowcaseParameterDefinition provenanceDef = result.ParameterRegistry.FindByName(ShowcaseParameterRegistry.GeometryProvenanceParameterName)!;

        // 1. Elevator shaft: pure vertical 4.5 m tube, surveyed, NetworkOverlay Elevator blue (0,110,220)
        ShowcaseElementPlan ev = result.Elements.Single(e => e.FeatureKey == "CONN-EV");
        Assert.Equal(ShowcaseElementKind.VerticalTube, ev.ElementKind);
        Assert.Equal(ShowcaseMaterialCatalog.NetElevator, ev.MaterialName);
        Assert.Equal(ShowcaseGeometryProvenance.Surveyed, ev.GeometryProvenance);
        Assert.Equal("surveyed", ev.ParameterAssignments.Single(a => a.Parameter.Guid == provenanceDef.Guid).TextValue);
        ShowcaseSweptTubeSegment evSeg = Assert.Single(ev.TubeSegments);
        Assert.InRange(Math.Abs(ShowcaseCoordinateTransform.FeetToMeters(evSeg.LengthFeet) - 4.5d), 0d, 1e-6d);
        Assert.Equal(20, evSeg.EstimatedTriangles);

        // 2. Escalator schematic: purple, slimmer schematic radius, schematic provenance + warning
        ShowcaseElementPlan esc = result.Elements.Single(e => e.FeatureKey == "CONN-ESC-SCHEM");
        Assert.Equal(ShowcaseMaterialCatalog.NetEscalator, esc.MaterialName);
        Assert.Equal(ShowcaseGeometryProvenance.Schematic, esc.GeometryProvenance);
        Assert.Equal("schematic", esc.ParameterAssignments.Single(a => a.Parameter.Guid == provenanceDef.Guid).TextValue);
        Assert.True(esc.TubeSegments[0].RadiusFeet < evSeg.RadiusFeet);
        Assert.Contains(result.Warnings, w => w.IndexOf("CONN-ESC-SCHEM", StringComparison.Ordinal) >= 0 && w.IndexOf("schematic", StringComparison.OrdinalIgnoreCase) >= 0);

        // 3. Untyped schematic connector -> M_KS_Net_Schematic + synthesized 3F elevation (7.8 + 4.5 = 12.3m + 0.40m)
        ShowcaseElementPlan untyped = result.Elements.Single(e => e.FeatureKey == "CONN-SCHEM-UNTYPED");
        Assert.Equal(ShowcaseMaterialCatalog.NetSchematic, untyped.MaterialName);
        Assert.Equal(ShowcaseGeometryProvenance.Schematic, untyped.GeometryProvenance);
        double topMeters = ShowcaseCoordinateTransform.FeetToMeters(untyped.TubeSegments[0].EndFeet.ZFeet);
        Assert.InRange(Math.Abs(topMeters - (12.3d + 0.40d)), 0d, 1e-6d);

        // 4. 3D surveyed ramp link: 2 segments, amber Ramp color, surveyed
        ShowcaseElementPlan ramp = result.Elements.Single(e => e.FeatureKey == "LINK-RAMP-3D");
        Assert.Equal(ShowcaseElementKind.VerticalTube, ramp.ElementKind);
        Assert.Equal(ShowcaseMaterialCatalog.NetRamp, ramp.MaterialName);
        Assert.Equal(ShowcaseGeometryProvenance.Surveyed, ramp.GeometryProvenance);
        Assert.Equal(2, ramp.TubeSegments.Count);

        // 5. Level-derived stairs link: brown Stairs color, schematic + warning
        ShowcaseElementPlan stairs = result.Elements.Single(e => e.FeatureKey == "LINK-STAIRS-LVL");
        Assert.Equal(ShowcaseElementKind.VerticalTube, stairs.ElementKind);
        Assert.Equal(ShowcaseMaterialCatalog.NetStairs, stairs.MaterialName);
        Assert.Equal(ShowcaseGeometryProvenance.Schematic, stairs.GeometryProvenance);
        Assert.Contains(result.Warnings, w => w.IndexOf("LINK-STAIRS-LVL", StringComparison.Ordinal) >= 0 && w.IndexOf("schematic", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void PlansRedNetJunctionFindingPucksAndStandardNetworkNodesAndHandlesMissingLevelsGracefully()
    {
        ShowcaseLayer netJunctionLayer = new(
            "net_junction",
            ShowcaseLayerKind.NetworkNode,
            new[]
            {
                new ShowcaseField("finding_kind", ShowcaseFieldType.Text),
                new ShowcaseField("severity", ShowcaseFieldType.Text),
                new ShowcaseField("detail", ShowcaseFieldType.Text),
            },
            new[]
            {
                new ShowcaseFeature(
                    "J-DEADEND",
                    "level:0",
                    "0",
                    new ShowcasePointGeometry(new Point2D(5, 5), 3.3d),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["finding_kind"] = ShowcaseAttributeValue.FromText("dead_end"),
                        ["severity"] = ShowcaseAttributeValue.FromText("warning"),
                        ["detail"] = ShowcaseAttributeValue.FromText("Walkway terminates at dead end."),
                    }),
                // Missing levelId/floorKey & no Z -> non-fatal warning + schematic fallback at 0.0m + offset
                new ShowcaseFeature(
                    "J-BROKEN-VERT",
                    null,
                    null,
                    new ShowcasePointGeometry(new Point2D(8, 5)),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["finding_kind"] = ShowcaseAttributeValue.FromText("broken_vertical"),
                        ["severity"] = ShowcaseAttributeValue.FromText("error"),
                        ["detail"] = ShowcaseAttributeValue.FromText("Vertical connector disconnected."),
                    }),
            });

        ShowcaseLayer networkNodeLayer = new(
            "network_node",
            ShowcaseLayerKind.NetworkNode,
            new[]
            {
                new ShowcaseField("node_id", ShowcaseFieldType.Text),
                new ShowcaseField("node_type", ShowcaseFieldType.Text),
                new ShowcaseField("severity", ShowcaseFieldType.Text),
            },
            new[]
            {
                // Ordinary junction node -> neutral gray NetworkNode
                new ShowcaseFeature(
                    "N-OK",
                    "level:0",
                    "0",
                    new ShowcasePointGeometry(new Point2D(12, 5), 3.3d),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["node_id"] = ShowcaseAttributeValue.FromText("N-OK"),
                        ["node_type"] = ShowcaseAttributeValue.FromText("corridor_junction"),
                        ["severity"] = ShowcaseAttributeValue.Null,
                    }),
                // Disconnected node inside network_node layer with info severity -> FindingPuck with M_KS_Finding_Info
                new ShowcaseFeature(
                    "N-INFO-FINDING",
                    "level:0",
                    "0",
                    new ShowcasePointGeometry(new Point2D(15, 5), 3.3d),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["node_id"] = ShowcaseAttributeValue.FromText("N-INFO-FINDING"),
                        ["node_type"] = ShowcaseAttributeValue.FromText("disconnected"),
                        ["severity"] = ShowcaseAttributeValue.FromText("info"),
                    }),
            });

        ShowcaseSnapshot snapshot = ParseSnapshot(
            "findings_and_nodes",
            new[] { new ShowcaseLevel("level:0", "0", 0, "1F", 3.3d, ShowcaseElevationSource.Surveyed) },
            new[] { netJunctionLayer, networkNodeLayer });

        ShowcasePoiAndNetworkPlanResult result = ShowcasePoiAndNetworkPlanner.Plan(snapshot, ShowcaseCoordinateTransform.Identity);

        Assert.Equal(3, result.FindingPuckCount);
        Assert.Equal(1, result.NetworkNodeCount);

        ShowcaseElementPlan deadEnd = result.Elements.Single(e => e.FeatureKey == "J-DEADEND");
        Assert.Equal(ShowcaseElementKind.FindingPuck, deadEnd.ElementKind);
        Assert.StartsWith("KNZ_FRM_NetJunction_", deadEnd.ElementName, StringComparison.Ordinal);
        Assert.Equal(ShowcaseMaterialCatalog.FindingAlert, deadEnd.MaterialName);
        Assert.Equal(new ShowcaseColorRgb(220, 30, 30), ShowcaseMaterialCatalog.GetRequired(ShowcaseMaterialCatalog.FindingAlert).Color);
        Assert.Equal(40, deadEnd.MarkerPlacements[0].EstimatedTriangles);
        Assert.True(deadEnd.MarkerPlacements[0].EstimatedTriangles <= ShowcaseMarkerPlacement.MaxAllowedMarkerTriangles);

        ShowcaseElementPlan brokenVert = result.Elements.Single(e => e.FeatureKey == "J-BROKEN-VERT");
        Assert.Equal(ShowcaseElementKind.FindingPuck, brokenVert.ElementKind);
        Assert.Equal(ShowcaseGeometryProvenance.Schematic, brokenVert.GeometryProvenance);
        Assert.Contains(result.Warnings, w => w.IndexOf("J-BROKEN-VERT", StringComparison.Ordinal) >= 0 && w.IndexOf("falling back to 0.0 m", StringComparison.OrdinalIgnoreCase) >= 0);

        ShowcaseElementPlan infoFinding = result.Elements.Single(e => e.FeatureKey == "N-INFO-FINDING");
        Assert.Equal(ShowcaseElementKind.FindingPuck, infoFinding.ElementKind);
        Assert.Equal(ShowcaseMaterialCatalog.FindingInfo, infoFinding.MaterialName);

        ShowcaseElementPlan okNode = result.Elements.Single(e => e.FeatureKey == "N-OK");
        Assert.Equal(ShowcaseElementKind.NetworkNode, okNode.ElementKind);
        Assert.StartsWith("KNZ_FRM_NetNode_", okNode.ElementName, StringComparison.Ordinal);
        Assert.Equal(ShowcaseMaterialCatalog.NetNode, okNode.MaterialName);
    }

    [Fact]
    public void ReconcilesPoiNetworkAndFindingElementsAcrossAllReconciliationActionsAndPassesExportGuards()
    {
        ShowcaseSnapshot snapshot = ShowcaseSnapshotReader.Parse(ShowcaseSnapshotReaderTests.BuildValidFullSnapshotJson());
        ShowcasePoiAndNetworkPlanResult plan = ShowcasePoiAndNetworkPlanner.Plan(snapshot, ShowcaseCoordinateTransform.Identity);

        // Full snapshot contains 5 F4 features:
        //   point_facility:SYM-001
        //   point_facility_network:SYM-001
        //   network_link:L-001
        //   network_connector:C-001
        //   network_node:N-001
        Assert.Equal(5, plan.Elements.Count);

        // EVERY F4 element must trigger ALL F2 ShowcaseExportGuard defense-in-depth markers!
        foreach (ShowcaseElementPlan elem in plan.Elements)
        {
            Assert.True(ShowcaseExportGuard.IsShowcaseElement(elem.ToGuardDescriptor(ShowcaseExportGuard.ShowcaseModelRole)));
            Assert.True(ShowcaseExportGuard.IsShowcaseElement(elem.ToGuardDescriptor(sourceDocumentModelRole: null)));
            Assert.True(ShowcaseExportGuard.IsShowcaseElement(applicationId: elem.ApplicationId));
            Assert.True(ShowcaseExportGuard.IsShowcaseElement(typeName: elem.ElementName));
            Assert.True(ShowcaseExportGuard.IsShowcaseElement(
                parameterNames: elem.ParameterAssignments.Select(a => a.Parameter.Name)));
            Assert.True(elem.EstimatedTriangleCount > 0);
        }

        ShowcaseElementPlan poiPlan = plan.Elements.Single(e => e.ApplicationDataId == "point_facility:SYM-001");
        ShowcaseElementPlan linkPlan = plan.Elements.Single(e => e.ApplicationDataId == "network_link:L-001");
        Guid[] poiPopulatedGuids = poiPlan.ParameterAssignments
            .Where(a => !a.IsNull && !a.IsTextFallback)
            .Select(a => a.Parameter.Guid)
            .ToArray();
        Guid removedOldParamGuid = ShowcaseParameterRegistry.ComputeParameterGuid("shared", "obsolete_col", ShowcaseParameterDataType.Text);

        ShowcaseExistingElementInfo[] existing =
        {
            // POI unchanged category + exact populated GUIDs -> UpdateInPlace
            new(3001L, "point_facility:SYM-001", "OST_GenericModel", poiPopulatedGuids),
            // Attachment category changed (was OST_SpecialityEquipment, now OST_GenericModel) -> ReplaceCategoryChanged
            new(3002L, "point_facility_network:SYM-001", "OST_SpecialityEquipment"),
            // Network link had an obsolete ks_* parameter now removed -> ReplaceParametersCleared
            new(3003L, "network_link:L-001", "OST_GenericModel", linkPlan.ParameterAssignments.Where(a => !a.IsNull).Select(a => a.Parameter.Guid).Concat(new[] { removedOldParamGuid })),
            // Stale deleted finding puck -> DeleteStale
            new(3004L, "net_junction:J-OLD", "OST_GenericModel"),
            // network_connector:C-001 and network_node:N-001 are new -> Create
        };

        ShowcaseReconciliationPlan rec = ShowcaseElementReconciler.BuildPlan(plan.Elements, existing);

        Assert.Equal(2, rec.CreateCount);
        Assert.Equal(1, rec.UpdateInPlaceCount);
        Assert.Equal(2, rec.ReplaceCount);
        Assert.Equal(1, rec.DeleteStaleCount);

        Assert.Equal(ShowcaseReconciliationActionKind.UpdateInPlace, rec.Steps.Single(s => s.ApplicationDataId == "point_facility:SYM-001").Action);
        Assert.Equal(ShowcaseReconciliationActionKind.ReplaceCategoryChanged, rec.Steps.Single(s => s.ApplicationDataId == "point_facility_network:SYM-001").Action);
        ShowcaseReconciliationStep clearedStep = rec.Steps.Single(s => s.ApplicationDataId == "network_link:L-001");
        Assert.Equal(ShowcaseReconciliationActionKind.ReplaceParametersCleared, clearedStep.Action);
        Assert.Contains(removedOldParamGuid, clearedStep.ClearedParameterGuids);
        Assert.Equal(ShowcaseReconciliationActionKind.DeleteStale, rec.Steps.Single(s => s.ApplicationDataId == "net_junction:J-OLD").Action);
    }

    [Fact]
    public void SnapshotReaderIngests3DLineStringsMultiLineStringsAndMultiPointsWithValidContentHash()
    {
        ShowcaseLayer multiPtPoiLayer = new(
            "point_facility",
            ShowcaseLayerKind.PointFacility,
            new[] { new ShowcaseField("symbol_id", ShowcaseFieldType.Text) },
            new[]
            {
                new ShowcaseFeature(
                    "MP-001",
                    "level:0",
                    "0",
                    new ShowcaseMultiPointGeometry(new[]
                    {
                        new ShowcasePointGeometry(new Point2D(1, 2), 3.3d),
                        new ShowcasePointGeometry(new Point2D(4, 2), 3.3d),
                    }),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["symbol_id"] = ShowcaseAttributeValue.FromText("MP-001"),
                    }),
            });

        ShowcaseLayer multiLine3DLayer = new(
            "network_link",
            ShowcaseLayerKind.NetworkLink,
            new[] { new ShowcaseField("link_type", ShowcaseFieldType.Text) },
            new[]
            {
                new ShowcaseFeature(
                    "ML-3D",
                    null,
                    null,
                    new ShowcaseMultiLineStringGeometry(
                        new IReadOnlyList<Point2D>[]
                        {
                            new[] { new Point2D(0, 0), new Point2D(5, 0) },
                            new[] { new Point2D(5, 0), new Point2D(10, 0) },
                        },
                        new IReadOnlyList<double?>?[]
                        {
                            new double?[] { 3.3d, 5.5d },
                            new double?[] { 5.5d, 7.8d },
                        }),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["link_type"] = ShowcaseAttributeValue.FromText("ramp"),
                    }),
            });

        ShowcaseSnapshot draft = new(
            ShowcaseContract.Schema,
            ShowcaseContract.Version,
            "3d_geom_test",
            basedOn: null,
            writtenUtc: "2026-11-27T00:00:00Z",
            kanizu: "0.9.0",
            epsg: 6677,
            contentHash: string.Empty,
            levels: new[] { new ShowcaseLevel("level:0", "0", 0, "1F", 3.3d, ShowcaseElevationSource.Surveyed) },
            layers: new[] { multiPtPoiLayer, multiLine3DLayer },
            quarantined: Array.Empty<QuarantinedShowcaseFeature>(),
            summary: ShowcaseSnapshotReader.ComputeSummary(new[] { multiPtPoiLayer, multiLine3DLayer }, Array.Empty<QuarantinedShowcaseFeature>()));

        string expectedHash = ShowcaseSnapshotReader.ComputeContentHash(draft);

        string json = $$"""
        {
          "schema": "kanizu.gis-showcase",
          "version": 1,
          "scope": "3d_geom_test",
          "writtenUtc": "2026-11-27T00:00:00Z",
          "kanizu": "0.9.0",
          "epsg": 6677,
          "contentHash": "{{expectedHash}}",
          "levels": [
            { "levelId": "level:0", "floorKey": "0", "ordinal": 0, "shortName": "1F", "tpM": 3.3, "elevationSource": "surveyed" }
          ],
          "layers": [
            {
              "name": "point_facility",
              "kind": "pointFacility",
              "fields": [ { "name": "symbol_id", "fieldType": "text", "preserveAsText": false } ],
              "features": [
                {
                  "key": "MP-001",
                  "levelId": "level:0",
                  "floorKey": "0",
                  "geometry": { "type": "multiPoint", "points": [ [1.0, 2.0, 3.3], [4.0, 2.0, 3.3] ] },
                  "attributes": { "symbol_id": "MP-001" }
                }
              ]
            },
            {
              "name": "network_link",
              "kind": "networkLink",
              "fields": [ { "name": "link_type", "fieldType": "text", "preserveAsText": false } ],
              "features": [
                {
                  "key": "ML-3D",
                  "geometry": {
                    "type": "multiLineString",
                    "lines": [
                      [ [0.0, 0.0, 3.3], [5.0, 0.0, 5.5] ],
                      [ [5.0, 0.0, 5.5], [10.0, 0.0, 7.8] ]
                    ]
                  },
                  "attributes": { "link_type": "ramp" }
                }
              ]
            }
          ],
          "quarantined": [],
          "summary": {
            "countsByKind": { "pointFacility": 1, "networkLink": 1 },
            "totalLayers": 2,
            "totalFeatures": 2,
            "totalQuarantined": 0
          }
        }
        """;

        ShowcaseSnapshot parsed = ShowcaseSnapshotReader.Parse(json);
        ShowcaseMultiPointGeometry parsedMp = Assert.IsType<ShowcaseMultiPointGeometry>(parsed.Layers[0].Features[0].Geometry);
        Assert.Equal(2, parsedMp.Points.Count);
        Assert.Equal(3.3d, parsedMp.Points[1].Z);

        ShowcaseMultiLineStringGeometry parsedMl = Assert.IsType<ShowcaseMultiLineStringGeometry>(parsed.Layers[1].Features[0].Geometry);
        Assert.True(parsedMl.HasZCoordinates);
        Assert.Equal(7.8d, parsedMl.LineZs![1]![1]);

        // Plan them through F4 planner: MultiPoint produces 2 instanced markers (sharing 1 prototype!); 3D MultiLine produces 2 vertical ramp tube segments!
        ShowcasePoiAndNetworkPlanResult planned = ShowcasePoiAndNetworkPlanner.Plan(parsed, ShowcaseCoordinateTransform.Identity);
        Assert.Empty(planned.Warnings);
        Assert.Equal(1, planned.PoiMarkerCount);
        Assert.Equal(1, planned.VerticalTubeCount);

        ShowcaseElementPlan mpElem = planned.Elements.Single(e => e.FeatureKey == "MP-001");
        Assert.Equal(2, mpElem.MarkerPlacements.Count);
        Assert.Equal(88, mpElem.EstimatedTriangleCount);
        Assert.Equal(44, planned.UniqueInstancedPrototypeTriangles); // 1 shared prototype for both markers!

        ShowcaseElementPlan mlElem = planned.Elements.Single(e => e.FeatureKey == "ML-3D");
        Assert.Equal(ShowcaseElementKind.VerticalTube, mlElem.ElementKind);
        Assert.Equal(ShowcaseGeometryProvenance.Surveyed, mlElem.GeometryProvenance);
        Assert.Equal(2, mlElem.TubeSegments.Count);
        Assert.Equal(40, mlElem.EstimatedTriangleCount);
    }

    [Fact]
    public void SnapshotReaderCanonicalHashMatchesForEmbeddedZPointAndNullZCoordinates()
    {
        // Build the canonical model first to compute ComputeContentHash(ShowcaseSnapshot), then verify that
        // ShowcaseSnapshotReader.Parse accepts raw JSON using embedded 3-element point.xy ([x, y, z]) AND
        // [x, y, null] coordinates in lineString, multiLineString, and multiPoint with VerifyContentHash = true!
        ShowcaseLayer poiLayer = new(
            "point_facility",
            ShowcaseLayerKind.PointFacility,
            new[] { new ShowcaseField("symbol_id", ShowcaseFieldType.Text) },
            new[]
            {
                new ShowcaseFeature(
                    "PT-3D",
                    "level:0",
                    "0",
                    new ShowcasePointGeometry(new Point2D(10, 20), 3.3d),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal) { ["symbol_id"] = ShowcaseAttributeValue.FromText("PT-3D") }),
                new ShowcaseFeature(
                    "PT-NULLZ",
                    "level:0",
                    "0",
                    new ShowcasePointGeometry(new Point2D(12, 22), null),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal) { ["symbol_id"] = ShowcaseAttributeValue.FromText("PT-NULLZ") }),
                new ShowcaseFeature(
                    "MPT-MIX",
                    "level:0",
                    "0",
                    new ShowcaseMultiPointGeometry(new[]
                    {
                        new ShowcasePointGeometry(new Point2D(1, 2), null),
                        new ShowcasePointGeometry(new Point2D(3, 4), 3.3d),
                    }),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal) { ["symbol_id"] = ShowcaseAttributeValue.FromText("MPT-MIX") }),
            });

        ShowcaseLayer linkLayer = new(
            "network_link",
            ShowcaseLayerKind.NetworkLink,
            new[] { new ShowcaseField("link_type", ShowcaseFieldType.Text) },
            new[]
            {
                new ShowcaseFeature(
                    "LS-NULLZ",
                    "level:0",
                    "0",
                    new ShowcaseLineStringGeometry(
                        new[] { new Point2D(0, 0), new Point2D(10, 0) },
                        new double?[] { null, 3.3d }),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal) { ["link_type"] = ShowcaseAttributeValue.FromText("walkway") }),
                new ShowcaseFeature(
                    "MLS-NULLZ",
                    "level:0",
                    "0",
                    new ShowcaseMultiLineStringGeometry(
                        new IReadOnlyList<Point2D>[]
                        {
                            new[] { new Point2D(0, 5), new Point2D(5, 5) },
                            new[] { new Point2D(5, 5), new Point2D(10, 5) },
                        },
                        new IReadOnlyList<double?>?[]
                        {
                            null,
                            new double?[] { 3.3d, null },
                        }),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal) { ["link_type"] = ShowcaseAttributeValue.FromText("walkway") }),
            });

        ShowcaseLayer[] layers = { poiLayer, linkLayer };
        ShowcaseSnapshot modelDraft = new(
            ShowcaseContract.Schema,
            ShowcaseContract.Version,
            "raw_3d_hash_parity",
            basedOn: null,
            writtenUtc: "2026-11-27T00:00:00Z",
            kanizu: "0.9.0",
            epsg: 6677,
            contentHash: string.Empty,
            levels: new[] { new ShowcaseLevel("level:0", "0", 0, "1F", 3.3d, ShowcaseElevationSource.Surveyed) },
            layers: layers,
            quarantined: Array.Empty<QuarantinedShowcaseFeature>(),
            summary: ShowcaseSnapshotReader.ComputeSummary(layers, Array.Empty<QuarantinedShowcaseFeature>()));

        string expectedModelHash = ShowcaseSnapshotReader.ComputeContentHash(modelDraft);

        string rawJson = $$"""
        {
          "schema": "kanizu.gis-showcase",
          "version": 1,
          "scope": "raw_3d_hash_parity",
          "writtenUtc": "2026-11-27T00:00:00Z",
          "kanizu": "0.9.0",
          "epsg": 6677,
          "contentHash": "{{expectedModelHash}}",
          "levels": [
            { "levelId": "level:0", "floorKey": "0", "ordinal": 0, "shortName": "1F", "tpM": 3.3, "elevationSource": "surveyed" }
          ],
          "layers": [
            {
              "name": "point_facility",
              "kind": "pointFacility",
              "fields": [ { "name": "symbol_id", "fieldType": "text", "preserveAsText": false } ],
              "features": [
                {
                  "key": "PT-3D",
                  "levelId": "level:0",
                  "floorKey": "0",
                  "geometry": { "type": "point", "xy": [10.0, 20.0, 3.3] },
                  "attributes": { "symbol_id": "PT-3D" }
                },
                {
                  "key": "PT-NULLZ",
                  "levelId": "level:0",
                  "floorKey": "0",
                  "geometry": { "type": "point", "xy": [12.0, 22.0, null] },
                  "attributes": { "symbol_id": "PT-NULLZ" }
                },
                {
                  "key": "MPT-MIX",
                  "levelId": "level:0",
                  "floorKey": "0",
                  "geometry": { "type": "multiPoint", "points": [ [1.0, 2.0, null], [3.0, 4.0, 3.3] ] },
                  "attributes": { "symbol_id": "MPT-MIX" }
                }
              ]
            },
            {
              "name": "network_link",
              "kind": "networkLink",
              "fields": [ { "name": "link_type", "fieldType": "text", "preserveAsText": false } ],
              "features": [
                {
                  "key": "LS-NULLZ",
                  "levelId": "level:0",
                  "floorKey": "0",
                  "geometry": { "type": "lineString", "coords": [ [0.0, 0.0, null], [10.0, 0.0, 3.3] ] },
                  "attributes": { "link_type": "walkway" }
                },
                {
                  "key": "MLS-NULLZ",
                  "levelId": "level:0",
                  "floorKey": "0",
                  "geometry": {
                    "type": "multiLineString",
                    "lines": [
                      [ [0.0, 5.0, null], [5.0, 5.0, null] ],
                      [ [5.0, 5.0, 3.3], [10.0, 5.0, null] ]
                    ]
                  },
                  "attributes": { "link_type": "walkway" }
                }
              ]
            }
          ],
          "quarantined": [],
          "summary": {
            "countsByKind": { "pointFacility": 3, "networkLink": 2 },
            "totalLayers": 2,
            "totalFeatures": 5,
            "totalQuarantined": 0
          }
        }
        """;

        ShowcaseSnapshot parsed = ShowcaseSnapshotReader.Parse(rawJson);
        Assert.Equal(expectedModelHash, parsed.ContentHash);
        Assert.Equal(expectedModelHash, ShowcaseSnapshotReader.ComputeContentHash(parsed));

        ShowcasePointGeometry pt3d = Assert.IsType<ShowcasePointGeometry>(parsed.Layers[0].Features[0].Geometry);
        Assert.Equal(3.3d, pt3d.Z);
        ShowcasePointGeometry ptNullZ = Assert.IsType<ShowcasePointGeometry>(parsed.Layers[0].Features[1].Geometry);
        Assert.Null(ptNullZ.Z);
    }

    [Fact]
    public void Mixed2DAnd3DMultiLineStringVerticalLinkUsesResolvedElevationsInsteadOfZeroAndMarksSchematic()
    {
        ShowcaseLevel[] levels =
        {
            new("level:0", "0", 0, "1F", 10.0d, ShowcaseElevationSource.Surveyed),
            new("level:1", "1", 1, "2F", 14.5d, ShowcaseElevationSource.Surveyed),
        };

        ShowcaseLayer mixedLayer = new(
            "network_link",
            ShowcaseLayerKind.NetworkLink,
            new[]
            {
                new ShowcaseField("link_type", ShowcaseFieldType.Text),
                new ShowcaseField("start_level", ShowcaseFieldType.Text),
                new ShowcaseField("end_level", ShowcaseFieldType.Text),
            },
            new[]
            {
                // Mixed 2D/3D MultiLineString WITH start_level/end_level:
                // Part 0 has surveyed 3D Z (10.0 -> 14.5m); Part 1 is 2D (zs == null) and must interpolate 10.0 -> 14.5m (NOT 0.0m!)
                new ShowcaseFeature(
                    "MIXED-WITH-LEVELS",
                    "level:0",
                    "0",
                    new ShowcaseMultiLineStringGeometry(
                        new IReadOnlyList<Point2D>[]
                        {
                            new[] { new Point2D(0, 0), new Point2D(6, 0) },
                            new[] { new Point2D(6, 0), new Point2D(12, 0) },
                        },
                        new IReadOnlyList<double?>?[]
                        {
                            new double?[] { 10.0d, 14.5d },
                            null,
                        }),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["link_type"] = ShowcaseAttributeValue.FromText("ramp"),
                        ["start_level"] = ShowcaseAttributeValue.FromText("level:0"),
                        ["end_level"] = ShowcaseAttributeValue.FromText("level:1"),
                    }),
                // Mixed 2D/3D MultiLineString WITHOUT start_level/end_level:
                // Part 1 (2D) must fall back to feature's level (10.0m) or surveyed minZ, NEVER 0.0m!
                new ShowcaseFeature(
                    "MIXED-NO-SPAN",
                    "level:0",
                    "0",
                    new ShowcaseMultiLineStringGeometry(
                        new IReadOnlyList<Point2D>[]
                        {
                            new[] { new Point2D(0, 4), new Point2D(6, 4) },
                            new[] { new Point2D(6, 4), new Point2D(12, 4) },
                        },
                        new IReadOnlyList<double?>?[]
                        {
                            new double?[] { 10.0d, 14.5d },
                            null,
                        }),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["link_type"] = ShowcaseAttributeValue.FromText("stairs"),
                        ["start_level"] = ShowcaseAttributeValue.Null,
                        ["end_level"] = ShowcaseAttributeValue.Null,
                    }),
            });

        ShowcaseSnapshot snapshot = ParseSnapshot("mixed_2d_3d_vertical", levels, new[] { mixedLayer });
        ShowcasePoiAndNetworkPlanResult result = ShowcasePoiAndNetworkPlanner.Plan(snapshot, ShowcaseCoordinateTransform.Identity);

        Assert.Equal(2, result.VerticalTubeCount);

        ShowcaseElementPlan withLevels = result.Elements.Single(e => e.FeatureKey == "MIXED-WITH-LEVELS");
        Assert.Equal(ShowcaseGeometryProvenance.Schematic, withLevels.GeometryProvenance);
        Assert.Equal(2, withLevels.TubeSegments.Count);
        Assert.InRange(Math.Abs(ShowcaseCoordinateTransform.FeetToMeters(withLevels.TubeSegments[1].StartFeet.ZFeet) - 10.40d), 0d, 1e-6d);
        Assert.InRange(Math.Abs(ShowcaseCoordinateTransform.FeetToMeters(withLevels.TubeSegments[1].EndFeet.ZFeet) - 14.90d), 0d, 1e-6d);
        Assert.Contains(result.Warnings, w => w.IndexOf("MIXED-WITH-LEVELS", StringComparison.Ordinal) >= 0 && w.IndexOf("schematic", StringComparison.OrdinalIgnoreCase) >= 0);

        ShowcaseElementPlan noSpan = result.Elements.Single(e => e.FeatureKey == "MIXED-NO-SPAN");
        Assert.Equal(ShowcaseGeometryProvenance.Schematic, noSpan.GeometryProvenance);
        Assert.Equal(2, noSpan.TubeSegments.Count);
        Assert.InRange(Math.Abs(ShowcaseCoordinateTransform.FeetToMeters(noSpan.TubeSegments[1].StartFeet.ZFeet) - 10.40d), 0d, 1e-6d);
        Assert.InRange(Math.Abs(ShowcaseCoordinateTransform.FeetToMeters(noSpan.TubeSegments[1].EndFeet.ZFeet) - 10.40d), 0d, 1e-6d);
    }

    [Fact]
    public void PoiMarkerTempFamilyPathUsesDeterministicFamilyFilenameWithoutGuidSuffix()
    {
        string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KanizuShowcase_testdir_1234");
        string rfaPath = ShowcasePoiAndNetworkPlanner.BuildPoiMarkerTempFamilyPath(tempDir);

        Assert.Equal(ShowcasePoiAndNetworkPlanner.PoiMarkerFamilyName + ".rfa", System.IO.Path.GetFileName(rfaPath));
        Assert.Equal(ShowcasePoiAndNetworkPlanner.PoiMarkerFamilyName, System.IO.Path.GetFileNameWithoutExtension(rfaPath));
        Assert.Equal(tempDir, System.IO.Path.GetDirectoryName(rfaPath));
    }

    [Fact]
    public void WalkwayWithStepFreeFalseRendersOrangeNetWalkNotStepFree()
    {
        SortedDictionary<string, ShowcaseAttributeValue> walkwayNotStepFree = new(StringComparer.Ordinal)
        {
            ["link_type"] = ShowcaseAttributeValue.FromText("walkway"),
            ["step_free"] = ShowcaseAttributeValue.FromBoolean(false),
        };
        SortedDictionary<string, ShowcaseAttributeValue> corridorNoWheelchair = new(StringComparer.Ordinal)
        {
            ["link_type"] = ShowcaseAttributeValue.FromText("corridor"),
            ["wheelchair"] = ShowcaseAttributeValue.FromText("no"),
        };
        SortedDictionary<string, ShowcaseAttributeValue> walkwayStepFree = new(StringComparer.Ordinal)
        {
            ["link_type"] = ShowcaseAttributeValue.FromText("walkway"),
            ["step_free"] = ShowcaseAttributeValue.FromBoolean(true),
        };

        Assert.Equal(
            ShowcaseMaterialCatalog.NetWalkNotStepFree,
            ShowcaseMaterialCatalog.ResolveNetworkLinkMaterialName(walkwayNotStepFree, ShowcaseGeometryProvenance.Surveyed));
        Assert.Equal(
            ShowcaseMaterialCatalog.NetWalkNotStepFree,
            ShowcaseMaterialCatalog.ResolveNetworkLinkMaterialName(corridorNoWheelchair, ShowcaseGeometryProvenance.Surveyed));
        Assert.Equal(
            ShowcaseMaterialCatalog.NetWalk,
            ShowcaseMaterialCatalog.ResolveNetworkLinkMaterialName(walkwayStepFree, ShowcaseGeometryProvenance.Surveyed));
    }

    [Fact]
    public void SymbolIdJoinPreventsFeatureKeyShadowingWarnsOnDuplicatesAndHandlesAllMultiPointPlacements()
    {
        ShowcaseLayer poiLayer = new(
            "point_facility",
            ShowcaseLayerKind.PointFacility,
            new[] { new ShowcaseField("symbol_id", ShowcaseFieldType.Text) },
            new[]
            {
                // POI A's feature.Key ("SYM-2") matches POI B's explicit symbol_id ("SYM-2"): must NOT shadow POI B!
                new ShowcaseFeature(
                    "SYM-2",
                    "level:0",
                    "0",
                    new ShowcasePointGeometry(new Point2D(0, 0), 3.3d),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal) { ["symbol_id"] = ShowcaseAttributeValue.FromText("SYM-1") }),
                new ShowcaseFeature(
                    "POI-B",
                    "level:0",
                    "0",
                    new ShowcasePointGeometry(new Point2D(10, 0), 3.3d),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal) { ["symbol_id"] = ShowcaseAttributeValue.FromText("SYM-2") }),
                // Duplicate explicit symbol_id ("SYM-DUP") on two distinct POI features -> warning + nearest match
                new ShowcaseFeature(
                    "DUP-1",
                    "level:0",
                    "0",
                    new ShowcasePointGeometry(new Point2D(0, 20), 3.3d),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal) { ["symbol_id"] = ShowcaseAttributeValue.FromText("SYM-DUP") }),
                new ShowcaseFeature(
                    "DUP-2",
                    "level:0",
                    "0",
                    new ShowcasePointGeometry(new Point2D(30, 20), 3.3d),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal) { ["symbol_id"] = ShowcaseAttributeValue.FromText("SYM-DUP") }),
                // MultiPoint POI at x=10 and x=40
                new ShowcaseFeature(
                    "MP-POI",
                    "level:0",
                    "0",
                    new ShowcaseMultiPointGeometry(new[]
                    {
                        new ShowcasePointGeometry(new Point2D(10, 40), 3.3d),
                        new ShowcasePointGeometry(new Point2D(40, 40), 3.3d),
                    }),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal) { ["symbol_id"] = ShowcaseAttributeValue.FromText("SYM-MP") }),
            });

        ShowcaseLayer attachLayer = new(
            "point_facility_network",
            ShowcaseLayerKind.PointFacilityNetwork,
            new[] { new ShowcaseField("symbol_id", ShowcaseFieldType.Text) },
            new[]
            {
                // Must match POI-B (explicit symbol_id "SYM-2" at x=10), NOT feature.Key "SYM-2" at x=0!
                new ShowcaseFeature(
                    "ATT-SHADOW",
                    "level:0",
                    "0",
                    new ShowcasePointGeometry(new Point2D(11, 0), 3.3d),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal) { ["symbol_id"] = ShowcaseAttributeValue.FromText("SYM-2") }),
                // Near DUP-2 (x=30) -> must connect to DUP-2 (~1m), not DUP-1 (30m away)!
                new ShowcaseFeature(
                    "ATT-DUP",
                    "level:0",
                    "0",
                    new ShowcasePointGeometry(new Point2D(31, 20), 3.3d),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal) { ["symbol_id"] = ShowcaseAttributeValue.FromText("SYM-DUP") }),
                // MultiPoint attachment (at x=11 and x=39) -> must emit TWO short ~1m stems to x=10 and x=40!
                new ShowcaseFeature(
                    "ATT-MP",
                    "level:0",
                    "0",
                    new ShowcaseMultiPointGeometry(new[]
                    {
                        new ShowcasePointGeometry(new Point2D(11, 40), 3.3d),
                        new ShowcasePointGeometry(new Point2D(39, 40), 3.3d),
                    }),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal) { ["symbol_id"] = ShowcaseAttributeValue.FromText("SYM-MP") }),
            });

        ShowcaseSnapshot snapshot = ParseSnapshot(
            "poi_join_edge_cases",
            new[] { new ShowcaseLevel("level:0", "0", 0, "1F", 3.3d, ShowcaseElevationSource.Surveyed) },
            new[] { poiLayer, attachLayer });

        ShowcasePoiAndNetworkPlanResult result = ShowcasePoiAndNetworkPlanner.Plan(snapshot, ShowcaseCoordinateTransform.Identity);

        // 1. Explicit symbol_id beats feature.Key shadowing
        ShowcasePoiAttachmentJoinRecord shadowJoin = result.AttachmentJoins.Single(j => j.AttachmentApplicationDataId == "point_facility_network:ATT-SHADOW");
        Assert.Equal(ShowcaseAttachmentJoinStatus.Matched, shadowJoin.Status);
        Assert.Equal("point_facility:POI-B", shadowJoin.MatchedPoiApplicationDataId);
        Assert.True(shadowJoin.ConnectorStemLengthMeters < 2.0d);

        // 2. Duplicate symbol_id warns AND picks nearest candidate (DUP-2)
        Assert.Contains(result.Warnings, w => w.IndexOf("Duplicate point_facility symbol_id 'SYM-DUP'", StringComparison.Ordinal) >= 0);
        ShowcasePoiAttachmentJoinRecord dupJoin = result.AttachmentJoins.Single(j => j.AttachmentApplicationDataId == "point_facility_network:ATT-DUP");
        Assert.Equal("point_facility:DUP-2", dupJoin.MatchedPoiApplicationDataId);
        Assert.True(dupJoin.ConnectorStemLengthMeters < 2.0d);

        // 3. MultiPoint POI + MultiPoint attachment emits 2 short stems (~1 m each)
        ShowcaseElementPlan mpAttach = result.Elements.Single(e => e.FeatureKey == "ATT-MP");
        Assert.Equal(2, mpAttach.MarkerPlacements.Count);
        Assert.Equal(2, mpAttach.TubeSegments.Count);
        Assert.All(mpAttach.TubeSegments, seg =>
            Assert.True(ShowcaseCoordinateTransform.FeetToMeters(seg.LengthFeet) < 2.0d, "Each MultiPoint attachment should stem to its nearest MultiPoint POI placement."));
    }

    [Fact]
    public void HorizontalMultiLineStringScansAllPartsForFirstFiniteZ()
    {
        ShowcaseLayer multiLineLayer = new(
            "network_link",
            ShowcaseLayerKind.NetworkLink,
            new[] { new ShowcaseField("link_type", ShowcaseFieldType.Text) },
            new[]
            {
                // No levelId/floorKey; part 0 has null Zs, part 1 has surveyed Z = 6.5 m -> resolves 6.5m surveyed with zero missing-level warning!
                new ShowcaseFeature(
                    "ML-HORIZ-Z",
                    null,
                    null,
                    new ShowcaseMultiLineStringGeometry(
                        new IReadOnlyList<Point2D>[]
                        {
                            new[] { new Point2D(0, 0), new Point2D(5, 0) },
                            new[] { new Point2D(5, 0), new Point2D(10, 0) },
                        },
                        new IReadOnlyList<double?>?[]
                        {
                            null,
                            new double?[] { 6.5d, 6.5d },
                        }),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["link_type"] = ShowcaseAttributeValue.FromText("walkway"),
                    }),
            });

        ShowcaseSnapshot snapshot = ParseSnapshot(
            "horiz_multiline_z",
            Array.Empty<ShowcaseLevel>(),
            new[] { multiLineLayer });

        ShowcasePoiAndNetworkPlanResult result = ShowcasePoiAndNetworkPlanner.Plan(snapshot, ShowcaseCoordinateTransform.Identity);

        Assert.Empty(result.Warnings);
        ShowcaseElementPlan ribbon = Assert.Single(result.Elements);
        Assert.Equal(ShowcaseElementKind.NetworkRibbon, ribbon.ElementKind);
        Assert.Equal(ShowcaseGeometryProvenance.Surveyed, ribbon.GeometryProvenance);
        double baseMeters = ShowcaseCoordinateTransform.FeetToMeters(ribbon.Solids[0].BaseElevationFeet);
        Assert.InRange(Math.Abs(baseMeters - (6.5d + 0.40d)), 0d, 1e-6d);
    }

    private static ShowcaseSnapshot ParseSnapshot(
        string scope,
        IReadOnlyList<ShowcaseLevel> levels,
        IReadOnlyList<ShowcaseLayer> layers)
    {
        // Note: SerializeSnapshotWithValidHash does not serialize 3D Z arrays on LineString, so construct directly
        // when 3D Zs are present and otherwise round-trip through ShowcaseSnapshotReader.Parse to test JSON integrity too.
        bool has3DLineStrings = layers.Any(l => l.Features.Any(f =>
            (f.Geometry is ShowcaseLineStringGeometry ls && ls.HasZCoordinates) ||
            (f.Geometry is ShowcaseMultiLineStringGeometry mls && mls.HasZCoordinates) ||
            f.Geometry is ShowcaseMultiPointGeometry));

        if (has3DLineStrings)
        {
            ShowcaseSummary summary = ShowcaseSnapshotReader.ComputeSummary(layers, Array.Empty<QuarantinedShowcaseFeature>());
            ShowcaseSnapshot draft = new(
                ShowcaseContract.Schema,
                ShowcaseContract.Version,
                scope,
                basedOn: null,
                writtenUtc: "2026-11-27T00:00:00Z",
                kanizu: "0.9.0",
                epsg: 6677,
                contentHash: string.Empty,
                levels,
                layers,
                Array.Empty<QuarantinedShowcaseFeature>(),
                summary);
            string hash = ShowcaseSnapshotReader.ComputeContentHash(draft);
            return new ShowcaseSnapshot(
                draft.Schema,
                draft.Version,
                draft.Scope,
                draft.BasedOn,
                draft.WrittenUtc,
                draft.Kanizu,
                draft.Epsg,
                hash,
                draft.Levels,
                draft.Layers,
                draft.Quarantined,
                draft.Summary);
        }

        string json = ShowcaseSnapshotReaderTests.SerializeSnapshotWithValidHash(
            scope: scope,
            basedOn: null,
            writtenUtc: "2026-11-27T00:00:00Z",
            kanizu: "0.9.0",
            epsg: 6677,
            levels: levels,
            layers: layers,
            quarantined: Array.Empty<QuarantinedShowcaseFeature>());
        return ShowcaseSnapshotReader.Parse(json);
    }
}
