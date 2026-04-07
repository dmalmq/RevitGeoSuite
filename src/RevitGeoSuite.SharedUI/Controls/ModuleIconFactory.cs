using System.Windows;
using System.Windows.Media;
using RevitGeoSuite.Core.Modules;

namespace RevitGeoSuite.SharedUI.Controls;

public static class ModuleIconFactory
{
    private const double CanvasSize = 32d;

    public static ImageSource CreateLarge(RibbonIconKind iconKind)
    {
        return Create(iconKind, 32d);
    }

    public static ImageSource CreateSmall(RibbonIconKind iconKind)
    {
        return Create(iconKind, 16d);
    }

    public static ImageSource CreateRail(RibbonIconKind iconKind)
    {
        return Create(iconKind, 20d);
    }

    private static ImageSource Create(RibbonIconKind iconKind, double size)
    {
        DrawingGroup drawing = new DrawingGroup();
        drawing.Transform = new ScaleTransform(size / CanvasSize, size / CanvasSize);

        using (DrawingContext context = drawing.Open())
        {
            DrawBackground(context, GetBackgroundBrush(iconKind));

            switch (iconKind)
            {
                case RibbonIconKind.Georeference:
                    DrawGeoreference(context);
                    break;
                case RibbonIconKind.MeshInspector:
                    DrawMeshInspector(context);
                    break;
                case RibbonIconKind.Validation:
                    DrawValidation(context);
                    break;
                case RibbonIconKind.PlateauImport:
                    DrawPlateauImport(context);
                    break;
                case RibbonIconKind.Tiles3DExport:
                    DrawTilesExport(context);
                    break;
                case RibbonIconKind.CityGmlExport:
                    DrawCityGmlExport(context);
                    break;
                default:
                    DrawDefault(context);
                    break;
            }
        }

        Freeze(drawing);
        DrawingImage image = new DrawingImage(drawing);
        Freeze(image);
        return image;
    }

    private static void DrawBackground(DrawingContext context, Brush brush)
    {
        context.DrawRoundedRectangle(brush, null, new Rect(2d, 2d, 28d, 28d), 6d, 6d);
    }

    private static void DrawGeoreference(DrawingContext context)
    {
        Pen pen = CreatePen(Colors.White, 2.2d);
        context.DrawEllipse(null, pen, new Point(16d, 15d), 7d, 7d);
        context.DrawLine(pen, new Point(16d, 6.5d), new Point(16d, 9.5d));
        context.DrawLine(pen, new Point(16d, 20.5d), new Point(16d, 23.5d));
        context.DrawLine(pen, new Point(7.5d, 15d), new Point(10.5d, 15d));
        context.DrawLine(pen, new Point(21.5d, 15d), new Point(24.5d, 15d));
        context.DrawEllipse(CreateBrush(Colors.White), null, new Point(16d, 15d), 2.2d, 2.2d);
        context.DrawLine(pen, new Point(16d, 22d), new Point(16d, 27d));
    }

    private static void DrawMeshInspector(DrawingContext context)
    {
        Pen pen = CreatePen(Colors.White, 1.9d);
        Brush accent = CreateBrush(Color.FromArgb(90, 255, 255, 255));
        context.DrawRoundedRectangle(null, pen, new Rect(7d, 7d, 18d, 18d), 2d, 2d);
        context.DrawLine(pen, new Point(13d, 7d), new Point(13d, 25d));
        context.DrawLine(pen, new Point(19d, 7d), new Point(19d, 25d));
        context.DrawLine(pen, new Point(7d, 13d), new Point(25d, 13d));
        context.DrawLine(pen, new Point(7d, 19d), new Point(25d, 19d));
        context.DrawRectangle(accent, null, new Rect(13d, 13d, 6d, 6d));
    }

    private static void DrawValidation(DrawingContext context)
    {
        Pen pen = CreatePen(Colors.White, 2.4d);
        context.DrawEllipse(null, pen, new Point(16d, 16d), 8.5d, 8.5d);
        context.DrawLine(pen, new Point(11d, 16d), new Point(14.5d, 20d));
        context.DrawLine(pen, new Point(14.5d, 20d), new Point(22d, 12d));
    }

