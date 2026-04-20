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

        Dictionary<ElementId, Tiles3DMaterialColor> materialColorCache = new Dictionary<ElementId, Tiles3DMaterialColor>();
        List<Tiles3DMeshPrimitive> meshes = new List<Tiles3DMeshPrimitive>();
        AppendHostMeshes(document, selectedView, hostOptions, frame, scope.ScopeMode, meshes, materialColorCache);

        foreach (Tiles3DExportLinkOption linkOption in scope.SelectedLinkedModels)
        {
            AppendLinkedMeshes(document, selectedView, linkOption, linkedOptions, frame, scope.ScopeMode, meshes, materialColorCache);
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
        List<Tiles3DMeshPrimitive> meshes,
        Dictionary<ElementId, Tiles3DMaterialColor> materialColorCache)
    {
        FilteredElementCollector collector = scopeMode == Tiles3DExportScopeMode.Selected3DView && selectedView is not null
            ? new FilteredElementCollector(document, selectedView.Id).WhereElementIsNotElementType()
            : new FilteredElementCollector(document).WhereElementIsNotElementType();

        foreach (Element element in collector)
        {
            AppendElementMesh(document, element, options, frame, Transform.Identity, meshes, null, materialColorCache);
        }
    }

    private static void AppendLinkedMeshes(
        Document hostDocument,
        View3D? selectedView,
        Tiles3DExportLinkOption linkOption,
        Options options,
        GeometryExtractionFrame frame,
        Tiles3DExportScopeMode scopeMode,
        List<Tiles3DMeshPrimitive> meshes,
        Dictionary<ElementId, Tiles3DMaterialColor> materialColorCache)
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

                AppendElementMesh(linkDocument, element, options, frame, linkTransform, meshes, linkOption.Title, materialColorCache);
            }

            return;
        }

        FilteredElementCollector collector = new FilteredElementCollector(linkDocument).WhereElementIsNotElementType();
        foreach (Element element in collector)
        {
            AppendElementMesh(linkDocument, element, options, frame, linkTransform, meshes, linkOption.Title, materialColorCache);
        }
    }

    private static void AppendElementMesh(
        Document document,
        Element element,
        Options options,
        GeometryExtractionFrame frame,
        Transform transform,
        List<Tiles3DMeshPrimitive> meshes,
        string? sourcePrefix,
        Dictionary<ElementId, Tiles3DMaterialColor> materialColorCache)
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

        Dictionary<Tiles3DMaterialColor, List<Tiles3DTriangle>> trianglesByColor = new Dictionary<Tiles3DMaterialColor, List<Tiles3DTriangle>>();
        AppendGeometry(document, geometry, transform, frame, trianglesByColor, materialColorCache);

        string name = BuildElementName(element);
        if (!string.IsNullOrWhiteSpace(sourcePrefix))
        {
            name = $"{sourcePrefix}: {name}";
        }

        string categoryName = element.Category?.Name ?? "Uncategorized";
        foreach (KeyValuePair<Tiles3DMaterialColor, List<Tiles3DTriangle>> entry in trianglesByColor)
        {
            if (entry.Value.Count == 0)
            {
                continue;
            }

            meshes.Add(new Tiles3DMeshPrimitive
            {
                Name = name,
                CategoryName = categoryName,
                Color = entry.Key,
                Triangles = entry.Value
            });
        }
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

    private static void AppendGeometry(
        Document document,
        GeometryElement geometryElement,
        Transform transform,
        GeometryExtractionFrame frame,
        Dictionary<Tiles3DMaterialColor, List<Tiles3DTriangle>> trianglesByColor,
        Dictionary<ElementId, Tiles3DMaterialColor> materialColorCache)
    {
        foreach (GeometryObject geometryObject in geometryElement)
        {
            if (geometryObject is Solid solid)
            {
                AppendSolid(document, solid, transform, frame, trianglesByColor, materialColorCache);
                continue;
            }

            if (geometryObject is Mesh mesh)
            {
                AddTrianglesToColor(Tiles3DMaterialColor.Default, trianglesByColor, mesh, transform, frame);
                continue;
            }

            if (geometryObject is GeometryInstance instance)
            {
                AppendGeometry(document, instance.GetInstanceGeometry(), transform, frame, trianglesByColor, materialColorCache);
            }
        }
    }

    private static void AppendSolid(
        Document document,
        Solid solid,
        Transform transform,
        GeometryExtractionFrame frame,
        Dictionary<Tiles3DMaterialColor, List<Tiles3DTriangle>> trianglesByColor,
        Dictionary<ElementId, Tiles3DMaterialColor> materialColorCache)
    {
        if (solid.Faces.IsEmpty || solid.Volume <= 1e-9)
        {
            return;
        }

        foreach (Face face in solid.Faces)
        {
            Tiles3DMaterialColor color = ResolveFaceMaterialColor(document, face, materialColorCache);
            Mesh mesh = face.Triangulate();
            AddTrianglesToColor(color, trianglesByColor, mesh, transform, frame);
        }
    }

    private static Tiles3DMaterialColor ResolveFaceMaterialColor(
        Document document,
        Face face,
        Dictionary<ElementId, Tiles3DMaterialColor> cache)
    {
        ElementId materialId = face.MaterialElementId;
        if (materialId == ElementId.InvalidElementId)
        {
            return Tiles3DMaterialColor.Default;
        }

        if (cache.TryGetValue(materialId, out Tiles3DMaterialColor cached))
        {
            return cached;
        }

        Tiles3DMaterialColor color = Tiles3DMaterialColor.Default;
        if (document.GetElement(materialId) is Material material && material.Color is Color revitColor)
        {
            color = new Tiles3DMaterialColor(revitColor.Red, revitColor.Green, revitColor.Blue);
        }

        cache[materialId] = color;
        return color;
    }

    private static void AddTrianglesToColor(
        Tiles3DMaterialColor color,
        Dictionary<Tiles3DMaterialColor, List<Tiles3DTriangle>> trianglesByColor,
        Mesh mesh,
        Transform transform,
        GeometryExtractionFrame frame)
    {
        if (mesh is null || mesh.NumTriangles == 0)
        {
            return;
        }

        if (!trianglesByColor.TryGetValue(color, out List<Tiles3DTriangle> triangles))
        {
            triangles = new List<Tiles3DTriangle>();
            trianglesByColor[color] = triangles;
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