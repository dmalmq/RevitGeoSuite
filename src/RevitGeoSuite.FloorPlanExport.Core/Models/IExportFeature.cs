using System.Collections.Generic;

namespace RevitGeoSuite.FloorPlanExport.Core.Models;

public interface IExportFeature
{
    IReadOnlyDictionary<string, object?> Attributes { get; }

    IEnumerable<Point2D> GetAllPoints();
}
