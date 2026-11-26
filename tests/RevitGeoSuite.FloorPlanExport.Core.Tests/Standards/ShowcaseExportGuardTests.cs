using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using RevitGeoSuite.CesiumHandoff;
using RevitGeoSuite.FloorPlanExport.Core.Diagnostics;
using RevitGeoSuite.FloorPlanExport.Core.GeoPackage;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Standards;

public sealed class ShowcaseExportGuardTests : IDisposable
{
    private readonly string _tempDir;

    public ShowcaseExportGuardTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RevitGeoSuite", "showcase-guard-tests", Guid.NewGuid().ToString("N"));
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
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void FrozenModelRoleGuidMatchesGisSharedParameters()
    {
        Assert.True(GisSharedParameters.TryGet(GisSharedParameters.GisModelRole, out GisParameterDefinition definition));
        Assert.Equal(definition.Guid, ShowcaseExportGuard.GisModelRoleParameterGuid);
    }

    [Theory]
    [InlineData("showcase", true, "showcase", "showcase")]
    [InlineData("SHOWCASE", true, "showcase", "showcase")]
    [InlineData("  ShOwCaSe  ", true, "showcase", "showcase")]
    [InlineData("authoring", false, "authoring", "authoring")]
    [InlineData("AUTHORING", false, "authoring", "authoring")]
    [InlineData("  Authoring ", false, "authoring", "authoring")]
    [InlineData(null, false, null, "authoring")]
    [InlineData("", false, null, "authoring")]
    [InlineData("   ", false, null, "authoring")]
    [InlineData("draft", false, null, "authoring")]
    [InlineData("showcase\n", false, null, "authoring")]
    public void ModelRoleClassificationAndNormalizationHandleAllCases(
        string? rawRole,
        bool expectedIsShowcase,
        string? expectedNormalized,
        string expectedEffective)
    {
        Assert.Equal(expectedIsShowcase, ShowcaseExportGuard.IsShowcaseModelRole(rawRole));
        Assert.Equal(expectedNormalized, ShowcaseExportGuard.NormalizeModelRole(rawRole));
        Assert.Equal(expectedEffective, ShowcaseExportGuard.ResolveEffectiveModelRole(rawRole));
    }

