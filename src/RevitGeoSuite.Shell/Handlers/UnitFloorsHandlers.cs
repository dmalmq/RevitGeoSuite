using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using NetTopologySuite.Geometries;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.FloorPlanExport.Core.Coordinates;
using RevitGeoSuite.FloorPlanExport.Core.UnitFloors;
using RevitGeoSuite.PlateauImport;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using RevitGeoSuite.RevitInterop.Storage;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using NtsPolygon = NetTopologySuite.Geometries.Polygon;
using RevitTransaction = Autodesk.Revit.DB.Transaction;

namespace RevitGeoSuite.Shell.Handlers;

/// <summary>Lists a GeoPackage's feature tables with the model's levels and floor types.</summary>
public sealed class UnitFloorsInspectHandler : IRpcHandler
{
    public string Method => "unitFloors.inspect";

    public async Task<object?> HandleAsync(object? payload)
    {
        UnitFloorsInspectRequest request = UnitFloorsWork.Parse<UnitFloorsInspectRequest>(payload);
        IReadOnlyList<GisFeatureTableInfo> tables = GeoPackageFeatureReader.ListTables(request.Path);
        return await RevitContext.Instance.InvokeWithDocumentAsync(doc =>
        {
            GisLevelOption[] levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(level => level.Elevation)
                .ThenBy(level => level.Name, StringComparer.Ordinal)
                .Select(level => new GisLevelOption { Id = level.Id.Value, Name = level.Name ?? string.Empty, ElevationFeet = level.Elevation })
                .ToArray();
            long? active = doc.ActiveView is ViewPlan plan && plan.GenLevel is Level level ? level.Id.Value : null;
            UnitFloorsFloorType[] types = new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .Select(type => new UnitFloorsFloorType { Id = type.Id.Value, Name = type.Name ?? string.Empty })
                .OrderBy(type => type.Name, StringComparer.Ordinal)
                .ToArray();
            return (object?)new UnitFloorsInspectResponse
            {
                Tables = tables
                    .Select(table => new UnitFloorsTable
                    {
                        Name = table.Name,
                        Columns = table.Columns.ToArray(),
                        FeatureCount = table.FeatureCount,
                        Epsg = table.Epsg,
                    })
                    .ToArray(),
                Levels = levels,
                DefaultLevelId = active.HasValue && levels.Any(l => l.Id == active.Value) ? active : levels.FirstOrDefault()?.Id,
                FloorTypes = types,
            };
        }).ConfigureAwait(false);
    }
}

/// <summary>Plans one floor per unit row without changing the model.</summary>
public sealed class UnitFloorsPreviewHandler : IRpcHandler
{
    public string Method => "unitFloors.preview";

    public async Task<object?> HandleAsync(object? payload)
    {
        UnitFloorsRequest request = UnitFloorsWork.Parse<UnitFloorsRequest>(payload);
        GisFeatureTable table = GeoPackageFeatureReader.Read(request.Path, request.Table);
        return await RevitContext.Instance.InvokeWithDocumentAsync(doc =>
        {
            UnitFloorsWork.Prepared prepared = UnitFloorsWork.Prepare(doc, request, table);
            return (object?)UnitFloorsWork.Summarise(prepared, request);
        }).ConfigureAwait(false);
    }
}

/// <summary>
/// Makes one floor per unit row, each with the type of the existing floor under it, optionally
/// deletes the existing floors the new ones cover, and writes the link file beside the GeoPackage.
/// Everything happens in one transaction, so a single Undo in Revit takes it all back.
/// </summary>
public sealed class UnitFloorsCreateHandler : IRpcHandler
{
    public string Method => "unitFloors.create";

    public async Task<object?> HandleAsync(object? payload)
    {
        UnitFloorsRequest request = UnitFloorsWork.Parse<UnitFloorsRequest>(payload);
        GisFeatureTable table = GeoPackageFeatureReader.Read(request.Path, request.Table);
        return await RevitContext.Instance.InvokeWithDocumentAsync(doc =>
        {
            UnitFloorsWork.Prepared prepared = UnitFloorsWork.Prepare(doc, request, table);
            return (object?)UnitFloorsWork.Create(doc, prepared, request);
        }).ConfigureAwait(false);
    }
}

internal static class UnitFloorsWork
{
    private const double FeetToMetres = 0.3048d;

