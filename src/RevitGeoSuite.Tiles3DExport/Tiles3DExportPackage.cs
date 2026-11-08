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

    /// <summary>
    /// Where <see cref="GeoidHeightOffsetMeters"/> came from: <see cref="Tiles3DGeoidModels.Egm2008"/>
    /// or <see cref="Tiles3DGeoidModels.Typed"/>. The anchor's orthometric height is
    /// <c>ReferenceContext.AnchorElevationMeters - GeoidHeightOffsetMeters</c>.
    /// </summary>
    public string GeoidModel { get; set; } = Tiles3DGeoidModels.Egm2008;
}

/// <summary>The geoid model names written to package manifests.</summary>
public static class Tiles3DGeoidModels
{
    public const string Egm2008 = "EGM2008";

    /// <summary>The user typed the offset; it may be 0 when the elevations are already ellipsoidal.</summary>
    public const string Typed = "typed";
}
