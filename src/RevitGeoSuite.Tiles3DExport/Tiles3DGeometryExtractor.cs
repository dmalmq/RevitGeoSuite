using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DGeometryExtractor
{
    private const double FeetToMeters = 0.3048d;
    internal const string FootprintSubcategoryName = "K_Footprint";

    internal static readonly Guid GisIdParameterGuid = new Guid("64e7d743-c3ae-4831-a208-33c2762ac50a");
    internal static readonly Guid GisKeyParameterGuid = new Guid("0816cab3-3717-4632-854f-c6c7c11e0881");
    internal static readonly Guid GisKindParameterGuid = new Guid("ff63e172-c407-4265-b075-4e1d5e9822b8");
    internal static readonly Guid GisCategoryParameterGuid = new Guid("d4ed1842-369c-4064-97fd-93fab33f0a87");
    internal static readonly Guid GisInstanceCategoryParameterGuid = new Guid("7fb0aa6b-c570-437d-a86a-f25f02537165");
    internal static readonly Guid GisLevelIdParameterGuid = new Guid("84f1637a-1421-4c6c-9670-c000206ffea0");

    public IReadOnlyCollection<Tiles3DMeshPrimitive> Extract(
        Document document,
        Tiles3DExportReferenceContext referenceContext,
        Tiles3DExportScopeSelection scope,
        ICollection<string>? warnings = null,
        Tiles3DMaterialPalette materialPalette = Tiles3DMaterialPalette.Source)
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

        Tiles3DShowcaseGuard.ThrowIfAuthoringExportBlocked(document);

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
            DetailLevel = ResolveLinkedDetailLevel(selectedView?.DetailLevel),
            IncludeNonVisibleObjects = false,
            ComputeReferences = false
        };

        List<Tiles3DMeshPrimitive> meshes = new List<Tiles3DMeshPrimitive>();
        AppendHostMeshes(document, selectedView, hostOptions, frame, scope.ScopeMode, materialPalette, meshes, warnings);

        foreach (Tiles3DExportLinkOption linkOption in scope.SelectedLinkedModels)
        {
            AppendLinkedMeshes(document, selectedView, linkOption, linkedOptions, frame, scope.ScopeMode, materialPalette, meshes, warnings);
        }

        return meshes;
    }

    internal static ViewDetailLevel ResolveLinkedDetailLevel(ViewDetailLevel? selectedViewDetailLevel)
    {
        if (selectedViewDetailLevel.HasValue && selectedViewDetailLevel.Value != ViewDetailLevel.Undefined)
        {
            return selectedViewDetailLevel.Value;
        }

        return ViewDetailLevel.Fine;
    }

    internal static bool IsExcludedSubcategoryName(string? subcategoryName)
    {
        if (string.IsNullOrWhiteSpace(subcategoryName))
        {
            return false;
        }

        return string.Equals(subcategoryName!.Trim(), FootprintSubcategoryName, StringComparison.OrdinalIgnoreCase)
            || Tiles3DShowcaseGuard.IsShowcaseFamilyOrTypeName(subcategoryName);
    }

    /// <summary>
    /// The host model's levels whose UniqueIds are listed, without any element geometry: name, key
    /// and elevation in the same frame <see cref="Extract"/> gives a mesh's level, so a levels file
    /// written from them reads like one written from meshes. Send to Kanizu writes it.
    /// </summary>
    public IReadOnlyList<Tiles3DLevelGroup> ExtractLevels(
        Document document,
        Tiles3DExportReferenceContext referenceContext,
        IReadOnlyCollection<string> levelUniqueIds)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (referenceContext is null)
        {
            throw new ArgumentNullException(nameof(referenceContext));
        }

        var wanted = new HashSet<string>(levelUniqueIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        GeometryExtractionFrame frame = GeometryExtractionFrame.Create(document.ActiveProjectLocation, referenceContext);
        // Two levels whose names make one key share a group, as Tiles3DLevelGrouper groups meshes:
        // levels.json then has one entry per key, carrying both UniqueIds, at the lower elevation.
        return new FilteredElementCollector(document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .Where(level => wanted.Contains(level.UniqueId ?? string.Empty))
            .Select(level => (Level: level, Key: Tiles3DLevelMetadata.BuildLevelKey(level.Name), Elevation: frame.ToLocalMeters(new XYZ(0, 0, level.ProjectElevation)).Z))
            .GroupBy(level => level.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var ordered = group.OrderBy(level => level.Elevation).ThenBy(level => level.Level.UniqueId, StringComparer.Ordinal).ToList();
                double elevation = ordered[0].Elevation;
                return new Tiles3DLevelGroup
                {
                    LevelKey = group.Key,
                    LevelName = ordered[0].Level.Name ?? string.Empty,
                    LevelElevationMeters = elevation,
                    LevelUniqueIds = ordered.Select(level => level.Level.UniqueId ?? string.Empty).ToList(),
                    MinZMeters = elevation,
                    MaxZMeters = elevation,
                };
            })
            .OrderBy(group => group.LevelElevationMeters)
            .ThenBy(group => group.LevelName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Every placed room in the host and in the selected linked models, with its finish-face
    /// footprint in plane-grid easting and northing. Rooms have no geometry in a 3D view, so the
    /// scope's view does not filter them; its linked models do.
    /// </summary>
    public IReadOnlyList<Tiles3DRoomFootprint> ExtractRooms(
        Document document,
        Tiles3DExportReferenceContext referenceContext,
        Tiles3DExportScopeSelection scope,
        ICollection<string>? warnings = null)
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

        Tiles3DShowcaseGuard.ThrowIfAuthoringExportBlocked(document);

        GeometryExtractionFrame frame = GeometryExtractionFrame.Create(document.ActiveProjectLocation, referenceContext);
        var rooms = new List<Tiles3DRoomFootprint>();
        AppendRooms(document, Transform.Identity, frame, referenceContext, document.Title, string.Empty, string.Empty, rooms, warnings);

        foreach (Tiles3DExportLinkOption linkOption in scope.SelectedLinkedModels)
        {
            if (linkOption.LinkInstanceId <= 0
                || document.GetElement(ToElementId(linkOption.LinkInstanceId)) is not RevitLinkInstance linkInstance
                || linkInstance.GetLinkDocument() is not Document linkDocument)
            {
                // The mesh pass already warned about a link it could not open.
                continue;
            }

            if (Tiles3DShowcaseGuard.IsShowcaseDocument(linkDocument))
            {
                continue;
            }

            AppendRooms(
                linkDocument,
                linkInstance.GetTotalTransform(),
                frame,
                referenceContext,
                linkDocument.Title,
                linkOption.Title,
                linkInstance.UniqueId ?? string.Empty,
                rooms,
                warnings);
        }

        return rooms;
    }

    private static void AppendRooms(
        Document document,
        Transform transform,
        GeometryExtractionFrame frame,
        Tiles3DExportReferenceContext referenceContext,
        string sourceDocument,
        string sourceLinkName,
        string sourceLinkInstanceUniqueId,
        List<Tiles3DRoomFootprint> rooms,
        ICollection<string>? warnings)
    {
        var options = new SpatialElementBoundaryOptions
        {
            SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish,
            StoreFreeBoundaryFaces = false,
        };
        double originEasting = referenceContext.AnchorProjectedCoordinate.Easting;
        double originNorthing = referenceContext.AnchorProjectedCoordinate.Northing;

        IEnumerable<Room> placed = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .OfType<Room>()
            .Where(room => room.Location is not null && room.Area > 0d && !Tiles3DShowcaseGuard.IsShowcaseElement(room));
        foreach (Room room in placed)
        {
            try
            {
                IList<IList<BoundarySegment>>? loops = room.GetBoundarySegments(options);
                if (loops is null || loops.Count == 0)
                {
                    continue;
                }

                var rings = new List<List<double[]>>();
                foreach (IList<BoundarySegment> loop in loops)
                {
                    var ring = new List<double[]>();
                    foreach (BoundarySegment segment in loop)
                    {
                        Curve? curve = segment.GetCurve();
                        IList<XYZ>? points = curve?.Tessellate();
                        if (points is null)
                        {
                            continue;
                        }

                        // Each curve starts where the last one ended.
                        for (int i = ring.Count == 0 ? 0 : 1; i < points.Count; i++)
                        {
                            Tiles3DPoint local = frame.ToLocalMeters(transform.OfPoint(points[i]));
                            ring.Add(new[] { originEasting + local.X, originNorthing + local.Y });
                        }
                    }

                    if (ring.Count >= 3)
                    {
                        rings.Add(ring);
                    }
                }

                if (rings.Count == 0)
                {
                    continue;
                }

                var footprint = new Tiles3DRoomFootprint
                {
                    UniqueId = room.UniqueId ?? string.Empty,
                    ElementId = room.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    SourceDocument = sourceDocument,
                    SourceLinkName = sourceLinkName,
                    SourceLinkInstanceUniqueId = sourceLinkInstanceUniqueId,
                    Name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? string.Empty,
                    Number = room.Number ?? string.Empty,
                    AreaSquareMeters = room.Area * FeetToMeters * FeetToMeters,
                    Rings = rings,
                };
                if (document.GetElement(room.LevelId) is Level level)
                {
                    footprint.LevelUniqueId = level.UniqueId ?? string.Empty;
                    footprint.LevelName = level.Name;
                    // As for element levels: ProjectElevation through the link's transform, in
                    // the anchor's height datum.
                    XYZ levelInHost = transform.OfPoint(new XYZ(0, 0, level.ProjectElevation));
                    footprint.ElevationMeters = referenceContext.AnchorElevationMeters + frame.ToLocalMeters(levelInHost).Z;
                }

                rooms.Add(footprint);
            }
            catch (Exception ex)
            {
                warnings?.Add($"Skipped room {FormatElementLabel(room)}{(sourceLinkName.Length == 0 ? string.Empty : $" from '{sourceLinkName}'")}: {ex.Message}");
            }
        }
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

        return document.GetElement(ToElementId(scope.SelectedView!.ViewId)) as View3D
            ?? throw new InvalidOperationException("The selected 3D Tiles export view could not be resolved as a 3D view.");
    }

    private static void AppendHostMeshes(
        Document document,
        View3D? selectedView,
        Options options,
        GeometryExtractionFrame frame,
        Tiles3DExportScopeMode scopeMode,
        Tiles3DMaterialPalette materialPalette,
        List<Tiles3DMeshPrimitive> meshes,
        ICollection<string>? warnings)
    {
        FilteredElementCollector collector = scopeMode == Tiles3DExportScopeMode.Selected3DView && selectedView is not null
            ? new FilteredElementCollector(document, selectedView.Id).WhereElementIsNotElementType()
            : new FilteredElementCollector(document).WhereElementIsNotElementType();

        foreach (Element element in collector)
        {
            try
            {
                AppendElementMesh(element, options, frame, Transform.Identity, materialPalette, meshes, document.Title, string.Empty);
            }
            catch (Exception ex)
            {
                warnings?.Add($"Skipped host element {FormatElementLabel(element)}: {ex.Message}");
            }
        }
    }

    private static void AppendLinkedMeshes(
        Document hostDocument,
        View3D? selectedView,
        Tiles3DExportLinkOption linkOption,
        Options options,
        GeometryExtractionFrame frame,
        Tiles3DExportScopeMode scopeMode,
        Tiles3DMaterialPalette materialPalette,
        List<Tiles3DMeshPrimitive> meshes,
        ICollection<string>? warnings)
    {
        int firstLinkedMesh = meshes.Count;
        string linkInstanceUniqueId = string.Empty;
        try
        {
            if (linkOption.LinkInstanceId <= 0)
            {
                warnings?.Add($"Skipped linked model '{FormatLinkTitle(linkOption)}' because the link instance id is invalid.");
                return;
            }

            RevitLinkInstance? linkInstance = hostDocument.GetElement(ToElementId(linkOption.LinkInstanceId)) as RevitLinkInstance;
            if (linkInstance is null)
            {
                warnings?.Add($"Skipped linked model '{FormatLinkTitle(linkOption)}' because the link instance could not be resolved.");
                return;
            }

            linkInstanceUniqueId = linkInstance.UniqueId ?? string.Empty;
            Document? linkDocument = linkInstance.GetLinkDocument();
            if (linkDocument is null)
            {
                warnings?.Add($"Skipped linked model '{FormatLinkTitle(linkOption)}' because the linked document is not loaded.");
                return;
            }

            if (Tiles3DShowcaseGuard.IsShowcaseDocument(linkDocument))
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

                    AppendLinkedElementMesh(element, options, frame, linkTransform, materialPalette, meshes, linkDocument.Title, linkOption.Title, warnings);
                }

                return;
            }

            FilteredElementCollector collector = new FilteredElementCollector(linkDocument).WhereElementIsNotElementType();
            foreach (Element element in collector)
            {
                AppendLinkedElementMesh(element, options, frame, linkTransform, materialPalette, meshes, linkDocument.Title, linkOption.Title, warnings);
            }
        }
        catch (Exception ex)
        {
            warnings?.Add($"Skipped linked model '{FormatLinkTitle(linkOption)}': {ex.Message}");
        }
        finally
        {
            // Two instances of one link share document title and element UniqueIds; the
            // instance id tells their elements apart.
            for (int i = firstLinkedMesh; i < meshes.Count; i++)
            {
                meshes[i].Metadata.SourceLinkInstanceUniqueId = linkInstanceUniqueId;
            }
        }
    }

    private static void AppendLinkedElementMesh(
        Element element,
        Options options,
        GeometryExtractionFrame frame,
        Transform linkTransform,
        Tiles3DMaterialPalette materialPalette,
        List<Tiles3DMeshPrimitive> meshes,
        string sourceDocument,
        string sourceLinkName,
        ICollection<string>? warnings)
    {
        try
        {
            AppendElementMesh(element, options, frame, linkTransform, materialPalette, meshes, sourceDocument, sourceLinkName);
        }
        catch (Exception ex)
        {
            warnings?.Add($"Skipped linked element {FormatElementLabel(element)} from '{sourceLinkName}': {ex.Message}");
        }
    }

    private static ElementId ToElementId(long value)
    {
        return new ElementId(value);
    }

    private static void AppendElementMesh(
        Element element,
        Options options,
        GeometryExtractionFrame frame,
        Transform transform,
        Tiles3DMaterialPalette materialPalette,
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

        MaterialColorResolver materialColorResolver = new MaterialColorResolver(element, materialPalette);
        List<Tiles3DTriangle> triangles = new List<Tiles3DTriangle>();
        AppendGeometry(element.Document, geometry, transform, frame, materialColorResolver, triangles);
        if (triangles.Count == 0)
        {
            return;
        }

        Tiles3DObjectMetadata metadata = BuildElementMetadata(element, sourceDocument, sourceLinkName, triangles, transform, frame);
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

        if (Tiles3DShowcaseGuard.IsShowcaseElement(element))
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
        try
        {
            string categoryName = element.Category?.Name ?? "Element";
            string elementName = string.IsNullOrWhiteSpace(element.Name) ? "Unnamed" : element.Name;
            return $"{categoryName} #{element.Id.Value} ({elementName})";
        }
        catch (Exception)
        {
            return "Element";
        }
    }

    private static string FormatLinkTitle(Tiles3DExportLinkOption linkOption)
    {
        return string.IsNullOrWhiteSpace(linkOption.Title)
            ? $"Link {linkOption.LinkInstanceId}"
            : linkOption.Title;
    }

    private static Tiles3DObjectMetadata BuildElementMetadata(
        Element element,
        string sourceDocument,
        string sourceLinkName,
        IReadOnlyCollection<Tiles3DTriangle> triangles,
        Transform transform,
        GeometryExtractionFrame frame)
    {
        string typeName = string.Empty;
        string familyName = string.Empty;
        Element? typeElement = null;
        ElementId typeId = element.GetTypeId();
        if (typeId != ElementId.InvalidElementId && element.Document.GetElement(typeId) is Element resolvedType)
        {
            typeElement = resolvedType;
            typeName = resolvedType.Name ?? string.Empty;
            if (resolvedType is ElementType elementType)
            {
                familyName = elementType.FamilyName ?? string.Empty;
            }
        }

        if (element is FamilyInstance familyInstance && familyInstance.Symbol is not null)
        {
            typeElement = familyInstance.Symbol;
            familyName = familyInstance.Symbol.FamilyName ?? familyName;
            typeName = familyInstance.Symbol.Name ?? typeName;
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
            SourceLinkName = sourceLinkName ?? string.Empty,
            GisId = ReadSharedText(element, GisIdParameterGuid),
            GisRevitKey = ReadSharedText(element, GisKeyParameterGuid),
            GisKind = FirstNonEmpty(
                ReadSharedText(element, GisKindParameterGuid),
                ReadSharedText(typeElement, GisKindParameterGuid)),
            GisCategory = FirstNonEmpty(
                ReadSharedText(element, GisInstanceCategoryParameterGuid),
                ReadSharedText(element, GisCategoryParameterGuid),
                ReadSharedText(typeElement, GisCategoryParameterGuid),
                ReadSharedText(typeElement, GisInstanceCategoryParameterGuid))
        };

        ResolveElementLevel(element, metadata, transform, frame);
        ResolveVerticalExtents(triangles, metadata);
        return metadata;
    }

    private static void ResolveElementLevel(Element element, Tiles3DObjectMetadata metadata, Transform transform, GeometryExtractionFrame frame)
    {
        ElementId levelId = ResolveLevelIdFromElement(element);

        if (levelId == ElementId.InvalidElementId && element is StairsRun stairRun)
        {
            Stairs? parent = stairRun.GetStairs();
            if (parent is not null)
            {
                levelId = ResolveLevelIdFromElement(parent);
            }
        }

        if (levelId == ElementId.InvalidElementId && element is StairsLanding stairLanding)
        {
            Stairs? parent = stairLanding.GetStairs();
            if (parent is not null)
            {
                levelId = ResolveLevelIdFromElement(parent);
            }
        }

        if (levelId == ElementId.InvalidElementId)
        {
            metadata.GisLevelId = ReadSharedText(element, GisLevelIdParameterGuid);
            return;
        }

        if (element.Document.GetElement(levelId) is Level level)
        {
            metadata.LevelName = level.Name;
            metadata.LevelKey = Tiles3DLevelMetadata.BuildLevelKey(level.Name);
            metadata.LevelUniqueId = level.UniqueId ?? string.Empty;
            metadata.GisLevelId = FirstNonEmpty(
                ReadSharedText(level, GisLevelIdParameterGuid),
                ReadSharedText(element, GisLevelIdParameterGuid));
            // Apply the link instance transform and tileset frame so the elevation lands
            // in the same coordinate space as triangle vertex Z values. Without this,
            // linked model levels would be in local linked-doc space and miss the link's
            // placement offset (transform = Transform.Identity for the host model).
            // ProjectElevation, not Elevation: vertices are in internal coordinates, and
            // Elevation is relative to the level's Elevation Base setting (Survey Point in
            // TP-based projects), which puts level planes in a different frame than the mesh.
            XYZ levelInHost = transform.OfPoint(new XYZ(0, 0, level.ProjectElevation));
            metadata.LevelElevationMeters = frame.ToLocalMeters(levelInHost).Z;
        }
        else
        {
            metadata.GisLevelId = ReadSharedText(element, GisLevelIdParameterGuid);
        }
    }

    /// <summary>
    /// A Kanizu GIS shared parameter's text, read only by its frozen GUID so a same-named project
    /// parameter can never leak into the join columns. Empty when unbound or blank.
    /// </summary>
    private static string ReadSharedText(Element? element, Guid guid)
    {
        if (element is null)
        {
            return string.Empty;
        }

        Parameter? parameter = element.get_Parameter(guid);
        if (parameter is null || !parameter.HasValue || parameter.StorageType != StorageType.String)
        {
            return string.Empty;
        }

        return (parameter.AsString() ?? string.Empty).Trim();
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static ElementId ResolveLevelIdFromElement(Element element)
    {
        ElementId levelId = element.LevelId;
        if (levelId != ElementId.InvalidElementId)
        {
            return levelId;
        }

        Parameter? levelParam = element.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
        if (levelParam != null && levelParam.HasValue)
        {
            ElementId paramId = levelParam.AsElementId();
            if (paramId != null && paramId != ElementId.InvalidElementId)
            {
                return paramId;
            }
        }

        Parameter? stairsBaseLevelParam = element.get_Parameter(BuiltInParameter.STAIRS_BASE_LEVEL_PARAM);
        if (stairsBaseLevelParam != null && stairsBaseLevelParam.HasValue)
        {
            ElementId paramId = stairsBaseLevelParam.AsElementId();
            if (paramId != null && paramId != ElementId.InvalidElementId)
            {
                return paramId;
            }
        }

        return FindLevelIdFromAnyParameter(element);
    }

    internal static ElementId FindLevelIdFromAnyParameter(Element element)
    {
        foreach (Parameter param in element.Parameters)
        {
            if (param.StorageType != StorageType.ElementId || !param.HasValue)
            {
                continue;
            }

            ElementId id = param.AsElementId();
            if (id == null || id == ElementId.InvalidElementId)
            {
                continue;
            }

            if (element.Document.GetElement(id) is Level)
            {
                return id;
            }
        }

        return ElementId.InvalidElementId;
    }

    internal static void ResolveVerticalExtents(IReadOnlyCollection<Tiles3DTriangle> triangles, Tiles3DObjectMetadata metadata)
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
        Document ownerDocument,
        GeometryElement geometryElement,
        Transform transform,
        GeometryExtractionFrame frame,
        MaterialColorResolver materialColorResolver,
        List<Tiles3DTriangle> triangles)
    {
        foreach (GeometryObject geometryObject in geometryElement)
        {
            if (IsExcludedSubcategory(ownerDocument, geometryObject))
            {
                continue;
            }

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
                AppendGeometry(ownerDocument, instance.GetInstanceGeometry(), transform, frame, materialColorResolver, triangles);
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

    /// <summary>
    /// Whether <paramref name="geometryObject"/> is drawn in the <c>K_Footprint</c> subcategory, the 2D
    /// footprint masses Kanizu families carry for plans. Its graphics style lives in the document
    /// that owns the element, so a linked element resolves it in the link, not the host.
    /// </summary>
    private static bool IsExcludedSubcategory(Document ownerDocument, GeometryObject geometryObject)
    {
        ElementId styleId = geometryObject.GraphicsStyleId;
        if (styleId is null || styleId == ElementId.InvalidElementId)
        {
            return false;
        }

        if (ownerDocument.GetElement(styleId) is not GraphicsStyle style)
        {
            return false;
        }

        return IsExcludedSubcategoryName(style.GraphicsStyleCategory?.Name)
            || IsExcludedSubcategoryName(style.Name);
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
        private readonly Tiles3DMaterialPalette materialPalette;
        private readonly Dictionary<long, Tiles3DMaterialColor> materialColors = new Dictionary<long, Tiles3DMaterialColor>();

        public MaterialColorResolver(Element element, Tiles3DMaterialPalette materialPalette)
        {
            document = element.Document;
            this.materialPalette = materialPalette;
            FallbackColor = ResolveFallbackColor(element, materialPalette);
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
                && TryGetMaterialColor(material, materialPalette, out Tiles3DMaterialColor materialColor)
                    ? materialColor
                    : FallbackColor;
            materialColors[key] = color;
            return color;
        }

        private static Tiles3DMaterialColor ResolveFallbackColor(Element element, Tiles3DMaterialPalette materialPalette)
        {
            if (TryGetMaterialColor(element.Category?.Material, materialPalette, out Tiles3DMaterialColor categoryColor))
            {
                return categoryColor;
            }

            if (TryGetFirstElementMaterialColor(element, false, materialPalette, out Tiles3DMaterialColor elementColor))
            {
                return elementColor;
            }

            if (TryGetFirstElementMaterialColor(element, true, materialPalette, out Tiles3DMaterialColor paintColor))
            {
                return paintColor;
            }

            return Tiles3DMaterialPaletteResolver.Resolve(materialPalette, null, Tiles3DMaterialColor.Default);
        }

        private static bool TryGetFirstElementMaterialColor(
            Element element,
            bool returnPaintMaterials,
            Tiles3DMaterialPalette materialPalette,
            out Tiles3DMaterialColor color)
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
                        && TryGetMaterialColor(material, materialPalette, out color))
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

        private static bool TryGetMaterialColor(Material? material, Tiles3DMaterialPalette materialPalette, out Tiles3DMaterialColor color)
        {
            if (material is not null && material.Color is Color revitColor && revitColor.IsValid)
            {
                Tiles3DMaterialColor authored = new Tiles3DMaterialColor(
                    revitColor.Red,
                    revitColor.Green,
                    revitColor.Blue,
                    TransparencyToAlpha(material.Transparency));
                color = Tiles3DMaterialPaletteResolver.Resolve(materialPalette, material.Name, authored);
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
