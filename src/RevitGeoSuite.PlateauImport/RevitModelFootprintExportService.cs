using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoSuite.Core.Coordinates;

namespace RevitGeoSuite.PlateauImport;

/// <summary>
/// Extracts real wall and floor footprints from the active Revit document and projects
/// them to shared/projected metres for inclusion in the context export package.
/// </summary>
public sealed class RevitModelFootprintExportService
{
    private const double FeetToMeters = 0.3048d;

    public IReadOnlyList<RevitModelFootprintFeature> ExtractFootprints(
        Document? document,
        PlateauImportReferenceContext? referenceContext,
        ICollection<string>? warnings = null)
    {
        if (document is null || referenceContext is null)
        {
            return Array.Empty<RevitModelFootprintFeature>();
        }

        ProjectionFrame frame;
        try
        {
            frame = ProjectionFrame.Create(document.ActiveProjectLocation, referenceContext);
        }
        catch (Exception ex)
        {
            warnings?.Add($"Revit footprint extraction skipped: projection frame could not be built ({ex.Message}).");
            return Array.Empty<RevitModelFootprintFeature>();
        }

        List<RevitModelFootprintFeature> features = new List<RevitModelFootprintFeature>();

        ExtractFloors(document, frame, features, warnings);
        ExtractWalls(document, frame, features, warnings);

        return features;
    }

