using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Showcase;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Showcase;

public sealed class ShowcaseF5IncrementalViewsAndBudgetsTests
{
    [Fact]
    public void IdempotentRefreshSkipsAllUnchangedElementsWith100PercentUniqueIdPreservation()
    {
        ShowcaseSnapshot snapshot = ShowcaseSnapshotReader.Parse(ShowcaseSnapshotReaderTests.BuildValidFullSnapshotJson());
        ShowcaseParameterRegistry registry = ShowcaseParameterRegistry.Build(snapshot);
        ShowcaseGeometryPlanResult f3 = ShowcaseGeometryPlanner.Plan(snapshot, ShowcaseCoordinateTransform.Identity, registry);
        ShowcasePoiAndNetworkPlanResult f4 = ShowcasePoiAndNetworkPlanner.Plan(snapshot, ShowcaseCoordinateTransform.Identity, registry);

        List<ShowcaseElementPlan> planned = f3.Elements.Concat(f4.Elements).ToList();
        Assert.NotEmpty(planned);

        // Simulate existing companion elements freshly built from the exact same snapshot (with ks_source_hash & UniqueIds).
        List<ShowcaseExistingElementInfo> existing = planned
            .Select((plan, idx) => new ShowcaseExistingElementInfo(
                elementId: 5000L + idx,
                applicationDataId: plan.ApplicationDataId,
                categoryName: plan.PreferredCategoryName,
                populatedKsParameterGuids: CollectPopulatedGuids(plan).Concat(new[] { ShowcaseParameterRegistry.SourceHashParameterGuid }),
                uniqueId: $"uid-{idx:D4}",
                contentDigest: plan.ComputeContentDigest(),
                representationKind: ShowcaseElementRepresentationKind.DirectShape))
            .ToList();

        ShowcaseReconciliationPlan rec = ShowcaseElementReconciler.BuildPlan(
            planned,
            existing,
            resolveTargetRepresentationKind: _ => ShowcaseElementRepresentationKind.DirectShape);

        Assert.Equal(0, rec.CreateCount);
        Assert.Equal(0, rec.UpdateInPlaceCount);
        Assert.Equal(0, rec.ReplaceCount);
        Assert.Equal(0, rec.DeleteStaleCount);
        Assert.Equal(planned.Count, rec.UnchangedCount);
        Assert.Equal(planned.Count, rec.PreservedUniqueIdCount);
        Assert.Equal(planned.Count, rec.SurvivingElementCount);
        Assert.Equal(1.0d, rec.UniqueIdPreservationRate, precision: 9);
        Assert.Equal(planned.Count, rec.PreservedUniqueIds.Count);
        Assert.Empty(rec.ReplacedUniqueIds);
        Assert.All(rec.Steps, s => Assert.Equal(ShowcaseReconciliationActionKind.SkipUnchanged, s.Action));

        ShowcaseRefreshSummary summary = ShowcaseRefreshSummary.FromReconciliationPlan(
            priorSnapshotContentHash: snapshot.ContentHash,
            newSnapshotContentHash: snapshot.ContentHash,
            plan: rec);

        Assert.True(summary.SnapshotContentHashUnchanged);
        Assert.True(summary.IsIdempotentNoOp);
        Assert.False(summary.HasFormaCommentDisplacementRisk);
        Assert.Equal(1.0d, summary.UniqueIdPreservationRate, precision: 9);
        Assert.Equal(planned.Count, summary.UnchangedCount);
    }

