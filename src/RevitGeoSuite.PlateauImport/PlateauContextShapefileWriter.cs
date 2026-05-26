using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Esri;
using NetTopologySuite.IO.Esri.Dbf.Fields;
using NetTopologySuite.IO.Esri.Shapefiles.Writers;
using RevitGeoSuite.Core.Coordinates;

namespace RevitGeoSuite.PlateauImport;

public static class PlateauContextShapefileWriter
{
    private const double AreaEpsilon = 1e-9d;
    private const string RoadLayer = "PLATEAU_ROADS";

    public const string RevitBuildingsLayer = "REVIT_BUILDINGS";
    public const string RevitWallsLayer = "REVIT_WALLS";

    private static readonly string[] SidecarExtensions = { ".shp", ".shx", ".dbf", ".prj", ".cpg" };

    private static readonly (string Layer, string Suffix, string Stage)[] PlateauCategoryExports =
    {
        ("PLATEAU_BUILDINGS", "_plateau_buildings", "Writing PLATEAU building polygons"),
        ("PLATEAU_BRIDGES", "_plateau_bridges", "Writing PLATEAU bridge polygons"),
        ("PLATEAU_VEGETATION", "_plateau_vegetation", "Writing PLATEAU vegetation polygons"),
        ("PLATEAU_RELIEF", "_plateau_relief", "Writing PLATEAU relief polygons"),
        (PlateauContextOutlinesDxfWriter.PlateauLandUseLayer, "_plateau_landuse", "Writing PLATEAU land-use polygons"),
    };

    private static readonly string[] KnownCompanionSuffixes =
    {
        "_plateau_roads",
        "_plateau_buildings",
        "_plateau_bridges",
        "_plateau_vegetation",
        "_plateau_relief",
        "_plateau_landuse",
        "_gsi_railways",
        "_gsi_water",
        "_gsi_landuse",
        "_gsi_sidewalks",
        "_revit_buildings",
        "_revit_walls",
    };

    public sealed class WriteResult
    {
        public WriteResult(
            int featureCount,
            int roadFeatureCount,
            int footprintFeatureCount,
            IReadOnlyList<string> files,
            IReadOnlyList<string> warnings)
            : this(featureCount, roadFeatureCount, footprintFeatureCount, 0, 0, 0, files, warnings)
        {
        }

        public WriteResult(
            int featureCount,
            int roadFeatureCount,
            int footprintFeatureCount,
            int sidewalkFeatureCount,
            int railwayFeatureCount,
            IReadOnlyList<string> files,
            IReadOnlyList<string> warnings)
            : this(featureCount, roadFeatureCount, footprintFeatureCount, sidewalkFeatureCount, railwayFeatureCount, 0, files, warnings)
        {
        }

        public WriteResult(
            int featureCount,
            int roadFeatureCount,
            int footprintFeatureCount,
            int sidewalkFeatureCount,
            int railwayFeatureCount,
            int kibanWaterFeatureCount,
            IReadOnlyList<string> files,
            IReadOnlyList<string> warnings)
            : this(featureCount, roadFeatureCount, footprintFeatureCount, sidewalkFeatureCount, railwayFeatureCount, kibanWaterFeatureCount, 0, 0, files, warnings)
        {
        }

        public WriteResult(
            int featureCount,
            int roadFeatureCount,
            int footprintFeatureCount,
            int sidewalkFeatureCount,
            int railwayFeatureCount,
            int kibanWaterFeatureCount,
            int revitBuildingFeatureCount,
            int revitWallFeatureCount,
            IReadOnlyList<string> files,
            IReadOnlyList<string> warnings)
            : this(featureCount, roadFeatureCount, footprintFeatureCount, sidewalkFeatureCount, railwayFeatureCount, kibanWaterFeatureCount, 0, revitBuildingFeatureCount, revitWallFeatureCount, files, warnings)
        {
        }

        public WriteResult(
            int featureCount,
            int roadFeatureCount,
            int footprintFeatureCount,
            int sidewalkFeatureCount,
            int railwayFeatureCount,
            int kibanWaterFeatureCount,
            int kibanLandUseFeatureCount,
            int revitBuildingFeatureCount,
            int revitWallFeatureCount,
            IReadOnlyList<string> files,
            IReadOnlyList<string> warnings)
        {
            FeatureCount = featureCount;
            RoadFeatureCount = roadFeatureCount;
            FootprintFeatureCount = footprintFeatureCount;
            SidewalkFeatureCount = sidewalkFeatureCount;
            RailwayFeatureCount = railwayFeatureCount;
            KibanWaterFeatureCount = kibanWaterFeatureCount;
            KibanLandUseFeatureCount = kibanLandUseFeatureCount;
            RevitBuildingFeatureCount = revitBuildingFeatureCount;
            RevitWallFeatureCount = revitWallFeatureCount;
            Files = files ?? throw new ArgumentNullException(nameof(files));
            Warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
        }

        public int FeatureCount { get; }

        public int RoadFeatureCount { get; }

        public int FootprintFeatureCount { get; }

        public int SidewalkFeatureCount { get; }

        public int RailwayFeatureCount { get; }

        public int KibanWaterFeatureCount { get; }

        public int KibanLandUseFeatureCount { get; }

        public int RevitBuildingFeatureCount { get; }

        public int RevitWallFeatureCount { get; }

        public int RevitModelFeatureCount => RevitBuildingFeatureCount + RevitWallFeatureCount;

        public int LineFeatureCount => SidewalkFeatureCount + RailwayFeatureCount;

        public IReadOnlyList<string> Files { get; }

        public IReadOnlyList<string> Warnings { get; }
    }

    public static StreamingWriteSession OpenStreaming(
        string shapefilePath,
        CrsReference projectCrs,
        ICollection<string>? warnings = null)
    {
        return new StreamingWriteSession(shapefilePath, projectCrs, warnings);
    }

    public sealed class StreamingWriteSession : IDisposable
    {
        private readonly string normalizedPath;
        private readonly CrsReference projectCrs;
        private readonly ICollection<string> warnings;
        private readonly Dictionary<string, StreamingLayerWriter> writers = new Dictionary<string, StreamingLayerWriter>(StringComparer.Ordinal);
        private bool completed;
        private int featureCount;
        private int roadFeatureCount;
        private int footprintFeatureCount;
        private int sidewalkFeatureCount;
        private int railwayFeatureCount;
        private int kibanWaterFeatureCount;
        private int kibanLandUseFeatureCount;
        private int revitBuildingFeatureCount;
        private int revitWallFeatureCount;

        internal StreamingWriteSession(string shapefilePath, CrsReference projectCrs, ICollection<string>? warnings)
        {
            if (string.IsNullOrWhiteSpace(shapefilePath)) throw new ArgumentException("A shapefile path is required.", nameof(shapefilePath));
            this.projectCrs = projectCrs ?? throw new ArgumentNullException(nameof(projectCrs));
            this.warnings = warnings ?? new List<string>();
            normalizedPath = Path.ChangeExtension(shapefilePath, ".shp");
            DeleteKnownCompanionSidecars(normalizedPath);
        }

        public void WritePlateauRoadAreas(IReadOnlyCollection<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas)
        {
            if (roadAreas is null) throw new ArgumentNullException(nameof(roadAreas));
            foreach (PlateauContextOutlinesDxfWriter.AreaFeature roadArea in roadAreas)
            {
                IReadOnlyList<Polygon> polygons = CreatePolygonalGeometries(roadArea.ExteriorRingMetres, roadArea.InteriorRingsMetres);
                if (polygons.Count == 0)
                {
                    warnings.Add($"Skipped {roadArea.SourceId ?? "road"}: road polygon boundary could not be written.");
                    continue;
                }

                StreamingLayerWriter writer = GetWriter("_plateau_roads", ShapeType.Polygon, StreamingSchema.PlateauPolygon);
                foreach (Polygon polygon in polygons)
                {
                    writer.Write(CreateFeature(
                        polygon,
                        writer.NextRowId,
                        RoadLayer,
                        roadArea.SourceId,
                        dissolved: true,
                        projectCrs.EpsgCode));
                    featureCount++;
                    roadFeatureCount++;
                }
            }
        }

