using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RevitGeoSuite.PlateauImport;

/// <summary>
/// Writes a DXF for a context-export package using the Civil 3D shared-coordinates
/// convention: (0,0) is positioned at the Survey Point's shared/projected position
/// (carried on the package as OriginOffsetMetres), and a marker block is emitted at
/// the Project Base Point.
/// </summary>
public sealed class PlateauContextDxfExportService
{
    public sealed class WriteResult
    {
        public WriteResult(int polylineCount, int areaFillCount, IReadOnlyList<string> files, IReadOnlyList<string> warnings)
        {
            PolylineCount = polylineCount;
            AreaFillCount = areaFillCount;
            Files = files ?? throw new ArgumentNullException(nameof(files));
            Warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
        }

        public int PolylineCount { get; }

        public int AreaFillCount { get; }

        public IReadOnlyList<string> Files { get; }

        public IReadOnlyList<string> Warnings { get; }
    }

    /// <summary>
    /// Writes a DXF at <paramref name="dxfPath"/> containing the requested subsets of the package.
    /// </summary>
    public WriteResult Write(
        string dxfPath,
        PlateauOutlineDxfExportPackage package,
        bool includePlateauContext,
        bool includeRevitModel,
        Action<string>? onStage = null)
    {
        if (string.IsNullOrWhiteSpace(dxfPath)) throw new ArgumentException("A DXF path is required.", nameof(dxfPath));
        if (package is null) throw new ArgumentNullException(nameof(package));

        string normalizedPath = Path.ChangeExtension(dxfPath, ".dxf");
        string? directory = Path.GetDirectoryName(normalizedPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        List<PlateauContextOutlinesDxfWriter.OutlineFeature> outlines = new List<PlateauContextOutlinesDxfWriter.OutlineFeature>();
        List<PlateauContextOutlinesDxfWriter.AreaFeature> areas = new List<PlateauContextOutlinesDxfWriter.AreaFeature>();

        if (includePlateauContext)
        {
            outlines.AddRange(package.Features);
            outlines.AddRange(package.KibanFeatures);
            areas.AddRange(package.RoadAreas);
        }

        if (includeRevitModel)
        {
            foreach (RevitModelFootprintFeature feature in package.RevitModelFeatures)
            {
                if (!feature.IsPolygon)
                {
                    continue;
                }

                if (feature.VerticesMetres.Count < 3)
                {
                    continue;
                }

                outlines.Add(new PlateauContextOutlinesDxfWriter.OutlineFeature(
                    feature.Layer,
                    feature.VerticesMetres,
                    sourceId: feature.ElementId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        if (outlines.Count == 0 && areas.Count == 0)
        {
            return new WriteResult(0, 0, Array.Empty<string>(), new List<string> { "No DXF features were available to write." });
        }

        onStage?.Invoke("Writing DXF");

        PlateauContextOutlinesDxfWriter.WriteResult innerResult;
        using (StreamWriter writer = new StreamWriter(normalizedPath, append: false, encoding: System.Text.Encoding.ASCII))
        {
            innerResult = PlateauContextOutlinesDxfWriter.Write(
                writer,
                outlines,
                areas,
                package.ProjectBasePointMarkerMetres,
                package.OriginOffsetMetres);
        }

        List<string> files = new List<string> { normalizedPath };
        return new WriteResult(
            innerResult.PolylineCount,
            innerResult.FillCount,
            files,
            innerResult.Warnings.ToArray());
    }
}
