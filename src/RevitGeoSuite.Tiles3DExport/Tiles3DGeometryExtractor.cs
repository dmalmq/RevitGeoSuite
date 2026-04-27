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
            AppendElementMesh(element, options, frame, Transform.Identity, meshes, document.Title, string.Empty);
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

                AppendElementMesh(element, options, frame, linkTransform, meshes, linkDocument.Title, linkOption.Title);
            }

            return;
        }

        FilteredElementCollector collector = new FilteredElementCollector(linkDocument).WhereElementIsNotElementType();
        foreach (Element element in collector)
        {
            AppendElementMesh(element, options, frame, linkTransform, meshes, linkDocument.Title, linkOption.Title);
        }
    }

    private static void AppendElementMesh(
        Element element,
        Options options,
        GeometryExtractionFrame frame,
        Transform transform,
        List<Tiles3DMeshPrimitive> meshes,
        string sourceDocument,
        string sourceLinkName)
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

        MaterialColorResolver materialColorResolver = new MaterialColorResolver(element);
        List<Tiles3DTriangle> triangles = new List<Tiles3DTriangle>();
        AppendGeometry(geometry, transform, frame, materialColorResolver, triangles);
        if (triangles.Count == 0)
        {
            return;
        }

        Tiles3DObjectMetadata metadata = BuildElementMetadata(element, sourceDocument, sourceLinkName, triangles);
        if (!string.IsNullOrWhiteSpace(sourceLinkName))
        {
            metadata.Name = $"{sourceLinkName}: {metadata.Name}";
        }

        Tiles3DMeshPrimitive primitive = new Tiles3DMeshPrimitive
        {
            Metadata = metadata,
            Triangles = triangles
        };
        meshes.Add(primitive);
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

    private static Tiles3DObjectMetadata BuildElementMetadata(
        Element element,
        string sourceDocument,
        string sourceLinkName,
        IReadOnlyCollection<Tiles3DTriangle> triangles)
    {
        string typeName = string.Empty;
        string familyName = string.Empty;
        ElementId typeId = element.GetTypeId();
        if (typeId != ElementId.InvalidElementId && element.Document.GetElement(typeId) is Element typeElement)
        {
            typeName = typeElement.Name ?? string.Empty;
            if (typeElement is ElementType elementType)
            {
                familyName = elementType.FamilyName ?? string.Empty;
            }
        }

        if (element is FamilyInstance familyInstance)
        {
            familyName = familyInstance.Symbol?.FamilyName ?? familyName;
            typeName = familyInstance.Symbol?.Name ?? typeName;
        }

        Tiles3DObjectMetadata metadata = new Tiles3DObjectMetadata
        {
            RevitElementId = element.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            RevitUniqueId = element.UniqueId ?? string.Empty,
            Name = BuildElementName(element),
            Category = element.Category?.Name ?? "Uncategorized",
            FamilyName = familyName,
            TypeName = typeName,
            SourceDocument = sourceDocument ?? string.Empty,
            SourceLinkName = sourceLinkName ?? string.Empty
        };

        ResolveElementLevel(element, metadata);
        ResolveVerticalExtents(triangles, metadata);
        return metadata;
    }

    private static void ResolveElementLevel(Element element, Tiles3DObjectMetadata metadata)
    {
        ElementId levelId = element.LevelId;
        if (levelId == ElementId.InvalidElementId)
        {
            Parameter? levelParam = element.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
            if (levelParam != null && levelParam.HasValue)
            {
                ElementId paramId = levelParam.AsElementId();
                if (paramId != null && paramId != ElementId.InvalidElementId)
                {
                    levelId = paramId;
                }
            }
        }

        if (levelId == ElementId.InvalidElementId)
        {
            return;
        }

        if (element.Document.GetElement(levelId) is Level level)
        {
            metadata.LevelName = level.Name;
            metadata.LevelKey = Tiles3DLevelMetadata.BuildLevelKey(level.Name);
            metadata.LevelElevationMeters = level.Elevation * FeetToMeters;
        }
    }

    private static void ResolveVerticalExtents(IReadOnlyCollection<Tiles3DTriangle> triangles, Tiles3DObjectMetadata metadata)
    {
        if (triangles.Count == 0)
        {
            return;
        }

        double minZ = double.MaxValue;
        double maxZ = double.MinValue;
        foreach (Tiles3DTriangle triangle in triangles)
        {
            minZ = Math.Min(minZ, Math.Min(triangle.A.Z, Math.Min(triangle.B.Z, triangle.C.Z)));
            maxZ = Math.Max(maxZ, Math.Max(triangle.A.Z, Math.Max(triangle.B.Z, triangle.C.Z)));
        }

        metadata.MinZMeters = minZ;
        metadata.MaxZMeters = maxZ;
        metadata.HeightMeters = Math.Max(0d, maxZ - minZ);
    }

    private static void AppendGeometry(
        GeometryElement geometryElement,
        Transform transform,
        GeometryExtractionFrame frame,
        MaterialColorResolver materialColorResolver,
        List<Tiles3DTriangle> triangles)
    {
        foreach (GeometryObject geometryObject in geometryElement)
        {
            if (geometryObject is Solid solid)
            {
                AppendSolid(solid, transform, frame, materialColorResolver, triangles);
                continue;
            }

            if (geometryObject is Mesh mesh)
            {
                AppendMesh(mesh, transform, frame, materialColorResolver.Resolve(mesh.MaterialElementId), triangles);
                continue;
            }

            if (geometryObject is GeometryInstance instance)
            {
                AppendGeometry(instance.GetInstanceGeometry(), transform, frame, materialColorResolver, triangles);
            }
        }
    }

    private static void AppendSolid(
        Solid solid,
        Transform transform,
        GeometryExtractionFrame frame,
        MaterialColorResolver materialColorResolver,
        List<Tiles3DTriangle> triangles)
    {
        if (solid.Faces.IsEmpty || solid.Volume <= 1e-9)
        {
            return;
        }

        foreach (Face face in solid.Faces)
        {
            AppendMesh(face.Triangulate(), transform, frame, materialColorResolver.Resolve(face.MaterialElementId), triangles);
        }
    }

    private static void AppendMesh(
        Mesh mesh,
        Transform transform,
        GeometryExtractionFrame frame,
        Tiles3DMaterialColor materialColor,
        List<Tiles3DTriangle> triangles)
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
            triangles.Add(new Tiles3DTriangle(
                frame.ToLocalMeters(a),
                frame.ToLocalMeters(b),
                frame.ToLocalMeters(c),
                materialColor));
        }
    }

    private sealed class MaterialColorResolver
    {
        private readonly Document document;
        private readonly Dictionary<long, Tiles3DMaterialColor> materialColors = new Dictionary<long, Tiles3DMaterialColor>();

        public MaterialColorResolver(Element element)
        {
            document = element.Document;
            FallbackColor = ResolveFallbackColor(element);
        }

        public Tiles3DMaterialColor FallbackColor { get; }

        public Tiles3DMaterialColor Resolve(ElementId materialId)
        {
            if (materialId == ElementId.InvalidElementId)
            {
                return FallbackColor;
            }

            long key = materialId.Value;
            if (materialColors.TryGetValue(key, out Tiles3DMaterialColor cached))
            {
                return cached;
            }

            Tiles3DMaterialColor color = document.GetElement(materialId) is Material material
                && TryGetMaterialColor(material, out Tiles3DMaterialColor materialColor)
                    ? materialColor
                    : FallbackColor;
            materialColors[key] = color;
            return color;
        }

        private static Tiles3DMaterialColor ResolveFallbackColor(Element element)
        {
            if (TryGetMaterialColor(element.Category?.Material, out Tiles3DMaterialColor categoryColor))
            {
                return categoryColor;
            }

            if (TryGetFirstElementMaterialColor(element, false, out Tiles3DMaterialColor elementColor))
            {
                return elementColor;
            }

            if (TryGetFirstElementMaterialColor(element, true, out Tiles3DMaterialColor paintColor))
            {
                return paintColor;
            }

            return Tiles3DMaterialColor.Default;
        }

        private static bool TryGetFirstElementMaterialColor(Element element, bool returnPaintMaterials, out Tiles3DMaterialColor color)
        {
            try
            {
                foreach (ElementId materialId in element.GetMaterialIds(returnPaintMaterials))
                {
                    if (materialId == ElementId.InvalidElementId)
                    {
                        continue;
                    }

                    if (element.Document.GetElement(materialId) is Material material
                        && TryGetMaterialColor(material, out color))
                    {
                        return true;
                    }
                }
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
            }

            color = default;
            return false;
        }

        private static bool TryGetMaterialColor(Material? material, out Tiles3DMaterialColor color)
        {
            if (material is not null && material.Color is Color revitColor && revitColor.IsValid)
            {
                color = new Tiles3DMaterialColor(
                    revitColor.Red,
                    revitColor.Green,
                    revitColor.Blue,
                    TransparencyToAlpha(material.Transparency));
                return true;
            }

            color = default;
            return false;
        }

        private static byte TransparencyToAlpha(int transparency)
        {
            int clamped = transparency;
            if (clamped < 0)
            {
                clamped = 0;
            }
            else if (clamped > 100)
            {
                clamped = 100;
            }

            return (byte)Math.Round(255d * (100d - clamped) / 100d);
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