    private static void ExtractFloors(
        Document document,
        ProjectionFrame frame,
        List<RevitModelFootprintFeature> features,
        ICollection<string>? warnings)
    {
        foreach (Element element in new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_Floors)
            .WhereElementIsNotElementType())
        {
            if (element is not Floor floor)
            {
                continue;
            }

            IReadOnlyList<(double X, double Y)>? ring = TryExtractFloorBoundary(floor, frame, warnings);
            if (ring is null || ring.Count < 3)
            {
                continue;
            }

            features.Add(new RevitModelFootprintFeature(
                PlateauContextShapefileWriter.RevitBuildingsLayer,
                category: "Floor",
                isPolygon: true,
                verticesMetres: ring,
                elementId: GetElementIdValue(floor.Id),
                elementName: floor.Name));
        }
    }

    private static IReadOnlyList<(double X, double Y)>? TryExtractFloorBoundary(
        Floor floor,
        ProjectionFrame frame,
        ICollection<string>? warnings)
    {
        Sketch? sketch = null;
        try
        {
            sketch = floor.Document.GetElement(floor.SketchId) as Sketch;
        }
        catch (Exception)
        {
            sketch = null;
        }

        if (sketch?.Profile is null || sketch.Profile.Size == 0)
        {
            return TryExtractFromBoundingBox(floor, frame);
        }

        CurveArray? outerLoop = null;
        foreach (CurveArray candidate in sketch.Profile)
        {
            if (outerLoop is null || candidate.Size > outerLoop.Size)
            {
                outerLoop = candidate;
            }
        }

        if (outerLoop is null || outerLoop.Size == 0)
        {
            return TryExtractFromBoundingBox(floor, frame);
        }

        List<(double X, double Y)> ring = new List<(double X, double Y)>();
        foreach (Curve curve in outerLoop)
        {
            IList<XYZ> tessellated;
            try
            {
                tessellated = curve.Tessellate();
            }
            catch (Exception)
            {
                continue;
            }

            for (int i = 0; i < tessellated.Count; i++)
            {
                ProjectedCoordinate projected = frame.ToProjectedMeters(tessellated[i]);
                (double X, double Y) vertex = (projected.Easting, projected.Northing);
                if (ring.Count == 0 || !ApproximatelyEqual(ring[ring.Count - 1], vertex))
                {
                    ring.Add(vertex);
                }
            }
        }

        while (ring.Count > 1 && ApproximatelyEqual(ring[0], ring[ring.Count - 1]))
        {
            ring.RemoveAt(ring.Count - 1);
        }

        return ring.Count >= 3 ? ring : null;
    }

    private static IReadOnlyList<(double X, double Y)>? TryExtractFromBoundingBox(Floor floor, ProjectionFrame frame)
    {
        BoundingBoxXYZ? bbox = floor.get_BoundingBox(null);
        if (bbox is null)
        {
            return null;
        }

        Transform transform = bbox.Transform ?? Transform.Identity;
        XYZ a = transform.OfPoint(new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Min.Z));
        XYZ b = transform.OfPoint(new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Min.Z));
        XYZ c = transform.OfPoint(new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Min.Z));
        XYZ d = transform.OfPoint(new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Min.Z));

        List<(double X, double Y)> ring = new List<(double X, double Y)>
        {
            ToVertex(frame, a),
            ToVertex(frame, b),
            ToVertex(frame, c),
            ToVertex(frame, d)
        };
        return ring;
    }

    private static void ExtractWalls(
        Document document,
        ProjectionFrame frame,
        List<RevitModelFootprintFeature> features,
        ICollection<string>? warnings)
    {
        foreach (Element element in new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_Walls)
            .WhereElementIsNotElementType())
        {
            if (element is not Wall wall)
            {
                continue;
            }

            if (wall.Location is not LocationCurve locationCurve || locationCurve.Curve is null)
            {
                continue;
            }

            IList<XYZ> tessellated;
            try
            {
                tessellated = locationCurve.Curve.Tessellate();
            }
            catch (Exception)
            {
                continue;
            }

            if (tessellated.Count < 2)
            {
                continue;
            }

            List<(double X, double Y)> vertices = new List<(double X, double Y)>(tessellated.Count);
            for (int i = 0; i < tessellated.Count; i++)
            {
                (double X, double Y) vertex = ToVertex(frame, tessellated[i]);
                if (vertices.Count == 0 || !ApproximatelyEqual(vertices[vertices.Count - 1], vertex))
                {
                    vertices.Add(vertex);
                }
            }

            if (vertices.Count < 2)
            {
                continue;
            }

            features.Add(new RevitModelFootprintFeature(
                PlateauContextShapefileWriter.RevitWallsLayer,
                category: "Wall",
                isPolygon: false,
                verticesMetres: vertices,
                elementId: GetElementIdValue(wall.Id),
                elementName: wall.Name));
        }
    }

    private static (double X, double Y) ToVertex(ProjectionFrame frame, XYZ point)
    {
        ProjectedCoordinate projected = frame.ToProjectedMeters(point);
        return (projected.Easting, projected.Northing);
    }

    private static bool ApproximatelyEqual((double X, double Y) a, (double X, double Y) b)
    {
        return Math.Abs(a.X - b.X) < 1e-6d && Math.Abs(a.Y - b.Y) < 1e-6d;
    }

    private static long GetElementIdValue(ElementId id)
    {
        if (id is null)
        {
            return -1;
        }

        try
        {
            return id.Value;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    private readonly struct SharedVector
    {
        public SharedVector(double eastFeet, double northFeet, double upFeet)
        {
            EastFeet = eastFeet;
            NorthFeet = northFeet;
            UpFeet = upFeet;
        }

        public double EastFeet { get; }

        public double NorthFeet { get; }

        public double UpFeet { get; }
    }

    private sealed class ProjectionFrame
    {
        private readonly XYZ anchorPoint;
        private readonly SharedVector xAxis;
        private readonly SharedVector yAxis;
        private readonly SharedVector zAxis;
        private readonly PlateauImportReferenceContext referenceContext;

        private ProjectionFrame(
            XYZ anchorPoint,
            SharedVector xAxis,
            SharedVector yAxis,
            SharedVector zAxis,
            PlateauImportReferenceContext referenceContext)
        {
            this.anchorPoint = anchorPoint;
            this.xAxis = xAxis;
            this.yAxis = yAxis;
            this.zAxis = zAxis;
            this.referenceContext = referenceContext;
        }

        public static ProjectionFrame Create(ProjectLocation projectLocation, PlateauImportReferenceContext referenceContext)
        {
            XYZ anchorPoint = new XYZ(referenceContext.AnchorXFeet, referenceContext.AnchorYFeet, referenceContext.AnchorZFeet);
            ProjectPosition anchor = projectLocation.GetProjectPosition(anchorPoint);
            ProjectPosition plusX = projectLocation.GetProjectPosition(new XYZ(anchorPoint.X + 1d, anchorPoint.Y, anchorPoint.Z));
            ProjectPosition plusY = projectLocation.GetProjectPosition(new XYZ(anchorPoint.X, anchorPoint.Y + 1d, anchorPoint.Z));
            ProjectPosition plusZ = projectLocation.GetProjectPosition(new XYZ(anchorPoint.X, anchorPoint.Y, anchorPoint.Z + 1d));

            return new ProjectionFrame(
                anchorPoint,
                new SharedVector(plusX.EastWest - anchor.EastWest, plusX.NorthSouth - anchor.NorthSouth, plusX.Elevation - anchor.Elevation),
                new SharedVector(plusY.EastWest - anchor.EastWest, plusY.NorthSouth - anchor.NorthSouth, plusY.Elevation - anchor.Elevation),
                new SharedVector(plusZ.EastWest - anchor.EastWest, plusZ.NorthSouth - anchor.NorthSouth, plusZ.Elevation - anchor.Elevation),
                referenceContext);
        }

        public ProjectedCoordinate ToProjectedMeters(XYZ point)
        {
            double deltaX = point.X - anchorPoint.X;
            double deltaY = point.Y - anchorPoint.Y;
            double deltaZ = point.Z - anchorPoint.Z;

            double eastFeet = (deltaX * xAxis.EastFeet) + (deltaY * yAxis.EastFeet) + (deltaZ * zAxis.EastFeet);
            double northFeet = (deltaX * xAxis.NorthFeet) + (deltaY * yAxis.NorthFeet) + (deltaZ * zAxis.NorthFeet);

            return new ProjectedCoordinate(
                referenceContext.AnchorProjectedCoordinate.Easting + (eastFeet * FeetToMeters),
                referenceContext.AnchorProjectedCoordinate.Northing + (northFeet * FeetToMeters));
        }
    }
}