    internal sealed class Prepared
    {
        public Prepared(Level level, PlateauImportReferenceContext reference, UnitFloorPlan plan, int? sourceEpsg, List<string> warnings)
        {
            Level = level;
            Reference = reference;
            Plan = plan;
            SourceEpsg = sourceEpsg;
            Warnings = warnings;
        }

        public Level Level { get; }

        public PlateauImportReferenceContext Reference { get; }

        public UnitFloorPlan Plan { get; }

        public int? SourceEpsg { get; }

        public List<string> Warnings { get; }
    }

    public static T Parse<T>(object? payload)
        where T : new()
    {
        return payload is JObject jObject ? jObject.ToObject<T>() ?? new T() : new T();
    }

    public static Prepared Prepare(Document doc, UnitFloorsRequest request, GisFeatureTable table)
    {
        Level level = doc.GetElement(new ElementId(request.LevelId)) as Level
            ?? throw new InvalidOperationException("Choose a Revit level for the floors.");
        PlateauImportReferenceContext reference = ResolveReference(doc)
            ?? throw new InvalidOperationException(
                "This project isn't georeferenced yet. Complete Georeference Setup (CRS + Project Base Point) first.");
        int projectEpsg = reference.ProjectCrs.EpsgCode;
        if (projectEpsg <= 0)
        {
            throw new InvalidOperationException("The project's CRS has no EPSG code, so the units cannot be placed.");
        }

        List<string> warnings = new(table.Warnings);
        (Func<double, double, (double X, double Y)> project, string? reprojectionWarning) =
            CoordinateSystemCatalog.CreateReprojector(table.SourceEpsg, table.SourceWkt, projectEpsg);
        if (!string.IsNullOrWhiteSpace(reprojectionWarning))
        {
            warnings.Add(reprojectionWarning!);
        }

        string keyColumn = request.KeyColumn?.Trim() ?? string.Empty;
        string filterColumn = request.FilterColumn?.Trim() ?? string.Empty;
        string filterValue = request.FilterValue?.Trim() ?? string.Empty;
        if (keyColumn.Length > 0 && !table.Info.Columns.Contains(keyColumn))
        {
            throw new InvalidOperationException($"The table has no column \"{keyColumn}\".");
        }

        if (filterColumn.Length > 0 && !table.Info.Columns.Contains(filterColumn))
        {
            throw new InvalidOperationException($"The table has no column \"{filterColumn}\".");
        }

        List<UnitFloorSource> units = new();
        foreach (GisFeature feature in table.Features)
        {
            if (filterColumn.Length > 0
                && !string.Equals(feature.Attributes[filterColumn]?.Trim() ?? string.Empty, filterValue, StringComparison.Ordinal))
            {
                continue;
            }

            string key = keyColumn.Length > 0
                ? feature.Attributes[keyColumn]?.Trim() ?? string.Empty
                : feature.Fid.ToString(CultureInfo.InvariantCulture);
            NtsGeometry? geometry = feature.Geometry?.Copy();
            if (geometry is not null)
            {
                geometry.Apply(new ReprojectFilter(project));
                geometry.GeometryChanged();
            }

            units.Add(new UnitFloorSource(key, geometry));
        }

        if (units.Count == 0)
        {
            throw new InvalidOperationException("No rows match the filter.");
        }

        List<HostFloorOutline> hosts = ReadHostFloors(doc, level, reference, warnings);
        UnitFloorPlan plan = UnitFloorPlanner.Plan(units, hosts);
        return new Prepared(level, reference, plan, table.SourceEpsg, warnings);
    }

