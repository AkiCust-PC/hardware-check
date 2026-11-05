using AkikazuHardwareCheck.Core.Configuration;

namespace AkikazuHardwareCheck.Core.Tests;

public class BackgroundTests
{
    [Theory]
    [InlineData(1600, 900, 1000, 1000, -389, 0, 1778, 1000)]
    [InlineData(900, 1600, 1000, 1000, 0, -389, 1000, 1778)]
    [InlineData(1600, 900, 800, 450, 0, 0, 800, 450)]
    [InlineData(100, 100, 400, 200, 0, -100, 400, 400)]
    public void PictureCoversTheWindowWithItsShorterSideFitted(int pictureWidth, int pictureHeight, int windowWidth, int windowHeight, int x, int y, int width, int height)
    {
        Assert.Equal(new BackgroundFrame(x, y, width, height), BackgroundTransform.Cover(pictureWidth, pictureHeight, windowWidth, windowHeight));
    }

    [Theory]
    [InlineData(1600, 900, 1183, 777)]
    [InlineData(333, 1000, 1920, 1080)]
    [InlineData(1, 5000, 640, 480)]
    public void CoverKeepsTheShapeAndLeavesNoGap(int pictureWidth, int pictureHeight, int windowWidth, int windowHeight)
    {
        var frame = BackgroundTransform.Cover(pictureWidth, pictureHeight, windowWidth, windowHeight);

        Assert.True(frame.Width >= windowWidth && frame.Height >= windowHeight);
        Assert.True(frame.X <= 0 && frame.Y <= 0);
        Assert.True(frame.X + frame.Width >= windowWidth && frame.Y + frame.Height >= windowHeight);
        Assert.True(frame.Width == windowWidth || frame.Height == windowHeight || Math.Abs((frame.Width / (double)frame.Height) - (pictureWidth / (double)pictureHeight)) < 0.01);
    }

    [Fact]
    public void ViewMovesAndResizesFromTheCoveringPosition()
    {
        Assert.Equal(new BackgroundFrame(0, 0, 800, 450), BackgroundView.Default.Place(1600, 900, 800, 450));
        Assert.Equal(new BackgroundFrame(200, 112, 400, 225), new BackgroundView(0.5, 0, 0).Place(1600, 900, 800, 450));
        Assert.Equal(new BackgroundFrame(80, -45, 800, 450), new BackgroundView(1, 0.1, -0.1).Place(1600, 900, 800, 450));
    }

    [Fact]
    public void TurnsAndMirrorsCombineLikeRealOnes()
    {
        var start = new BackgroundTransform(0, false);

        Assert.Equal(new BackgroundTransform(90, false), start.RotateClockwise());
        Assert.Equal(start, start.RotateClockwise().RotateClockwise().RotateClockwise().RotateClockwise());
        Assert.Equal(start, start.MirrorHorizontally().MirrorHorizontally());
        Assert.Equal(start, start.MirrorVertically().MirrorVertically());

        // Mirroring both ways is a half turn.
        Assert.Equal(new BackgroundTransform(180, false), start.MirrorHorizontally().MirrorVertically());

        // After a mirror, a clockwise turn on screen runs the other way in the picture.
        Assert.Equal(new BackgroundTransform(270, true), start.MirrorHorizontally().RotateClockwise());

        // A turn, a mirror, the same turn, and the mirror again cancel out.
        Assert.Equal(start, start.RotateClockwise().MirrorHorizontally().RotateClockwise().MirrorHorizontally());
    }
}
