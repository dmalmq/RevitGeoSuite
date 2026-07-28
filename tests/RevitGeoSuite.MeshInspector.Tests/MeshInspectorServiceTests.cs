using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using Xunit;

namespace RevitGeoSuite.MeshInspector.Tests;

public sealed class MeshInspectorServiceTests
{
    [Fact]
    public void BuildSummary_returns_mesh_details_for_canonical_survey_origin()
    {
        JapanMeshCalculator calculator = new JapanMeshCalculator();
        MeshInspectorService service = new MeshInspectorService(calculator);
        CurrentProjectStateSummary currentState = new CurrentProjectStateSummary
        {
            DocumentTitle = "MeshSample.rvt",
            IsSupportedDocument = true,
            IsReadOnly = false,
            HasStoredGeoInfo = true
        };

        GeoProjectInfo info = new GeoProjectInfo
        {
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            Origin = new ProjectOrigin { Latitude = 35.6762, Longitude = 139.7653, ElevationMeters = 12.5 },
            Confidence = GeoConfidenceLevel.Verified,
            SetupSource = "Test"
        };

        MeshInspectorSummary summary = service.BuildSummary(currentState, info, MeshReferenceSource.SurveyPoint);

        Assert.Equal(calculator.Calculate(35.6762, 139.7653, JapanMeshLevel.Tertiary).Value, summary.PrimaryMeshCode);
        Assert.Equal(8, summary.NeighborMeshCodes.Count);
        Assert.True(summary.HasOverlay);
        Assert.True(summary.CanSavePrimaryMeshCode);
        Assert.Equal("Survey Point / Canonical Origin", summary.ReferenceSourceTitle);
        Assert.Contains(summary.DetailRows, row => row.Label == "Mesh Status" && row.Value == "Not stored yet");
    }

