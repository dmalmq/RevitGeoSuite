using Autodesk.Revit.DB;
using RevitGeoSuite.Core.Storage;

namespace RevitGeoSuite.RevitInterop;

public sealed class RevitDocumentHandle : IDocumentHandle
{
    public RevitDocumentHandle(Document document)
    {
        Document = document ?? throw new System.ArgumentNullException(nameof(document));
        DocumentKey = string.IsNullOrWhiteSpace(document.PathName)
            ? document.Title
            : document.PathName;
    }

    public Document Document { get; }

    public string DocumentKey { get; }
}