        public void WritePlateauOutlines(IReadOnlyCollection<PlateauContextOutlinesDxfWriter.OutlineFeature> features)
        {
            if (features is null) throw new ArgumentNullException(nameof(features));
            foreach (PlateauContextOutlinesDxfWriter.OutlineFeature source in features)
            {
                if (!TryGetPlateauCategorySuffix(source.Layer, out string? suffix))
                {
                    continue;
                }

                IReadOnlyList<Polygon> polygons = CreatePolygonalGeometries(
                    source.VerticesMetres,
                    Array.Empty<IReadOnlyList<(double X, double Y)>>());
                if (polygons.Count == 0)
                {
                    warnings.Add($"Skipped {source.SourceId ?? "feature"}: footprint polygon boundary could not be written.");
                    continue;
                }

                StreamingLayerWriter writer = GetWriter(suffix!, ShapeType.Polygon, StreamingSchema.PlateauPolygon);
                foreach (Polygon polygon in polygons)
                {
                    writer.Write(CreateFeature(
                        polygon,
                        writer.NextRowId,
                        source.Layer,
                        source.SourceId,
                        dissolved: false,
                        projectCrs.EpsgCode,
                        source.ClassCode,
                        source.ClassName));
                    featureCount++;
                    footprintFeatureCount++;
                }
            }
        }

        public void WriteKibanLines(IReadOnlyCollection<KibanLineExportFeature> features)
        {
            if (features is null) throw new ArgumentNullException(nameof(features));
            foreach (KibanLineExportFeature source in features)
            {
                if (!string.Equals(source.Layer, PlateauContextOutlinesDxfWriter.GsiRailwaysLayer, StringComparison.Ordinal))
                {
                    continue;
                }

                LineString? lineString = CreateLineGeometry(source.VerticesMetres);
                if (lineString is null)
                {
                    warnings.Add($"Skipped {source.SourceId ?? "gsi-line"}: line geometry could not be written.");
                    continue;
                }

                StreamingLayerWriter writer = GetWriter("_gsi_railways", ShapeType.PolyLine, StreamingSchema.KibanLine);
                writer.Write(CreateLineFeature(lineString, writer.NextRowId, source, projectCrs.EpsgCode));
                featureCount++;
                railwayFeatureCount++;
            }
        }

        public void WriteKibanPolygons(IReadOnlyCollection<KibanPolygonExportFeature> features)
        {
            if (features is null) throw new ArgumentNullException(nameof(features));
            foreach (KibanPolygonExportFeature source in features)
            {
                if (!TryGetKibanPolygonSuffix(source.Layer, out string? suffix, out string featureLabel))
                {
                    continue;
                }

                IReadOnlyList<Polygon> polygons = CreatePolygonalGeometries(source.ExteriorRingMetres, source.InteriorRingsMetres);
                if (polygons.Count == 0)
                {
                    warnings.Add($"Skipped {source.SourceId ?? "gsi-polygon"}: {featureLabel} polygon boundary could not be written.");
                    continue;
                }

                StreamingLayerWriter writer = GetWriter(suffix!, ShapeType.Polygon, StreamingSchema.KibanPolygon);
                foreach (Polygon polygon in polygons)
                {
                    writer.Write(CreateKibanPolygonFeature(polygon, writer.NextRowId, source, projectCrs.EpsgCode));
                    featureCount++;
                    if (string.Equals(source.Layer, KibanGmlParser.WaterLayer, StringComparison.Ordinal))
                    {
                        kibanWaterFeatureCount++;
                    }
                    else if (string.Equals(source.Layer, KibanGmlParser.LandUseLayer, StringComparison.Ordinal))
                    {
                        kibanLandUseFeatureCount++;
                    }
                    else if (string.Equals(source.Layer, PlateauContextOutlinesDxfWriter.GsiSidewalksLayer, StringComparison.Ordinal))
                    {
                        sidewalkFeatureCount++;
                    }
                }
            }
        }

        public void WriteRevitModelFeatures(IReadOnlyCollection<RevitModelFootprintFeature> features)
        {
            if (features is null) throw new ArgumentNullException(nameof(features));
            foreach (RevitModelFootprintFeature source in features)
            {
                if (source.IsPolygon && string.Equals(source.Layer, RevitBuildingsLayer, StringComparison.Ordinal))
                {
                    WriteRevitPolygon(source);
                }
                else if (!source.IsPolygon && string.Equals(source.Layer, RevitWallsLayer, StringComparison.Ordinal))
                {
                    WriteRevitLine(source);
                }
            }
        }

        public WriteResult Complete()
        {
            if (featureCount == 0)
            {
                throw new InvalidOperationException("No valid polygon or line features were available for shapefile export.");
            }

            List<string> files = new List<string>();
            try
            {
                foreach (StreamingLayerWriter writer in writers.Values)
                {
                    writer.Complete(projectCrs, warnings, files);
                }
            }
            catch
            {
                foreach (StreamingLayerWriter writer in writers.Values)
                {
                    writer.DeletePartialFiles();
                }

                throw;
            }

            completed = true;
            return new WriteResult(
                featureCount,
                roadFeatureCount,
                footprintFeatureCount,
                sidewalkFeatureCount,
                railwayFeatureCount,
                kibanWaterFeatureCount,
                kibanLandUseFeatureCount,
                revitBuildingFeatureCount,
                revitWallFeatureCount,
                files,
                warnings.ToArray());
        }

        public void Dispose()
        {
            foreach (StreamingLayerWriter writer in writers.Values)
            {
                if (completed)
                {
                    writer.Dispose();
                }
                else
                {
                    writer.DeletePartialFiles();
                }
            }
        }

        private void WriteRevitPolygon(RevitModelFootprintFeature source)
        {
            IReadOnlyList<Polygon> polygons = CreatePolygonalGeometries(
                source.VerticesMetres,
                Array.Empty<IReadOnlyList<(double X, double Y)>>());
            if (polygons.Count == 0)
            {
                warnings.Add($"Skipped Revit element {source.ElementId}: footprint polygon could not be written.");
                return;
            }

            StreamingLayerWriter writer = GetWriter("_revit_buildings", ShapeType.Polygon, StreamingSchema.RevitPolygon);
            foreach (Polygon polygon in polygons)
            {
                writer.Write(CreateRevitPolygonFeature(polygon, writer.NextRowId, source, projectCrs.EpsgCode));
                featureCount++;
                revitBuildingFeatureCount++;
            }
        }

        private void WriteRevitLine(RevitModelFootprintFeature source)
        {
            LineString? lineString = CreateLineGeometry(source.VerticesMetres);
            if (lineString is null)
            {
                warnings.Add($"Skipped Revit element {source.ElementId}: line geometry could not be written.");
                return;
            }

            StreamingLayerWriter writer = GetWriter("_revit_walls", ShapeType.PolyLine, StreamingSchema.RevitLine);
            writer.Write(CreateRevitLineFeature(lineString, writer.NextRowId, source, projectCrs.EpsgCode));
            featureCount++;
            revitWallFeatureCount++;
        }

        private StreamingLayerWriter GetWriter(string suffix, ShapeType shapeType, StreamingSchema schema)
        {
            if (!writers.TryGetValue(suffix, out StreamingLayerWriter? writer))
            {
                writer = new StreamingLayerWriter(BuildCompanionShapefilePath(normalizedPath, suffix), shapeType, schema);
                writers.Add(suffix, writer);
            }

            return writer;
        }
    }

