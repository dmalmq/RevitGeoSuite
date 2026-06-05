namespace RevitGeoSuite.Core.Modules;

public enum RibbonIconKind
{
    Default,

    // Web shell suite entry points. Glyphs mirror the in-app nav rail (Rail.svelte):
    // folded map (GEO), arrow-into-tray (IMP), arrow-out-of-tray (EXP).
    WebGeoreference,
    WebImport,
    WebExport
}
