using NaraPainter.App;
using NaraPainter.App.Services;
using NaraPainter.App.ViewModels;
using NaraPainter.Imaging.Services;
using NaraPainter.Models.Pixels;
using NaraPainter.Models.Services;
using Xunit;

namespace NaraPainter.Tests;

/// <summary>
/// The eyedropper reads the flattened canvas, and the two filters edit one layer's pixels as a single
/// undoable step.
/// </summary>
[Collection(LocalizedState.Name)]
public class ColorPickerAndFilterTests
{
    [Fact]
    public void PickingReadsTheColourThatIsOnTheCanvas()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenPattern();

        document.ColorPicker.Pick(1, 1);

        Assert.True(document.ColorPicker.HasSample);
        Assert.Equal(layer.Source![1, 1].R, document.ColorPicker.Sample.R);
        Assert.Equal(layer.Source[1, 1].G, document.ColorPicker.Sample.G);
        Assert.Equal(layer.Source[1, 1].B, document.ColorPicker.Sample.B);
    }

    [Fact]
    public void TheSampleIsShownAsHexAndAsChannels()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenPattern();
        Rgba32 expected = layer.Source![1, 1];

        document.ColorPicker.Pick(1, 1);

        Assert.Equal($"#{expected.R:X2}{expected.G:X2}{expected.B:X2}", document.ColorPicker.Hex);
        Assert.Contains($"R {expected.R}", document.ColorPicker.Channels, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingIsReportedBeforeTheFirstPick()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenPattern();

        Assert.False(document.ColorPicker.HasSample);
        Assert.Equal(string.Empty, document.ColorPicker.Hex);
    }

    [Fact]
    public void APickOutsideTheCanvasIsIgnored()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenPattern();

        document.ColorPicker.Pick(9999, 9999);

        Assert.False(document.ColorPicker.HasSample);
    }

    [Fact]
    public void TurningThePickerOnTurnsTheMaskBrushOff()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenPattern();
        document.MaskBrush.IsActive = true;

        document.ColorPicker.IsActive = true;

        Assert.False(document.MaskBrush.IsActive);
    }

    [Fact]
    public void BlurringSoftensTheEdgeBetweenTwoColours()
    {
        PixelBuffer source = Halves(16, 8);

        PixelBuffer blurred = Filters.GaussianBlur(source, 2);

        // The edge has to end up between the two flat values rather than on one of them: that is what
        // softening means, and it does not depend on which way a particular kernel leans.
        byte dark = source[1, 4].R;
        byte bright = source[12, 4].R;
        byte after = blurred[7, 4].R;
        Assert.True(after > dark && after < bright, $"expected a value strictly between {dark} and {bright}, got {after}");

        // The middle of each flat half is untouched, which is the other half of the same statement.
        Assert.Equal(dark, blurred[1, 4].R);
        Assert.Equal(bright, blurred[12, 4].R);

        // Coverage is not a colour, so a filter must not touch it.
        for (int p = 0; p < blurred.PixelCount; p++) Assert.Equal(source.Data[(p * 4) + 3], blurred.Data[(p * 4) + 3]);
    }

    [Fact]
    public void BlurringWithNoRadiusChangesNothing()
    {
        PixelBuffer source = Halves(16, 8);

        PixelBuffer same = Filters.GaussianBlur(source, 0);

        Assert.Equal(source.Data, same.Data);
        Assert.NotSame(source, same);
    }

    [Fact]
    public void SharpeningIncreasesTheContrastAcrossAnEdge()
    {
        PixelBuffer source = Halves(16, 8);
        PixelBuffer blurred = Filters.GaussianBlur(source, 2);

        PixelBuffer sharpened = Filters.UnsharpMask(source, 2, 1.0);

        int before = Spread(blurred);
        int after = Spread(sharpened);
        Assert.True(after > before, $"expected a wider spread across the edge: {before} -> {after}");
    }

    [Fact]
    public void BlurringALayerIsOneUndoStep()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenPattern();
        byte[] before = (byte[])layer.Source!.Data.Clone();

        document.Transform.GaussianBlur(3);

        Assert.NotEqual(before, layer.Source!.Data);
        Assert.Equal(Localization.Get("Undo_GaussianBlur"), document.History.UndoName);

        document.Undo();

        Assert.Equal(before, layer.Source!.Data);
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void SharpeningALayerIsUndoableAndRedoable()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenPattern();
        byte[] before = (byte[])layer.Source!.Data.Clone();

        document.Transform.Sharpen(2, 1.5);

        byte[] sharpened = (byte[])layer.Source!.Data.Clone();
        Assert.NotEqual(before, sharpened);

        document.Undo();
        Assert.Equal(before, layer.Source!.Data);

        document.Redo();
        Assert.Equal(sharpened, layer.Source!.Data);
    }

    /// <summary>The first half dark, the second half bright, so there is one edge to measure.</summary>
    private static PixelBuffer Halves(int width, int height)
    {
        var buffer = new PixelBuffer(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte value = x < width / 2 ? (byte)40 : (byte)220;
                int i = buffer.Offset(x, y);
                buffer.Data[i] = value;
                buffer.Data[i + 1] = value;
                buffer.Data[i + 2] = value;
                buffer.Data[i + 3] = 200;
            }
        }

        return buffer;
    }

    private static int Spread(PixelBuffer buffer)
    {
        int min = 255;
        int max = 0;
        for (int y = 0; y < buffer.Height; y++)
        {
            for (int x = 0; x < buffer.Width; x++)
            {
                int value = buffer[x, y].R;
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }
        }

        return max - min;
    }

    private static (DocumentViewModel Document, LayerViewModel Layer) OpenPattern()
    {
        var codec = new ImageCodec();
        string path = Path.Combine(ScratchDirectory(), "pick-source.png");
        codec.Write(Halves(16, 8), path, new ImageSaveOptions(ImageFileFormat.Png));

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
        string directory = Path.Combine(AppContext.BaseDirectory, "pick-scratch");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
