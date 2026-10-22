using Avalonia;
using Avalonia.Layout;
using SharpAmp.Avalonia.Controls;

namespace SharpAmp.Tests;

public sealed class PedalboardCanvasTests
{
    [Fact]
    public void EmptyCanvasFillsTheViewportAndShrinksAfterLeavingFullscreen()
    {
        var canvas = new PedalboardCanvas();
        ArrangeViewport(canvas, new Size(680, 420));
        Assert.Equal(new Size(680, 420), canvas.Bounds.Size);

        ArrangeViewport(canvas, new Size(1720, 1000));
        Assert.Equal(new Size(1720, 1000), canvas.Bounds.Size);

        canvas.UpdateViewport(new Size(680, 420), new Vector(200, 100));
        ArrangeViewport(canvas, new Size(680, 420));
        Assert.Equal(new Size(680, 420), canvas.Bounds.Size);
        Assert.Equal(default(Size), canvas.DesiredSize);
        Assert.True(double.IsNaN(canvas.Width));
        Assert.True(double.IsNaN(canvas.Height));
        Assert.Equal(HorizontalAlignment.Stretch, canvas.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Stretch, canvas.VerticalAlignment);
    }

    [Fact]
    public void DropPositionUsesCanvasCoordinatesAndHonorsTheGridPreference()
    {
        var canvas = new PedalboardCanvas();
        canvas.UpdateViewport(new Size(680, 420), new Vector(1200, 800));

        Assert.Equal(new Point(1584, 976), canvas.GetDropPosition(new Point(1704, 1049)));
        Assert.Equal(new Point(32, 64), canvas.GetDropPosition(default));
        Assert.Equal(new Point(100000, 100000), canvas.GetDropPosition(new Point(200000, 200000)));

        canvas.SnapToGrid = false;
        Assert.Equal(new Point(1586, 977), canvas.GetDropPosition(new Point(1704, 1049)));
        Assert.Equal(new Point(24, 60), canvas.GetDropPosition(default));
    }

    private static void ArrangeViewport(PedalboardCanvas canvas, Size viewport)
    {
        canvas.UpdateViewport(viewport, default);
        canvas.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        canvas.Arrange(new Rect(viewport));
    }
}