    public static UnitFloorsResponse Summarise(Prepared prepared, UnitFloorsRequest request)
    {
        UnitFloorPlan plan = prepared.Plan;
        return new UnitFloorsResponse
        {
            Units = plan.Units.Count,
            Matched = plan.CountOf(UnitFloorStatus.Matched),
            NoHost = plan.CountOf(UnitFloorStatus.NoHost),
            NotPolygon = plan.CountOf(UnitFloorStatus.NotPolygon),
            DuplicateKey = plan.CountOf(UnitFloorStatus.DuplicateKey),
            Hosts = plan.Hosts
                .OrderByDescending(host => host.UnitCount)
                .ThenBy(host => host.Host.TypeName, StringComparer.Ordinal)
                .Select(host => new UnitFloorsHost
                {
                    ElementId = host.Host.ElementId,
                    TypeName = host.Host.TypeName,
                    UnitCount = host.UnitCount,
                    CoveredShare = Math.Round(host.CoveredShare, 4),
                    Replaceable = host.Replaceable,
                })
                .ToArray(),
            Unplaced = plan.Units
                .Where(unit => unit.Status != UnitFloorStatus.Matched)
                .Select(unit => new UnitFloorsUnit
                {
                    Key = unit.Unit.Key,
                    Status = unit.Status.ToString(),
                    AreaSquareMetres = Math.Round(unit.Area, 2),
                })
                .ToArray(),
            Warnings = prepared.Warnings.ToArray(),
        };
    }

    public static UnitFloorsResponse Create(Document doc, Prepared prepared, UnitFloorsRequest request)
    {
        UnitFloorsResponse response = Summarise(prepared, request);
        ElementId? fallbackType = request.FallbackTypeId.HasValue && doc.GetElement(new ElementId(request.FallbackTypeId.Value)) is FloorType
            ? new ElementId(request.FallbackTypeId.Value)
            : null;
        Level level = prepared.Level;

        UnitFloorLinkFile link = new()
        {
            CreatedUtc = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        };
        link.Source.Path = request.Path;
        link.Source.Table = request.Table;
        link.Source.KeyColumn = string.IsNullOrWhiteSpace(request.KeyColumn) ? null : request.KeyColumn.Trim();
        link.Source.FilterColumn = string.IsNullOrWhiteSpace(request.FilterColumn) ? null : request.FilterColumn.Trim();
        link.Source.FilterValue = string.IsNullOrWhiteSpace(request.FilterColumn) ? null : request.FilterValue?.Trim();
        link.Source.Epsg = prepared.SourceEpsg;
        link.Source.ProjectEpsg = prepared.Reference.ProjectCrs.EpsgCode;
        link.Model.Title = doc.Title ?? string.Empty;
        link.Model.Path = doc.PathName ?? string.Empty;
        link.Level.UniqueId = level.UniqueId;
        link.Level.ElementId = level.Id.Value;
        link.Level.Name = level.Name ?? string.Empty;

        List<UnitFloorsUnit> unplaced = new();
        int created = 0;
        int deleted = 0;
        WarningSwallower swallower = new();
        using (RevitTransaction transaction = new(doc, "Create unit floors"))
        {
            FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(swallower);
            transaction.SetFailureHandlingOptions(options);
            transaction.Start();

            foreach (PlannedUnitFloor unit in prepared.Plan.Units)
            {
                if (!unit.WillCreate)
                {
                    link.Skipped.Add(new UnitFloorLinkSkipped { Key = unit.Unit.Key, Reason = unit.Status.ToString() });
                    continue;
                }

                ElementId? typeId = unit.Host is not null ? new ElementId(unit.Host.TypeId) : fallbackType;
                if (typeId is null)
                {
                    link.Skipped.Add(new UnitFloorLinkSkipped { Key = unit.Unit.Key, Reason = UnitFloorStatus.NoHost.ToString() });
                    continue;
                }

                List<CurveLoop> loops = new();
                foreach (NtsPolygon polygon in unit.Polygons)
                {
                    loops.AddRange(ToCurveLoops(polygon, prepared.Reference, level.Elevation));
                }

                Floor? floor = TryCreate(doc, loops, typeId, level.Id);
                if (floor is null)
                {
                    unplaced.Add(new UnitFloorsUnit { Key = unit.Unit.Key, Status = "Failed", AreaSquareMetres = Math.Round(unit.Area, 2) });
                    link.Skipped.Add(new UnitFloorLinkSkipped { Key = unit.Unit.Key, Reason = "Failed" });
                    continue;
                }

                CopyOffset(unit.Host, doc, floor);
                created++;
                link.Floors.Add(new UnitFloorLinkFloor
                {
                    Key = unit.Unit.Key,
                    FloorUniqueId = floor.UniqueId,
                    FloorElementId = floor.Id.Value,
                    TypeName = doc.GetElement(typeId)?.Name ?? string.Empty,
                    HostUniqueId = unit.Host?.UniqueId,
                    AreaSquareMetres = Math.Round(unit.Area, 3),
                });
            }

            if (request.DeleteReplaced)
            {
                foreach (HostFloorCoverage host in prepared.Plan.Hosts.Where(host => host.Replaceable))
                {
                    ElementId id = new(host.Host.ElementId);
                    if (doc.GetElement(id) is null)
                    {
                        continue;
                    }

                    doc.Delete(id);
                    deleted++;
                    link.ReplacedFloors.Add(new UnitFloorLinkReplaced
                    {
                        UniqueId = host.Host.UniqueId,
                        ElementId = host.Host.ElementId,
                        TypeName = host.Host.TypeName,
                        CoveredShare = Math.Round(host.CoveredShare, 4),
                    });
                }
            }

            if (transaction.Commit() != TransactionStatus.Committed)
            {
                throw new InvalidOperationException("Revit did not commit the new floors.");
            }
        }

        string linkPath = UnitFloorLinkFile.PathFor(request.Path, request.Table, level.Name ?? "level");
        List<string> warnings = new(response.Warnings);
        try
        {
            link.Save(linkPath);
            response.LinkFile = linkPath;
        }
        catch (Exception ex)
        {
            warnings.Add($"The floors were made, but the link file could not be written: {ex.Message}");
        }

        if (swallower.Count > 0)
        {
            warnings.Add(string.Format(CultureInfo.InvariantCulture, "Revit raised {0} warning(s) while making the floors, such as overlapping floors.", swallower.Count));
        }

        response.Created = created;
        response.Deleted = deleted;
        response.Failed = unplaced.Count;
        response.Unplaced = response.Unplaced.Concat(unplaced).ToArray();
        response.Warnings = warnings.ToArray();
        return response;
    }

