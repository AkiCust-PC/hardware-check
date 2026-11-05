namespace AkikazuHardwareCheck.Core.Configuration;

/// <summary>Where a picture lands in the window, in window pixels. The position is negative on the side being cropped.</summary>
public readonly record struct BackgroundFrame(int X, int Y, int Width, int Height);

/// <summary>Where the picture sits after the staff member moved and resized it. Zoom 1 just covers the window.</summary>
public readonly record struct BackgroundView(double Zoom, double PanX, double PanY)
{
    public const double MIN_ZOOM = 0.2;
    public const double MAX_ZOOM = 5;

    public static BackgroundView Default { get; } = new(1, 0, 0);

    public BackgroundFrame Place(int pictureWidth, int pictureHeight, int windowWidth, int windowHeight)
    {
        var cover = BackgroundTransform.Cover(pictureWidth, pictureHeight, windowWidth, windowHeight);
        var width = Math.Max(1, (int)Math.Round(cover.Width * Zoom));
        var height = Math.Max(1, (int)Math.Round(cover.Height * Zoom));
        return new BackgroundFrame(
            ((windowWidth - width) / 2) + (int)Math.Round(PanX * windowWidth),
            ((windowHeight - height) / 2) + (int)Math.Round(PanY * windowHeight),
            width,
            height);
    }
}

/// <summary>
/// How the background picture is turned: rotated clockwise first, then mirrored left to right, which is the order
/// Windows' image rotation applies them in. Any mix of turns and mirrors reduces to these two values.
/// </summary>
public readonly record struct BackgroundTransform(int RotationDegrees, bool IsMirrored)
{
    private const int QUARTER_TURN = 90;
    private const int HALF_TURN = 180;
    private const int FULL_TURN = 360;

    /// <summary>Mirroring after a turn is the same as turning the other way before the mirror.</summary>
    public BackgroundTransform RotateClockwise() =>
        this with { RotationDegrees = Normalize(RotationDegrees + (IsMirrored ? -QUARTER_TURN : QUARTER_TURN)) };

    public BackgroundTransform MirrorHorizontally() => this with { IsMirrored = !IsMirrored };

    /// <summary>A top-to-bottom mirror is a left-to-right mirror plus a half turn.</summary>
    public BackgroundTransform MirrorVertically() => new(Normalize(RotationDegrees + HALF_TURN), !IsMirrored);

    /// <summary>
    /// Scales the picture without changing its shape so its shorter side, relative to the window, matches the window.
    /// The window is then fully covered, and the overflow of the other side is cropped equally on both ends.
    /// </summary>
    public static BackgroundFrame Cover(int pictureWidth, int pictureHeight, int windowWidth, int windowHeight)
    {
        var scale = Math.Max(windowWidth / (double)pictureWidth, windowHeight / (double)pictureHeight);
        var width = (int)Math.Ceiling(pictureWidth * scale);
        var height = (int)Math.Ceiling(pictureHeight * scale);
        return new BackgroundFrame((windowWidth - width) / 2, (windowHeight - height) / 2, width, height);
    }

    private static int Normalize(int degrees) => ((degrees % FULL_TURN) + FULL_TURN) % FULL_TURN;
}