    private static void DrawPlateauImport(DrawingContext context)
    {
        Pen pen = CreatePen(Colors.White, 2.0d);
        Brush fill = CreateBrush(Color.FromArgb(70, 255, 255, 255));
        StreamGeometry folder = new StreamGeometry();
        using (StreamGeometryContext geometry = folder.Open())
        {
            geometry.BeginFigure(new Point(6d, 12d), false, true);
            geometry.LineTo(new Point(12d, 12d), true, false);
            geometry.LineTo(new Point(14.5d, 9d), true, false);
            geometry.LineTo(new Point(26d, 9d), true, false);
            geometry.LineTo(new Point(26d, 24d), true, false);
            geometry.LineTo(new Point(6d, 24d), true, false);
        }

        Freeze(folder);
        context.DrawGeometry(fill, pen, folder);
        context.DrawLine(pen, new Point(12d, 15d), new Point(20d, 15d));
        context.DrawLine(pen, new Point(12d, 18.5d), new Point(20d, 18.5d));
        context.DrawLine(pen, new Point(22d, 13d), new Point(22d, 21d));
        context.DrawLine(pen, new Point(19.5d, 18.5d), new Point(22d, 21d));
        context.DrawLine(pen, new Point(24.5d, 18.5d), new Point(22d, 21d));
    }

    private static void DrawTilesExport(DrawingContext context)
    {
        Pen pen = CreatePen(Colors.White, 1.9d);
        Brush fill = CreateBrush(Color.FromArgb(70, 255, 255, 255));
        StreamGeometry cube = new StreamGeometry();
        using (StreamGeometryContext geometry = cube.Open())
        {
            geometry.BeginFigure(new Point(9d, 12d), true, true);
            geometry.LineTo(new Point(17d, 8d), true, false);
            geometry.LineTo(new Point(24d, 12d), true, false);
            geometry.LineTo(new Point(16d, 16d), true, false);
        }
        Freeze(cube);
        context.DrawGeometry(fill, pen, cube);
        context.DrawLine(pen, new Point(9d, 12d), new Point(9d, 20d));
        context.DrawLine(pen, new Point(16d, 16d), new Point(16d, 24d));
        context.DrawLine(pen, new Point(24d, 12d), new Point(24d, 20d));
        context.DrawLine(pen, new Point(9d, 20d), new Point(16d, 24d));
        context.DrawLine(pen, new Point(16d, 24d), new Point(24d, 20d));
        context.DrawLine(pen, new Point(21d, 10d), new Point(26d, 5d));
        context.DrawLine(pen, new Point(23.5d, 5d), new Point(26d, 5d));
        context.DrawLine(pen, new Point(26d, 5d), new Point(26d, 7.5d));
    }

    private static void DrawCityGmlExport(DrawingContext context)
    {
        Pen pen = CreatePen(Colors.White, 1.8d);
        Brush fill = CreateBrush(Color.FromArgb(70, 255, 255, 255));
        context.DrawRoundedRectangle(fill, pen, new Rect(8d, 6d, 12d, 20d), 2d, 2d);
        context.DrawLine(pen, new Point(11d, 12d), new Point(17d, 12d));
        context.DrawLine(pen, new Point(11d, 16d), new Point(17d, 16d));
        context.DrawLine(pen, new Point(11d, 20d), new Point(17d, 20d));
        context.DrawLine(pen, new Point(22d, 11d), new Point(26d, 11d));
        context.DrawLine(pen, new Point(22d, 17d), new Point(26d, 17d));
        context.DrawLine(pen, new Point(24d, 11d), new Point(24d, 17d));
        context.DrawLine(pen, new Point(20d, 8d), new Point(27d, 8d));
        context.DrawLine(pen, new Point(20d, 20d), new Point(27d, 20d));
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
            case RibbonIconKind.Georeference:
                return CreateBrush(Color.FromRgb(32, 113, 122));
            case RibbonIconKind.MeshInspector:
                return CreateBrush(Color.FromRgb(143, 106, 62));
            case RibbonIconKind.Validation:
                return CreateBrush(Color.FromRgb(76, 133, 84));
            case RibbonIconKind.PlateauImport:
                return CreateBrush(Color.FromRgb(166, 92, 54));
            case RibbonIconKind.Tiles3DExport:
                return CreateBrush(Color.FromRgb(54, 106, 156));
            case RibbonIconKind.CityGmlExport:
                return CreateBrush(Color.FromRgb(111, 78, 136));
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
