using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitGeoSuite.CityGmlExport;

public sealed class CityGmlExportScopeSelection
{
    public CityGmlExportViewOption? SelectedView { get; set; }

    public IReadOnlyCollection<CityGmlExportLinkOption> SelectedLinkedModels { get; set; } = Array.Empty<CityGmlExportLinkOption>();

    public bool HasSelectedView => SelectedView is not null && SelectedView.ViewId != ElementId.InvalidElementId;

    public IReadOnlyCollection<ElementId> SelectedLinkedModelIds => SelectedLinkedModels
        .Select(option => option.LinkInstanceId)
        .ToArray();

    public IReadOnlyCollection<string> SelectedLinkedModelNames => SelectedLinkedModels
        .Select(option => option.Title)
        .ToArray();
}
