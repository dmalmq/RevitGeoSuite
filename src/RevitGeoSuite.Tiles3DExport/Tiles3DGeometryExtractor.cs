using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DGeometryExtractor
{
    private const double FeetToMeters = 0.3048d;

    public IReadOnlyCollection<Tiles3DMeshPrimitive> Extract(
        Document document,
        Tiles3DExportReferenceContext referenceContext,
        Tiles3DExportScopeSelection scope)
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

        View3D? selectedView = ResolveSelectedView(document, scope);
        GeometryExtractionFrame frame = GeometryExtractionFrame.Create(document.ActiveProjectLocation, referenceContext);
        Options hostOptions = new Options
        {
            IncludeNonVisibleObjects = false,
            ComputeReferences = false
        };
        if (selectedView is not null)
        {
            hostOptions.View = selectedView;
        }
        else
        {
            hostOptions.DetailLevel = ViewDetailLevel.Fine;
        }
        Options linkedOptions = new Options
        {
            DetailLevel = ViewDetailLevel.Fine,
            IncludeNonVisibleObjects = false,
            ComputeReferences = false
        };

        List<Tiles3DMeshPrimitive> meshes = new List<Tiles3DMeshPrimitive>();
        AppendHostMeshes(document, selectedView, hostOptions, frame, scope.ScopeMode, meshes);

        foreach (Tiles3DExportLinkOption linkOption in scope.SelectedLinkedModels)
        {
            AppendLinkedMeshes(document, selectedView, linkOption, linkedOptions, frame, scope.ScopeMode, meshes);
        }

        return meshes;
    }

    private static View3D? ResolveSelectedView(Document document, Tiles3DExportScopeSelection scope)
    {
        if (scope.ScopeMode != Tiles3DExportScopeMode.Selected3DView)
        {
            return null;
        }

        if (!scope.HasSelectedView)
        {
            throw new InvalidOperationException("Select a non-template 3D view before extracting 3D Tiles geometry from a selected view.");
        }

        return document.GetElement(scope.SelectedView!.ViewId) as View3D
            ?? throw new InvalidOperationException("The selected 3D Tiles export view could not be resolved as a 3D view.");
    }

    private static void AppendHostMeshes(
        Document document,
        View3D? selectedView,
        Options options,
        GeometryExtractionFrame frame,
        Tiles3DExportScopeMode scopeMode,
        List<Tiles3DMeshPrimitive> meshes)
    {
        FilteredElementCollector collector = scopeMode == Tiles3DExportScopeMode.Selected3DView && selectedView is not null
            ? new FilteredElementCollector(document, selectedView.Id).WhereElementIsNotElementType()
            : new FilteredElementCollector(document).WhereElementIsNotElementType();

        foreach (Element element in collector)
        {
            AppendElementMesh(element, options, frame, Transform.Identity, meshes, null);
        }
    }

    private static void AppendLinkedMeshes(
        Document hostDocument,
        View3D? selectedView,
        Tiles3DExportLinkOption linkOption,
        Options options,
        GeometryExtractionFrame frame,
        Tiles3DExportScopeMode scopeMode,
        List<Tiles3DMeshPrimitive> meshes)
    {
        RevitLinkInstance? linkInstance = hostDocument.GetElement(linkOption.LinkInstanceId) as RevitLinkInstance;
        if (linkInstance is null)
        {
            return;
        }

        Document? linkDocument = linkInstance.GetLinkDocument();
        if (linkDocument is null)
        {
            return;
        }

        Transform linkTransform = linkInstance.GetTotalTransform();
        if (scopeMode == Tiles3DExportScopeMode.Selected3DView && selectedView is not null)
        {
            FilteredElementCollector visibleCollector = new FilteredElementCollector(hostDocument, selectedView.Id, linkInstance.Id).WhereElementIsNotElementType();
            foreach (Element visibleElement in visibleCollector)
            {
                Element? element = visibleElement.Document.Equals(linkDocument)
                    ? visibleElement
                    : linkDocument.GetElement(visibleElement.Id);
                if (element is null)
                {
                    continue;
                }

                AppendElementMesh(element, options, frame, linkTransform, meshes, linkOption.Title);
            }

            return;
        }

        FilteredElementCollector collector = new FilteredElementCollector(linkDocument).WhereElementIsNotElementType();
        foreach (Element element in collector)
        {
            AppendElementMesh(element, options, frame, linkTransform, meshes, linkOption.Title);
        }
    }

    private static void AppendElementMesh(
        Element element,
        Options options,
        GeometryExtractionFrame frame,
        Transform transform,
        List<Tiles3DMeshPrimitive> meshes,
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

        List<Tiles3DTriangle> triangles = new List<Tiles3DTriangle>();
        AppendGeometry(geometry, transform, frame, triangles);
        if (triangles.Count == 0)
        {
            return;
        }

        string name = BuildElementName(element);
        if (!string.IsNullOrWhiteSpace(sourcePrefix))
        {
            name = $"{sourcePrefix}: {name}";
        }

        meshes.Add(new Tiles3DMeshPrimitive
        {
            Name = name,
            CategoryName = element.Category?.Name ?? "Uncategorized",
            Triangles = triangles
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

    private static void AppendGeometry(GeometryElement geometryElement, Transform transform, GeometryExtractionFrame frame, List<Tiles3DTriangle> triangles)
    {
        foreach (GeometryObject geometryObject in geometryElement)
        {
            if (geometryObject is Solid solid)
            {
                AppendSolid(solid, transform, frame, triangles);
                continue;
            }

            if (geometryObject is Mesh mesh)
            {
                AppendMesh(mesh, transform, frame, triangles);
                continue;
            }

            if (geometryObject is GeometryInstance instance)
            {
                AppendGeometry(instance.GetInstanceGeometry(), transform, frame, triangles);
            }
        }
    }

    private static void AppendSolid(Solid solid, Transform transform, GeometryExtractionFrame frame, List<Tiles3DTriangle> triangles)
    {
        if (solid.Faces.IsEmpty || solid.Volume <= 1e-9)
        {
            return;
        }

        foreach (Face face in solid.Faces)
        {
            AppendMesh(face.Triangulate(), transform, frame, triangles);
        }
    }

    private static void AppendMesh(Mesh mesh, Transform transform, GeometryExtractionFrame frame, List<Tiles3DTriangle> triangles)
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
            triangles.Add(new Tiles3DTriangle(frame.ToLocalMeters(a), frame.ToLocalMeters(b), frame.ToLocalMeters(c)));
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

        private GeometryExtractionFrame(XYZ anchorPoint, SharedVector xAxis, SharedVector yAxis, SharedVector zAxis)
        {
            this.anchorPoint = anchorPoint;
            this.xAxis = xAxis;
            this.yAxis = yAxis;
            this.zAxis = zAxis;
        }

        public static GeometryExtractionFrame Create(ProjectLocation projectLocation, Tiles3DExportReferenceContext referenceContext)
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
                new SharedVector(plusZ.EastWest - anchor.EastWest, plusZ.NorthSouth - anchor.NorthSouth, plusZ.Elevation - anchor.Elevation));
        }

        public Tiles3DPoint ToLocalMeters(XYZ point)
        {
            double deltaX = point.X - anchorPoint.X;
            double deltaY = point.Y - anchorPoint.Y;
            double deltaZ = point.Z - anchorPoint.Z;

            double eastFeet = (deltaX * xAxis.EastFeet) + (deltaY * yAxis.EastFeet) + (deltaZ * zAxis.EastFeet);
            double northFeet = (deltaX * xAxis.NorthFeet) + (deltaY * yAxis.NorthFeet) + (deltaZ * zAxis.NorthFeet);
            double upFeet = (deltaX * xAxis.UpFeet) + (deltaY * yAxis.UpFeet) + (deltaZ * zAxis.UpFeet);
            return new Tiles3DPoint(eastFeet * FeetToMeters, northFeet * FeetToMeters, upFeet * FeetToMeters);
        }
    }
}