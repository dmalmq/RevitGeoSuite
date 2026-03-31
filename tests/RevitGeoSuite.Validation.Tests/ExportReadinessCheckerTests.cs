using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Validation;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using Xunit;

namespace RevitGeoSuite.Validation.Tests;

public sealed class ExportReadinessCheckerTests
{
    [Fact]
    public void Build_returns_blocked_when_canonical_geo_state_is_missing()
    {
        ExportReadinessChecker checker = new ExportReadinessChecker();
        CurrentProjectStateSummary currentState = new CurrentProjectStateSummary
        {
            DocumentTitle = "ValidationSample.rvt",
            IsSupportedDocument = true,
            IsReadOnly = false
        };

        ExportReadinessSummary summary = checker.Build(currentState, null, new ValidationResult[0]);

        Assert.Equal(ExportReadinessStatus.Blocked, summary.Status);
    }

    [Fact]
    public void Build_returns_needs_attention_for_stale_mesh()
    {
        ExportReadinessChecker checker = new ExportReadinessChecker();
        CurrentProjectStateSummary currentState = new CurrentProjectStateSummary
        {
            DocumentTitle = "ValidationSample.rvt",
            IsSupportedDocument = true,
            IsReadOnly = false
        };
        GeoProjectInfo info = new GeoProjectInfo
        {
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            Origin = new ProjectOrigin { Latitude = 35.6762, Longitude = 139.7653, ElevationMeters = 12.5 },
            PrimaryMeshCode = new MeshCode { Value = "53390000" },
            Confidence = GeoConfidenceLevel.Verified,
            SetupSource = "Test"
        };

        ExportReadinessSummary summary = checker.Build(currentState, info, new[]
        {
            new ValidationResult("stale-mesh", ValidationSeverity.Warning, "Primary mesh code does not match the current canonical location state.")
        });

        Assert.Equal(ExportReadinessStatus.NeedsAttention, summary.Status);
    }
}
