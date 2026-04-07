using System;
using Newtonsoft.Json;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class TilesetJsonWriter
{
    private readonly GeospatialTransformBuilder transformBuilder;

    public TilesetJsonWriter(GeospatialTransformBuilder? transformBuilder = null)
    {
        this.transformBuilder = transformBuilder ?? new GeospatialTransformBuilder();
    }

    public string BuildJson(Tiles3DExportPackage package)
    {
        if (package is null)
        {
            throw new ArgumentNullException(nameof(package));
        }

        object document = new
        {
            asset = new
            {
                version = "1.1",
                generator = "RevitGeoSuite.Tiles3DExport"
            },
            geometricError = package.GeometricError,
            root = new
            {
                boundingVolume = new
                {
                    box = package.BoundingBox
                },
                geometricError = 0d,
                refine = "ADD",
                transform = transformBuilder.BuildEastNorthUpTransform(
                    package.ReferenceContext.AnchorLatitude,
                    package.ReferenceContext.AnchorLongitude,
                    package.ReferenceContext.AnchorElevationMeters),
                content = new
                {
                    uri = package.ContentFileName
                }
            }
        };

        return JsonConvert.SerializeObject(document, Formatting.Indented);
    }
}
