using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Showcase;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Showcase;

public sealed class ShowcaseGeometryPlannerTests
{
    [Fact]
    public void PlansUnitWithHolesOpeningFixtureAndNonOccludingLevelBandAllCaughtByShowcaseExportGuard()
    {
        ShowcaseSnapshot snapshot = ShowcaseSnapshotReader.Parse(ShowcaseSnapshotReaderTests.BuildValidFullSnapshotJson());
        ShowcaseCoordinateTransform transform = ShowcaseCoordinateTransform.FromSharedOrigin(
            originEastingMeters: 0.0d,
            originNorthingMeters: 0.0d,
            originElevationMeters: 0.0d,
            rotationDegrees: 15.0d);

        ShowcaseGeometryPlanResult plan = ShowcaseGeometryPlanner.Plan(snapshot, transform);

        Assert.Empty(plan.Warnings);
        // F3 materializes unit, opening, fixture, level (F4 handles POI + network layers)
        Assert.Equal(4, plan.Elements.Count);

        // 1. Unit with exterior + 2 interior holes, strictly winding-normalized (CCW outer, CW holes)
        ShowcaseElementPlan unit = plan.Elements.Single(e => e.LayerKind == ShowcaseLayerKind.Unit);
        Assert.Equal("Kanizu.Showcase", unit.ApplicationId);
        Assert.Equal("1F_unit:unit-001", unit.ApplicationDataId);
        Assert.StartsWith("KNZ_FRM_Unit", unit.ElementName, StringComparison.Ordinal);
        Assert.False(unit.IsNonOccludingOutlineBand);

        ShowcaseExtrusionSolid unitSolid = Assert.Single(unit.Solids);
        Assert.Equal(2, unitSolid.InteriorRingsFeet.Count);
        Assert.True(ShowcaseGeometryPlanner.ComputeSignedAreaOpen(unitSolid.OuterRingFeet) > 0d, "Outer ring must be CCW.");
        foreach (IReadOnlyList<Point2D> hole in unitSolid.InteriorRingsFeet)
        {
            Assert.True(ShowcaseGeometryPlanner.ComputeSignedAreaOpen(hole) < 0d, "Interior hole ring must be CW.");
        }

        // Elevation matches 3.3m TP converted to feet
        Assert.InRange(
            Math.Abs(unitSolid.BaseElevationFeet - ShowcaseCoordinateTransform.MetersToFeet(3.3d)),
            0d,
            1e-6d);

        // 2. Opening thickened from LineString into valid CCW prism
        ShowcaseElementPlan opening = plan.Elements.Single(e => e.LayerKind == ShowcaseLayerKind.Opening);
        Assert.Equal("1F_opening:op-001", opening.ApplicationDataId);
        Assert.StartsWith("KNZ_FRM_Opening", opening.ElementName, StringComparison.Ordinal);
        ShowcaseExtrusionSolid openingSolid = Assert.Single(opening.Solids);
        Assert.True(ShowcaseGeometryPlanner.ComputeSignedAreaOpen(openingSolid.OuterRingFeet) > 0d);
        Assert.InRange(
            Math.Abs(openingSolid.HeightFeet - ShowcaseCoordinateTransform.MetersToFeet(ShowcaseGeometryPlanOptions.Default.OpeningExtrusionHeightMeters)),
            0d,
            1e-6d);

        // 3. Fixture prism
        ShowcaseElementPlan fixture = plan.Elements.Single(e => e.LayerKind == ShowcaseLayerKind.Fixture);
        Assert.Equal("1F_fixture:fxt-001", fixture.ApplicationDataId);
        Assert.StartsWith("KNZ_FRM_Fixture", fixture.ElementName, StringComparison.Ordinal);
        ShowcaseExtrusionSolid fixtureSolid = Assert.Single(fixture.Solids);
        Assert.True(ShowcaseGeometryPlanner.ComputeSignedAreaOpen(fixtureSolid.OuterRingFeet) > 0d);

        // 4. Level outline band: strictly non-occluding perimeter frame with open interior hole!
        ShowcaseElementPlan levelBand = plan.Elements.Single(e => e.LayerKind == ShowcaseLayerKind.Level);
        Assert.Equal("1F_level:level-0", levelBand.ApplicationDataId);
        Assert.StartsWith("KNZ_FRM_LevelBand", levelBand.ElementName, StringComparison.Ordinal);
        Assert.True(levelBand.IsNonOccludingOutlineBand);

        ShowcaseExtrusionSolid bandSolid = Assert.Single(levelBand.Solids);
        Assert.True(ShowcaseGeometryPlanner.ComputeSignedAreaOpen(bandSolid.OuterRingFeet) > 0d, "Band outer ring must be CCW.");
        Assert.NotEmpty(bandSolid.InteriorRingsFeet);
        Assert.All(bandSolid.InteriorRingsFeet, hole =>
            Assert.True(ShowcaseGeometryPlanner.ComputeSignedAreaOpen(hole) < 0d, "Band inner hole must be CW."));

        // Verify the center of the 20m x 12m level (10m, 6m) is strictly OUTSIDE the band solid (non-occluding!)
        Point2D centerFeet = transform.ToRevitInternal2D(10.0d, 6.0d);
        Polygon bandPolygonNts = ToNtsPolygon(bandSolid);
        Assert.False(
            bandPolygonNts.Covers(new Point(centerFeet.X, centerFeet.Y)),
            "Level outline band must NEVER occlude the level interior or lower floors.");

        // 5. EVERY planned element triggers ALL F2 ShowcaseExportGuard defense-in-depth layers!
        foreach (ShowcaseElementPlan element in plan.Elements)
        {
            // Caught with document role = "showcase"
            Assert.True(ShowcaseExportGuard.IsShowcaseElement(element.ToGuardDescriptor(ShowcaseExportGuard.ShowcaseModelRole)));

            // Caught even if copy-pasted into an authoring document (sourceDocumentModelRole = null)
            Assert.True(ShowcaseExportGuard.IsShowcaseElement(element.ToGuardDescriptor(sourceDocumentModelRole: null)));

            // Each individual marker independently triggers ShowcaseExportGuard
            Assert.True(ShowcaseExportGuard.IsShowcaseElement(applicationId: element.ApplicationId));
            Assert.True(ShowcaseExportGuard.IsShowcaseElement(typeName: element.ElementName));
            Assert.True(ShowcaseExportGuard.IsShowcaseElement(
                parameterNames: element.ParameterAssignments.Select(a => a.Parameter.Name)));
        }
    }