    [Fact]
    public void MixedIncrementalRefreshTracksExactDiffCountsAndUniqueIdPreservationRate()
    {
        ShowcasePolygonGeometry squareA = MakeRect(0, 0, 5, 5);
        ShowcasePolygonGeometry squareEdited = MakeRect(0, 0, 6, 5);

        ShowcaseLayer unitLayerV1 = new(
            "1F_unit",
            ShowcaseLayerKind.Unit,
            new[]
            {
                new ShowcaseField("id", ShowcaseFieldType.Text),
                new ShowcaseField("name", ShowcaseFieldType.Text),
                new ShowcaseField("note", ShowcaseFieldType.Text),
            },
            new[]
            {
                MakeUnit("u-unchanged", squareA, "Concourse", "keep-note"),
                MakeUnit("u-geom-edit", squareA, "Hall A", "note-a"),
                MakeUnit("u-attr-edit", squareA, "Shop Old", "note-b"),
                MakeUnit("u-param-cleared", squareA, "Lounge", "will-become-null"),
                MakeUnit("u-cat-changed", squareA, "Room C", "note-c"),
                MakeUnit("u-rep-changed", squareA, "Room D", "note-d"),
                MakeUnit("u-deleted", squareA, "Demolished", "gone"),
            });

        ShowcaseLayer unitLayerV2 = new(
            "1F_unit",
            ShowcaseLayerKind.Unit,
            unitLayerV1.Fields,
            new[]
            {
                MakeUnit("u-unchanged", squareA, "Concourse", "keep-note"),
                MakeUnit("u-geom-edit", squareEdited, "Hall A", "note-a"),
                MakeUnit("u-attr-edit", squareA, "Shop Renamed", "note-b"),
                MakeUnit("u-param-cleared", squareA, "Lounge", null),
                MakeUnit("u-cat-changed", squareA, "Room C", "note-c"),
                MakeUnit("u-rep-changed", squareA, "Room D", "note-d"),
                MakeUnit("u-created", squareA, "New Kiosk", "fresh"),
            });

        ShowcaseSnapshot snapV1 = BuildSnapshot("mixed_v1", new[] { new ShowcaseLevel("level:0", "0", 0, "1F", 0.0d, ShowcaseElevationSource.Surveyed) }, new[] { unitLayerV1 });
        ShowcaseSnapshot snapV2 = BuildSnapshot("mixed_v2", new[] { new ShowcaseLevel("level:0", "0", 0, "1F", 0.0d, ShowcaseElevationSource.Surveyed) }, new[] { unitLayerV2 });

        ShowcaseGeometryPlanResult planV1 = ShowcaseGeometryPlanner.Plan(snapV1, ShowcaseCoordinateTransform.Identity);
        ShowcaseGeometryPlanResult planV2 = ShowcaseGeometryPlanner.Plan(snapV2, ShowcaseCoordinateTransform.Identity);

        List<ShowcaseExistingElementInfo> existingV1 = new();
        long nextId = 1000L;
        foreach (ShowcaseElementPlan p in planV1.Elements)
        {
            string cat = p.FeatureKey == "u-cat-changed" ? "OST_GenericModel" : p.PreferredCategoryName;
            ShowcaseElementRepresentationKind rep = p.FeatureKey == "u-rep-changed"
                ? ShowcaseElementRepresentationKind.FamilyInstance
                : ShowcaseElementRepresentationKind.DirectShape;

            existingV1.Add(new ShowcaseExistingElementInfo(
                nextId++,
                p.ApplicationDataId,
                cat,
                CollectPopulatedGuids(p),
                uniqueId: $"uid-{p.FeatureKey}",
                contentDigest: p.ComputeContentDigest(),
                representationKind: rep));
        }

        // Also add one duplicate stale element with same ApplicationDataId as u-unchanged.
        existingV1.Add(new ShowcaseExistingElementInfo(
            9999L,
            "1F_unit:u-unchanged",
            "OST_Floors",
            Array.Empty<Guid>(),
            uniqueId: "uid-dup-stale",
            contentDigest: "old",
            representationKind: ShowcaseElementRepresentationKind.DirectShape));

        ShowcaseReconciliationPlan rec = ShowcaseElementReconciler.BuildPlan(
            planV2.Elements,
            existingV1,
            resolveTargetRepresentationKind: _ => ShowcaseElementRepresentationKind.DirectShape);

        Assert.Equal(1, rec.UnchangedCount);
        Assert.Equal(2, rec.UpdateInPlaceCount); // u-geom-edit + u-attr-edit
        Assert.Equal(1, rec.ReplaceParametersClearedCount); // u-param-cleared
        Assert.Equal(1, rec.ReplaceCategoryChangedCount); // u-cat-changed
        Assert.Equal(1, rec.ReplaceRepresentationChangedCount); // u-rep-changed
        Assert.Equal(3, rec.ReplaceCount);
        Assert.Equal(1, rec.CreateCount); // u-created
        Assert.Equal(2, rec.DeleteStaleCount); // u-deleted + duplicate 9999

        // Surviving elements = 1 unchanged + 2 updatedInPlace + 3 replaced = 6; Preserved = 3 => 50% UniqueId preservation
        Assert.Equal(3, rec.PreservedUniqueIdCount);
        Assert.Equal(6, rec.SurvivingElementCount);
        Assert.Equal(0.5d, rec.UniqueIdPreservationRate, precision: 9);

        Assert.Equal(
            new[] { "uid-u-unchanged", "uid-u-geom-edit", "uid-u-attr-edit" },
            rec.PreservedUniqueIds);
        Assert.Equal(3, rec.ReplacedUniqueIds.Count);
        Assert.Contains(rec.ReplacedUniqueIds, r => r.PriorUniqueId == "uid-u-param-cleared" && r.Reason == ShowcaseReconciliationActionKind.ReplaceParametersCleared);
        Assert.Contains(rec.ReplacedUniqueIds, r => r.PriorUniqueId == "uid-u-cat-changed" && r.Reason == ShowcaseReconciliationActionKind.ReplaceCategoryChanged);
        Assert.Contains(rec.ReplacedUniqueIds, r => r.PriorUniqueId == "uid-u-rep-changed" && r.Reason == ShowcaseReconciliationActionKind.ReplaceRepresentationChanged);

        ShowcaseRefreshSummary summary = ShowcaseRefreshSummary.FromReconciliationPlan(snapV1.ContentHash, snapV2.ContentHash, rec);
        Assert.False(summary.SnapshotContentHashUnchanged);
        Assert.True(summary.HasFormaCommentDisplacementRisk);
        Assert.False(summary.IsIdempotentNoOp);
        Assert.Equal(0.5d, summary.UniqueIdPreservationRate, precision: 9);
    }