    [Theory]
    [InlineData("Kanizu.Showcase", true)]
    [InlineData("kanizu.showcase", true)]
    [InlineData("  KANIZU.SHOWCASE  ", true)]
    [InlineData("Kanizu.Authoring", false)]
    [InlineData("Revit.DirectShape", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void DirectShapeApplicationIdGuardIsCaseInsensitive(string? applicationId, bool expected)
    {
        Assert.Equal(expected, ShowcaseExportGuard.IsShowcaseApplicationId(applicationId));
        ShowcaseElementCheckResult result = ShowcaseExportGuard.ClassifyElement(applicationId: applicationId);
        Assert.Equal(expected, result.IsShowcase);
        Assert.Equal(expected ? ShowcaseElementReason.ShowcaseApplicationId : ShowcaseElementReason.None, result.Reason);
    }

    [Theory]
    [InlineData("KNZ_FRM_PoiMarker", true)]
    [InlineData("knz_frm_unit_extrusion", true)]
    [InlineData("  Knz_Frm_NetworkRibbon ", true)]
    [InlineData("KNZ_Stair_Monolithic", false)]
    [InlineData("KNZ_POI_FacilityMarker", false)]
    [InlineData("j 自動改札", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void FamilyTypeAndCategoryPrefixGuardCatchesKnzFrm(string? name, bool expected)
    {
        Assert.Equal(expected, ShowcaseExportGuard.IsShowcaseFamilyOrTypeName(name));

        ShowcaseElementCheckResult asFamily = ShowcaseExportGuard.ClassifyElement(familyName: name);
        Assert.Equal(expected, asFamily.IsShowcase);
        Assert.Equal(expected ? ShowcaseElementReason.ShowcaseFamilyName : ShowcaseElementReason.None, asFamily.Reason);

        ShowcaseElementCheckResult asType = ShowcaseExportGuard.ClassifyElement(typeName: name);
        Assert.Equal(expected, asType.IsShowcase);
        Assert.Equal(expected ? ShowcaseElementReason.ShowcaseTypeName : ShowcaseElementReason.None, asType.Reason);

        ShowcaseElementCheckResult asCategory = ShowcaseExportGuard.ClassifyElement(categoryName: name);
        Assert.Equal(expected, asCategory.IsShowcase);
        Assert.Equal(expected ? ShowcaseElementReason.ShowcaseCategoryName : ShowcaseElementReason.None, asCategory.Reason);
    }

    [Theory]
    [InlineData("ks_layer", true)]
    [InlineData("KS_SymbolId", true)]
    [InlineData("  ks_attributes_json ", true)]
    [InlineData("KS_Id", true)]
    [InlineData("gis_category", false)]
    [InlineData("K_Footprint", false)]
    [InlineData("ks", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CompanionParameterPrefixGuardCatchesKsAndLowercaseKs(string? parameterName, bool expected)
    {
        Assert.Equal(expected, ShowcaseExportGuard.IsShowcaseParameterName(parameterName));

        ShowcaseElementCheckResult result = ShowcaseExportGuard.ClassifyElement(
            parameterNames: parameterName == null ? null : new[] { "Mark", parameterName });
        Assert.Equal(expected, result.IsShowcase);
        Assert.Equal(expected ? ShowcaseElementReason.ShowcaseParameter : ShowcaseElementReason.None, result.Reason);
    }

    [Fact]
    public void LinkedShowcaseDocumentClassifiesElementsEvenWithoutElementMarkers()
    {
        bool isShowcase = ShowcaseExportGuard.IsShowcaseElement(
            applicationId: null,
            familyName: "Standard Floor",
            typeName: "GIS_walkway",
            parameterNames: new[] { "gis_category", "id" },
            sourceDocumentModelRole: "SHOWCASE",
            out ShowcaseElementReason reason,
            out string detail);

        Assert.True(isShowcase);
        Assert.Equal(ShowcaseElementReason.LinkedShowcaseDocument, reason);
        Assert.Contains("showcase", detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuthoringExportGateBlocksShowcaseAndAllowsLegacyAndAuthoringModels()
    {
        ShowcaseExportGateResult blocked = ShowcaseExportGuard.EvaluateAuthoringExportGate("SHOWCASE", "2D GeoPackage");
        Assert.True(blocked.IsBlocked);
        Assert.False(blocked.IsAllowed);
        Assert.Equal("showcase", blocked.NormalizedModelRole);
        Assert.Contains("gis_model_role", blocked.Message);
        Assert.Contains("2D GeoPackage", blocked.Message);
        Assert.False(string.IsNullOrWhiteSpace(blocked.MessageJa));
        Assert.Throws<InvalidOperationException>(() => ShowcaseExportGuard.ThrowIfAuthoringExportBlocked("showcase", "3D Tiles"));

        // Legacy v1/v2 models without gis_model_role and explicit authoring models pass cleanly.
        Assert.True(ShowcaseExportGuard.EvaluateAuthoringExportGate(null, "2D GeoPackage").IsAllowed);
        Assert.True(ShowcaseExportGuard.EvaluateAuthoringExportGate("", "2D GeoPackage").IsAllowed);
        Assert.True(ShowcaseExportGuard.EvaluateAuthoringExportGate("authoring", "3D Tiles").IsAllowed);
        ShowcaseExportGuard.ThrowIfAuthoringExportBlocked(null, "2D GeoPackage");
        ShowcaseExportGuard.ThrowIfAuthoringExportBlocked("authoring", "3D Tiles");
    }

    [Fact]
    public void GisStandardCheckBlocksExportWhenModelRoleIsShowcase()
    {
        GisStandardModelState showcaseModel = new()
        {
            StandardVersionText = GisStandard.Version.ToString(),
            BoundParameters = GisSharedParameters.All
                .Select(definition => new GisBoundParameterState(definition.Name, definition.Guid, definition.Binding))
                .ToList(),
            GisViewCount = 2,
            ModelRole = "showcase",
        };

        GisStandardStatus status = GisStandardCheck.Check(showcaseModel);
        Assert.True(status.BlocksExport);
        Assert.Contains(status.Issues, issue => issue.BlocksExport && issue.Message.Contains("gis_model_role"));

        showcaseModel.ModelRole = "authoring";
        Assert.False(GisStandardCheck.Check(showcaseModel).BlocksExport);

        showcaseModel.ModelRole = string.Empty;
        Assert.False(GisStandardCheck.Check(showcaseModel).BlocksExport);
    }

    [Fact]
    public void KzFed009FailsWhenHostOrLinkedModelHasShowcaseRoleAndProvidesEnJaMessages()
    {
        ReadinessCheckSnapshot clean = CleanProjectSnapshot();
        ShowcaseRuleEvaluationResult cleanEval = ShowcaseReadinessEvaluator.EvaluateFederationGuard(clean);
        Assert.Equal("KZ-FED-009", cleanEval.RuleId);
        Assert.Equal(RuleSeverity.Error, cleanEval.Severity);
        Assert.Equal(ReadinessCheckStatus.Pass, cleanEval.Status);
        Assert.False(string.IsNullOrWhiteSpace(cleanEval.MessageEn));
        Assert.False(string.IsNullOrWhiteSpace(cleanEval.MessageJa));

        ReadinessCheckSnapshot showcaseHost = CleanProjectSnapshot();
        showcaseHost.HostModelRole = "SHOWCASE";
        ShowcaseRuleEvaluationResult hostFail = ShowcaseReadinessEvaluator.EvaluateFederationGuard(showcaseHost);
        Assert.Equal(ReadinessCheckStatus.Fail, hostFail.Status);
        Assert.Contains("showcase", hostFail.MessageEn, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("showcase", hostFail.MessageJa, StringComparison.OrdinalIgnoreCase);

        ReadinessCheckSnapshot linkedShowcase = CleanProjectSnapshot();
        linkedShowcase.LinkedModels = new[]
        {
            new LinkedModelReadinessInfo("Tokyo_Platform.rvt", isLoaded: true, modelId: "platform-01", modelRole: "authoring"),
            new LinkedModelReadinessInfo("Tokyo_GIS_Showcase.rvt", isLoaded: true, modelId: "showcase-99", modelRole: "showcase"),
        };
        ShowcaseRuleEvaluationResult linkFail = ShowcaseReadinessEvaluator.EvaluateFederationGuard(linkedShowcase);
        Assert.Equal(ReadinessCheckStatus.Fail, linkFail.Status);
        Assert.Contains("Tokyo_GIS_Showcase.rvt", linkFail.MessageEn);
        Assert.Contains("Tokyo_GIS_Showcase.rvt", linkFail.MessageJa);

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(linkedShowcase, ReadinessMode.Project);
        Assert.False(report.IsReady);
        ReadinessCheckItem item = report.Groups.SelectMany(g => g.Items).Single(i => i.RuleId == "KZ-FED-009");
        Assert.Equal(ReadinessCheckStatus.Fail, item.Status);
    }

    [Fact]
    public void KzElem005FailsWhenShowcaseElementsOrKnzFrmFamiliesLeakIntoAuthoringModel()
    {
        ReadinessCheckSnapshot clean = CleanProjectSnapshot();
        ShowcaseRuleEvaluationResult cleanEval = ShowcaseReadinessEvaluator.EvaluateShowcaseElements(clean);
        Assert.Equal("KZ-ELEM-005", cleanEval.RuleId);
        Assert.Equal(RuleSeverity.Error, cleanEval.Severity);
        Assert.Equal(ReadinessCheckStatus.Pass, cleanEval.Status);
        Assert.False(string.IsNullOrWhiteSpace(cleanEval.MessageEn));
        Assert.False(string.IsNullOrWhiteSpace(cleanEval.MessageJa));

        ReadinessCheckSnapshot leaked = CleanProjectSnapshot();
        leaked.ShowcaseElements = new[]
        {
            new ShowcaseElementFinding(9001, "Generic Models: Unit_01", ShowcaseElementReason.ShowcaseApplicationId, "Kanizu.Showcase"),
            new ShowcaseElementFinding(9002, "CustomMarker", ShowcaseElementReason.ShowcaseParameter, "ks_layer"),
        };
        leaked.LoadedFamilies = leaked.LoadedFamilies
            .Concat(new[]
            {
                new LoadedFamilyCheckInfo(9003, "KNZ_FRM_PoiMarker", "Default", "OST_GenericModel", instanceIds: new[] { 9101L }),
            })
            .ToList();

        ShowcaseRuleEvaluationResult leakedEval = ShowcaseReadinessEvaluator.EvaluateShowcaseElements(leaked);
        Assert.Equal(ReadinessCheckStatus.Fail, leakedEval.Status);
        Assert.Contains(9001L, leakedEval.ElementIds);
        Assert.Contains(9002L, leakedEval.ElementIds);
        Assert.Contains(9101L, leakedEval.ElementIds);
        Assert.Contains("Kanizu.Showcase", leakedEval.MessageEn);
        Assert.Contains("KNZ_FRM_PoiMarker", leakedEval.MessageEn);
        Assert.False(string.IsNullOrWhiteSpace(leakedEval.MessageJa));

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(leaked, ReadinessMode.Project);
        Assert.False(report.IsReady);
        ReadinessCheckItem item = report.Groups.SelectMany(g => g.Items).Single(i => i.RuleId == "KZ-ELEM-005");
        Assert.Equal(ReadinessCheckStatus.Fail, item.Status);

        HandoverReceipt receipt = HandoverReceipt.FromReadinessReport(
            report,
            "1.0.0",
            "2024.0.0",
            "contractor",
            new HandoverReceiptModel("model-1", new Guid("11111111-2222-3333-4444-555555555555"), 5, "host"),
            new DateTime(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(HandoverReceiptVerdict.Fail, receipt.Verdict);
        Assert.Equal(HandoverRuleStatus.Fail, receipt.Rules.Single(r => r.RuleId == "KZ-ELEM-005").Status);
        Assert.Equal(HandoverRuleStatus.Pass, receipt.Rules.Single(r => r.RuleId == "KZ-FED-009").Status);
    }

    [Fact]
    public void RoundTripSkipsShowcasePackagesInStationFolder()
    {
        string station = Path.Combine(_tempDir, "station");
        WriteStationVersion(station, "concourse-11111111", "20261126T000000Z-aaaa1111", "11111111-0000-4000-8000-000000000001", "authoring");
        WriteStationVersion(station, "showcase-22222222", "20261126T000001Z-bbbb2222", "22222222-0000-4000-8000-000000000002", "showcase");

        IReadOnlyList<RoundTripModel> models = RoundTrip.Models(station);
        RoundTripModel only = Assert.Single(models);
        Assert.Equal("11111111-0000-4000-8000-000000000001", only.Model);
    }

    [Fact]
    public void TwoDAuthoringExportIsByteIdenticalWithLinkedShowcaseAndLeakedShowcaseElements()
    {
        // Authoring baseline: host concourse + authoring linked platform.
        Authoring2DLink authoringLink = new(
            "Tokyo_Platform.rvt",
            modelRole: "authoring",
            units: new[]
            {
                new Authoring2DCandidate(
                    "unit-platform-1",
                    "walkway",
                    Rectangle(10, 0, 20, 6),
                    new ShowcaseElementDescriptor(familyName: "Floor", typeName: "GIS_walkway", parameterNames: new[] { "gis_category", "id" }, sourceDocumentModelRole: "authoring")),
            },
            openings: new[]
            {
                new Authoring2DLineCandidate(
                    "opening-platform-1",
                    "pedestrian",
                    Line(10, 2, 10, 4),
                    new ShowcaseElementDescriptor(familyName: "StationDoor", typeName: "Sliding_1800", parameterNames: new[] { "gis_door", "gis_access_con" }, sourceDocumentModelRole: "authoring")),
            });

        Authoring2DCandidate[] hostUnitsBaseline =
        {
            new(
                "unit-concourse-1",
                "walkway",
                Rectangle(0, 0, 10, 8),
                new ShowcaseElementDescriptor(familyName: "Floor", typeName: "GIS_walkway", parameterNames: new[] { "gis_category", "id" })),
            new(
                "fixture-gate-1",
                "turnstile",
                Rectangle(4, 2, 6, 4),
                new ShowcaseElementDescriptor(familyName: "j 自動改札", typeName: "Standard", parameterNames: new[] { "gis_kind", "gis_fixture_type" })),
        };

        Authoring2DLineCandidate[] hostOpeningsBaseline =
        {
            new(
                "opening-host-1",
                "pedestrian.principal",
                Line(0, 3, 0, 5),
                new ShowcaseElementDescriptor(familyName: "StationDoor", typeName: "Auto_2000", parameterNames: new[] { "gis_door" })),
        };

        // Polluted authoring model: adds a linked Forma Companion showcase .rvt + host-leaked
        // Kanizu.Showcase DirectShape + KNZ_FRM_* family instances + KS_*/ks_* tagged elements.
        Authoring2DLink showcaseLink = new(
            "Tokyo_GIS_Showcase.rvt",
            modelRole: "showcase",
            units: new[]
            {
                new Authoring2DCandidate(
                    "showcase-link-unit-1",
                    "retail",
                    Rectangle(100, 100, 120, 120),
                    new ShowcaseElementDescriptor(applicationId: "Kanizu.Showcase", familyName: "DirectShape", sourceDocumentModelRole: "showcase")),
            },
            openings: new[]
            {
                new Authoring2DLineCandidate(
                    "showcase-link-opening-1",
                    "pedestrian",
                    Line(100, 100, 102, 100),
                    new ShowcaseElementDescriptor(applicationId: "Kanizu.Showcase", sourceDocumentModelRole: "showcase")),
            });

        Authoring2DCandidate[] hostUnitsPolluted = hostUnitsBaseline
            .Concat(new[]
            {
                new Authoring2DCandidate(
                    "leaked-ds-1",
                    "walkway",
                    Rectangle(50, 50, 60, 60),
                    new ShowcaseElementDescriptor(applicationId: "Kanizu.Showcase", familyName: "DirectShape", typeName: "UnitExtrusion")),
                new Authoring2DCandidate(
                    "leaked-knz-frm-1",
                    "fixture",
                    Rectangle(52, 52, 53, 53),
                    new ShowcaseElementDescriptor(familyName: "KNZ_FRM_PoiMarker", typeName: "Standard")),
                new Authoring2DCandidate(
                    "leaked-ks-param-1",
                    "retail",
                    Rectangle(55, 55, 58, 58),
                    new ShowcaseElementDescriptor(familyName: "GenericBox", typeName: "Box", parameterNames: new[] { "Mark", "ks_layer", "KS_SymbolId" })),
            })
            .ToArray();

        Authoring2DLineCandidate[] hostOpeningsPolluted = hostOpeningsBaseline
            .Concat(new[]
            {
                new Authoring2DLineCandidate(
                    "leaked-opening-1",
                    "pedestrian",
                    Line(50, 50, 52, 50),
                    new ShowcaseElementDescriptor(familyName: "KNZ_FRM_OpeningLine", typeName: "Ribbon")),
                new Authoring2DLineCandidate(
                    "leaked-opening-2",
                    "pedestrian",
                    Line(54, 50, 56, 50),
                    new ShowcaseElementDescriptor(applicationId: "kanizu.showcase")),
            })
            .ToArray();

        string baselinePath = Path.Combine(_tempDir, "baseline.gpkg");
        string pollutedPath = Path.Combine(_tempDir, "polluted.gpkg");

        ExportLayer[] baselineLayers = BuildFiltered2DLayers(hostUnitsBaseline, hostOpeningsBaseline, new[] { authoringLink });
        ExportLayer[] pollutedLayers = BuildFiltered2DLayers(hostUnitsPolluted, hostOpeningsPolluted, new[] { authoringLink, showcaseLink });

        GpkgWriter writer = new();
        writer.Write(baselinePath, 6677, baselineLayers);
        writer.Write(pollutedPath, 6677, pollutedLayers);

        // Layer fingerprints and every written GeoPackage feature row + WKB geometry bytes are identical.
        ExportFingerprintBuilder fingerprinter = new();
        Assert.Equal(
            fingerprinter.ComputeLayerFingerprint(baselineLayers),
            fingerprinter.ComputeLayerFingerprint(pollutedLayers));
        Assert.Equal(ReadGpkgRows(baselinePath, "unit"), ReadGpkgRows(pollutedPath, "unit"));
        Assert.Equal(ReadGpkgRows(baselinePath, "opening"), ReadGpkgRows(pollutedPath, "opening"));
        Assert.Equal(3, ReadGpkgRows(pollutedPath, "unit").Count);
        Assert.Equal(2, ReadGpkgRows(pollutedPath, "opening").Count);
    }

    private static ExportLayer[] BuildFiltered2DLayers(
        IReadOnlyList<Authoring2DCandidate> hostUnits,
        IReadOnlyList<Authoring2DLineCandidate> hostOpenings,
        IReadOnlyList<Authoring2DLink> links)
    {
        IReadOnlyList<Authoring2DLink> authoringLinks =
            ShowcaseExportGuard.FilterAuthoringLinkedDocuments(links, link => link.ModelRole, link => link.Name);

        IReadOnlyList<Authoring2DCandidate> units = ShowcaseExportGuard.FilterAuthoringItems(
            hostUnits.Concat(authoringLinks.SelectMany(link => link.Units)),
            item => item.Descriptor);
        IReadOnlyList<Authoring2DLineCandidate> openings = ShowcaseExportGuard.FilterAuthoringItems(
            hostOpenings.Concat(authoringLinks.SelectMany(link => link.Openings)),
            item => item.Descriptor);

        ExportLayer unitLayer = new("unit", GpkgGeometryType.MultiPolygon, new[]
        {
            new AttributeDefinition("id", ExportAttributeType.Text),
            new AttributeDefinition("category", ExportAttributeType.Text),
        });
        foreach (Authoring2DCandidate unit in units)
        {
            unitLayer.AddFeature(new ExportPolygon(
                unit.Polygons,
                new Dictionary<string, object?> { ["id"] = unit.Id, ["category"] = unit.Category }));
        }

        ExportLayer openingLayer = new("opening", GpkgGeometryType.LineString, new[]
        {
            new AttributeDefinition("id", ExportAttributeType.Text),
            new AttributeDefinition("category", ExportAttributeType.Text),
        });
        foreach (Authoring2DLineCandidate opening in openings)
        {
            openingLayer.AddFeature(new ExportLineString(
                opening.Line,
                new Dictionary<string, object?> { ["id"] = opening.Id, ["category"] = opening.Category }));
        }

        return new[] { unitLayer, openingLayer };
    }

    private static List<string> ReadGpkgRows(string path, string tableName)
    {
        List<string> rows = new();
        using SqliteConnection connection = new($"Data Source={path};Mode=ReadOnly");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT id, category, hex(geom) FROM \"{tableName}\" ORDER BY fid";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add($"{reader.GetString(0)}|{reader.GetString(1)}|{reader.GetString(2)}");
        }

        return rows;
    }

    private static IReadOnlyList<Polygon2D> Rectangle(double x0, double y0, double x1, double y1) =>
        new[]
        {
            new Polygon2D(new[]
            {
                new Point2D(x0, y0),
                new Point2D(x1, y0),
                new Point2D(x1, y1),
                new Point2D(x0, y1),
                new Point2D(x0, y0),
            }),
        };

    private static LineString2D Line(double x0, double y0, double x1, double y1) =>
        new(new[] { new Point2D(x0, y0), new Point2D(x1, y1) });

    private static void WriteStationVersion(string station, string modelFolder, string versionFolder, string modelId, string? modelRole)
    {
        string versionDir = Path.Combine(station, CesiumStationFolder.RevitDirectoryName, modelFolder, versionFolder);
        Directory.CreateDirectory(Path.Combine(versionDir, "gis"));
        File.WriteAllText(Path.Combine(versionDir, "gis", "model.gpkg"), "gpkg");
        CesiumPackageManifest manifest = new()
        {
            PackageId = Guid.NewGuid().ToString("D"),
            CreatedUtc = new DateTime(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc),
            Source = new CesiumPackageSource
            {
                Model = modelFolder + ".rvt",
                DocumentKey = modelFolder,
                ModelId = modelId,
                ModelRole = modelRole,
            },
            Gis = new CesiumPackageGis
            {
                Format = "geopackage",
                Artifacts = new List<CesiumPackageGisArtifact> { new() { Path = "gis/model.gpkg" } },
            },
        };
        File.WriteAllText(Path.Combine(versionDir, "cesium-package.json"), CesiumPackageManifestSerializer.Serialize(manifest));
    }

    private static ReadinessCheckSnapshot CleanProjectSnapshot()
    {
        GisToolStandard standard = GisToolStandard.LoadEmbedded();
        return new ReadinessCheckSnapshot
        {
            DocumentTitle = "TokyoStation_Concourse.rvt",
            DocumentPath = @"D:\Projects\TokyoStation_Concourse.rvt",
            IsEmptyModel = false,
            StandardVersionText = standard.Version.ToString(),
            BoundParameters = standard.Parameters
                .Select(p => new BoundParameterInfo(p.Name, p.Guid, p.Binding, p.Categories))
                .ToList(),
            Materials = standard.Materials
                .Select((m, idx) => new MaterialInfo(100 + idx, m.Name, m.FillHex))
                .ToList(),
            FloorTypes = standard.FloorTypes
                .Select((ft, idx) => new FloorTypeInfo(200 + idx, ft.Name, ft.GisCategory, ft.GisRestriction, ft.Material))
                .ToList(),
            LoadedFamilies = new[]
            {
                new LoadedFamilyCheckInfo(301, "j EV", "EV_1500", "OST_MechanicalEquipment", gisKind: "elevator", hasGisKindParameter: true),
                new LoadedFamilyCheckInfo(304, "StationDoor", "Sliding_1800", "OST_Doors", gisDoor: "sliding", gisAccessCon: "none", hasGisDoorParameter: true, hasGisAccessConParameter: true),
            },
            Schedules = TemplateStandardDefinitions.QaSchedules
                .Select((s, idx) => new QaScheduleInfo(
                    400 + idx,
                    s.Name,
                    s.RequiredFields,
                    s.CategoryName,
                    s.RequiredFields.Select(name => new QaScheduleFieldInfo(
                        name,
                        GisSharedParameters.TryGet(name, out GisParameterDefinition def) ? def.Guid : (Guid?)null))))
                .ToList(),
            ViewTemplateNames = new[] { TemplateStandardDefinitions.ExportPlanViewTemplateName },
            ProjectCrsEpsgCode = 6677,
            ProjectCrsName = "JGD2011 / Japan Plane Rectangular CS IX",
            IsGeoreferenceVerified = true,
            HostModelId = "tokyo-concourse-01",
            HostModelRole = "authoring",
            LinkedModels = new[]
            {
                new LinkedModelReadinessInfo("TokyoStation_Platform.rvt", isLoaded: true, modelId: "tokyo-platform-02", modelRole: "authoring"),
            },
            Levels = new[]
            {
                new LevelInfo(501, "1FL", ordinal: 0, shortName: "1F", hasFloors: true),
            },
        };
    }

    private sealed class Authoring2DLink
    {
        public Authoring2DLink(string name, string? modelRole, IReadOnlyList<Authoring2DCandidate> units, IReadOnlyList<Authoring2DLineCandidate> openings)
        {
            Name = name;
            ModelRole = modelRole;
            Units = units;
            Openings = openings;
        }

        public string Name { get; }
        public string? ModelRole { get; }
        public IReadOnlyList<Authoring2DCandidate> Units { get; }
        public IReadOnlyList<Authoring2DLineCandidate> Openings { get; }
    }

    private sealed class Authoring2DCandidate
    {
        public Authoring2DCandidate(string id, string category, IReadOnlyList<Polygon2D> polygons, ShowcaseElementDescriptor descriptor)
        {
            Id = id;
            Category = category;
            Polygons = polygons;
            Descriptor = descriptor;
        }

        public string Id { get; }
        public string Category { get; }
        public IReadOnlyList<Polygon2D> Polygons { get; }
        public ShowcaseElementDescriptor Descriptor { get; }
    }

    private sealed class Authoring2DLineCandidate
    {
        public Authoring2DLineCandidate(string id, string category, LineString2D line, ShowcaseElementDescriptor descriptor)
        {
            Id = id;
            Category = category;
            Line = line;
            Descriptor = descriptor;
        }

        public string Id { get; }
        public string Category { get; }
        public LineString2D Line { get; }
        public ShowcaseElementDescriptor Descriptor { get; }
    }
}
