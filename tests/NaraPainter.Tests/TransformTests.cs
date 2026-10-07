using NaraPainter.App;
using NaraPainter.App.Services;
using NaraPainter.App.ViewModels;
using NaraPainter.Imaging.Services;
using NaraPainter.Models.Pixels;
using NaraPainter.Models.Services;
using Xunit;

namespace NaraPainter.Tests;

/// <summary>
/// Crop, rotate and flip work on one layer's pixels; a resize replaces the canvas. Both leave one
/// undo step behind, and undoing the resize has to restore every layer, not just the one on screen.
/// </summary>
public class TransformTests
{
    [Fact]
    public void CropKeepsTheRequestedCorner()
    {
        PixelBuffer source = Pattern(8, 6);

        PixelBuffer cropped = Geometry.Crop(source, 2, 1, 3, 2);

        Assert.Equal(3, cropped.Width);
        Assert.Equal(2, cropped.Height);
        Assert.Equal(source[2, 1], cropped[0, 0]);
        Assert.Equal(source[4, 2], cropped[2, 1]);
    }

    [Fact]
    public void CropPastTheEdgeIsClampedRatherThanRefused()
    {
        PixelBuffer source = Pattern(8, 6);

        PixelBuffer cropped = Geometry.Crop(source, 6, 4, 100, 100);

        Assert.Equal(2, cropped.Width);
        Assert.Equal(2, cropped.Height);
        Assert.Equal(source[6, 4], cropped[0, 0]);
    }

    [Fact]
    public void RotatingClockwiseMovesTheFirstPixelToTheRightEdge()
    {
        PixelBuffer source = Pattern(4, 3);

        PixelBuffer rotated = Geometry.RotateQuarter(source, QuarterTurn.Clockwise);

        Assert.Equal(3, rotated.Width);
        Assert.Equal(4, rotated.Height);
        Assert.Equal(source[0, 0], rotated[rotated.Width - 1, 0]);
        Assert.Equal(source[3, 0], rotated[rotated.Width - 1, 3]);
    }

    [Fact]
    public void FourQuarterTurnsComeBackToTheStart()
    {
        PixelBuffer source = Pattern(5, 3);

        PixelBuffer turned = source;
        for (int i = 0; i < 4; i++) turned = Geometry.RotateQuarter(turned, QuarterTurn.Clockwise);

        Assert.Equal(source.Width, turned.Width);
        Assert.Equal(source.Height, turned.Height);
        Assert.Equal(source.Data, turned.Data);
    }

    [Fact]
    public void TheTwoDirectionsAreEachOthersInverse()
    {
        PixelBuffer source = Pattern(5, 3);

        PixelBuffer there = Geometry.RotateQuarter(source, QuarterTurn.Clockwise);
        PixelBuffer back = Geometry.RotateQuarter(there, QuarterTurn.CounterClockwise);

        Assert.Equal(source.Data, back.Data);
    }

    [Fact]
    public void FlippingTwiceRestoresTheImage()
    {
        PixelBuffer source = Pattern(6, 4);

        Assert.Equal(source.Data, Geometry.Flip(Geometry.Flip(source, FlipAxis.Horizontal), FlipAxis.Horizontal).Data);
        Assert.Equal(source.Data, Geometry.Flip(Geometry.Flip(source, FlipAxis.Vertical), FlipAxis.Vertical).Data);

        PixelBuffer horizontal = Geometry.Flip(source, FlipAxis.Horizontal);
        Assert.Equal(source[0, 0], horizontal[source.Width - 1, 0]);
    }

    [Fact]
    public void ResizeKeepsCornersAndSolidColours()
    {
        var solid = new PixelBuffer(4, 4);
        for (int i = 0; i < solid.Data.Length; i += 4)
        {
            solid.Data[i] = 10;
            solid.Data[i + 1] = 20;
            solid.Data[i + 2] = 30;
            solid.Data[i + 3] = 255;
        }

        PixelBuffer smaller = Geometry.Resize(solid, 2, 2);

        Assert.Equal(2, smaller.Width);
        foreach (int i in new[] { 0, 4, 8, 12 })
        {
            Assert.Equal(10, smaller.Data[i]);
            Assert.Equal(20, smaller.Data[i + 1]);
            Assert.Equal(30, smaller.Data[i + 2]);
        }
    }

    [Fact]
    public void ResizeOfTheSameSizeReturnsACopy()
    {
        PixelBuffer source = Pattern(4, 4);

        PixelBuffer same = Geometry.Resize(source, 4, 4);

        Assert.NotSame(source, same);
        Assert.Equal(source.Data, same.Data);
    }