    [Fact]
    public void ApplicationDataIdEscapesColonsAndPercentWithoutCollidingAndPreservesLegacyPlainIds()
    {
        // Backward compatibility for plain layer:key pairs
        Assert.Equal("1F_unit:unit-001", ShowcaseGeometryPlanner.BuildApplicationDataId("1F_unit", "unit-001"));
        Assert.Equal("point_facility:SYM-WC", ShowcaseGeometryPlanner.BuildApplicationDataId("point_facility", "SYM-WC"));

        // Colon and percent escaping eliminates delimiter ambiguity (bijective encoding)
        string id1 = ShowcaseGeometryPlanner.BuildApplicationDataId("a:b", "c");
        string id2 = ShowcaseGeometryPlanner.BuildApplicationDataId("a", "b:c");
        string id3 = ShowcaseGeometryPlanner.BuildApplicationDataId("a%3Ab", "c");
        string id4 = ShowcaseGeometryPlanner.BuildApplicationDataId("a", "b%3Ac");

        Assert.Equal("a%3Ab:c", id1);
        Assert.Equal("a:b%3Ac", id2);
        Assert.Equal("a%253Ab:c", id3);
        Assert.Equal("a:b%253Ac", id4);
        Assert.Equal(4, new[] { id1, id2, id3, id4 }.Distinct(StringComparer.Ordinal).Count());

        // Plan with both ("a:b", "c") and ("a", "b:c") produces 2 globally unique ApplicationDataIds
        ShowcasePolygonGeometry poly = MakeRect(0, 0, 4, 4);
        ShowcaseLayer layerAB = new("a:b", ShowcaseLayerKind.Unit, new[] { new ShowcaseField("id", ShowcaseFieldType.Text) }, new[] { MakeUnit("c", poly, "AB", null) });
        ShowcaseLayer layerA = new("a", ShowcaseLayerKind.Unit, new[] { new ShowcaseField("id", ShowcaseFieldType.Text) }, new[] { MakeUnit("b:c", poly, "BC", null) });
        ShowcaseSnapshot collidingSnap = BuildSnapshot("colon_test", new[] { new ShowcaseLevel("level:0", "0", 0, "1F", 0.0d, ShowcaseElevationSource.Surveyed) }, new[] { layerAB, layerA });

        ShowcaseGeometryPlanResult plan = ShowcaseGeometryPlanner.Plan(collidingSnap, ShowcaseCoordinateTransform.Identity);
        Assert.Equal(2, plan.Elements.Count);
        ShowcaseElementReconciler.ValidateUniqueApplicationDataIds(plan.Elements);

        // Unambiguous pre-F5 legacy ID ("1F_level:level:0") matches escaped ("1F_level:level%3A0") in-place, preserving UniqueId!
        ShowcaseLayer levelLayer = new("1F_level", ShowcaseLayerKind.Unit, new[] { new ShowcaseField("id", ShowcaseFieldType.Text) }, new[] { MakeUnit("level:0", poly, "1F", null) });
        ShowcaseSnapshot legacySnap = BuildSnapshot("legacy_upgrade", new[] { new ShowcaseLevel("level:0", "0", 0, "1F", 0.0d, ShowcaseElevationSource.Surveyed) }, new[] { levelLayer });
        ShowcaseGeometryPlanResult legacyPlan = ShowcaseGeometryPlanner.Plan(legacySnap, ShowcaseCoordinateTransform.Identity);
        ShowcaseElementPlan escapedElem = Assert.Single(legacyPlan.Elements);
        Assert.Equal("1F_level:level%3A0", escapedElem.ApplicationDataId);

        ShowcaseExistingElementInfo preF5Existing = new(
            3001L,
            "1F_level:level:0",
            "OST_Floors",
            CollectPopulatedGuids(escapedElem),
            uniqueId: "uid-pre-f5",
            contentDigest: escapedElem.ComputeContentDigest());

        ShowcaseReconciliationPlan upgraded = ShowcaseElementReconciler.BuildPlan(legacyPlan.Elements, new[] { preF5Existing });
        Assert.Equal(1, upgraded.UpdateInPlaceCount); // upgrades ApplicationDataId in-place rather than skipping or replacing!
        Assert.Equal(0, upgraded.ReplaceCount);
        Assert.Equal(0, upgraded.DeleteStaleCount);
        ShowcaseReconciliationStep upgradeStep = Assert.Single(upgraded.Steps);
        Assert.Equal("1F_level:level:0", upgradeStep.MatchedLegacyApplicationDataId);
        Assert.Equal("1F_level:level%3A0", upgradeStep.ApplicationDataId);
        Assert.Equal("uid-pre-f5", upgradeStep.ExistingUniqueId);

        // Global uniqueness guard rejects duplicate ApplicationDataIds before mutating Revit
        Assert.Throws<InvalidOperationException>(() =>
            ShowcaseElementReconciler.ValidateUniqueApplicationDataIds(new[] { escapedElem, escapedElem }));
    }

