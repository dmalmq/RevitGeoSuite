using RevitGeoSuite.Core.ProjectMetadata;

namespace RevitGeoSuite.Core.Storage;

public interface IGeoProjectInfoStore
{
    void Save(IDocumentHandle document, GeoProjectInfo info);

    GeoProjectInfo? Load(IDocumentHandle document);

    bool HasData(IDocumentHandle document);
}
