using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Validation;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using Xunit;

namespace RevitGeoSuite.Validation.Tests;

public sealed class ProjectHealthCheckerTests
{
    [Fact]
    public void BuildSummary_returns_ready_when_shared_geo_state_is_complete()
    {
        CrsRegistry registry = new CrsRegistry();
        CoordinateTransformer transformer = new CoordinateTransformer(registry);
        JapanMeshCalculator meshCalculator = new JapanMeshCalculator();
        CoordinateValidator validator = new CoordinateValidator(registry, transformer, meshCalculator);
        ProjectHealthChecker checker = new ProjectHealthChecker(validator);
        MeshCode meshCode = meshCalculator.Calculate(35.6762, 139.7653, JapanMeshLevel.Tertiary);
        CurrentProjectStateSummary currentState = new CurrentProjectStateSummary
        {
            DocumentTitle = "ValidationSample.rvt",
            IsSupportedDocument = true,
            IsReadOnly = false,
            HasStoredGeoInfo = true
        };
        GeoProjectInfo info = new GeoProjectInfo
        {
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            Origin = new ProjectOrigin { Latitude = 35.6762, Longitude = 139.7653, ElevationMeters = 12.5 },
            PrimaryMeshCode = meshCode,
            Confidence = GeoConfidenceLevel.Verified,
            SetupSource = "Test"
        };

        ProjectHealthSummary summary = checker.BuildSummary(currentState, info);

        Assert.Empty(summary.Findings);
        Assert.Equal(ExportReadinessStatus.Ready, summary.ExportReadiness.Status);
    }

    [Fact]
    public void BuildSummary_returns_blocking_error_when_shared_geo_state_is_missing()
    {
        CrsRegistry registry = new CrsRegistry();
        CoordinateTransformer transformer = new CoordinateTransformer(registry);
        CoordinateValidator validator = new CoordinateValidator(registry, transformer, new JapanMeshCalculator());
        ProjectHealthChecker checker = new ProjectHealthChecker(validator);
        CurrentProjectStateSummary currentState = new CurrentProjectStateSummary
        {
            DocumentTitle = "ValidationSample.rvt",
            IsSupportedDocument = true,
            IsReadOnly = false,
            HasStoredGeoInfo = false
        };

        ProjectHealthSummary summary = checker.BuildSummary(currentState, null);

        Assert.Contains(summary.Findings, finding => finding.Code == "missing-info" && finding.Severity == ValidationSeverity.Error);
        Assert.Equal(ExportReadinessStatus.Blocked, summary.ExportReadiness.Status);
    }

    [Fact]
    public void BuildSummary_reports_unknown_confidence_and_stale_mesh_as_attention_items()
    {
        CrsRegistry registry = new CrsRegistry();
        CoordinateTransformer transformer = new CoordinateTransformer(registry);
        CoordinateValidator validator = new CoordinateValidator(registry, transformer, new JapanMeshCalculator());
        ProjectHealthChecker checker = new ProjectHealthChecker(validator);
        CurrentProjectStateSummary currentState = new CurrentProjectStateSummary
        {
            DocumentTitle = "ValidationSample.rvt",
            IsSupportedDocument = true,
            IsReadOnly = false,
            HasStoredGeoInfo = true
        };
        GeoProjectInfo info = new GeoProjectInfo
        {
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            Origin = new ProjectOrigin { Latitude = 35.6762, Longitude = 139.7653, ElevationMeters = 12.5 },
            PrimaryMeshCode = new MeshCode { Value = "53390000" },
            Confidence = GeoConfidenceLevel.Unknown,
            SetupSource = "Test"
        };

        ProjectHealthSummary summary = checker.BuildSummary(currentState, info);

        Assert.Contains(summary.Findings, finding => finding.Code == "unknown-confidence");
        Assert.Contains(summary.Findings, finding => finding.Code == "stale-mesh");
        Assert.Equal(ExportReadinessStatus.NeedsAttention, summary.ExportReadiness.Status);
    }
}
