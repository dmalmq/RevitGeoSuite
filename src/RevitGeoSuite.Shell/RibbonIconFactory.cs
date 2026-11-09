using System.Windows;
using System.Windows.Media;
using RevitGeoSuite.Core.Modules;

namespace RevitGeoSuite.Shell;

/// <summary>
/// Draws the ribbon buttons as a pine tile (Kanizu's Framed sheet primary, #1D5E51) with a white
/// Lucide glyph, the same glyphs the web UI uses for each workflow (src/lib/kit/icons.ts):
/// map-pin for Georeference, download for Import, upload for Export.
/// </summary>
internal static class RibbonIconFactory
{
    private const double CanvasSize = 32d;

    // Lucide draws on a 24 unit grid with a 2 unit stroke. Shown at 20/24 inside the 32 unit tile,
    // centred, the glyph keeps a 6 unit margin and reads at Revit's 16 and 32 px sizes.
    private const double GlyphSize = 20d;
    private const double GlyphOffset = (CanvasSize - GlyphSize) / 2d;

    private static readonly Color Pine = Color.FromRgb(0x1D, 0x5E, 0x51);
    private static readonly Color Slate = Color.FromRgb(0x56, 0x60, 0x5C);

    public static ImageSource CreateLarge(RibbonIconKind iconKind)
    {
        return Create(iconKind, 32d);
    }

    public static ImageSource CreateSmall(RibbonIconKind iconKind)
    {
        return Create(iconKind, 16d);
    }

    private static ImageSource Create(RibbonIconKind iconKind, double size)
    {
        DrawingGroup drawing = new DrawingGroup();
        drawing.Transform = new ScaleTransform(size / CanvasSize, size / CanvasSize);

        using (DrawingContext context = drawing.Open())
        {
            bool known = iconKind is RibbonIconKind.WebGeoreference or RibbonIconKind.WebImport or RibbonIconKind.WebExport;
            context.DrawRoundedRectangle(CreateBrush(known ? Pine : Slate), null, new Rect(1d, 1d, 30d, 30d), 7d, 7d);

            TransformGroup glyphTransform = new TransformGroup();
            glyphTransform.Children.Add(new ScaleTransform(GlyphSize / 24d, GlyphSize / 24d));
            glyphTransform.Children.Add(new TranslateTransform(GlyphOffset, GlyphOffset));
            context.PushTransform(glyphTransform);

            Pen pen = CreatePen(Colors.White, 2d);
            foreach (Geometry geometry in Glyph(iconKind))
            {
                context.DrawGeometry(null, pen, geometry);
            }

            context.Pop();
        }

        Freeze(drawing);
        DrawingImage image = new DrawingImage(drawing);
        Freeze(image);
        return image;
    }

    // Lucide 0.468.0 path data, with polylines and lines written as paths.
    private static Geometry[] Glyph(RibbonIconKind iconKind)
    {
        switch (iconKind)
        {
            case RibbonIconKind.WebGeoreference:
                // map-pin
                return new[]
                {
                    Parse("M20 10c0 4.993-5.539 10.193-7.399 11.799a1 1 0 0 1-1.202 0C9.539 20.193 4 14.993 4 10a8 8 0 0 1 16 0"),
                    Frozen(new EllipseGeometry(new Point(12d, 10d), 3d, 3d))
                };
            case RibbonIconKind.WebImport:
                // download
                return new[]
                {
                    Parse("M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"),
                    Parse("M7 10L12 15L17 10"),
                    Parse("M12 15V3")
                };
            case RibbonIconKind.WebExport:
                // upload
                return new[]
                {
                    Parse("M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"),
                    Parse("M17 8L12 3L7 8"),
                    Parse("M12 3V15")
                };
            default:
                // x
                return new[] { Parse("M18 6L6 18"), Parse("M6 6L18 18") };
        }
    }

    private static Geometry Parse(string data)
    {
        return Frozen(Geometry.Parse(data));
    }

    private static Geometry Frozen(Geometry geometry)
    {
        Freeze(geometry);
        return geometry;
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        SolidColorBrush brush = new SolidColorBrush(color);
        Freeze(brush);
        return brush;
    }

    private static Pen CreatePen(Color color, double thickness)
    {
        Pen pen = new Pen(CreateBrush(color), thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        Freeze(pen);
        return pen;
    }

    private static void Freeze(Freezable freezable)
    {
        if (freezable.CanFreeze)
        {
            freezable.Freeze();
        }
    }
}
