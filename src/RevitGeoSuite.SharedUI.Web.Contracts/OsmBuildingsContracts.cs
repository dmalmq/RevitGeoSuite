using System;

namespace RevitGeoSuite.SharedUI.Web.Contracts;

/// <summary>Payload of the <c>osm.importBuildings</c> request.</summary>
[TsExport]
public sealed class OsmBuildingsImportRequest
{
    /// <summary>Cesium Ion access token (required for OSM Buildings asset 96188).</summary>
    public string IonToken { get; set; } = string.Empty;

    /// <summary>Import radius in metres around the project anchor point.</summary>
    public double RadiusMeters { get; set; } = 500d;

    /// <summary>"solids" (default) builds 3D DirectShapes; "dxf" imports a lightweight 2D CAD basemap.</summary>
    public string Mode { get; set; } = "solids";
}

/// <summary>Result payload emitted by the <c>osm.importBuildings</c> job.</summary>
[TsExport]
public sealed class OsmBuildingsImportResponse
{
    public int ImportedElements { get; set; }
    public int Groups { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string[] Warnings { get; set; } = Array.Empty<string>();
}