    public static WriteResult Write(string shapefilePath, PlateauOutlineDxfExportPackage package)
    {
        return Write(shapefilePath, package, onStage: null);
    }

    public static WriteResult Write(string shapefilePath, PlateauOutlineDxfExportPackage package, Action<string>? onStage)
    {
        if (package is null) throw new ArgumentNullException(nameof(package));

        string normalizedPath = Path.ChangeExtension(shapefilePath, ".shp");
        DeleteExistingSidecars(normalizedPath);
        List<string> files = new List<string>();
        List<string> warnings = new List<string>();
        int featureCount = 0;
        int roadFeatureCount = 0;
        int footprintFeatureCount = 0;
        int sidewalkFeatureCount = 0;
        int railwayFeatureCount = 0;
        int kibanWaterFeatureCount = 0;
        int kibanLandUseFeatureCount = 0;
        int revitBuildingFeatureCount = 0;
        int revitWallFeatureCount = 0;

        if (package.RoadAreas.Count > 0)
        {
            onStage?.Invoke("Writing PLATEAU road polygons");
            WriteResult roadResult = Write(
                BuildCompanionShapefilePath(normalizedPath, "_plateau_roads"),
                Array.Empty<PlateauContextOutlinesDxfWriter.OutlineFeature>(),
                package.RoadAreas,
                package.ProjectCrs);
            featureCount += roadResult.FeatureCount;
            roadFeatureCount += roadResult.RoadFeatureCount;
            footprintFeatureCount += roadResult.FootprintFeatureCount;
            files.AddRange(roadResult.Files);
            warnings.AddRange(roadResult.Warnings);
        }

        foreach ((string layer, string suffix, string stage) in PlateauCategoryExports)
        {
            PlateauContextOutlinesDxfWriter.OutlineFeature[] categoryFeatures = package.Features
                .Where(feature => string.Equals(feature.Layer, layer, StringComparison.Ordinal))
                .ToArray();
            if (categoryFeatures.Length == 0)
            {
                continue;
            }

            onStage?.Invoke(stage);
            WriteResult categoryResult = Write(
                BuildCompanionShapefilePath(normalizedPath, suffix),
                categoryFeatures,
                Array.Empty<PlateauContextOutlinesDxfWriter.AreaFeature>(),
                package.ProjectCrs);
            featureCount += categoryResult.FeatureCount;
            footprintFeatureCount += categoryResult.FootprintFeatureCount;
            files.AddRange(categoryResult.Files);
            warnings.AddRange(categoryResult.Warnings);
        }

        KibanLineExportFeature[] railwayFeatures = package.KibanLineFeatures
            .Where(feature => string.Equals(feature.Layer, PlateauContextOutlinesDxfWriter.GsiRailwaysLayer, StringComparison.Ordinal))
            .ToArray();
        if (railwayFeatures.Length > 0)
        {
            onStage?.Invoke("Writing GSI railway lines");
            WriteResult railwayResult = WriteLineShapefile(
                BuildCompanionShapefilePath(normalizedPath, "_gsi_railways"),
                railwayFeatures,
                package.ProjectCrs);
            featureCount += railwayResult.FeatureCount;
            sidewalkFeatureCount += railwayResult.SidewalkFeatureCount;
            railwayFeatureCount += railwayResult.RailwayFeatureCount;
            files.AddRange(railwayResult.Files);
            warnings.AddRange(railwayResult.Warnings);
        }

        if (package.KibanPolygonFeatures.Count > 0)
        {
            KibanPolygonExportFeature[] waterFeatures = package.KibanPolygonFeatures
                .Where(feature => string.Equals(feature.Layer, KibanGmlParser.WaterLayer, StringComparison.Ordinal))
                .ToArray();
            if (waterFeatures.Length > 0)
            {
                onStage?.Invoke("Writing GSI water polygons");
                WriteResult waterResult = WriteKibanPolygonShapefile(
                    BuildCompanionShapefilePath(normalizedPath, "_gsi_water"),
                    waterFeatures,
                    package.ProjectCrs,
                    "water");
                featureCount += waterResult.FeatureCount;
                kibanWaterFeatureCount += waterResult.KibanWaterFeatureCount;
                files.AddRange(waterResult.Files);
                warnings.AddRange(waterResult.Warnings);
            }

            KibanPolygonExportFeature[] landUseFeatures = package.KibanPolygonFeatures
                .Where(feature => string.Equals(feature.Layer, KibanGmlParser.LandUseLayer, StringComparison.Ordinal))
                .ToArray();
            if (landUseFeatures.Length > 0)
            {
                onStage?.Invoke("Writing GSI land-use polygons");
                WriteResult landUseResult = WriteKibanPolygonShapefile(
                    BuildCompanionShapefilePath(normalizedPath, "_gsi_landuse"),
                    landUseFeatures,
                    package.ProjectCrs,
                    "land-use");
                featureCount += landUseResult.FeatureCount;
                kibanLandUseFeatureCount += landUseResult.KibanLandUseFeatureCount;
                files.AddRange(landUseResult.Files);
                warnings.AddRange(landUseResult.Warnings);
            }

            KibanPolygonExportFeature[] sidewalkStripFeatures = package.KibanPolygonFeatures
                .Where(feature => string.Equals(feature.Layer, PlateauContextOutlinesDxfWriter.GsiSidewalksLayer, StringComparison.Ordinal))
                .ToArray();
            if (sidewalkStripFeatures.Length > 0)
            {
                onStage?.Invoke("Writing GSI sidewalk-strip polygons");
                WriteResult sidewalkResult = WriteKibanPolygonShapefile(
                    BuildCompanionShapefilePath(normalizedPath, "_gsi_sidewalks"),
                    sidewalkStripFeatures,
                    package.ProjectCrs,
                    "sidewalk-strip");
                featureCount += sidewalkResult.FeatureCount;
                sidewalkFeatureCount += sidewalkResult.SidewalkFeatureCount;
                files.AddRange(sidewalkResult.Files);
                warnings.AddRange(sidewalkResult.Warnings);
            }
        }

        if (package.RevitModelFeatures.Count > 0)
        {
            RevitModelFootprintFeature[] revitBuildings = package.RevitModelFeatures
                .Where(feature => feature.IsPolygon && string.Equals(feature.Layer, RevitBuildingsLayer, StringComparison.Ordinal))
                .ToArray();
            if (revitBuildings.Length > 0)
            {
                onStage?.Invoke("Writing Revit building footprints");
                WriteResult buildingResult = WriteRevitModelPolygonShapefile(
                    BuildCompanionShapefilePath(normalizedPath, "_revit_buildings"),
                    revitBuildings,
                    package.ProjectCrs);
                featureCount += buildingResult.FeatureCount;
                revitBuildingFeatureCount += buildingResult.RevitBuildingFeatureCount;
                files.AddRange(buildingResult.Files);
                warnings.AddRange(buildingResult.Warnings);
            }

            RevitModelFootprintFeature[] revitWalls = package.RevitModelFeatures
                .Where(feature => !feature.IsPolygon && string.Equals(feature.Layer, RevitWallsLayer, StringComparison.Ordinal))
                .ToArray();
            if (revitWalls.Length > 0)
            {
                onStage?.Invoke("Writing Revit wall outlines");
                WriteResult wallResult = WriteRevitModelLineShapefile(
                    BuildCompanionShapefilePath(normalizedPath, "_revit_walls"),
                    revitWalls,
                    package.ProjectCrs);
                featureCount += wallResult.FeatureCount;
                revitWallFeatureCount += wallResult.RevitWallFeatureCount;
                files.AddRange(wallResult.Files);
                warnings.AddRange(wallResult.Warnings);
            }
        }

        if (featureCount == 0)
        {
            throw new InvalidOperationException("No valid polygon or line features were available for shapefile export.");
        }

        return new WriteResult(
            featureCount,
            roadFeatureCount,
            footprintFeatureCount,
            sidewalkFeatureCount,
            railwayFeatureCount,
            kibanWaterFeatureCount,
            kibanLandUseFeatureCount,
            revitBuildingFeatureCount,
            revitWallFeatureCount,
            files,
            warnings);
    }