    private static Floor? TryCreate(Document doc, IList<CurveLoop> loops, ElementId typeId, ElementId levelId)
    {
        if (loops.Count == 0)
        {
            return null;
        }

        using SubTransaction sub = new(doc);
        sub.Start();
        try
        {
            Floor floor = Floor.Create(doc, loops, typeId, levelId);
            sub.Commit();
            return floor;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            sub.RollBack();
            return null;
        }
        catch (ArgumentException)
        {
            sub.RollBack();
            return null;
        }
    }

    private static void CopyOffset(HostFloorOutline? host, Document doc, Floor floor)
    {
        if (host is null || doc.GetElement(new ElementId(host.ElementId)) is not Floor source)
        {
            return;
        }

        Parameter? from = source.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
        Parameter? to = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
        if (from is not null && to is not null && !to.IsReadOnly)
        {
            to.Set(from.AsDouble());
        }
    }

    private static IEnumerable<CurveLoop> ToCurveLoops(NtsPolygon polygon, PlateauImportReferenceContext reference, double elevationFeet)
    {
        foreach (IReadOnlyList<(double X, double Y)> ring in UnitFloorRings.FromPolygon(polygon))
        {
            List<XYZ> points = ring
                .Select(point => PlateauReferenceFrame.ToLocalFeet(point.X, point.Y, reference))
                .Select(local => new XYZ(local.XFeet, local.YFeet, elevationFeet))
                .ToList();
            CurveLoop loop = new();
            for (int i = 0; i < points.Count; i++)
            {
                loop.Append(Line.CreateBound(points[i], points[(i + 1) % points.Count]));
            }

            yield return loop;
        }
    }

