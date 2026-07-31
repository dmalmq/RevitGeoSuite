using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitGeoSuite.CityGmlExport;

public static class CityGmlExportDocumentCatalog
{
    public static IReadOnlyCollection<CityGmlExportViewOption> CreateViewOptions(Document? document)
    {
        if (document is null)
        {
            return Array.Empty<CityGmlExportViewOption>();
        }

        string? activeViewUniqueId = document.ActiveView is View3D activeView && !activeView.IsTemplate
            ? activeView.UniqueId
            : null;

        return new FilteredElementCollector(document)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .Where(view => !view.IsTemplate)
            .OrderByDescending(view => string.Equals(view.UniqueId, activeViewUniqueId, StringComparison.Ordinal))
            .ThenBy(view => view.Name, StringComparer.OrdinalIgnoreCase)
            .Select(view => new CityGmlExportViewOption
            {
                ViewId = view.Id,
                UniqueId = view.UniqueId,
                Title = view.Name,
                Description = string.Equals(view.UniqueId, activeViewUniqueId, StringComparison.Ordinal)
                    ? "Active 3D view. Only model geometry visible in this view will be exported."
                    : "Only model geometry visible in this 3D view will be exported."
            })
            .ToArray();
    }

    public static IReadOnlyCollection<CityGmlExportLinkOption> CreateLinkOptions(Document? document)
    {
        if (document is null)
        {
            return Array.Empty<CityGmlExportLinkOption>();
        }

        return new FilteredElementCollector(document)
            .OfClass(typeof(RevitLinkInstance))
            .Cast<RevitLinkInstance>()
            .Where(instance => instance.GetLinkDocument() is not null)
            .OrderBy(instance => instance.Name, StringComparer.OrdinalIgnoreCase)
            .Select(instance =>
            {
                Document linkDocument = instance.GetLinkDocument()!;
                return new CityGmlExportLinkOption
                {
                    LinkInstanceId = instance.Id,
                    UniqueId = instance.UniqueId,
                    Title = string.IsNullOrWhiteSpace(linkDocument.Title) ? instance.Name : linkDocument.Title,
                    Description = $"Linked model '{linkDocument.Title}' through instance '{instance.Name}'. Visible geometry from the selected 3D view will be exported when checked."
                };
            })
            .ToArray();
    }
}