    [Fact]
    public void FitInsidePreservesTheAspectRatio()
    {
        (int width, int height) = Geometry.FitInside(4000, 3000, 800, 800);

        Assert.Equal(800, width);
        Assert.Equal(600, height);
    }

    [Fact]
    public void CroppingALayerIsOneUndoStep()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenPattern(8, 6);
        PixelBuffer before = layer.Source!;

        document.Transform.Crop(0, 0, 4, 3);

        Assert.Equal(4, layer.Source!.Width);
        Assert.Equal(Localization.Get("Undo_CropLayer"), document.History.UndoName);

        document.Undo();

        Assert.Equal(before.Width, layer.Source!.Width);
        Assert.Equal(before.Data, layer.Source!.Data);
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void RotatingALayerIsUndoableAndRedoable()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenPattern(8, 6);
        byte[] before = (byte[])layer.Source!.Data.Clone();

        document.Transform.Rotate(QuarterTurn.Clockwise);
        Assert.Equal(6, layer.Source!.Width);
        Assert.Equal(8, layer.Source.Height);

        document.Undo();
        Assert.Equal(before, layer.Source!.Data);

        document.Redo();
        Assert.Equal(6, layer.Source!.Width);
    }

    [Fact]
    public void FlippingALayerIsUndoable()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenPattern(8, 6);
        byte[] before = (byte[])layer.Source!.Data.Clone();

        document.Transform.Flip(FlipAxis.Horizontal);

        Assert.NotEqual(before, layer.Source!.Data);
        document.Undo();
        Assert.Equal(before, layer.Source!.Data);
    }

    [Fact]
    public void ResizingTheCanvasIsOneUndoStepAndRestoresEveryLayer()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenPattern(8, 6);
        LayerViewModel second = document.AddLayer();
        second.Opacity = 50;

        Assert.Equal(8, document.Document.Width);

        document.Transform.ResizeCanvas(16, 12);

        Assert.Equal(16, document.Document.Width);
        Assert.Equal(12, document.Document.Height);
        Assert.Equal("16 × 12", document.SizeLabel);
        Assert.Equal(Localization.Get("Undo_ResizeCanvas"), document.History.UndoName);

        document.Undo();

        Assert.Equal(8, document.Document.Width);
        Assert.Equal(6, document.Document.Height);
        Assert.Equal(2, document.Layers.Count);

        document.Redo();
        Assert.Equal(16, document.Document.Width);
    }

    [Fact]
    public void ExportingAtASmallerSizeWritesThatSizeAndLeavesTheDocumentAlone()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenPattern(64, 48);
        var codec = new ImageCodec();
        string path = Path.Combine(ScratchDirectory(), "export-half.png");

        document.Export(path, 32, 24);

        PixelBuffer written = codec.Read(path);
        Assert.Equal(32, written.Width);
        Assert.Equal(24, written.Height);

        // The canvas is untouched: exporting at another resolution is not an edit.
        Assert.Equal(64, document.Document.Width);
        Assert.Equal(48, document.Document.Height);

        File.Delete(path);
    }

    [Fact]
    public void ExportingWithNoSizeWritesTheCanvasSize()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenPattern(40, 30);
        var codec = new ImageCodec();
        string path = Path.Combine(ScratchDirectory(), "export-full.png");

        document.Export(path);

        PixelBuffer written = codec.Read(path);
        Assert.Equal(40, written.Width);
        Assert.Equal(30, written.Height);

        File.Delete(path);
    }
    private static PixelBuffer Pattern(int width, int height)
    {
        var buffer = new PixelBuffer(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = buffer.Offset(x, y);
                buffer.Data[i] = (byte)((x * 37) % 256);
                buffer.Data[i + 1] = (byte)((y * 53) % 256);
                buffer.Data[i + 2] = (byte)(((x + y) * 11) % 256);
                buffer.Data[i + 3] = 255;
            }
        }

        return buffer;
    }

    private static (DocumentViewModel Document, LayerViewModel Layer) OpenPattern(int width, int height)
    {
        var codec = new ImageCodec();
        string path = Path.Combine(ScratchDirectory(), $"transform-{width}x{height}.png");
        codec.Write(Pattern(width, height), path, new ImageSaveOptions(ImageFileFormat.Png));

        try
        {
            var document = new DocumentViewModel(new ImageImporter(codec), codec, new AdjustmentFilter(), new SelectionMaskBuilder());
            document.Open(path);
            document.SelectedLayer = document.Layers[0];
            return (document, document.SelectedLayer!);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string ScratchDirectory()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "transform-scratch");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
