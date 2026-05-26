using System;
using System.Collections.Generic;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Plateau.Tiles3D;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauOutlineDxfExportPackage
{
    public PlateauOutlineDxfExportPackage(
        IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> features,
        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas,
        CrsReference projectCrs,
        Vector3d projectBasePointMarkerMetres,
        Vector3d originOffsetMetres)
        : this(
            features,
            roadAreas,
            Array.Empty<PlateauContextOutlinesDxfWriter.OutlineFeature>(),
            Array.Empty<KibanLineExportFeature>(),
            projectCrs,
            projectBasePointMarkerMetres,
            originOffsetMetres)
    {
    }

    public PlateauOutlineDxfExportPackage(
        IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> features,
        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas,
        IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> kibanFeatures,
        CrsReference projectCrs,
        Vector3d projectBasePointMarkerMetres,
        Vector3d originOffsetMetres)
        : this(features, roadAreas, kibanFeatures, Array.Empty<KibanLineExportFeature>(), Array.Empty<KibanPolygonExportFeature>(), projectCrs, projectBasePointMarkerMetres, originOffsetMetres)
    {
    }

    public PlateauOutlineDxfExportPackage(
        IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> features,
        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas,
        IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> kibanFeatures,
        IReadOnlyList<KibanLineExportFeature> kibanLineFeatures,
        CrsReference projectCrs,
        Vector3d projectBasePointMarkerMetres,
        Vector3d originOffsetMetres)
        : this(features, roadAreas, kibanFeatures, kibanLineFeatures, Array.Empty<KibanPolygonExportFeature>(), Array.Empty<RevitModelFootprintFeature>(), projectCrs, projectBasePointMarkerMetres, originOffsetMetres)
    {
    }

    public PlateauOutlineDxfExportPackage(
        IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> features,
        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas,
        IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> kibanFeatures,
        IReadOnlyList<KibanLineExportFeature> kibanLineFeatures,
        IReadOnlyList<KibanPolygonExportFeature> kibanPolygonFeatures,
        CrsReference projectCrs,
        Vector3d projectBasePointMarkerMetres,
        Vector3d originOffsetMetres)
        : this(features, roadAreas, kibanFeatures, kibanLineFeatures, kibanPolygonFeatures, Array.Empty<RevitModelFootprintFeature>(), projectCrs, projectBasePointMarkerMetres, originOffsetMetres)
    {
    }

    public PlateauOutlineDxfExportPackage(
        IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> features,
        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas,
        IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> kibanFeatures,
        IReadOnlyList<KibanLineExportFeature> kibanLineFeatures,
        IReadOnlyList<KibanPolygonExportFeature> kibanPolygonFeatures,
        IReadOnlyList<RevitModelFootprintFeature> revitModelFeatures,
        CrsReference projectCrs,
        Vector3d projectBasePointMarkerMetres,
        Vector3d originOffsetMetres)
    {
        Features = features ?? throw new ArgumentNullException(nameof(features));
        RoadAreas = roadAreas ?? throw new ArgumentNullException(nameof(roadAreas));
        KibanFeatures = kibanFeatures ?? throw new ArgumentNullException(nameof(kibanFeatures));
        KibanLineFeatures = kibanLineFeatures ?? throw new ArgumentNullException(nameof(kibanLineFeatures));
        KibanPolygonFeatures = kibanPolygonFeatures ?? throw new ArgumentNullException(nameof(kibanPolygonFeatures));
        RevitModelFeatures = revitModelFeatures ?? throw new ArgumentNullException(nameof(revitModelFeatures));
        ProjectCrs = projectCrs ?? throw new ArgumentNullException(nameof(projectCrs));
        ProjectBasePointMarkerMetres = projectBasePointMarkerMetres;
        OriginOffsetMetres = originOffsetMetres;
    }

    public IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> Features { get; }

    public IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> RoadAreas { get; }

    public IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> KibanFeatures { get; }

    public IReadOnlyList<KibanLineExportFeature> KibanLineFeatures { get; }

    public IReadOnlyList<KibanPolygonExportFeature> KibanPolygonFeatures { get; }

    public IReadOnlyList<RevitModelFootprintFeature> RevitModelFeatures { get; }

    public CrsReference ProjectCrs { get; }

    public Vector3d ProjectBasePointMarkerMetres { get; }

    public Vector3d OriginOffsetMetres { get; }
}