    [Fact]
    public void CompanionTrackingParametersAreReservedGuardCompliantAndValidInSharedParamFile()
    {
        Assert.True(ShowcaseExportGuard.IsShowcaseParameterName(ShowcaseParameterRegistry.SourceHashParameterName));
        Assert.True(ShowcaseExportGuard.IsShowcaseParameterName(ShowcaseParameterRegistry.SnapshotHashParameterName));
        Assert.NotEqual(ShowcaseParameterRegistry.SourceHashParameterGuid, ShowcaseParameterRegistry.SnapshotHashParameterGuid);

        // Even if a GIS layer contains columns literally named "source_hash" and "snapshot_hash", they get disambiguated
        ShowcaseLayer layer = new(
            "1F_unit",
            ShowcaseLayerKind.Unit,
            new[]
            {
                new ShowcaseField("source_hash", ShowcaseFieldType.Text),
                new ShowcaseField("snapshot_hash", ShowcaseFieldType.Text),
            },
            new[]
            {
                new ShowcaseFeature(
                    "u1",
                    "level:0",
                    "0",
                    MakeRect(0, 0, 5, 5),
                    new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
                    {
                        ["source_hash"] = ShowcaseAttributeValue.FromText("user-source"),
                        ["snapshot_hash"] = ShowcaseAttributeValue.FromText("user-snap"),
                    }),
            });

        ShowcaseSnapshot snap = BuildSnapshot("tracking_collision", new[] { new ShowcaseLevel("level:0", "0", 0, "1F", 0.0d, ShowcaseElevationSource.Surveyed) }, new[] { layer });
        ShowcaseParameterRegistry registry = ShowcaseParameterRegistry.Build(snap);

        ShowcaseParameterDefinition userSrc = registry.FindDirectParameter(ShowcaseLayerKind.Unit, "source_hash")!;
        ShowcaseParameterDefinition userSnap = registry.FindDirectParameter(ShowcaseLayerKind.Unit, "snapshot_hash")!;
        Assert.NotEqual(ShowcaseParameterRegistry.SourceHashParameterName, userSrc.Name);
        Assert.NotEqual(ShowcaseParameterRegistry.SourceHashParameterGuid, userSrc.Guid);
        Assert.NotEqual(ShowcaseParameterRegistry.SnapshotHashParameterName, userSnap.Name);
        Assert.NotEqual(ShowcaseParameterRegistry.SnapshotHashParameterGuid, userSnap.Guid);

        string spText = registry.ToSharedParameterFileText(includeGisModelRoleParameter: true, includeCompanionTrackingParameters: true);
        Assert.Contains($"{ShowcaseParameterRegistry.SourceHashParameterGuid:D}\t{ShowcaseParameterRegistry.SourceHashParameterName}\tTEXT\t", spText);
        Assert.Contains($"{ShowcaseParameterRegistry.SnapshotHashParameterGuid:D}\t{ShowcaseParameterRegistry.SnapshotHashParameterName}\tTEXT\t", spText);

        // Ensure all emitted PARAM GUIDs and names are 100% unique
        string[] paramLines = spText.Split('\n').Where(l => l.StartsWith("PARAM\t", StringComparison.Ordinal)).ToArray();
        Assert.Equal(registry.AllParameters.Count + 3, paramLines.Length); // AllParameters + ks_source_hash + ks_snapshot_hash + gis_model_role
        Assert.Equal(paramLines.Length, paramLines.Select(l => l.Split('\t')[1]).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(paramLines.Length, paramLines.Select(l => l.Split('\t')[2]).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ViewPlannerProducesOverviewNetworkAndMonotonicNonOverlappingPerFloorSectionBoxes()
    {
        ShowcaseLevel[] levels =
        {
            // Intentionally out of ordinal order in input to verify deterministic sorting!
            new("level:2", "2", 2, "3F", 10.0d, ShowcaseElevationSource.Surveyed),
            new("level:-2", "-2", -2, "B2F", -9.0d, ShowcaseElevationSource.Surveyed),
            new("level:0", "0", 0, "1F", 0.0d, ShowcaseElevationSource.Surveyed),
            new("level:-1", "-1", -1, "B1F", -4.5d, ShowcaseElevationSource.Surveyed),
            new("level:1", "1", 1, "2F", 5.0d, ShowcaseElevationSource.Surveyed),
        };

        // Populate B2F, 1F, 3F; leave B1F and 2F sparse to verify station XY fallback.
        ShowcaseLayer units = new(
            "station_unit",
            ShowcaseLayerKind.Unit,
            new[] { new ShowcaseField("id", ShowcaseFieldType.Text) },
            new[]
            {
                new ShowcaseFeature("u-b2", "level:-2", "-2", MakeRect(0, 0, 20, 10), Attrs("u-b2")),
                new ShowcaseFeature("u-1f", "level:0", "0", MakeRect(5, 2, 15, 8), Attrs("u-1f")),
                new ShowcaseFeature("u-3f", "level:2", "2", MakeRect(10, 0, 40, 25), Attrs("u-3f")),
            });

        ShowcaseSnapshot snapshot = BuildSnapshot("multi_floor", levels, new[] { units });
        ShowcaseGeometryPlanResult f3 = ShowcaseGeometryPlanner.Plan(snapshot, ShowcaseCoordinateTransform.Identity);

        FormaPublishViewSetPlan viewPlan = ShowcaseViewPlanner.Plan(
            snapshot,
            ShowcaseCoordinateTransform.Identity,
            f3.Elements);

        Assert.Equal("Kanizu Forma Publish", viewPlan.ViewSetName);
        Assert.Equal(7, viewPlan.Views.Count); // Overview + Network & POIs + 5 floors
        Assert.Equal("Forma - 00 Overview", viewPlan.Views[0].Name);
        Assert.Equal(ShowcaseViewKind.Overview, viewPlan.Views[0].Kind);
        Assert.Equal("Forma - 01 Network & POIs", viewPlan.Views[1].Name);
        Assert.Equal(ShowcaseViewKind.NetworkAndPois, viewPlan.Views[1].Kind);

        // Network & POIs hides F3 solid categories + clutter and keeps OST_GenericModel visible
        Assert.Contains("OST_Floors", viewPlan.Views[1].HiddenCategoryNames);
        Assert.Contains("OST_Doors", viewPlan.Views[1].HiddenCategoryNames);
        Assert.Contains("OST_SpecialityEquipment", viewPlan.Views[1].HiddenCategoryNames);
        Assert.Contains("OST_Levels", viewPlan.Views[1].HiddenCategoryNames);
        Assert.Contains("OST_GenericModel", viewPlan.Views[1].VisibleCategoryNames);

        // Per-floor views strictly sorted by Ordinal: B2F (-2), B1F (-1), 1F (0), 2F (1), 3F (2)
        ShowcaseView3DPlan[] floorViews = viewPlan.Views.Where(v => v.Kind == ShowcaseViewKind.PerFloor).ToArray();
        Assert.Equal(new long?[] { -2, -1, 0, 1, 2 }, floorViews.Select(v => v.Ordinal).ToArray());

        double[] levelBaseZsFeet = new[] { -9.0d, -4.5d, 0.0d, 5.0d, 10.0d }
            .Select(ShowcaseCoordinateTransform.MetersToFeet)
            .ToArray();

        for (int i = 0; i < floorViews.Length; i++)
        {
            ShowcaseBoundingBox3D box = floorViews[i].SectionBoxFeet;
            Assert.True(box.IsValid, $"View '{floorViews[i].Name}' must have a valid positive-volume section box.");
            Assert.True(box.MinZFeet < levelBaseZsFeet[i], "Section box must extend slightly below floor base Z.");
            Assert.True(box.MaxZFeet > levelBaseZsFeet[i], "Section box must extend above floor base Z.");

            if (i + 1 < floorViews.Length)
            {
                // Strictly below next floor's base elevation -> zero upper-floor ceiling/slab occlusion!
                Assert.True(
                    box.MaxZFeet < levelBaseZsFeet[i + 1],
                    $"Floor '{floorViews[i].Name}' MaxZ ({box.MaxZFeet:F2}) must stay strictly below next floor base ({levelBaseZsFeet[i + 1]:F2}).");
                Assert.True(
                    box.MinZFeet < floorViews[i + 1].SectionBoxFeet.MinZFeet,
                    "Per-floor MinZ must be strictly monotonic.");
                Assert.True(
                    box.MaxZFeet < floorViews[i + 1].SectionBoxFeet.MaxZFeet,
                    "Per-floor MaxZ must be strictly monotonic.");
            }
        }

        // Populated 1F view has tighter XY than the full station (5..15 vs 0..40), while sparse B1F falls back to station XY
        ShowcaseView3DPlan viewB1F = floorViews.Single(v => v.Ordinal == -1);
        ShowcaseView3DPlan view1F = floorViews.Single(v => v.Ordinal == 0);
        Assert.True(viewB1F.UsedStationXyFallback);
        Assert.False(view1F.UsedStationXyFallback);
        Assert.True(view1F.SectionBoxFeet.WidthFeet < viewB1F.SectionBoxFeet.WidthFeet);
    }

    [Fact]
    public void ViewPlannerSanitizesForbiddenCharsDisambiguatesCollisionsAndReconcilesStaleViews()
    {
        // Two levels with Revit-forbidden chars that sanitize to the exact same base name
        ShowcaseLevel[] trickyLevels =
        {
            new("level:a", "A:1", 0, "1F[North]:{A}|;<>?`~\\", 0.0d, ShowcaseElevationSource.Surveyed),
            new("level:b", "A_1", 1, "1F_North___A_________", 4.5d, ShowcaseElevationSource.Surveyed),
        };

        ShowcaseSnapshot snapshot = BuildSnapshot("sanitize_views", trickyLevels, Array.Empty<ShowcaseLayer>());
        FormaPublishViewSetPlan plan = ShowcaseViewPlanner.Plan(snapshot, ShowcaseCoordinateTransform.Identity, Array.Empty<ShowcaseElementPlan>());

        Assert.All(plan.Views, v => Assert.True(ShowcaseViewPlanner.IsValidRevitViewName(v.Name), $"Invalid view name: '{v.Name}'"));
        Assert.Equal(plan.Views.Count, plan.Views.Select(v => v.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        ShowcaseView3DPlan[] floorViews = plan.Views.Where(v => v.Kind == ShowcaseViewKind.PerFloor).ToArray();
        Assert.Equal(2, floorViews.Length);
        Assert.EndsWith(" (2)", floorViews[1].Name, StringComparison.Ordinal);

        // Reconcile against existing views: 1 matching Forma view (preserve UniqueId), 1 stale Forma view (delete),
        // 1 unmanaged user 3D view (keep untouched!), 1 view template (ignore!).
        ShowcaseExistingViewInfo[] existingViews =
        {
            new(101L, "Forma - 00 Overview", "view-uid-overview"),
            new(102L, "Forma - B3F (removed)", "view-uid-stale-b3f"),
            new(103L, "{3D}", "view-uid-user-3d"),
            new(104L, "Forma - Template", "view-uid-template", isTemplate: true),
        };

        ShowcaseViewReconciliationPlan rec = ShowcaseViewPlanner.ReconcileViews(plan, existingViews);
        Assert.Equal(1, rec.UpdateInPlaceCount); // Forma - 00 Overview
        Assert.Equal(3, rec.CreateCount); // Network & POIs + 2 floor views
        Assert.Equal(1, rec.DeleteStaleCount); // Forma - B3F (removed) only!

        ShowcaseViewReconciliationStep overviewStep = rec.Steps.Single(s => s.ViewName == "Forma - 00 Overview");
        Assert.Equal(ShowcaseViewReconciliationActionKind.UpdateInPlace, overviewStep.Action);
        Assert.Equal("view-uid-overview", overviewStep.ExistingUniqueId);

        ShowcaseViewReconciliationStep deleteStep = Assert.Single(rec.Steps, s => s.Action == ShowcaseViewReconciliationActionKind.DeleteStale);
        Assert.Equal("Forma - B3F (removed)", deleteStep.ViewName);
        Assert.DoesNotContain(rec.Steps, s => s.ViewName == "{3D}");
        Assert.DoesNotContain(rec.Steps, s => s.ViewName == "Forma - Template");
    }

    [Fact]
    public void BudgetEvaluatorClassifiesOkWarningExceededAndEnforcesHardGates()
    {
        ShowcaseSnapshot snapshot = ShowcaseSnapshotReader.Parse(ShowcaseSnapshotReaderTests.BuildValidFullSnapshotJson());
        ShowcaseParameterRegistry registry = ShowcaseParameterRegistry.Build(snapshot);
        ShowcaseGeometryPlanResult f3 = ShowcaseGeometryPlanner.Plan(snapshot, ShowcaseCoordinateTransform.Identity, registry);
        ShowcasePoiAndNetworkPlanResult f4 = ShowcasePoiAndNetworkPlanner.Plan(snapshot, ShowcaseCoordinateTransform.Identity, registry);
        List<ShowcaseElementPlan> elements = f3.Elements.Concat(f4.Elements).ToList();

        // 1. Healthy small station -> Ok, Passed = true, TopHeaviestLayers populated
        ShowcaseBudgetReport okReport = ShowcaseBudgetEvaluator.Evaluate(
            elements,
            registry,
            outputFileSizeBytes: 12L * 1024L * 1024L); // 12 MiB

        Assert.True(okReport.Passed);
        Assert.Equal(ShowcaseBudgetSeverity.Ok, okReport.Severity);
        Assert.Equal(elements.Count, okReport.TotalElements);
        Assert.True(okReport.TotalEstimatedTriangles > 0);
        Assert.NotEmpty(okReport.TopHeaviestLayers);
        Assert.True(okReport.TopHeaviestLayers.Count <= 5);
        Assert.Empty(okReport.RecommendationsEn);
        Assert.Empty(okReport.RecommendationsJa);
        Assert.InRange(Math.Abs(okReport.LayerBreakdowns.Sum(b => b.ShareOfTotalTriangles) - 1.0d), 0d, 1e-9d);

        // 2. Soft warning limits -> Severity = Warning, Passed = true, EN/JA recommendations + WarnOnly appends warnings
        ShowcaseBudgetLimits warnLimits = new()
        {
            SoftTriangleLimit = Math.Max(1, okReport.TotalEstimatedTriangles - 1),
            HardTriangleLimit = okReport.TotalEstimatedTriangles + 10_000,
            SoftElementLimit = Math.Max(1, okReport.TotalElements - 1),
            HardElementLimit = okReport.TotalElements + 100,
            SingleElementTriangleWarningLimit = 1,
            SoftUniqueParameterLimit = 1,
            HardUniqueParameterLimit = 80,
            SoftFileSizeMiB = 5.0d,
            HardFileSizeMiB = 50.0d,
        };

        ShowcaseBudgetReport warnReport = ShowcaseBudgetEvaluator.Evaluate(
            elements,
            registry,
            outputFileSizeBytes: 12L * 1024L * 1024L,
            limits: warnLimits);

        Assert.True(warnReport.Passed);
        Assert.Equal(ShowcaseBudgetSeverity.Warning, warnReport.Severity);
        Assert.NotEmpty(warnReport.RecommendationsEn);
        Assert.NotEmpty(warnReport.RecommendationsJa);

        List<string> warnMessages = new();
        ShowcaseBudgetEvaluator.ApplyEnforcement(warnReport, ShowcaseBudgetEnforcementMode.WarnOnly, warnMessages);
        Assert.NotEmpty(warnMessages);
        Assert.All(warnMessages, w => Assert.StartsWith("[Budget:Warning]", w, StringComparison.Ordinal));

        List<string> reportOnlyMessages = new();
        ShowcaseBudgetEvaluator.ApplyEnforcement(warnReport, ShowcaseBudgetEnforcementMode.ReportOnly, reportOnlyMessages);
        Assert.Empty(reportOnlyMessages);

        // 3. Hard limit exceeded (triangles + elements + file size via WithOutputFileSize) -> Passed = false, FailOnHardLimit throws
        ShowcaseBudgetLimits hardLimits = new()
        {
            SoftTriangleLimit = 1,
            HardTriangleLimit = Math.Max(1, okReport.TotalEstimatedTriangles - 1),
            SoftElementLimit = 1,
            HardElementLimit = Math.Max(1, okReport.TotalElements - 1),
            SoftFileSizeMiB = 10.0d,
            HardFileSizeMiB = 20.0d,
        };

        ShowcaseBudgetReport preSaveExceeded = ShowcaseBudgetEvaluator.Evaluate(elements, registry, outputFileSizeBytes: null, limits: hardLimits);
        Assert.False(preSaveExceeded.Passed);
        Assert.Equal(ShowcaseBudgetSeverity.Exceeded, preSaveExceeded.Severity);

        ShowcaseBudgetExceededException ex = Assert.Throws<ShowcaseBudgetExceededException>(() =>
            ShowcaseBudgetEvaluator.ApplyEnforcement(preSaveExceeded, ShowcaseBudgetEnforcementMode.FailOnHardLimit));
        Assert.Same(preSaveExceeded, ex.Report);

        // 4. Post-save .rvt file size exceeding hard limit flips an otherwise Ok report to Exceeded
        ShowcaseBudgetReport postSaveExceeded = ShowcaseBudgetEvaluator.WithOutputFileSize(
            okReport,
            outputFileSizeBytes: 300L * 1024L * 1024L); // 300 MiB > 250 MiB hard limit
        Assert.False(postSaveExceeded.Passed);
        Assert.Equal(ShowcaseBudgetSeverity.Exceeded, postSaveExceeded.Severity);
        Assert.Equal(300.0d, postSaveExceeded.OutputFileSizeMiB!.Value, precision: 2);
        Assert.Throws<ShowcaseBudgetExceededException>(() =>
            ShowcaseBudgetEvaluator.ApplyEnforcement(postSaveExceeded, ShowcaseBudgetEnforcementMode.FailOnHardLimit));

        // 5. Per-marker triangle cap (> 48) triggers Exceeded even when total triangles are tiny
        ShowcaseElementPlan heavyMarkerPlan = new(
            ShowcaseLayerKind.PointFacility,
            "point_facility",
            "poi-heavy",
            ShowcaseExportGuard.ShowcaseApplicationId,
            "point_facility:poi-heavy",
            "KNZ_FRM_PoiMarker_poi-heavy",
            "OST_GenericModel",
            "level:0",
            "0",
            isNonOccludingOutlineBand: false,
            Array.Empty<ShowcaseExtrusionSolid>(),
            Array.Empty<ShowcaseParameterAssignment>(),
            overflowAttributesJson: null,
            elementKind: ShowcaseElementKind.PoiMarker,
            markerPlacements: new[]
            {
                new ShowcaseMarkerPlacement(new ShowcasePoint3D(0, 0, 0), ShowcaseMarkerShapeKind.PoiPin, 0.5, 1.5),
            });

        ShowcaseBudgetReport tightMarkerReport = ShowcaseBudgetEvaluator.Evaluate(
            new[] { heavyMarkerPlan },
            parameterRegistry: null,
            outputFileSizeBytes: null,
            limits: new ShowcaseBudgetLimits { MaxPoiMarkerTriangles = 32 }); // PoiPin = 44 > 32
        Assert.False(tightMarkerReport.Passed);
        Assert.Equal(44, tightMarkerReport.MaxPoiMarkerTrianglesObserved);
        Assert.Equal(
            ShowcaseBudgetSeverity.Exceeded,
            tightMarkerReport.Metrics.Single(m => m.MetricId == ShowcaseBudgetEvaluator.PoiMarkerTrianglesMetricId).Severity);
    }

    private static ShowcasePolygonGeometry MakeRect(double x0, double y0, double x1, double y1) =>
        new(new IReadOnlyList<Point2D>[]
        {
            new[]
            {
                new Point2D(x0, y0),
                new Point2D(x1, y0),
                new Point2D(x1, y1),
                new Point2D(x0, y1),
                new Point2D(x0, y0),
            },
        });

    private static ShowcaseFeature MakeUnit(string key, ShowcasePolygonGeometry geom, string name, string? note)
    {
        SortedDictionary<string, ShowcaseAttributeValue> attrs = new(StringComparer.Ordinal)
        {
            ["id"] = ShowcaseAttributeValue.FromText(key),
            ["name"] = ShowcaseAttributeValue.FromText(name),
            ["note"] = note is null ? ShowcaseAttributeValue.Null : ShowcaseAttributeValue.FromText(note),
        };
        return new ShowcaseFeature(key, "level:0", "0", geom, attrs);
    }

    private static IReadOnlyDictionary<string, ShowcaseAttributeValue> Attrs(string id) =>
        new SortedDictionary<string, ShowcaseAttributeValue>(StringComparer.Ordinal)
        {
            ["id"] = ShowcaseAttributeValue.FromText(id),
        };

    private static ShowcaseSnapshot BuildSnapshot(
        string scope,
        IReadOnlyList<ShowcaseLevel> levels,
        IReadOnlyList<ShowcaseLayer> layers) =>
        ShowcaseSnapshotReader.Parse(
            ShowcaseSnapshotReaderTests.SerializeSnapshotWithValidHash(
                scope: scope,
                basedOn: null,
                writtenUtc: "2026-11-27T00:00:00Z",
                kanizu: "0.9.0",
                epsg: 6677,
                levels: levels,
                layers: layers,
                quarantined: Array.Empty<QuarantinedShowcaseFeature>()));

    private static IEnumerable<Guid> CollectPopulatedGuids(ShowcaseElementPlan plan)
    {
        foreach (ShowcaseParameterAssignment assignment in plan.ParameterAssignments)
        {
            if (!assignment.IsNull && !assignment.IsTextFallback)
            {
                yield return assignment.Parameter.Guid;
            }
        }

        if (!string.IsNullOrEmpty(plan.OverflowAttributesJson))
        {
            yield return ShowcaseParameterRegistry.ComputeParameterGuid("showcase", "attributes_json", ShowcaseParameterDataType.Text);
        }
    }
}
