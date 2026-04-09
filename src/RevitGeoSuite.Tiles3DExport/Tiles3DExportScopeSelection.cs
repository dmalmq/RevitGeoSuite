using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DExportScopeSelection
{
    public Tiles3DExportScopeMode ScopeMode { get; set; } = Tiles3DExportScopeMode.WholeModel;

    public Tiles3DExportViewOption? SelectedView { get; set; }

    public IReadOnlyCollection<Tiles3DExportLinkOption> SelectedLinkedModels { get; set; } = Array.Empty<Tiles3DExportLinkOption>();

    public bool HasSelectedView => SelectedView is not null && SelectedView.ViewId != ElementId.InvalidElementId;

    public IReadOnlyCollection<ElementId> SelectedLinkedModelIds => SelectedLinkedModels
        .Select(option => option.LinkInstanceId)
        .ToArray();

    public IReadOnlyCollection<string> SelectedLinkedModelNames => SelectedLinkedModels
        .Select(option => option.Title)
        .ToArray();
}