    [Fact]
    public void CompanionDocumentGuardRefusesToMutateAuthoringDocuments()
    {
        // Existing documents with unset role (legacy v1/v2 authoring) or "authoring" MUST be rejected
        Assert.Throws<InvalidOperationException>(() =>
            ShowcaseCompanionDocumentGuard.ValidateTargetRole(existingModelRole: null, isFreshNewDocument: false, "Station_Authoring.rvt"));
        Assert.Throws<InvalidOperationException>(() =>
            ShowcaseCompanionDocumentGuard.ValidateTargetRole(existingModelRole: "", isFreshNewDocument: false, "Station_Authoring.rvt"));
        Assert.Throws<InvalidOperationException>(() =>
            ShowcaseCompanionDocumentGuard.ValidateTargetRole(existingModelRole: "authoring", isFreshNewDocument: false, "Station_Authoring.rvt"));
        Assert.Throws<InvalidOperationException>(() =>
            ShowcaseCompanionDocumentGuard.ValidateTargetRole(existingModelRole: "unknown", isFreshNewDocument: false, "Station_Authoring.rvt"));

        // Fresh document created from an authoring-stamped template is also rejected
        Assert.Throws<InvalidOperationException>(() =>
            ShowcaseCompanionDocumentGuard.ValidateTargetRole(existingModelRole: "authoring", isFreshNewDocument: true, "AuthoringTemplate.rte"));

        // Existing showcase document and fresh blank/showcase companion documents are permitted
        ShowcaseCompanionDocumentGuard.ValidateTargetRole(existingModelRole: "showcase", isFreshNewDocument: false, "Station_GIS_Showcase.rvt");
        ShowcaseCompanionDocumentGuard.ValidateTargetRole(existingModelRole: "SHOWCASE", isFreshNewDocument: false, "Station_GIS_Showcase.rvt");
        ShowcaseCompanionDocumentGuard.ValidateTargetRole(existingModelRole: null, isFreshNewDocument: true, "NewCompanion.rvt");
        ShowcaseCompanionDocumentGuard.ValidateTargetRole(existingModelRole: "", isFreshNewDocument: true, "NewCompanion.rvt");
        ShowcaseCompanionDocumentGuard.ValidateTargetRole(existingModelRole: "showcase", isFreshNewDocument: true, "NewCompanion.rvt");
    }

