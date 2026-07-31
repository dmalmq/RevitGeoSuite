using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitGeoSuite.CityGmlExport;

public sealed class CityGmlGeometryExtractor
{
    private const double FeetToMeters = 0.3048d;
    private readonly SemanticMapper semanticMapper;
    private readonly AttributeMapper attributeMapper;
    private readonly CodelistMapper codelistMapper;

    public CityGmlGeometryExtractor(
        SemanticMapper? semanticMapper = null,
        AttributeMapper? attributeMapper = null,
        CodelistMapper? codelistMapper = null)
    {
        this.semanticMapper = semanticMapper ?? new SemanticMapper();
        this.attributeMapper = attributeMapper ?? new AttributeMapper();
        this.codelistMapper = codelistMapper ?? new CodelistMapper();
    }

    public CityGmlExtractionResult Extract(
        Document document,
        CityGmlExportReferenceContext referenceContext,
        CityGmlExportScopeSelection scope,
        IReadOnlyDictionary<string, string>? categoryOverrides,
        IReadOnlyDictionary<string, string>? codelistOverrides)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (referenceContext is null)
        {
            throw new ArgumentNullException(nameof(referenceContext));
        }

        if (scope is null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        if (!scope.HasSelectedView)
        {
            throw new InvalidOperationException("Select a 3D view before extracting CityGML geometry.");
        }

        View3D selectedView = document.GetElement(scope.SelectedView!.ViewId) as View3D
            ?? throw new InvalidOperationException("The selected CityGML export view could not be resolved as a 3D view.");

        GeometryExtractionFrame frame = GeometryExtractionFrame.Create(document.ActiveProjectLocation, referenceContext);
        Options hostOptions = new Options
        {
            IncludeNonVisibleObjects = false,
            ComputeReferences = false,
            View = selectedView
        };
        Options linkedOptions = new Options
        {
            DetailLevel = ViewDetailLevel.Fine,
            IncludeNonVisibleObjects = false,
            ComputeReferences = false
        };

        List<CityGmlFeature> features = new List<CityGmlFeature>();
        List<string> warnings = new List<string>();
        AppendHostFeatures(document, selectedView, hostOptions, frame, categoryOverrides, codelistOverrides, features, warnings);

        foreach (CityGmlExportLinkOption linkOption in scope.SelectedLinkedModels)
        {
            AppendLinkedFeatures(document, selectedView, linkOption, linkedOptions, frame, categoryOverrides, codelistOverrides, features, warnings);
        }

        return new CityGmlExtractionResult
        {
            Features = features,
            Warnings = warnings
        };
    }

    private void AppendHostFeatures(
        Document document,
        View3D selectedView,
        Options options,
        GeometryExtractionFrame frame,
        IReadOnlyDictionary<string, string>? categoryOverrides,
        IReadOnlyDictionary<string, string>? codelistOverrides,
        List<CityGmlFeature> features,
        List<string> warnings)
    {
        FilteredElementCollector collector = new FilteredElementCollector(document, selectedView.Id).WhereElementIsNotElementType();
        foreach (Element element in collector)
        {
            try
            {
                AppendElementFeature(element, options, frame, Transform.Identity, categoryOverrides, codelistOverrides, features, $"revit-{element.Id.Value}", null);
            }
            catch (Exception ex)
            {
                warnings.Add($"Skipped host element {FormatElementLabel(element)}: {ex.Message}");
            }
        }
    }

