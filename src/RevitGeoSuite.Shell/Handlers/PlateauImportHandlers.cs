using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.Plateau.Schema;
using RevitGeoSuite.PlateauImport;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;

namespace RevitGeoSuite.Shell.Handlers;

public sealed class PlateauScanFolderHandler : IRpcHandler
{
    private readonly JobManager jobs;

    public PlateauScanFolderHandler(JobManager jobs)
    {
        this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    public string Method => "plateau.scanFolder";

    public Task<object?> HandleAsync(object? payload)
    {
        var path = (payload as JObject)?.Value<string>("path");
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult<object?>(new { error = "Path is required" });
        }

        // Enumerating the tile grid is pure file-name work (no parsing, no Revit API), so it runs on
        // the thread pool and returns near-instantly. Parsing every file just to count features loads
        // the whole municipality into memory, which is what crashed large 3D imports.
        string jobId = jobs.Start((ct, progress) =>
        {
            var scanService = new PlateauFolderScanService(new CityGmlParser());
            var tileFiles = scanService.EnumerateTileFiles(path!);
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<object?>(new { tiles = BuildTileList(tileFiles) });
        });

        return Task.FromResult<object?>(new JobStarted { JobId = jobId });
    }

    private static object[] BuildTileList(IReadOnlyList<PlateauTileFileSummary> tileFiles)
    {
        var meshCalculator = new JapanMeshCalculator();

        // A secondary-mesh file (e.g. roads "tran" or relief "dem" named "533945") would render
        // as one giant ~10 km cell covering — and intercepting clicks on — every 1 km tertiary
        // tile inside it. Keep only leaf tiles so coarse parents drop out of the selectable grid.
        // Their features still import: the selected tertiary tile pulls in its coarser file via the
        // hierarchical prefix match in PlateauFolderScanService.
        var selectableTileIds = new HashSet<string>(
            PlateauSchemaHelper.SelectLeafTileIds(tileFiles.Select(t => t.TileId)),
            StringComparer.Ordinal);

        return tileFiles
            .Where(t => selectableTileIds.Contains(t.TileId))
            .Select(t => new
            {
                id = t.TileId,
                fileSize = t.FileSizeBytes,
                geometry = BuildTileGeometry(t.TileId, meshCalculator)
            })
            .ToArray();
    }

    private static object? BuildTileGeometry(string tileId, JapanMeshCalculator meshCalculator)
    {
        if (string.IsNullOrWhiteSpace(tileId)) return null;

        MeshBounds bounds;
        try
        {
            bounds = meshCalculator.GetBounds(new MeshCode { Value = tileId });
        }
        catch
        {
            return null;
        }

        return new
        {
            type = "Polygon",
            coordinates = new[]
            {
                new[]
                {
                    new[] { bounds.WestLongitude, bounds.SouthLatitude },
                    new[] { bounds.EastLongitude, bounds.SouthLatitude },
                    new[] { bounds.EastLongitude, bounds.NorthLatitude },
                    new[] { bounds.WestLongitude, bounds.NorthLatitude },
                    new[] { bounds.WestLongitude, bounds.SouthLatitude }
                }
            }
        };
    }
}