    private static List<HostFloorOutline> ReadHostFloors(Document doc, Level level, PlateauImportReferenceContext reference, List<string> warnings)
    {
        GeometryFactory factory = new();
        List<HostFloorOutline> hosts = new();
        IEnumerable<Floor> floors = new FilteredElementCollector(doc)
            .OfClass(typeof(Floor))
            .Cast<Floor>()
            .Where(floor => floor.LevelId == level.Id);
        foreach (Floor floor in floors)
        {
            List<NtsPolygon> parts = new();
            foreach (Reference faceReference in HostObjectUtils.GetTopFaces(floor))
            {
                if (floor.GetGeometryObjectFromReference(faceReference) is not Face face)
                {
                    continue;
                }

                List<LinearRing> rings = new();
                foreach (CurveLoop loop in face.GetEdgesAsCurveLoops())
                {
                    List<Coordinate> coordinates = new();
                    foreach (Curve curve in loop)
                    {
                        IList<XYZ> points = curve.Tessellate();
                        for (int i = 0; i < points.Count - 1; i++)
                        {
                            (double east, double north) = ToProjected(points[i].X, points[i].Y, reference);
                            coordinates.Add(new Coordinate(east, north));
                        }
                    }

                    if (coordinates.Count >= 3)
                    {
                        coordinates.Add(coordinates[0].Copy());
                        rings.Add(factory.CreateLinearRing(coordinates.ToArray()));
                    }
                }

                // A top face's loops are its outline and its openings: the largest is the outline.
                rings.Sort((a, b) => Math.Abs(Area(b)).CompareTo(Math.Abs(Area(a))));
                if (rings.Count > 0)
                {
                    parts.Add(factory.CreatePolygon(rings[0], rings.Skip(1).ToArray()));
                }
            }

            if (parts.Count == 0)
            {
                warnings.Add($"Floor {floor.Id.Value} has no top face to compare with; it was left out.");
                continue;
            }

            NtsGeometry outline = parts.Count == 1 ? parts[0] : factory.CreateMultiPolygon(parts.ToArray());
            FloorType? type = doc.GetElement(floor.GetTypeId()) as FloorType;
            hosts.Add(new HostFloorOutline(floor.UniqueId, floor.Id.Value, floor.GetTypeId().Value, type?.Name ?? string.Empty, outline));
        }

        return hosts;
    }

    private static double Area(LinearRing ring)
    {
        return NetTopologySuite.Algorithm.Area.OfRingSigned(ring.CoordinateSequence);
    }

    /// <summary>Model coordinates (feet) to the project CRS (metres): the inverse of <see cref="PlateauReferenceFrame.ToLocalFeet"/>.</summary>
    private static (double East, double North) ToProjected(double xFeet, double yFeet, PlateauImportReferenceContext reference)
    {
        double determinant = (reference.SharedEastToLocalX * reference.SharedNorthToLocalY)
            - (reference.SharedNorthToLocalX * reference.SharedEastToLocalY);
        double dx = xFeet - reference.AnchorXFeet;
        double dy = yFeet - reference.AnchorYFeet;
        double eastFeet = ((dx * reference.SharedNorthToLocalY) - (reference.SharedNorthToLocalX * dy)) / determinant;
        double northFeet = ((reference.SharedEastToLocalX * dy) - (dx * reference.SharedEastToLocalY)) / determinant;
        return (
            reference.AnchorProjectedCoordinate.Easting + (eastFeet * FeetToMetres),
            reference.AnchorProjectedCoordinate.Northing + (northFeet * FeetToMetres));
    }

    private static PlateauImportReferenceContext? ResolveReference(Document doc)
    {
        GeoProjectInfoStorage geoStore = new();
        ModuleStateStorage moduleStateStore = new();
        ProjectLocationReader reader = new(geoStore, moduleStateStore: moduleStateStore);
        CurrentProjectStateSummary currentState = reader.Read(doc);
        var info = geoStore.Load(new RevitDocumentHandle(doc));
        CoordinateTransformer transformer = new(new CrsRegistry());
        PlateauImportReferenceResolver resolver = new(transformer, new RevitPlateauImportLocalBasisProvider(doc));
        return resolver.Resolve(currentState, info, PlateauImportReferenceSource.WorkingProjectBasePoint);
    }

    private sealed class ReprojectFilter : ICoordinateSequenceFilter
    {
        private readonly Func<double, double, (double X, double Y)> project;

        public ReprojectFilter(Func<double, double, (double X, double Y)> project)
        {
            this.project = project;
        }

        public bool Done => false;

        public bool GeometryChanged => true;

        public void Filter(CoordinateSequence seq, int i)
        {
            (double x, double y) = project(seq.GetX(i), seq.GetY(i));
            seq.SetX(i, x);
            seq.SetY(i, y);
        }
    }

    /// <summary>Deletes warnings, such as overlapping floors, so Revit does not stop the run with a dialog.</summary>
    private sealed class WarningSwallower : IFailuresPreprocessor
    {
        public int Count { get; private set; }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            foreach (FailureMessageAccessor failure in failuresAccessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(failure);
                    Count++;
                }
            }

            return FailureProcessingResult.Continue;
        }
    }
}
