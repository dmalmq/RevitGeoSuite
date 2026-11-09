namespace RevitGeoSuite.Core.Modules;

public enum RibbonIconKind
{
    Default,

    // Web shell suite entry points. Glyphs are the web UI's Lucide icons for each sidebar group:
    // map-pin (Set up / Georeference), download (Import), upload (Export).
    WebGeoreference,
    WebImport,
    WebExport
}
