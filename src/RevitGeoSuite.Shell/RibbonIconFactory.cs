using System.Windows;
using System.Windows.Media;
using RevitGeoSuite.Core.Modules;

namespace RevitGeoSuite.Shell;

internal static class RibbonIconFactory
{
    private const double CanvasSize = 32d;

    // The glyphs are authored in the 7..25 range of the 32-unit canvas, leaving large margins.
    // Scaling them about the canvas centre fills more of the colored tile so the icon reads
    // bigger within Revit's fixed large-button footprint. 1.3x keeps the artwork inside the
    // (1,1,30,30) tile (no clipping). Applies to both the 32px and 16px renders.
    private const double GlyphScale = 1.3d;
    private const double Center = CanvasSize / 2d;

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
            DrawBackground(context, GetBackgroundBrush(iconKind));

            // Enlarge only the glyph (not the tile) so it fills more of the button.
            context.PushTransform(new ScaleTransform(GlyphScale, GlyphScale, Center, Center));

            switch (iconKind)
            {
                case RibbonIconKind.WebGeoreference:
                    DrawWebGeoreference(context);
                    break;
                case RibbonIconKind.WebImport:
                    DrawWebImport(context);
                    break;
                case RibbonIconKind.WebExport:
                    DrawWebExport(context);
                    break;
                default:
                    DrawDefault(context);
                    break;
            }

            context.Pop();
        }

        Freeze(drawing);
        DrawingImage image = new DrawingImage(drawing);
        Freeze(image);
        return image;
    }

    private static void DrawBackground(DrawingContext context, Brush brush)
    {
        context.DrawRoundedRectangle(brush, null, new Rect(1d, 1d, 30d, 30d), 7d, 7d);
    }

    // The next three glyphs mirror the in-app web-shell nav rail (Rail.svelte):
    // folded map (GEO), arrow-into-tray (IMP), arrow-out-of-tray (EXP). They are drawn with
    // DrawingContext primitives in the same style as the other icons rather than parsing the
    // rail's raw SVG path data (WPF's path mini-language does not accept Heroicons' packed
    // arc flags).
    private static void DrawWebGeoreference(DrawingContext context)
    {
        Pen pen = CreatePen(Colors.White, 1.9d);
        Brush fill = CreateBrush(Color.FromArgb(70, 255, 255, 255));
        StreamGeometry map = new StreamGeometry();
        using (StreamGeometryContext geometry = map.Open())
        {
            // Accordion-folded map: alternating panel tops/bottoms.
            geometry.BeginFigure(new Point(7d, 12d), true, true);
            geometry.LineTo(new Point(13d, 9d), true, false);
            geometry.LineTo(new Point(19d, 12d), true, false);
            geometry.LineTo(new Point(25d, 9d), true, false);
            geometry.LineTo(new Point(25d, 21d), true, false);
            geometry.LineTo(new Point(19d, 24d), true, false);
            geometry.LineTo(new Point(13d, 21d), true, false);
            geometry.LineTo(new Point(7d, 24d), true, false);
        }

        Freeze(map);
        context.DrawGeometry(fill, pen, map);
        // Fold creases.
        context.DrawLine(pen, new Point(13d, 9d), new Point(13d, 21d));
        context.DrawLine(pen, new Point(19d, 12d), new Point(19d, 24d));
    }

    private static void DrawWebImport(DrawingContext context)
    {
        Pen pen = CreatePen(Colors.White, 2.0d);
        // Open-top tray.
        DrawTray(context, pen);
        // Down arrow descending into the tray.
        context.DrawLine(pen, new Point(16d, 7d), new Point(16d, 18d));
        context.DrawLine(pen, new Point(12d, 14d), new Point(16d, 18d));
        context.DrawLine(pen, new Point(20d, 14d), new Point(16d, 18d));
    }

    private static void DrawWebExport(DrawingContext context)
    {
        Pen pen = CreatePen(Colors.White, 2.0d);
        // Open-top tray.
        DrawTray(context, pen);
        // Up arrow rising out of the tray.
        context.DrawLine(pen, new Point(16d, 18d), new Point(16d, 7d));
        context.DrawLine(pen, new Point(12d, 11d), new Point(16d, 7d));
        context.DrawLine(pen, new Point(20d, 11d), new Point(16d, 7d));
    }

    private static void DrawTray(DrawingContext context, Pen pen)
    {
        StreamGeometry tray = new StreamGeometry();
        using (StreamGeometryContext geometry = tray.Open())
        {
            geometry.BeginFigure(new Point(8d, 16d), false, false);
            geometry.LineTo(new Point(8d, 22d), true, false);
            geometry.LineTo(new Point(24d, 22d), true, false);
            geometry.LineTo(new Point(24d, 16d), true, false);
        }

        Freeze(tray);
        context.DrawGeometry(null, pen, tray);
    }

    private static void DrawDefault(DrawingContext context)
    {
        Pen pen = CreatePen(Colors.White, 2.1d);
        context.DrawLine(pen, new Point(10d, 10d), new Point(22d, 22d));
        context.DrawLine(pen, new Point(22d, 10d), new Point(10d, 22d));
    }

    private static Brush GetBackgroundBrush(RibbonIconKind iconKind)
    {
        switch (iconKind)
        {
            case RibbonIconKind.WebGeoreference:
            case RibbonIconKind.WebImport:
            case RibbonIconKind.WebExport:
                // Shared teal (rail's active-state teal-600) so the trio reads as one app.
                return CreateBrush(Color.FromRgb(13, 148, 136));
            default:
                return CreateBrush(Color.FromRgb(95, 102, 112));
        }
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
