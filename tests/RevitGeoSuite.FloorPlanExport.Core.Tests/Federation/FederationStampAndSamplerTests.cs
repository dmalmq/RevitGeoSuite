using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Federation;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Federation;

public sealed class FederationStampAndSamplerTests
{
    private const double FeetPerMeter = 1.0d / 0.3048d;

    [Fact]
    public void FirstExportWritesTheFederationRoleChosenBeforeAStationFolderExisted()
    {
        string folder = Path.Combine(Path.GetTempPath(), "kz-fed-stamp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = GisModelFile.PathFor(folder, "spoke-01");
            Assert.False(File.Exists(path));

            GisModelFile file = GisModelFile.CreateFromTemplate();
            file.ApplyFederationStamp("spoke", "hub-001");
            file.Save(path);

            GisModelFile reloaded = GisModelFile.TryLoad(path)!;
            Assert.NotNull(reloaded.Federation);
            Assert.Equal("spoke", reloaded.Federation!.Mode);
            Assert.Equal("hub-001", reloaded.Federation.HubModelId);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void StandaloneStampWritesNoFederationKey()
    {
        GisModelFile file = GisModelFile.CreateFromTemplate();
        file.Federation = GisModelFederation.Create(KanizuFederationMode.Hub);

        file.ApplyFederationStamp("standalone", null);

        Assert.Null(file.Federation);
    }

    [Fact]
    public void ModelWithoutAStampKeepsWhatItsFileSays()
    {
        GisModelFile file = GisModelFile.CreateFromTemplate();
        file.Federation = GisModelFederation.Create(KanizuFederationMode.Spoke, "hub-001");

        file.ApplyFederationStamp(null, null);

        Assert.Equal("spoke", file.Federation!.Mode);
        Assert.Equal("hub-001", file.Federation.HubModelId);
    }

    [Fact]
    public void ResolvePrefersTheStampOverTheFile()
    {
        GisModelFederation fileBlock = GisModelFederation.Create(KanizuFederationMode.Hub);

        Assert.Same(fileBlock, GisModelFederation.Resolve(null, null, fileBlock));
        Assert.Equal("spoke", GisModelFederation.Resolve("spoke", "hub-9", fileBlock)!.Mode);
        Assert.Null(GisModelFederation.Resolve("standalone", null, fileBlock));
        Assert.Equal("satellite", GisModelFederation.FromStamp("satellite", null)!.Mode);
    }

    [Fact]
    public void StationExportViewsAreTheListedViewsElseTheGisViewsElseNone()
    {
        (string Id, string Name)[] views = { ("v1", "GIS_1FL"), ("v2", "GIS_2FL"), ("v3", "Structural 2SL") };
        GisModelFile listing = GisModelFile.CreateFromTemplate();
        listing.Views = new List<GisModelView> { new() { UniqueId = "v2", Name = "GIS_2FL" } };

        Assert.Equal(new[] { "v2" }, Ids(GisModelFile.ChooseStationExportViews(views, v => v.Id, v => v.Name, listing)));
        Assert.Equal(new[] { "v1", "v2" }, Ids(GisModelFile.ChooseStationExportViews(views, v => v.Id, v => v.Name, null)));
        Assert.Empty(GisModelFile.ChooseStationExportViews(new[] { views[2] }, v => v.Id, v => v.Name, null));
        Assert.Equal(new[] { "v3" }, Ids(GisModelFile.ChooseDefaultViews(new[] { views[2] }, v => v.Id, v => v.Name, null)));

        static string[] Ids(IEnumerable<(string Id, string Name)> chosen) => chosen.Select(v => v.Id).ToArray();
    }

    [Fact]
    public void ExportLevelsComeFromTheListedViewsLevels()
    {
        GisModelFile file = GisModelFile.CreateFromTemplate();
        file.Views = new List<GisModelView>
        {
            new() { UniqueId = "v1", Name = "GIS_1FL", LevelUniqueId = "level-1fl" },
            new() { UniqueId = "v0", Name = "GIS_old" },
        };

        GisModelFile reloaded = GisModelFile.Parse(Serialize(file), "test");

        Assert.Equal(new[] { "level-1fl" }, reloaded.ExportLevelUniqueIds().ToArray());
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(Serialize(file), "levelUniqueId"));
    }

    [Fact]
    public void SamplePointsSpanTheBaselineInFeet()
    {
        IReadOnlyList<(string Label, FederationPoint3D LinkInternalFeet)> points = ControlPointSampler.SamplePoints(100d);

        Assert.Equal(3, points.Count);
        Assert.Equal(new FederationPoint3D(0d, 0d, 0d), points[0].LinkInternalFeet);
        Assert.Equal(100d * FeetPerMeter, points[1].LinkInternalFeet.X, 9);
        Assert.Equal(0d, points[1].LinkInternalFeet.Y);
        Assert.Equal(100d * FeetPerMeter, points[2].LinkInternalFeet.Y, 9);
        Assert.Equal("+X Baseline (100m,0,0)", points[1].Label);
    }

    [Fact]
    public void PairConvertsSharedFeetToMetersOnBothChains()
    {
        IReadOnlyList<ControlPointPair> pairs = ControlPointSampler.Pair(
            100d,
            p => p,
            p => new FederationPoint3D(p.X + 1000d, p.Y, p.Z),
            p => new FederationPoint3D(p.X + 1000d, p.Y, p.Z + 10d));

        Assert.Equal(1000d * 0.3048d, pairs[0].HostSharedMeters.X, 9);
        Assert.Equal(0d, pairs[0].HostSharedMeters.Z, 9);
        Assert.Equal(10d * 0.3048d, pairs[0].LinkOwnSharedMeters.Z, 9);
        Assert.Equal((1000d * 0.3048d) + 100d, pairs[1].HostSharedMeters.X, 9);
    }

    [Fact]
    public void PairAppliesTheInstanceTransformBeforeTheHostSharedPosition()
    {
        Func<FederationPoint3D, FederationPoint3D> linkToHost = RotateThenShift(90d, 50d, 0d);
        Func<FederationPoint3D, FederationPoint3D> hostShared = RotateThenShift(30d, 2000d, 3000d);
        Func<FederationPoint3D, FederationPoint3D> linkShared = p => hostShared(linkToHost(p));
        Func<FederationPoint3D, FederationPoint3D> wrongOrder = p => linkToHost(hostShared(p));

        PoseConsistencyResult right = PoseConsistencyEvaluator.Evaluate(ControlPointSampler.Pair(100d, linkToHost, hostShared, linkShared));
        PoseConsistencyResult reversed = PoseConsistencyEvaluator.Evaluate(ControlPointSampler.Pair(100d, linkToHost, hostShared, wrongOrder));

        Assert.True(right.IsAligned);
        Assert.Equal(0d, right.MaxHorizontalTranslationMm, 6);
        Assert.False(reversed.IsAligned);
    }

    [Fact]
    public void BestPositionMatchesEachInstanceToItsOwnNamedSharedPosition()
    {
        Func<FederationPoint3D, FederationPoint3D> hostShared = RotateThenShift(15d, 5000d, 8000d);
        Func<FederationPoint3D, FederationPoint3D> instanceAtA = RotateThenShift(0d, 0d, 0d);
        Func<FederationPoint3D, FederationPoint3D> instanceAtB = RotateThenShift(45d, 600d, 200d);
        SharedPositionCandidate[] candidates =
        {
            new("Site A", p => hostShared(instanceAtA(p))),
            new("Site B", p => hostShared(instanceAtB(p))),
        };

        SampledLinkPose atA = ControlPointSampler.BestPosition(100d, instanceAtA, hostShared, candidates)!;
        SampledLinkPose atB = ControlPointSampler.BestPosition(100d, instanceAtB, hostShared, candidates)!;

        Assert.Equal("Site A", atA.PositionName);
        Assert.True(atA.Pose.IsAligned);
        Assert.Equal("Site B", atB.PositionName);
        Assert.True(atB.Pose.IsAligned);
    }

    [Fact]
    public void BestPositionReportsTheClosestPositionWhenNoneAlign()
    {
        Func<FederationPoint3D, FederationPoint3D> hostShared = p => p;
        SharedPositionCandidate[] candidates =
        {
            new("Far", RotateThenShift(0d, 300d, 0d)),
            new("Near", RotateThenShift(0d, 1d, 0d)),
        };

        SampledLinkPose sampled = ControlPointSampler.BestPosition(100d, p => p, hostShared, candidates)!;

        Assert.Equal("Near", sampled.PositionName);
        Assert.False(sampled.Pose.IsAligned);
        Assert.Equal(304.8d, sampled.Pose.MaxHorizontalTranslationMm, 6);
    }

    [Fact]
    public void BestPositionIsNullWithoutSharedPositions()
    {
        Assert.Null(ControlPointSampler.BestPosition(100d, p => p, p => p, Array.Empty<SharedPositionCandidate>()));
    }

    private static Func<FederationPoint3D, FederationPoint3D> RotateThenShift(double degrees, double dxFeet, double dyFeet)
    {
        double radians = degrees * Math.PI / 180d;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        return p => new FederationPoint3D((p.X * cos) - (p.Y * sin) + dxFeet, (p.X * sin) + (p.Y * cos) + dyFeet, p.Z);
    }

    private static string Serialize(GisModelFile file)
    {
        string path = Path.Combine(Path.GetTempPath(), "kz-gis-model-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            file.Save(path);
            return File.ReadAllText(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
