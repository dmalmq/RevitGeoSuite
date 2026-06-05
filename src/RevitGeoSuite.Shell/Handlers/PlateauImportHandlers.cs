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

        // Folder scanning is pure file parsing (no Revit API), so it runs on the thread pool.
        string jobId = jobs.Start((ct, progress) =>
        {
            var scanService = new PlateauFolderScanService(new CityGmlParser());
            var result = scanService.ScanFolder(path!, p =>
            {
                ct.ThrowIfCancellationRequested();
                progress.Report(new JobProgress
                {
                    Phase = p.Phase.ToString().ToLower(),
                    Current = p.Current,
                    Total = p.Total,
                    Percent = (int)Math.Round(p.Percent),
                    Message = p.CurrentFileName
                });
            });
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<object?>(new { tiles = BuildTileList(result) });
        });

        return Task.FromResult<object?>(new JobStarted { JobId = jobId });
    }

    private static object[] BuildTileList(PlateauFolderScanResult result)
    {
        var meshCalculator = new JapanMeshCalculator();

        var models = result.CityModels
            .Where(m => !string.IsNullOrWhiteSpace(m.FileTileId))
            .ToArray();

        // A secondary-mesh file (e.g. roads "tran" or relief "dem" named "533945") would render
        // as one giant ~10 km cell covering — and intercepting clicks on — every 1 km tertiary
        // tile inside it. Keep only leaf tiles so coarse parents drop out of the selectable grid.
        // Their features still export: PlateauExportContextSupport.IsTileSelectedForExport ties a
        // secondary-mesh feature to any selected tertiary child sharing its 6-digit prefix.
        var selectableTileIds = new HashSet<string>(
            PlateauSchemaHelper.SelectLeafTileIds(models.Select(m => m.FileTileId!)),
            StringComparer.Ordinal);

        return models
            .Where(m => selectableTileIds.Contains(m.FileTileId!))
            .GroupBy(m => m.FileTileId!)
            .Select(g => new
            {
                id = g.Key,
                featureCount = g.Sum(m => m.Features.Count),
                fileSize = g.Sum(m => new System.IO.FileInfo(m.SourcePath).Length),
                lod = g.Max(m => m.Features.Max(f => f.HighestLod)),
                geometry = BuildTileGeometry(g.Key, meshCalculator)
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
