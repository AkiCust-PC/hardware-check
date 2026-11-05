using System.Drawing.Drawing2D;
using AkikazuHardwareCheck.Core.Configuration;

namespace AkikazuHardwareCheck.App;

/// <summary>
/// Draws the background picture with Windows' own image library. A mirrored tile brush fills whatever the picture does
/// not cover, and because each tile mirrors its neighbor the pixels already match at every seam, so no edge blur is needed.
/// </summary>
internal static class BackgroundPainter
{
    public static Bitmap Turn(Bitmap source, BackgroundTransform transform)
    {
        var turned = (Bitmap)source.Clone();
        turned.RotateFlip((transform.RotationDegrees, transform.IsMirrored) switch
        {
            (90, false) => RotateFlipType.Rotate90FlipNone,
            (180, false) => RotateFlipType.Rotate180FlipNone,
            (270, false) => RotateFlipType.Rotate270FlipNone,
            (0, true) => RotateFlipType.RotateNoneFlipX,
            (90, true) => RotateFlipType.Rotate90FlipX,
            (180, true) => RotateFlipType.Rotate180FlipX,
            (270, true) => RotateFlipType.Rotate270FlipX,
            _ => RotateFlipType.RotateNoneFlipNone,
        });
        return turned;
    }

    public static void Paint(Graphics graphics, Bitmap turned, Size area, BackgroundView view, InterpolationMode quality)
    {
        var frame = view.Place(turned.Width, turned.Height, area.Width, area.Height);
        using var brush = new TextureBrush(turned, WrapMode.TileFlipXY);
        brush.TranslateTransform(frame.X, frame.Y);
        brush.ScaleTransform(frame.Width / (float)turned.Width, frame.Height / (float)turned.Height);
        graphics.InterpolationMode = quality;
        graphics.FillRectangle(brush, 0, 0, area.Width, area.Height);
    }
}
