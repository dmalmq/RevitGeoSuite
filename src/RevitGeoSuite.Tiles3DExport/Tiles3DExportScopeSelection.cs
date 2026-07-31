using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DExportScopeSelection
{
    public Tiles3DExportScopeMode ScopeMode { get; set; } = Tiles3DExportScopeMode.WholeModel;

    public Tiles3DExportViewOption? SelectedView { get; set; }

    public IReadOnlyCollection<Tiles3DExportLinkOption> SelectedLinkedModels { get; set; } = Array.Empty<Tiles3DExportLinkOption>();

    public bool HasSelectedView => SelectedView is not null && SelectedView.ViewId > 0;

    public IReadOnlyCollection<string> SelectedLinkedModelNames => SelectedLinkedModels
        .Select(option => option.Title)
        .ToArray();
}