    [Fact]
    public void BuildSummary_supports_sapporo_origin()
    {
        JapanMeshCalculator calculator = new JapanMeshCalculator();
        MeshInspectorService service = new MeshInspectorService(calculator);
        CurrentProjectStateSummary currentState = new CurrentProjectStateSummary
        {
            DocumentTitle = "SapporoSample.rvt",
            IsSupportedDocument = true,
            IsReadOnly = false,
            HasStoredGeoInfo = true
        };

        GeoProjectInfo info = new GeoProjectInfo
        {
            ProjectCrs = new CrsReference { EpsgCode = 6679, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS XI" },
            Origin = new ProjectOrigin { Latitude = 43.06417, Longitude = 141.34694, ElevationMeters = 15.2 },
            Confidence = GeoConfidenceLevel.Verified,
            SetupSource = "Test"
        };

        MeshInspectorSummary summary = service.BuildSummary(currentState, info, MeshReferenceSource.SurveyPoint);

        Assert.Equal("64414277", summary.PrimaryMeshCode);
        Assert.True(summary.HasOverlay);
        Assert.Equal(8, summary.NeighborMeshCodes.Count);
        Assert.Contains(summary.DetailRows, row => row.Label == "Calculated Mesh" && row.Value == "64414277");
    }

    [Fact]
    public void BuildSummary_reports_invalid_reference_location_instead_of_throwing()
    {
        JapanMeshCalculator calculator = new JapanMeshCalculator();
        MeshInspectorService service = new MeshInspectorService(calculator);
        CurrentProjectStateSummary currentState = new CurrentProjectStateSummary
        {
            DocumentTitle = "MeshSample.rvt",
            IsSupportedDocument = true,
            IsReadOnly = false,
            HasStoredGeoInfo = true
        };

        GeoProjectInfo info = new GeoProjectInfo
        {
            ProjectCrs = new CrsReference { EpsgCode = 6679, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS XI" },
            Origin = new ProjectOrigin { Latitude = 43.06417, Longitude = 80.0, ElevationMeters = 15.2 },
            Confidence = GeoConfidenceLevel.Verified,
            SetupSource = "Test"
        };

        MeshInspectorSummary summary = service.BuildSummary(currentState, info, MeshReferenceSource.SurveyPoint);

        Assert.False(summary.HasPrimaryMeshCode);
        Assert.False(summary.HasOverlay);
        Assert.Contains("outside the supported Japan mesh range", summary.StatusMessage);
        Assert.Contains(summary.DetailRows, row => row.Label == "Reference Check" && row.Value.Contains("supported Japan mesh range"));
    }

    [Fact]
    public void BuildSummary_uses_working_project_base_point_before_revit_estimate()
    {
        JapanMeshCalculator calculator = new JapanMeshCalculator();
        MeshInspectorService service = new MeshInspectorService(calculator);
        CurrentProjectStateSummary currentState = new CurrentProjectStateSummary
        {
            DocumentTitle = "MeshSample.rvt",
            IsSupportedDocument = true,
            IsReadOnly = false,
            HasStoredGeoInfo = true,
            ProjectBasePoint = new BasePointSnapshot
            {
                Name = "Project Base Point",
                EstimatedLatitudeDegrees = 35.6800,
                EstimatedLongitudeDegrees = 139.7700
            },
            StoredWorkingProjectBasePoint = new WorkingProjectBasePointReference
            {
                ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
                Origin = new ProjectOrigin { Latitude = 35.6895, Longitude = 139.7005, ElevationMeters = 15.0 },
                ProjectedCoordinate = new ProjectedCoordinate(0, 0),
                Confidence = GeoConfidenceLevel.Verified,
                SetupSource = "Test"
            }
        };

        GeoProjectInfo info = new GeoProjectInfo
        {
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            Origin = new ProjectOrigin { Latitude = 35.6762, Longitude = 139.7653, ElevationMeters = 12.5 },
            Confidence = GeoConfidenceLevel.Verified,
            SetupSource = "Test"
        };

        MeshInspectorSummary summary = service.BuildSummary(currentState, info, MeshReferenceSource.ProjectBasePoint);

        Assert.Equal(calculator.Calculate(35.6895, 139.7005, JapanMeshLevel.Tertiary).Value, summary.PrimaryMeshCode);
        Assert.False(summary.CanSavePrimaryMeshCode);
        Assert.Equal("Project Base Point", summary.ReferenceSourceTitle);
        Assert.Contains(summary.DetailRows, row => row.Label == "Reference Source" && row.Value == "Working Project Base Point");
        Assert.Contains("inspection-only", summary.StatusMessage);
    }

    [Fact]
    public void BuildSummary_falls_back_to_revit_project_base_point_estimate()
    {
        JapanMeshCalculator calculator = new JapanMeshCalculator();
        MeshInspectorService service = new MeshInspectorService(calculator);
        CurrentProjectStateSummary currentState = new CurrentProjectStateSummary
        {
            DocumentTitle = "MeshSample.rvt",
            IsSupportedDocument = true,
            IsReadOnly = false,
            HasStoredGeoInfo = true,
            ProjectBasePoint = new BasePointSnapshot
            {
                Name = "Project Base Point",
                EstimatedLatitudeDegrees = 35.6800,
                EstimatedLongitudeDegrees = 139.7700
            }
        };

        GeoProjectInfo info = new GeoProjectInfo
        {
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            Origin = new ProjectOrigin { Latitude = 35.6762, Longitude = 139.7653, ElevationMeters = 12.5 },
            Confidence = GeoConfidenceLevel.Verified,
            SetupSource = "Test"
        };

        MeshInspectorSummary summary = service.BuildSummary(currentState, info, MeshReferenceSource.ProjectBasePoint);

        Assert.Equal(calculator.Calculate(35.6800, 139.7700, JapanMeshLevel.Tertiary).Value, summary.PrimaryMeshCode);
        Assert.Contains(summary.DetailRows, row => row.Label == "Reference Source" && row.Value == "Revit Project Base Point");
    }

    [Fact]
    public void BuildSummary_disables_save_when_document_is_read_only()
    {
        JapanMeshCalculator calculator = new JapanMeshCalculator();
        MeshCode primary = calculator.Calculate(35.6762, 139.7653, JapanMeshLevel.Tertiary);
        MeshInspectorService service = new MeshInspectorService(calculator);
        CurrentProjectStateSummary currentState = new CurrentProjectStateSummary
        {
            DocumentTitle = "MeshSample.rvt",
            IsSupportedDocument = true,
            IsReadOnly = true,
            HasStoredGeoInfo = true
        };

        GeoProjectInfo info = new GeoProjectInfo
        {
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            Origin = new ProjectOrigin { Latitude = 35.6762, Longitude = 139.7653, ElevationMeters = 12.5 },
            PrimaryMeshCode = primary,
            Confidence = GeoConfidenceLevel.Verified,
            SetupSource = "Test"
        };

        MeshInspectorSummary summary = service.BuildSummary(currentState, info, MeshReferenceSource.SurveyPoint);

        Assert.False(summary.CanSavePrimaryMeshCode);
        Assert.Contains("read-only", summary.StatusMessage);
    }

    [Fact]
    public void BuildSummary_reports_missing_project_base_point_reference()
    {
        MeshInspectorService service = new MeshInspectorService(new JapanMeshCalculator());
        CurrentProjectStateSummary currentState = new CurrentProjectStateSummary
        {
            DocumentTitle = "MeshSample.rvt",
            IsSupportedDocument = true,
            IsReadOnly = false,
            HasStoredGeoInfo = true
        };
        GeoProjectInfo info = new GeoProjectInfo
        {
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            Origin = new ProjectOrigin { Latitude = 35.6762, Longitude = 139.7653, ElevationMeters = 12.5 },
            Confidence = GeoConfidenceLevel.Verified,
            SetupSource = "Test"
        };

        MeshInspectorSummary summary = service.BuildSummary(currentState, info, MeshReferenceSource.ProjectBasePoint);

        Assert.False(summary.HasPrimaryMeshCode);
        Assert.Contains("Project Base Point location is not available yet", summary.StatusMessage);
    }

    [Fact]
    public void BuildSummary_reports_missing_shared_geo_info()
    {
        MeshInspectorService service = new MeshInspectorService(new JapanMeshCalculator());
        CurrentProjectStateSummary currentState = new CurrentProjectStateSummary
        {
            DocumentTitle = "MeshSample.rvt",
            IsSupportedDocument = true,
            IsReadOnly = false,
            HasStoredGeoInfo = false
        };

        MeshInspectorSummary summary = service.BuildSummary(currentState, null, MeshReferenceSource.SurveyPoint);

        Assert.False(summary.HasPrimaryMeshCode);
        Assert.Contains("Run Georeference Setup", summary.StatusMessage);
    }
}