    [Fact]
    public void HandlesPointAndLineStringFixturesAndMultiPolygonUnits()
    {
        ShowcaseLayer multiUnitLayer = new(
            "1F_unit",
            ShowcaseLayerKind.Unit,
            new[] { new ShowcaseField("id", ShowcaseFieldType.Text) },
            new[]
            {
                new ShowcaseFeature(
                    "mp-001",
                    "level:0",
                    "0",
                    new ShowcaseMultiPolygonGeometry(new[]
                    {
                        new ShowcasePolygonGeometry(new IReadOnlyList<Point2D>[]
                        {
                            new[] { new Point2D(0, 0), new Point2D(4, 0), new Point2D(4, 4), new Point2D(0, 4), new Point2D(0, 0) },
                        }),
                        new ShowcasePolygonGeometry(new IReadOnlyList<Point2D>[]
                        {
                            new[] { new Point2D(6, 0), new Point2D(10, 0), new Point2D(10, 4), new Point2D(6, 4), new Point2D(6, 0) },
                        }),
                    }),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["id"] = ShowcaseAttributeValue.FromText("mp-001"),
                    }),
            });

        ShowcaseLayer pointFixtureLayer = new(
            "1F_fixture",
            ShowcaseLayerKind.Fixture,
            new[] { new ShowcaseField("id", ShowcaseFieldType.Text) },
            new[]
            {
                new ShowcaseFeature(
                    "pt-fxt-1",
                    "level:0",
                    "0",
                    new ShowcasePointGeometry(new Point2D(5, 2)),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["id"] = ShowcaseAttributeValue.FromText("pt-fxt-1"),
                    }),
            });

        ShowcaseSnapshot snapshot = ShowcaseSnapshotReader.Parse(
            ShowcaseSnapshotReaderTests.SerializeSnapshotWithValidHash(
                scope: "multi_geom",
                basedOn: null,
                writtenUtc: "2026-05-01T00:00:00Z",
                kanizu: "0.9.0",
                epsg: 6677,
                levels: new[] { new ShowcaseLevel("level:0", "0", 0, "1F", 0.0d, ShowcaseElevationSource.Surveyed) },
                layers: new[] { multiUnitLayer, pointFixtureLayer },
                quarantined: Array.Empty<QuarantinedShowcaseFeature>()));

        ShowcaseGeometryPlanResult result = ShowcaseGeometryPlanner.Plan(snapshot, ShowcaseCoordinateTransform.Identity);

        Assert.Equal(2, result.Elements.Count);
        ShowcaseElementPlan mpUnit = result.Elements.Single(e => e.FeatureKey == "mp-001");
        Assert.Equal(2, mpUnit.Solids.Count);
        Assert.Equal("1F_unit:mp-001", mpUnit.ApplicationDataId);

        ShowcaseElementPlan ptFixture = result.Elements.Single(e => e.FeatureKey == "pt-fxt-1");
        Assert.Single(ptFixture.Solids);
        Assert.Equal("1F_fixture:pt-fxt-1", ptFixture.ApplicationDataId);
    }

    [Fact]
    public void ReconcilerPlansCreateInPlaceUpdateCategoryReplaceStaleDeleteAndNullParamClears()
    {
        ShowcaseSnapshot snapshot = ShowcaseSnapshotReader.Parse(ShowcaseSnapshotReaderTests.BuildValidFullSnapshotJson());
        ShowcaseGeometryPlanResult planResult = ShowcaseGeometryPlanner.Plan(snapshot, ShowcaseCoordinateTransform.Identity);

        // Snapshot produces 4 elements:
        //   1F_unit:unit-001 (OST_Floors)
        //   1F_opening:op-001 (OST_Doors)
        //   1F_fixture:fxt-001 (OST_SpecialityEquipment)
        //   1F_level:level-0 (OST_GenericModel)
        ShowcaseExistingElementInfo[] existing =
        {
            // Matching key & matching category -> UpdateInPlace (keeps Revit ElementId / UniqueId)
            new(1001L, "1F_unit:unit-001", "OST_Floors"),
            // Duplicate legacy element with same key -> pruned via DeleteStale
            new(1002L, "1F_unit:unit-001", "OST_Floors"),
            // Matching key but category changed (was OST_GenericModel, now OST_Doors) -> ReplaceCategoryChanged
            new(1003L, "1F_opening:op-001", "OST_GenericModel"),
            // Removed from snapshot -> DeleteStale
            new(1004L, "1F_unit:deleted-old-unit", "OST_Floors"),
            // Fixture and Level are missing from existing -> Create
        };

        ShowcaseReconciliationPlan rec = ShowcaseElementReconciler.BuildPlan(planResult.Elements, existing);

        Assert.Equal(2, rec.CreateCount);
        Assert.Equal(1, rec.UpdateInPlaceCount);
        Assert.Equal(1, rec.ReplaceCount);
        Assert.Equal(2, rec.DeleteStaleCount);

        ShowcaseReconciliationStep unitUpdate = rec.Steps.Single(s => s.Action == ShowcaseReconciliationActionKind.UpdateInPlace);
        Assert.Equal("1F_unit:unit-001", unitUpdate.ApplicationDataId);
        Assert.Equal(1001L, unitUpdate.ExistingElementId);

        ShowcaseReconciliationStep openingReplace = rec.Steps.Single(s => s.Action == ShowcaseReconciliationActionKind.ReplaceCategoryChanged);
        Assert.Equal("1F_opening:op-001", openingReplace.ApplicationDataId);
        Assert.Equal(1003L, openingReplace.ExistingElementId);

        long[] deletedIds = rec.Steps
            .Where(s => s.Action == ShowcaseReconciliationActionKind.DeleteStale)
            .Select(s => s.ExistingElementId!.Value)
            .OrderBy(id => id)
            .ToArray();
        Assert.Equal(new[] { 1002L, 1004L }, deletedIds);

        // Verify null parameter clearing on in-place update:
        // unit-001 has nullable_note = null and no overflow JSON -> both MUST be scheduled for clearing!
        IReadOnlyList<ShowcaseParameterDefinition> cleared = ShowcaseElementReconciler.GetParametersToClearOnUpdate(
            planResult.ParameterRegistry,
            unitUpdate.TargetPlan!);

        ShowcaseParameterDefinition nullableNoteDef = planResult.ParameterRegistry.FindDirectParameter(ShowcaseLayerKind.Unit, "nullable_note")!;
        ShowcaseParameterDefinition emptyNoteDef = planResult.ParameterRegistry.FindDirectParameter(ShowcaseLayerKind.Unit, "empty_note")!;
        ShowcaseParameterDefinition categoryDef = planResult.ParameterRegistry.FindDirectParameter(ShowcaseLayerKind.Unit, "category")!;

        Assert.Contains(cleared, d => d.Guid == nullableNoteDef.Guid);
        Assert.Contains(cleared, d => d.Guid == planResult.ParameterRegistry.OverflowParameter.Guid);
        Assert.DoesNotContain(cleared, d => d.Guid == emptyNoteDef.Guid);
        Assert.DoesNotContain(cleared, d => d.Guid == categoryDef.Guid);
    }

    private static Polygon ToNtsPolygon(ShowcaseExtrusionSolid solid)
    {
        GeometryFactory factory = new();
        LinearRing shell = ToLinearRing(factory, solid.OuterRingFeet);
        LinearRing[] holes = solid.InteriorRingsFeet.Select(h => ToLinearRing(factory, h)).ToArray();
        return factory.CreatePolygon(shell, holes);
    }

    private static LinearRing ToLinearRing(GeometryFactory factory, IReadOnlyList<Point2D> openRing)
    {
        Coordinate[] coords = new Coordinate[openRing.Count + 1];
        for (int i = 0; i < openRing.Count; i++)
        {
            coords[i] = new Coordinate(openRing[i].X, openRing[i].Y);
        }

        coords[openRing.Count] = new Coordinate(openRing[0].X, openRing[0].Y);
        return factory.CreateLinearRing(coords);
    }
}
