using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitGeoSuite.Core.Coordinates;

namespace RevitGeoSuite.PlateauImport;

public sealed class RevitModelFootprintOverlayService
{
    private const double FeetToMeters = 0.3048d;
    private readonly ProjectedFootprintOverlayGeoJsonBuilder geoJsonBuilder;

    public RevitModelFootprintOverlayService(ICoordinateTransformer coordinateTransformer)
        : this(new ProjectedFootprintOverlayGeoJsonBuilder(coordinateTransformer))
    {
    }

    public RevitModelFootprintOverlayService(ProjectedFootprintOverlayGeoJsonBuilder geoJsonBuilder)
    {
        this.geoJsonBuilder = geoJsonBuilder ?? throw new ArgumentNullException(nameof(geoJsonBuilder));
    }

    public ModelFootprintOverlayResult Build(Document? document, PlateauImportReferenceContext? referenceContext)
    {
        if (document is null)
        {
            return new ModelFootprintOverlayResult
            {
                StatusMessage = "Host model overlay unavailable because no active Revit document is loaded."
            };
        }

        if (referenceContext is null)
        {
            return new ModelFootprintOverlayResult
            {
                StatusMessage = "Host model overlay unavailable until the PLATEAU reference context can be resolved."
            };
        }

        ProjectionFrame frame = ProjectionFrame.Create(document.ActiveProjectLocation, referenceContext);
        List<ProjectedCoordinate> projectedPoints = new List<ProjectedCoordinate>();
        int includedElementCount = 0;

        foreach (Element element in new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_Floors)
            .WhereElementIsNotElementType())
        {
            if (!IsOverlayCandidate(element))
            {
                continue;
            }

            BoundingBoxXYZ? boundingBox = element.get_BoundingBox(null);
            if (boundingBox is null)
            {
                continue;
            }

            bool addedPoint = false;
            foreach (XYZ corner in GetBoundingBoxCorners(boundingBox))
            {
                projectedPoints.Add(frame.ToProjectedMeters(corner));
                addedPoint = true;
            }

            if (addedPoint)
            {
                includedElementCount++;
            }
        }

        if (includedElementCount == 0)
        {
            return new ModelFootprintOverlayResult
            {
                StatusMessage = "No usable floor-based host-model footprint could be derived from the active Revit model."
            };
        }

        string geoJson = geoJsonBuilder.CreateGeoJson(
            projectedPoints,
            referenceContext.ProjectCrs,
            featureId: "revit-host-model-footprint",
            title: "Revit Floor Footprint",
            elementCount: includedElementCount);

        if (string.IsNullOrWhiteSpace(geoJson))
        {
            return new ModelFootprintOverlayResult
            {
                IncludedElementCount = includedElementCount,
                StatusMessage = "The floor-based host model footprint could not be simplified into a usable map overlay."
            };
        }

        return new ModelFootprintOverlayResult
        {
            GeoJson = geoJson,
            IncludedElementCount = includedElementCount,
            StatusMessage = $"Approximate host-model footprint loaded from {includedElementCount} floor elements."
        };
    }

    private static bool IsOverlayCandidate(Element element)
    {
        return element is not null
            && element is not RevitLinkInstance
            && !element.ViewSpecific
            && element.Category is not null
            && element.Category.Id.Value == (long)BuiltInCategory.OST_Floors;
    }

    private static IEnumerable<XYZ> GetBoundingBoxCorners(BoundingBoxXYZ boundingBox)
    {
        Transform transform = boundingBox.Transform ?? Transform.Identity;
        XYZ min = boundingBox.Min;
        XYZ max = boundingBox.Max;

        yield return transform.OfPoint(new XYZ(min.X, min.Y, min.Z));
        yield return transform.OfPoint(new XYZ(max.X, min.Y, min.Z));
        yield return transform.OfPoint(new XYZ(max.X, max.Y, min.Z));
        yield return transform.OfPoint(new XYZ(min.X, max.Y, min.Z));
        yield return transform.OfPoint(new XYZ(min.X, min.Y, max.Z));
        yield return transform.OfPoint(new XYZ(max.X, min.Y, max.Z));
        yield return transform.OfPoint(new XYZ(max.X, max.Y, max.Z));
        yield return transform.OfPoint(new XYZ(min.X, max.Y, max.Z));
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

