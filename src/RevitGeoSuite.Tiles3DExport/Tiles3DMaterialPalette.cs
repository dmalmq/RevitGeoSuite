namespace RevitGeoSuite.Tiles3DExport;

/// <summary>
/// Material colour strategy applied to extracted 3D Tiles geometry.
/// <see cref="Source"/> preserves the authored Revit material colours verbatim (default).
/// The Kanizu palettes remap <c>GIS_*</c> floor materials to Kanizu's indoor-map massing colours,
/// keep <c>K3D_*</c> and <c>M3D_*</c> authored family materials untouched, preserve transparent
/// glazing, and neutralise opaque architectural materials to matte white.
/// </summary>
public enum Tiles3DMaterialPalette
{
    Source = 0,
    KanizuSoft = 1,
    KanizuWhite = 2,
    KanizuFull = 3
}
