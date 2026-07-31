using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Storage;

namespace RevitGeoSuite.Georeference;

internal sealed class MissingGeoProjectInfoStore : IGeoProjectInfoStore
{
    public void Save(IDocumentHandle document, GeoProjectInfo info)
    {
    }

    public GeoProjectInfo? Load(IDocumentHandle document)
    {
        return null;
    }

    public bool HasData(IDocumentHandle document)
    {
        return false;
    }
}