    private void AppendLinkedFeatures(
        Document hostDocument,
        View3D selectedView,
        CityGmlExportLinkOption linkOption,
        Options options,
        GeometryExtractionFrame frame,
        IReadOnlyDictionary<string, string>? categoryOverrides,
        IReadOnlyDictionary<string, string>? codelistOverrides,
        List<CityGmlFeature> features,
        List<string> warnings)
    {
        try
        {
            RevitLinkInstance? linkInstance = hostDocument.GetElement(linkOption.LinkInstanceId) as RevitLinkInstance;
            if (linkInstance is null)
            {
                warnings.Add($"Skipped linked model '{linkOption.Title}' because the link instance could not be resolved.");
                return;
            }

            Document? linkDocument = linkInstance.GetLinkDocument();
            if (linkDocument is null)
            {
                warnings.Add($"Skipped linked model '{linkOption.Title}' because the linked document is not loaded.");
                return;
            }

            Transform linkTransform = linkInstance.GetTotalTransform();
            FilteredElementCollector collector = new FilteredElementCollector(hostDocument, selectedView.Id, linkInstance.Id).WhereElementIsNotElementType();
            foreach (Element visibleElement in collector)
            {
                Element? element = visibleElement.Document.Equals(linkDocument)
                    ? visibleElement
                    : linkDocument.GetElement(visibleElement.Id);
                if (element is null)
                {
                    continue;
                }

                try
                {
                    AppendElementFeature(
                        element,
                        options,
                        frame,
                        linkTransform,
                        categoryOverrides,
                        codelistOverrides,
                        features,
                        $"revit-link-{linkInstance.Id.Value}-{element.Id.Value}",
                        linkOption.Title);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Skipped linked element {FormatElementLabel(element)} from '{linkOption.Title}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Skipped linked model '{linkOption.Title}': {ex.Message}");
        }
    }

    private void AppendElementFeature(
        Element element,
        Options options,
        GeometryExtractionFrame frame,
        Transform transform,
        IReadOnlyDictionary<string, string>? categoryOverrides,
        IReadOnlyDictionary<string, string>? codelistOverrides,
        List<CityGmlFeature> features,
        string featureId,
        string? sourcePrefix)
    {
        if (!IsExportable(element))
        {
            return;
        }

        GeometryElement geometry = element.get_Geometry(options);
        if (geometry is null)
        {
            return;
        }

        List<CityGmlSurface> surfaces = new List<CityGmlSurface>();
        AppendGeometry(geometry, transform, frame, surfaces);
        if (surfaces.Count == 0)
        {
            return;
        }

        CityGmlSemanticType semanticType = semanticMapper.Map(element, categoryOverrides);
        string name = BuildElementName(element);
        if (!string.IsNullOrWhiteSpace(sourcePrefix))
        {
            name = $"{sourcePrefix}: {name}";
        }

        features.Add(new CityGmlFeature
        {
            Id = featureId,
            Name = name,
            CategoryName = element.Category?.Name ?? "Uncategorized",
            SemanticType = semanticType,
            Attributes = attributeMapper.Map(element).ToArray(),
            CodeAssignment = codelistMapper.Resolve(semanticType, element.Category?.Name ?? string.Empty, codelistOverrides),
            Surfaces = surfaces
        });
    }

    private static bool IsExportable(Element element)
    {
        if (element is RevitLinkInstance || element.ViewSpecific || element.Category is null)
        {
            return false;
        }

        return element.Category.CategoryType == CategoryType.Model;
    }

    private static string BuildElementName(Element element)
    {
        string categoryName = element.Category?.Name ?? "Element";
        return string.IsNullOrWhiteSpace(element.Name)
            ? $"{categoryName} #{element.Id.Value}"
            : $"{categoryName}: {element.Name}";
    }

    private static string FormatElementLabel(Element element)
    {
        string categoryName = element.Category?.Name ?? "Element";
        string elementName = string.IsNullOrWhiteSpace(element.Name) ? "Unnamed" : element.Name;
        return $"{categoryName} #{element.Id.Value} ({elementName})";
    }

    private static void AppendGeometry(GeometryElement geometryElement, Transform transform, GeometryExtractionFrame frame, List<CityGmlSurface> surfaces)
    {
        foreach (GeometryObject geometryObject in geometryElement)
        {
            if (geometryObject is Solid solid)
            {
                AppendSolid(solid, transform, frame, surfaces);
                continue;
            }

            if (geometryObject is Mesh mesh)
            {
                AppendMesh(mesh, transform, frame, surfaces);
                continue;
            }

            if (geometryObject is GeometryInstance instance)
            {
                // GetInstanceGeometry() is already returned in instance space, so only the outer transform
                // chain (for example a containing Revit link transform) should still be applied here.
                AppendGeometry(instance.GetInstanceGeometry(), transform, frame, surfaces);
            }
        }
    }

    private static void AppendSolid(Solid solid, Transform transform, GeometryExtractionFrame frame, List<CityGmlSurface> surfaces)
    {
        if (solid.Faces.IsEmpty || solid.Volume <= 1e-9)
        {
            return;
        }

        foreach (Face face in solid.Faces)
        {
            AppendMesh(face.Triangulate(), transform, frame, surfaces);
        }
    }

    private static void AppendMesh(Mesh mesh, Transform transform, GeometryExtractionFrame frame, List<CityGmlSurface> surfaces)
    {
        if (mesh is null || mesh.NumTriangles == 0)
        {
            return;
        }

        for (int triangleIndex = 0; triangleIndex < mesh.NumTriangles; triangleIndex++)
        {
            MeshTriangle triangle = mesh.get_Triangle(triangleIndex);
            XYZ a = transform.OfPoint(triangle.get_Vertex(0));
            XYZ b = transform.OfPoint(triangle.get_Vertex(1));
            XYZ c = transform.OfPoint(triangle.get_Vertex(2));
            CityGmlCoordinate aProjected = frame.ToProjectedMeters(a);
            CityGmlCoordinate bProjected = frame.ToProjectedMeters(b);
            CityGmlCoordinate cProjected = frame.ToProjectedMeters(c);
            surfaces.Add(new CityGmlSurface
            {
                ExteriorRing = new[]
                {
                    aProjected,
                    bProjected,
                    cProjected,
                    aProjected
                }
            });
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

    private sealed class GeometryExtractionFrame
    {
        private readonly XYZ anchorPoint;
        private readonly SharedVector xAxis;
        private readonly SharedVector yAxis;
        private readonly SharedVector zAxis;
        private readonly CityGmlExportReferenceContext referenceContext;

        private GeometryExtractionFrame(
            XYZ anchorPoint,
            SharedVector xAxis,
            SharedVector yAxis,
            SharedVector zAxis,
            CityGmlExportReferenceContext referenceContext)
        {
            this.anchorPoint = anchorPoint;
            this.xAxis = xAxis;
            this.yAxis = yAxis;
            this.zAxis = zAxis;
            this.referenceContext = referenceContext;
        }

        public static GeometryExtractionFrame Create(ProjectLocation projectLocation, CityGmlExportReferenceContext referenceContext)
        {
            XYZ anchorPoint = new XYZ(referenceContext.AnchorXFeet, referenceContext.AnchorYFeet, referenceContext.AnchorZFeet);
            ProjectPosition anchor = projectLocation.GetProjectPosition(anchorPoint);
            ProjectPosition plusX = projectLocation.GetProjectPosition(new XYZ(anchorPoint.X + 1d, anchorPoint.Y, anchorPoint.Z));
            ProjectPosition plusY = projectLocation.GetProjectPosition(new XYZ(anchorPoint.X, anchorPoint.Y + 1d, anchorPoint.Z));
            ProjectPosition plusZ = projectLocation.GetProjectPosition(new XYZ(anchorPoint.X, anchorPoint.Y, anchorPoint.Z + 1d));

            return new GeometryExtractionFrame(
                anchorPoint,
                new SharedVector(plusX.EastWest - anchor.EastWest, plusX.NorthSouth - anchor.NorthSouth, plusX.Elevation - anchor.Elevation),
                new SharedVector(plusY.EastWest - anchor.EastWest, plusY.NorthSouth - anchor.NorthSouth, plusY.Elevation - anchor.Elevation),
                new SharedVector(plusZ.EastWest - anchor.EastWest, plusZ.NorthSouth - anchor.NorthSouth, plusZ.Elevation - anchor.Elevation),
                referenceContext);
        }

        public CityGmlCoordinate ToProjectedMeters(XYZ point)
        {
            double deltaX = point.X - anchorPoint.X;
            double deltaY = point.Y - anchorPoint.Y;
            double deltaZ = point.Z - anchorPoint.Z;

            double eastFeet = (deltaX * xAxis.EastFeet) + (deltaY * yAxis.EastFeet) + (deltaZ * zAxis.EastFeet);
            double northFeet = (deltaX * xAxis.NorthFeet) + (deltaY * yAxis.NorthFeet) + (deltaZ * zAxis.NorthFeet);
            double upFeet = (deltaX * xAxis.UpFeet) + (deltaY * yAxis.UpFeet) + (deltaZ * zAxis.UpFeet);

            return new CityGmlCoordinate(
                referenceContext.AnchorProjectedCoordinate.Easting + (eastFeet * FeetToMeters),
                referenceContext.AnchorProjectedCoordinate.Northing + (northFeet * FeetToMeters),
                referenceContext.AnchorElevationMeters + (upFeet * FeetToMeters));
        }
    }
}

