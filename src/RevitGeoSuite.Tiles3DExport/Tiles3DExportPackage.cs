using System.Collections.Generic;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DExportPackage
{
    public Tiles3DExportReferenceContext ReferenceContext { get; set; } = new Tiles3DExportReferenceContext();

    public Tiles3DLevelOfDetail LevelOfDetail { get; set; }

    public List<Tiles3DMeshPrimitive> Meshes { get; set; } = new List<Tiles3DMeshPrimitive>();

    public int ElementCount { get; set; }

    public int TriangleCount { get; set; }

    public double GeometricError { get; set; }

    public double[] BoundingBox { get; set; } = new double[12];

    public string ContentFileName { get; set; } = "content.glb";

    public string LevelManifestFileName { get; set; } = "levels.json";

    public bool UsedPreciseCrsProjection { get; set; }

    public double GeoidHeightOffsetMeters { get; set; }
}