    public static WriteResult Write(
        string shapefilePath,
        IReadOnlyCollection<PlateauContextOutlinesDxfWriter.OutlineFeature> features,
        IReadOnlyCollection<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas,
        CrsReference projectCrs)
    {
        if (string.IsNullOrWhiteSpace(shapefilePath)) throw new ArgumentException("A shapefile path is required.", nameof(shapefilePath));
        if (features is null) throw new ArgumentNullException(nameof(features));
        if (roadAreas is null) throw new ArgumentNullException(nameof(roadAreas));
        if (projectCrs is null) throw new ArgumentNullException(nameof(projectCrs));

        string normalizedPath = Path.ChangeExtension(shapefilePath, ".shp");
        string? directory = Path.GetDirectoryName(normalizedPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        List<string> warnings = new List<string>();
        List<Feature> shapefileFeatures = new List<Feature>(features.Count + roadAreas.Count);
        int nextRowId = 1;
        int roadFeatureCount = 0;
        int footprintFeatureCount = 0;

        foreach (PlateauContextOutlinesDxfWriter.AreaFeature roadArea in roadAreas)
        {
            IReadOnlyList<Polygon> polygons = CreatePolygonalGeometries(roadArea.ExteriorRingMetres, roadArea.InteriorRingsMetres);
            if (polygons.Count == 0)
            {
                warnings.Add($"Skipped {roadArea.SourceId ?? "road"}: road polygon boundary could not be written.");
                continue;
            }

            foreach (Polygon polygon in polygons)
            {
                shapefileFeatures.Add(CreateFeature(
                    polygon,
                    nextRowId++,
                    RoadLayer,
                    roadArea.SourceId,
                    dissolved: true,
                    projectCrs.EpsgCode));
                roadFeatureCount++;
            }
        }

        foreach (PlateauContextOutlinesDxfWriter.OutlineFeature feature in features)
        {
            IReadOnlyList<Polygon> polygons = CreatePolygonalGeometries(
                feature.VerticesMetres,
                Array.Empty<IReadOnlyList<(double X, double Y)>>());
            if (polygons.Count == 0)
            {
                warnings.Add($"Skipped {feature.SourceId ?? "feature"}: footprint polygon boundary could not be written.");
                continue;
            }

            foreach (Polygon polygon in polygons)
            {
                shapefileFeatures.Add(CreateFeature(
                    polygon,
                    nextRowId++,
                    feature.Layer,
                    feature.SourceId,
                    dissolved: false,
                    projectCrs.EpsgCode,
                    feature.ClassCode,
                    feature.ClassName));
                footprintFeatureCount++;
            }
        }

        if (shapefileFeatures.Count == 0)
        {
            throw new InvalidOperationException("No valid polygon features were available for shapefile export.");
        }

        DeleteExistingSidecars(normalizedPath);
        Shapefile.WriteAllFeatures(shapefileFeatures, normalizedPath);
        WriteProjectionFile(normalizedPath, projectCrs, warnings);
        File.WriteAllText(Path.ChangeExtension(normalizedPath, ".cpg"), "UTF-8", Encoding.ASCII);

        return new WriteResult(
            shapefileFeatures.Count,
            roadFeatureCount,
            footprintFeatureCount,
            GetWrittenFiles(normalizedPath),
            warnings);
    }

    private static WriteResult WriteLineShapefile(
        string shapefilePath,
        IReadOnlyCollection<KibanLineExportFeature> features,
        CrsReference projectCrs)
    {
        string normalizedPath = Path.ChangeExtension(shapefilePath, ".shp");
        string? directory = Path.GetDirectoryName(normalizedPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        List<string> warnings = new List<string>();
        List<Feature> shapefileFeatures = new List<Feature>(features.Count);
        int nextRowId = 1;
        int sidewalkFeatureCount = 0;
        int railwayFeatureCount = 0;

        foreach (KibanLineExportFeature feature in features)
        {
            LineString? lineString = CreateLineGeometry(feature.VerticesMetres);
            if (lineString is null)
            {
                warnings.Add($"Skipped {feature.SourceId ?? "gsi-line"}: line geometry could not be written.");
                continue;
            }

            shapefileFeatures.Add(CreateLineFeature(
                lineString,
                nextRowId++,
                feature,
                projectCrs.EpsgCode));

            if (string.Equals(feature.Layer, PlateauContextOutlinesDxfWriter.GsiSidewalksLayer, StringComparison.Ordinal))
            {
                sidewalkFeatureCount++;
            }
            else if (string.Equals(feature.Layer, PlateauContextOutlinesDxfWriter.GsiRailwaysLayer, StringComparison.Ordinal))
            {
                railwayFeatureCount++;
            }
        }

        DeleteExistingSidecars(normalizedPath);
        if (shapefileFeatures.Count == 0)
        {
            warnings.Add($"No valid GSI line features were available for '{Path.GetFileName(normalizedPath)}'.");
            return new WriteResult(0, 0, 0, 0, 0, Array.Empty<string>(), warnings);
        }

        Shapefile.WriteAllFeatures(shapefileFeatures, normalizedPath);
        WriteProjectionFile(normalizedPath, projectCrs, warnings);
        File.WriteAllText(Path.ChangeExtension(normalizedPath, ".cpg"), "UTF-8", Encoding.ASCII);

        return new WriteResult(
            shapefileFeatures.Count,
            0,
            0,
            sidewalkFeatureCount,
            railwayFeatureCount,
            GetWrittenFiles(normalizedPath),
            warnings);
    }

    private static WriteResult WriteKibanPolygonShapefile(
        string shapefilePath,
        IReadOnlyCollection<KibanPolygonExportFeature> features,
        CrsReference projectCrs,
        string featureLabel)
    {
        string normalizedPath = Path.ChangeExtension(shapefilePath, ".shp");
        string? directory = Path.GetDirectoryName(normalizedPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        List<string> warnings = new List<string>();
        List<Feature> shapefileFeatures = new List<Feature>(features.Count);
        int nextRowId = 1;
        int kibanWaterFeatureCount = 0;
        int kibanLandUseFeatureCount = 0;
        int kibanSidewalkFeatureCount = 0;

        foreach (KibanPolygonExportFeature feature in features)
        {
            IReadOnlyList<Polygon> polygons = CreatePolygonalGeometries(feature.ExteriorRingMetres, feature.InteriorRingsMetres);
            if (polygons.Count == 0)
            {
                warnings.Add($"Skipped {feature.SourceId ?? "gsi-polygon"}: {featureLabel} polygon boundary could not be written.");
                continue;
            }

            foreach (Polygon polygon in polygons)
            {
                shapefileFeatures.Add(CreateKibanPolygonFeature(
                    polygon,
                    nextRowId++,
                    feature,
                    projectCrs.EpsgCode));
                if (string.Equals(feature.Layer, KibanGmlParser.WaterLayer, StringComparison.Ordinal))
                {
                    kibanWaterFeatureCount++;
                }
                else if (string.Equals(feature.Layer, KibanGmlParser.LandUseLayer, StringComparison.Ordinal))
                {
                    kibanLandUseFeatureCount++;
                }
                else if (string.Equals(feature.Layer, PlateauContextOutlinesDxfWriter.GsiSidewalksLayer, StringComparison.Ordinal))
                {
                    kibanSidewalkFeatureCount++;
                }
            }
        }

        DeleteExistingSidecars(normalizedPath);
        if (shapefileFeatures.Count == 0)
        {
            warnings.Add($"No valid GSI {featureLabel} polygons were available for '{Path.GetFileName(normalizedPath)}'.");
            return new WriteResult(0, 0, 0, 0, 0, 0, Array.Empty<string>(), warnings);
        }

        Shapefile.WriteAllFeatures(shapefileFeatures, normalizedPath);
        WriteProjectionFile(normalizedPath, projectCrs, warnings);
        File.WriteAllText(Path.ChangeExtension(normalizedPath, ".cpg"), "UTF-8", Encoding.ASCII);

        return new WriteResult(
            shapefileFeatures.Count,
            0,
            0,
            kibanSidewalkFeatureCount,
            0,
            kibanWaterFeatureCount,
            kibanLandUseFeatureCount,
            0,
            0,
            GetWrittenFiles(normalizedPath),
            warnings);
    }

    private static WriteResult WriteRevitModelPolygonShapefile(
        string shapefilePath,
        IReadOnlyCollection<RevitModelFootprintFeature> features,
        CrsReference projectCrs)
    {
        string normalizedPath = Path.ChangeExtension(shapefilePath, ".shp");
        string? directory = Path.GetDirectoryName(normalizedPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        List<string> warnings = new List<string>();
        List<Feature> shapefileFeatures = new List<Feature>(features.Count);
        int nextRowId = 1;
        int revitBuildingFeatureCount = 0;

        foreach (RevitModelFootprintFeature feature in features)
        {
            IReadOnlyList<Polygon> polygons = CreatePolygonalGeometries(
                feature.VerticesMetres,
                Array.Empty<IReadOnlyList<(double X, double Y)>>());
            if (polygons.Count == 0)
            {
                warnings.Add($"Skipped Revit element {feature.ElementId}: footprint polygon could not be written.");
                continue;
            }

            foreach (Polygon polygon in polygons)
            {
                shapefileFeatures.Add(CreateRevitPolygonFeature(polygon, nextRowId++, feature, projectCrs.EpsgCode));
                revitBuildingFeatureCount++;
            }
        }

        DeleteExistingSidecars(normalizedPath);
        if (shapefileFeatures.Count == 0)
        {
            warnings.Add($"No valid Revit building polygons were available for '{Path.GetFileName(normalizedPath)}'.");
            return new WriteResult(0, 0, 0, 0, 0, 0, 0, 0, Array.Empty<string>(), warnings);
        }

        Shapefile.WriteAllFeatures(shapefileFeatures, normalizedPath);
        WriteProjectionFile(normalizedPath, projectCrs, warnings);
        File.WriteAllText(Path.ChangeExtension(normalizedPath, ".cpg"), "UTF-8", Encoding.ASCII);

        return new WriteResult(
            shapefileFeatures.Count,
            0,
            0,
            0,
            0,
            0,
            revitBuildingFeatureCount,
            0,
            GetWrittenFiles(normalizedPath),
            warnings);
    }

    private static WriteResult WriteRevitModelLineShapefile(
        string shapefilePath,
        IReadOnlyCollection<RevitModelFootprintFeature> features,
        CrsReference projectCrs)
    {
        string normalizedPath = Path.ChangeExtension(shapefilePath, ".shp");
        string? directory = Path.GetDirectoryName(normalizedPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        List<string> warnings = new List<string>();
        List<Feature> shapefileFeatures = new List<Feature>(features.Count);
        int nextRowId = 1;
        int revitWallFeatureCount = 0;

        foreach (RevitModelFootprintFeature feature in features)
        {
            LineString? lineString = CreateLineGeometry(feature.VerticesMetres);
            if (lineString is null)
            {
                warnings.Add($"Skipped Revit element {feature.ElementId}: line geometry could not be written.");
                continue;
            }

            shapefileFeatures.Add(CreateRevitLineFeature(lineString, nextRowId++, feature, projectCrs.EpsgCode));
            revitWallFeatureCount++;
        }

        DeleteExistingSidecars(normalizedPath);
        if (shapefileFeatures.Count == 0)
        {
            warnings.Add($"No valid Revit wall lines were available for '{Path.GetFileName(normalizedPath)}'.");
            return new WriteResult(0, 0, 0, 0, 0, 0, 0, 0, Array.Empty<string>(), warnings);
        }

        Shapefile.WriteAllFeatures(shapefileFeatures, normalizedPath);
        WriteProjectionFile(normalizedPath, projectCrs, warnings);
        File.WriteAllText(Path.ChangeExtension(normalizedPath, ".cpg"), "UTF-8", Encoding.ASCII);

        return new WriteResult(
            shapefileFeatures.Count,
            0,
            0,
            0,
            0,
            0,
            0,
            revitWallFeatureCount,
            GetWrittenFiles(normalizedPath),
            warnings);
    }

    private enum StreamingSchema
    {
        PlateauPolygon,
        KibanLine,
        KibanPolygon,
        RevitPolygon,
        RevitLine
    }

    private sealed class StreamingLayerWriter : IDisposable
    {
        private readonly string shapefilePath;
        private readonly ShapeType shapeType;
        private readonly StreamingSchema schema;
        private ShapefileWriter? writer;

        public StreamingLayerWriter(string shapefilePath, ShapeType shapeType, StreamingSchema schema)
        {
            this.shapefilePath = Path.ChangeExtension(shapefilePath, ".shp");
            this.shapeType = shapeType;
            this.schema = schema;
        }

        public int NextRowId { get; private set; } = 1;

        public void Write(Feature feature)
        {
            if (feature is null) throw new ArgumentNullException(nameof(feature));
            EnsureOpen();
            writer!.Write(feature);
            NextRowId++;
        }

        public void Complete(CrsReference projectCrs, ICollection<string> warnings, ICollection<string> files)
        {
            Dispose();
            if (NextRowId <= 1)
            {
                return;
            }

            WriteProjectionFile(shapefilePath, projectCrs, warnings);
            File.WriteAllText(Path.ChangeExtension(shapefilePath, ".cpg"), "UTF-8", Encoding.ASCII);
            foreach (string file in GetWrittenFiles(shapefilePath))
            {
                files.Add(file);
            }
        }

        public void DeletePartialFiles()
        {
            Dispose();
            DeleteExistingSidecars(shapefilePath);
        }

        public void Dispose()
        {
            writer?.Dispose();
            writer = null;
        }

        private void EnsureOpen()
        {
            if (writer is not null)
            {
                return;
            }

            string? directory = Path.GetDirectoryName(shapefilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            DeleteExistingSidecars(shapefilePath);
            writer = Shapefile.OpenWrite(shapefilePath, CreateStreamingOptions(shapeType, schema));
        }
    }

    private static ShapefileWriterOptions CreateStreamingOptions(ShapeType shapeType, StreamingSchema schema)
    {
        ShapefileWriterOptions options = new ShapefileWriterOptions(shapeType, Array.Empty<DbfField>())
        {
            Encoding = Encoding.UTF8
        };

        switch (schema)
        {
            case StreamingSchema.PlateauPolygon:
                AddCommonPolygonFields(options);
                break;
            case StreamingSchema.KibanLine:
                AddKibanLineFields(options);
                break;
            case StreamingSchema.KibanPolygon:
                AddKibanPolygonFields(options);
                break;
            case StreamingSchema.RevitPolygon:
                AddRevitPolygonFields(options);
                break;
            case StreamingSchema.RevitLine:
                AddRevitLineFields(options);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(schema), schema, "Unsupported streaming shapefile schema.");
        }

        return options;
    }

    private static void AddCommonPolygonFields(ShapefileWriterOptions options)
    {
        options.AddNumericInt32Field("ROW_ID");
        options.AddCharacterField("TYPE", 32);
        options.AddCharacterField("LAYER", 64);
        options.AddCharacterField("SOURCE_ID", 254);
        options.AddLogicalField("DISSOLVED");
        options.AddNumericDoubleField("AREA_M2");
        options.AddNumericInt32Field("EPSG");
        options.AddCharacterField("FILL_RGB", 16);
        options.AddCharacterField("OUT_RGB", 16);
        options.AddNumericInt32Field("DRAW_ORDER");
        options.AddCharacterField("LU_CODE", 8);
        options.AddCharacterField("LU_NAME", 64);
    }

    private static void AddKibanLineFields(ShapefileWriterOptions options)
    {
        options.AddNumericInt32Field("ROW_ID");
        options.AddCharacterField("TYPE", 32);
        options.AddCharacterField("LAYER", 64);
        options.AddCharacterField("SOURCE_ID", 254);
        options.AddCharacterField("MESH", 16);
        options.AddCharacterField("FGD_TYPE", 80);
        options.AddCharacterField("VIS", 80);
        options.AddCharacterField("SRC_FILE", 120);
        options.AddNumericDoubleField("LENGTH_M");
        options.AddNumericInt32Field("EPSG");
        options.AddCharacterField("OUT_RGB", 16);
        options.AddNumericInt32Field("DRAW_ORDER");
    }

    private static void AddKibanPolygonFields(ShapefileWriterOptions options)
    {
        options.AddNumericInt32Field("ROW_ID");
        options.AddCharacterField("TYPE", 32);
        options.AddCharacterField("LAYER", 64);
        options.AddCharacterField("SOURCE_ID", 254);
        options.AddCharacterField("MESH", 16);
        options.AddCharacterField("FGD_TYPE", 80);
        options.AddCharacterField("WATER_TYPE", 64);
        options.AddCharacterField("LU_NAME", 64);
        options.AddCharacterField("VIS", 80);
        options.AddCharacterField("SRC_FILE", 120);
        options.AddNumericDoubleField("AREA_M2");
        options.AddNumericInt32Field("EPSG");
        options.AddCharacterField("FILL_RGB", 16);
        options.AddCharacterField("OUT_RGB", 16);
        options.AddNumericInt32Field("DRAW_ORDER");
    }

    private static void AddRevitPolygonFields(ShapefileWriterOptions options)
    {
        options.AddNumericInt32Field("ROW_ID");
        options.AddCharacterField("TYPE", 32);
        options.AddCharacterField("LAYER", 64);
        options.AddNumericInt64Field("ELEM_ID");
        options.AddCharacterField("ELEM_NAME", 120);
        options.AddCharacterField("CATEGORY", 64);
        options.AddNumericDoubleField("AREA_M2");
        options.AddNumericInt32Field("EPSG");
        options.AddCharacterField("FILL_RGB", 16);
        options.AddCharacterField("OUT_RGB", 16);
        options.AddNumericInt32Field("DRAW_ORDER");
    }

    private static void AddRevitLineFields(ShapefileWriterOptions options)
    {
        options.AddNumericInt32Field("ROW_ID");
        options.AddCharacterField("TYPE", 32);
        options.AddCharacterField("LAYER", 64);
        options.AddNumericInt64Field("ELEM_ID");
        options.AddCharacterField("ELEM_NAME", 120);
        options.AddCharacterField("CATEGORY", 64);
        options.AddNumericDoubleField("LENGTH_M");
        options.AddNumericInt32Field("EPSG");
        options.AddCharacterField("OUT_RGB", 16);
        options.AddNumericInt32Field("DRAW_ORDER");
    }

    private static Feature CreateRevitPolygonFeature(
        Polygon polygon,
        int rowId,
        RevitModelFootprintFeature source,
        int epsgCode)
    {
        FeatureStyle style = GetStyle(source.Layer);
        AttributesTable attributes = new AttributesTable
        {
            { "ROW_ID", rowId },
            { "TYPE", style.Type },
            { "LAYER", source.Layer },
            { "ELEM_ID", source.ElementId },
            { "ELEM_NAME", Truncate(source.ElementName, 120) },
            { "CATEGORY", Truncate(source.Category, 64) },
            { "AREA_M2", polygon.Area },
            { "EPSG", epsgCode },
            { "FILL_RGB", style.FillRgb },
            { "OUT_RGB", style.OutlineRgb },
            { "DRAW_ORDER", style.DrawOrder },
        };

        return new Feature(polygon, attributes);
    }

    private static Feature CreateRevitLineFeature(
        LineString lineString,
        int rowId,
        RevitModelFootprintFeature source,
        int epsgCode)
    {
        FeatureStyle style = GetStyle(source.Layer);
        AttributesTable attributes = new AttributesTable
        {
            { "ROW_ID", rowId },
            { "TYPE", style.Type },
            { "LAYER", source.Layer },
            { "ELEM_ID", source.ElementId },
            { "ELEM_NAME", Truncate(source.ElementName, 120) },
            { "CATEGORY", Truncate(source.Category, 64) },
            { "LENGTH_M", lineString.Length },
            { "EPSG", epsgCode },
            { "OUT_RGB", style.OutlineRgb },
            { "DRAW_ORDER", style.DrawOrder },
        };

        return new Feature(lineString, attributes);
    }

    private static Feature CreateKibanPolygonFeature(
        Polygon polygon,
        int rowId,
        KibanPolygonExportFeature source,
        int epsgCode)
    {
        FeatureStyle style = GetStyle(source.Layer);
        if (string.Equals(source.Layer, KibanGmlParser.LandUseLayer, StringComparison.Ordinal))
        {
            style = GetLandUseStyleByName(source.FeatureType, style);
        }
        AttributesTable attributes = new AttributesTable
        {
            { "ROW_ID", rowId },
            { "TYPE", style.Type },
            { "LAYER", source.Layer },
            { "SOURCE_ID", Truncate(source.SourceId ?? string.Empty, 254) },
            { "MESH", Truncate(source.MeshCode, 16) },
            { "FGD_TYPE", Truncate(source.FeatureType, 80) },
            { "WATER_TYPE", Truncate(source.FeatureType, 64) },
            { "LU_NAME", Truncate(source.FeatureType, 64) },
            { "VIS", Truncate(source.Visibility, 80) },
            { "SRC_FILE", Truncate(Path.GetFileName(source.SourcePath) ?? string.Empty, 120) },
            { "AREA_M2", polygon.Area },
            { "EPSG", epsgCode },
            { "FILL_RGB", style.FillRgb },
            { "OUT_RGB", style.OutlineRgb },
            { "DRAW_ORDER", style.DrawOrder },
        };

        return new Feature(polygon, attributes);
    }

    private static Feature CreateFeature(
        Polygon polygon,
        int rowId,
        string layer,
        string? sourceId,
        bool dissolved,
        int epsgCode,
        string? classCode = null,
        string? className = null)
    {
        FeatureStyle style = GetStyle(layer);
        if (string.Equals(layer, PlateauContextOutlinesDxfWriter.PlateauLandUseLayer, StringComparison.Ordinal))
        {
            style = GetLandUseStyleByName(className, style);
        }
        AttributesTable attributes = new AttributesTable
        {
            { "ROW_ID", rowId },
            { "TYPE", style.Type },
            { "LAYER", layer },
            { "SOURCE_ID", Truncate(sourceId ?? string.Empty, 254) },
            { "DISSOLVED", dissolved },
            { "AREA_M2", polygon.Area },
            { "EPSG", epsgCode },
            { "FILL_RGB", style.FillRgb },
            { "OUT_RGB", style.OutlineRgb },
            { "DRAW_ORDER", style.DrawOrder },
            { "LU_CODE", Truncate(classCode ?? string.Empty, 8) },
            { "LU_NAME", Truncate(className ?? string.Empty, 64) },
        };

        return new Feature(polygon, attributes);
    }

    private static Feature CreateLineFeature(
        LineString lineString,
        int rowId,
        KibanLineExportFeature source,
        int epsgCode)
    {
        FeatureStyle style = GetStyle(source.Layer);
        AttributesTable attributes = new AttributesTable
        {
            { "ROW_ID", rowId },
            { "TYPE", style.Type },
            { "LAYER", source.Layer },
            { "SOURCE_ID", Truncate(source.SourceId ?? string.Empty, 254) },
            { "MESH", Truncate(source.MeshCode, 16) },
            { "FGD_TYPE", Truncate(source.FeatureType, 80) },
            { "VIS", Truncate(source.Visibility, 80) },
            { "SRC_FILE", Truncate(Path.GetFileName(source.SourcePath) ?? string.Empty, 120) },
            { "LENGTH_M", lineString.Length },
            { "EPSG", epsgCode },
            { "OUT_RGB", style.OutlineRgb },
            { "DRAW_ORDER", style.DrawOrder },
        };

        return new Feature(lineString, attributes);
    }

    private static IReadOnlyList<Polygon> CreatePolygonalGeometries(
        IReadOnlyList<(double X, double Y)> exterior,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> interiors)
    {
        Geometry? geometry = CreatePolygonGeometry(exterior, interiors);
        if (geometry is null || geometry.IsEmpty)
        {
            return Array.Empty<Polygon>();
        }

        List<Polygon> polygons = new List<Polygon>();
        AddPolygons(geometry, polygons);
        return polygons;
    }

    private static Geometry? CreatePolygonGeometry(
        IReadOnlyList<(double X, double Y)> exterior,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> interiors)
    {
        GeometryFactory geometryFactory = new GeometryFactory();
        LinearRing? shell = CreateLinearRing(geometryFactory, exterior);
        if (shell is null)
        {
            return null;
        }

        List<LinearRing> holes = new List<LinearRing>(interiors.Count);
        foreach (IReadOnlyList<(double X, double Y)> interior in interiors)
        {
            LinearRing? hole = CreateLinearRing(geometryFactory, interior);
            if (hole is not null)
            {
                holes.Add(hole);
            }
        }

        try
        {
            Polygon polygon = geometryFactory.CreatePolygon(shell, holes.ToArray());
            if (polygon.IsEmpty || polygon.Area <= AreaEpsilon)
            {
                return null;
            }

            Geometry geometry = polygon.IsValid ? polygon : polygon.Buffer(0d);
            return geometry.IsEmpty || geometry.Area <= AreaEpsilon ? null : geometry;
        }
        catch (Exception ex) when (ex is TopologyException || ex is ArgumentException || ex is InvalidOperationException)
        {
            return null;
        }
    }

    private static LinearRing? CreateLinearRing(GeometryFactory geometryFactory, IReadOnlyList<(double X, double Y)> ring)
    {
        if (ring.Count < 3)
        {
            return null;
        }

        List<Coordinate> coordinates = new List<Coordinate>(ring.Count + 1);
        foreach ((double x, double y) in ring)
        {
            Coordinate coordinate = new Coordinate(x, y);
            if (coordinates.Count == 0 || !SameCoordinate(coordinates[coordinates.Count - 1], coordinate))
            {
                coordinates.Add(coordinate);
            }
        }

        while (coordinates.Count > 1 && SameCoordinate(coordinates[0], coordinates[coordinates.Count - 1]))
        {
            coordinates.RemoveAt(coordinates.Count - 1);
        }

        if (coordinates.Count < 3)
        {
            return null;
        }

        coordinates.Add(new Coordinate(coordinates[0]));
        return geometryFactory.CreateLinearRing(coordinates.ToArray());
    }

    private static LineString? CreateLineGeometry(IReadOnlyList<(double X, double Y)> vertices)
    {
        if (vertices.Count < 2)
        {
            return null;
        }

        GeometryFactory geometryFactory = new GeometryFactory();
        List<Coordinate> coordinates = new List<Coordinate>(vertices.Count);
        foreach ((double x, double y) in vertices)
        {
            Coordinate coordinate = new Coordinate(x, y);
            if (coordinates.Count == 0 || !SameCoordinate(coordinates[coordinates.Count - 1], coordinate))
            {
                coordinates.Add(coordinate);
            }
        }

        if (coordinates.Count < 2)
        {
            return null;
        }

        try
        {
            LineString lineString = geometryFactory.CreateLineString(coordinates.ToArray());
            return lineString.IsEmpty || lineString.Length <= 0d ? null : lineString;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static void AddPolygons(Geometry geometry, ICollection<Polygon> polygons)
    {
        if (geometry is Polygon polygon)
        {
            if (!polygon.IsEmpty && polygon.Area > AreaEpsilon)
            {
                polygons.Add(polygon);
            }

            return;
        }

        for (int index = 0; index < geometry.NumGeometries; index++)
        {
            Geometry child = geometry.GetGeometryN(index);
            if (!child.IsEmpty)
            {
                AddPolygons(child, polygons);
            }
        }
    }

    private static void WriteProjectionFile(string shapefilePath, CrsReference projectCrs, ICollection<string> warnings)
    {
        CrsRegistry registry = new CrsRegistry();
        if (!registry.TryGetByEpsgCode(projectCrs.EpsgCode, out CrsDefinition? definition) || definition is null)
        {
            warnings.Add($"Skipped .prj: EPSG:{projectCrs.EpsgCode} is not available in the CRS registry.");
            return;
        }

        File.WriteAllText(Path.ChangeExtension(shapefilePath, ".prj"), BuildEsriWkt(definition), Encoding.ASCII);
    }

    private static string BuildEsriWkt(CrsDefinition definition)
    {
        string datumEsri = BuildEsriDatumName(definition.DatumName);
        string zoneName = string.Format(CultureInfo.InvariantCulture, "{0}_Japan_Zone_{1}", datumEsri, definition.JapanZoneNumber);
        return string.Format(
            CultureInfo.InvariantCulture,
            "PROJCS[\"{0}\",GEOGCS[\"GCS_{1}\",DATUM[\"D_{1}\",SPHEROID[\"GRS_1980\",6378137.0,298.257222101]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"False_Easting\",{2}],PARAMETER[\"False_Northing\",{3}],PARAMETER[\"Central_Meridian\",{4}],PARAMETER[\"Scale_Factor\",{5}],PARAMETER[\"Latitude_Of_Origin\",{6}],UNIT[\"Meter\",1.0],AUTHORITY[\"EPSG\",\"{7}\"]]",
            zoneName,
            datumEsri,
            definition.FalseEasting,
            definition.FalseNorthing,
            definition.CentralMeridian,
            definition.ScaleFactor,
            definition.LatitudeOfOrigin,
            definition.EpsgCode);
    }

    private static string BuildEsriDatumName(string datumName)
    {
        string normalized = datumName.Replace(" ", "_");
        if (normalized.StartsWith("JGD", StringComparison.OrdinalIgnoreCase)
            && normalized.Length > 3
            && char.IsDigit(normalized[3]))
        {
            return normalized.Insert(3, "_");
        }

        return normalized;
    }

    private static void DeleteExistingSidecars(string shapefilePath)
    {
        foreach (string extension in SidecarExtensions)
        {
            string path = Path.ChangeExtension(shapefilePath, extension);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static void DeleteKnownCompanionSidecars(string shapefilePath)
    {
        DeleteExistingSidecars(shapefilePath);
        foreach (string suffix in KnownCompanionSuffixes)
        {
            DeleteExistingSidecars(BuildCompanionShapefilePath(shapefilePath, suffix));
        }
    }

    private static IReadOnlyList<string> GetWrittenFiles(string shapefilePath)
    {
        return SidecarExtensions
            .Select(extension => Path.ChangeExtension(shapefilePath, extension))
            .Where(File.Exists)
            .ToArray();
    }

    private static string BuildCompanionShapefilePath(string shapefilePath, string suffix)
    {
        string? directory = Path.GetDirectoryName(shapefilePath);
        string fileName = Path.GetFileNameWithoutExtension(shapefilePath);
        string companionName = string.Concat(fileName, suffix, ".shp");
        return string.IsNullOrWhiteSpace(directory)
            ? companionName
            : Path.Combine(directory, companionName);
    }

    private static bool TryGetPlateauCategorySuffix(string layer, out string? suffix)
    {
        foreach ((string categoryLayer, string categorySuffix, _) in PlateauCategoryExports)
        {
            if (string.Equals(layer, categoryLayer, StringComparison.Ordinal))
            {
                suffix = categorySuffix;
                return true;
            }
        }

        suffix = null;
        return false;
    }

    private static bool TryGetKibanPolygonSuffix(string layer, out string? suffix, out string featureLabel)
    {
        if (string.Equals(layer, KibanGmlParser.WaterLayer, StringComparison.Ordinal))
        {
            suffix = "_gsi_water";
            featureLabel = "water";
            return true;
        }

        if (string.Equals(layer, KibanGmlParser.LandUseLayer, StringComparison.Ordinal))
        {
            suffix = "_gsi_landuse";
            featureLabel = "land-use";
            return true;
        }

        if (string.Equals(layer, PlateauContextOutlinesDxfWriter.GsiSidewalksLayer, StringComparison.Ordinal))
        {
            suffix = "_gsi_sidewalks";
            featureLabel = "sidewalk-strip";
            return true;
        }

        suffix = null;
        featureLabel = "polygon";
        return false;
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    private static bool SameCoordinate(Coordinate left, Coordinate right)
    {
        return left.X == right.X && left.Y == right.Y;
    }

    private static FeatureStyle GetStyle(string layer)
    {
        switch (layer)
        {
            case "PLATEAU_ROADS":
                return new FeatureStyle("ROAD", "205,205,205", "170,170,170", 30);
            case "PLATEAU_BUILDINGS":
                return new FeatureStyle("BUILDING", "232,235,235", "190,195,195", 40);
            case "PLATEAU_BRIDGES":
                return new FeatureStyle("BRIDGE", "234,224,206", "190,180,165", 50);
            case "PLATEAU_VEGETATION":
                return new FeatureStyle("VEGETATION", "150,200,150", "105,160,105", 60);
            case "PLATEAU_LANDUSE":
                return new FeatureStyle("LANDUSE", "200,220,160", "120,160,80", 35);
            case "PLATEAU_RELIEF":
                return new FeatureStyle("RELIEF", "238,238,238", "205,205,205", 10);
            case "GSI_SIDEWALKS":
                return new FeatureStyle("SIDEWALK", "240,230,220", "180,160,140", 22);
            case "GSI_RAILWAYS":
                return new FeatureStyle("RAILWAY", "200,200,220", "160,160,180", 20);
            case "GSI_WATER":
                return new FeatureStyle("WATER", "175,210,235", "115,160,200", 15);
            case "GSI_LANDUSE":
                return new FeatureStyle("LANDUSE", "185,220,150", "95,150,75", 18);
            case RevitBuildingsLayer:
                return new FeatureStyle("REVIT_BUILDING", "255,230,180", "200,160,80", 70);
            case RevitWallsLayer:
                return new FeatureStyle("REVIT_WALL", "255,200,150", "180,100,40", 75);
            default:
                return new FeatureStyle("OTHER", "220,220,220", "180,180,180", 90);
        }
    }

    private static FeatureStyle GetLandUseStyleByName(string? className, FeatureStyle defaultStyle)
    {
        string name = className ?? string.Empty;
        if (name.Length == 0)
        {
            return defaultStyle;
        }

        // Forests / mountain woodland — dark green
        if (Contains(name, "森林") || Contains(name, "山林") || Contains(name, "樹林"))
            return new FeatureStyle(defaultStyle.Type, "120,170,110", "70,120,60", defaultStyle.DrawOrder);

        // Rice paddy — pale blue-green
        if (Contains(name, "田"))
            return new FeatureStyle(defaultStyle.Type, "200,225,200", "140,180,140", defaultStyle.DrawOrder);

        // Field / orchard / pasture — olive
        if (Contains(name, "畑"))
            return new FeatureStyle(defaultStyle.Type, "220,220,160", "170,170,100", defaultStyle.DrawOrder);

        // Agri-forestry-fisheries facility — muted olive
        if (Contains(name, "農林漁業"))
            return new FeatureStyle(defaultStyle.Type, "200,215,160", "150,170,110", defaultStyle.DrawOrder);

        // Grassland — light green
        if (Contains(name, "草地"))
            return new FeatureStyle(defaultStyle.Type, "190,225,170", "130,180,110", defaultStyle.DrawOrder);

        // Park / green space / garden / promenade — medium green
        if (Contains(name, "緑地") || Contains(name, "緑道") || Contains(name, "公園")
            || Contains(name, "庭園") || Contains(name, "園地"))
            return new FeatureStyle(defaultStyle.Type, "160,210,140", "100,160,80", defaultStyle.DrawOrder);

        // Golf course — bright green
        if (Contains(name, "ゴルフ"))
            return new FeatureStyle(defaultStyle.Type, "150,200,90", "100,150,50", defaultStyle.DrawOrder);

        // Wasteland / natural mosaic — tan
        if (Contains(name, "荒") || Contains(name, "原野") || Contains(name, "自然地"))
            return new FeatureStyle(defaultStyle.Type, "210,200,170", "160,150,120", defaultStyle.DrawOrder);

        // Water surface — light blue
        if (Contains(name, "水面") || Contains(name, "河川") || Contains(name, "湖沼") || Contains(name, "海"))
            return new FeatureStyle("WATER", "175,210,235", "115,160,200", defaultStyle.DrawOrder);

        // Residential — warm beige
        if (Contains(name, "住宅"))
            return new FeatureStyle(defaultStyle.Type, "245,225,200", "200,170,130", defaultStyle.DrawOrder);

        // Commercial — pink
        if (Contains(name, "商業"))
            return new FeatureStyle(defaultStyle.Type, "245,200,195", "200,140,135", defaultStyle.DrawOrder);

        // Industrial — light purple
        if (Contains(name, "工業"))
            return new FeatureStyle(defaultStyle.Type, "215,200,225", "165,150,180", defaultStyle.DrawOrder);

        // Solar power — yellow
        if (Contains(name, "太陽光"))
            return new FeatureStyle(defaultStyle.Type, "245,225,140", "190,170,80", defaultStyle.DrawOrder);

        // Transport / parking / road land — gray
        if (Contains(name, "交通") || Contains(name, "駐車") || Contains(name, "道路"))
            return new FeatureStyle(defaultStyle.Type, "210,210,210", "160,160,160", defaultStyle.DrawOrder);

        // Low-use / vacant — pale gray
        if (Contains(name, "低未利用"))
            return new FeatureStyle(defaultStyle.Type, "225,225,215", "170,170,165", defaultStyle.DrawOrder);

        // Public utility (non-green) — light orange
        if (Contains(name, "公益") || Contains(name, "公共"))
            return new FeatureStyle(defaultStyle.Type, "240,220,180", "190,170,120", defaultStyle.DrawOrder);

        return defaultStyle;
    }

    private static bool Contains(string name, string token)
    {
        return name.IndexOf(token, StringComparison.Ordinal) >= 0;
    }

    private readonly struct FeatureStyle
    {
        public FeatureStyle(string type, string fillRgb, string outlineRgb, int drawOrder)
        {
            Type = type;
            FillRgb = fillRgb;
            OutlineRgb = outlineRgb;
            DrawOrder = drawOrder;
        }

        public string Type { get; }
        public string FillRgb { get; }
        public string OutlineRgb { get; }
        public int DrawOrder { get; }
    }
}
