using NaraPainter.App;
using NaraPainter.App.Services;
using NaraPainter.App.ViewModels;
using NaraPainter.Compositing.Rendering;
using NaraPainter.Imaging.Services;
using NaraPainter.Models.Documents;
using NaraPainter.Models.Pixels;
using NaraPainter.Models.Services;
using NaraPainter.Models.Text;
using Xunit;

namespace NaraPainter.Tests;

/// <summary>
/// The text tool: a string becomes a layer of ordinary pixels, placed where it was asked for, and the
/// whole thing is one undo step.
/// </summary>
/// <remarks>
/// These run against the real Win2D rasterizer rather than a stand-in, because the piece that is easy
/// to get wrong is the platform one: the layout box, the premultiplied readback, and the choice of a
/// canvas-sized buffer so nothing resamples the glyphs.
/// </remarks>
[Collection(LocalizedState.Name)]
public class TextLayerTests
{
    private const int Width = 320;
    private const int Height = 200;

    [Fact]
    public void ATextLayerIsCanvasSizedSoNothingStretchesIt()
    {
        // A layer smaller than the canvas is stretched across it on the way to the screen and the
        // export, which would resample the glyphs. Canvas-sized means Fit leaves it alone.
        var document = new CanvasDocument(Width, Height);

        PixelBuffer? pixels = TextRasterizer.Render(TextStyle.Default("Hello"), Width, Height, 10, 10);

        Assert.NotNull(pixels);
        Assert.Equal(Width, pixels.Width);
        Assert.Equal(Height, pixels.Height);
        Assert.True(document.Fit(pixels).SameSizeAs(pixels), "the canvas stretched the text layer");
    }

    [Fact]
    public void TheRunStartsWhereItWasPlaced()
    {
        PixelBuffer? atLeft = TextRasterizer.Render(TextStyle.Default("Hello"), Width, Height, 8, 20);
        PixelBuffer? atRight = TextRasterizer.Render(TextStyle.Default("Hello"), Width, Height, 120, 20);

        Assert.NotNull(atLeft);
        Assert.NotNull(atRight);

        (int leftFirst, int leftLast) = LitColumns(atLeft);
        (int rightFirst, int rightLast) = LitColumns(atRight);

        // Moving the anchor by 112 pixels moves the ink by the same amount: no scaling, no centring.
        Assert.Equal(112, rightFirst - leftFirst);
        Assert.Equal(112, rightLast - leftLast);

        // And it starts just inside the anchor, allowing for the glyph's own left side bearing.
        Assert.InRange(leftFirst, 8, 24);
    }

    [Fact]
    public void TheInkIsTheColourThatWasAskedFor()
    {
        var style = TextStyle.Default("Ink") with { Colour = new Rgba32(200, 40, 60, 255) };

        PixelBuffer? pixels = TextRasterizer.Render(style, Width, Height, 10, 10);

        Assert.NotNull(pixels);
        int sampled = 0;
        string? stray = null;
        for (int y = 0; y < pixels.Height && stray is null; y++)
        {
            for (int x = 0; x < pixels.Width; x++)
            {
                Rgba32 pixel = pixels[x, y];
                if (pixel.A < 250) continue;

                sampled++;
                // Anti-aliased edges blend toward transparent, so a fully opaque pixel may still be a
                // partial colour. The ink is right while no channel has gone past what was asked for.
                if (pixel.R <= 200 && pixel.G <= 40 && pixel.B <= 60) continue;
                stray = $"({x}, {y}) came back as {pixel}, past the requested Rgba32 {{ R = 200, G = 40, B = 60, A = 255 }}";
                break;
            }
        }

        Assert.True(stray is null, $"solid ink {stray}");
        Assert.True(sampled > 100, $"only {sampled} solid pixels were drawn");
        Assert.Contains(pixels.Data.Where((_, index) => index % 4 == 0), channel => channel == 200);
    }

    [Fact]
    public void BlankInputDrawsNothing()
    {
        Assert.Null(TextRasterizer.Render(TextStyle.Default("   "), Width, Height, 0, 0));
        Assert.Null(TextRasterizer.Render(new TextStyle(string.Empty), Width, Height, 0, 0));
    }

    [Fact]
    public void AChineseRunIsOneLineRatherThanAStackOfCharacters()
    {
        // Asking the layout for zero width makes DirectWrite break after every character, which stacks
        // a CJK run one glyph per line. The regression shows up as a tall, narrow box.
        PixelBuffer? pixels = TextRasterizer.Render(TextStyle.Default("那菈绘梦污点修复"), Width, Height, 10, 20);

        Assert.NotNull(pixels);
        (int first, int last) = LitColumns(pixels);
        (int top, int bottom) = LitRows(pixels);

        Assert.True(last - first > top - bottom, $"the run is taller than it is wide: {last - first} x {bottom - top}");
        Assert.InRange(bottom - top, 20, 120);
    }

    [Fact]
    public void AddingTextIsOneUndoStepThatRestoresTheLayerCount()
    {
        (DocumentViewModel document, _) = OpenSurface();
        int before = document.Layers.Count;
        Assert.False(document.CanUndo);

        LayerViewModel? added = document.AddTextLayer(TextStyle.Default("Nara"), 40, 30);

        Assert.NotNull(added);
        Assert.Equal(before + 1, document.Layers.Count);
        Assert.Equal(Localization.Get("Undo_AddTextLayer"), document.History.UndoName);
        Assert.Same(added, document.SelectedLayer);
        Assert.NotNull(added.Source);
        Assert.Equal(Width, added.Source.Width);

        document.Undo();
        Assert.Equal(before, document.Layers.Count);
        Assert.False(document.CanUndo);

        document.Redo();
        Assert.Equal(before + 1, document.Layers.Count);
        Assert.False(document.CanRedo);
    }

    [Fact]
    public void ABlankStringAddsNoLayerAndNoHistoryEntry()
    {
        (DocumentViewModel document, _) = OpenSurface();
        int before = document.Layers.Count;

        Assert.Null(document.AddTextLayer(TextStyle.Default("   "), 10, 10));
        Assert.Equal(before, document.Layers.Count);
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void TheTextLayerIsNamedAfterWhatWasTyped()
    {
        (DocumentViewModel document, _) = OpenSurface();

        LayerViewModel? added = document.AddTextLayer(TextStyle.Default("那菈绘梦"), 20, 20);

        Assert.NotNull(added);
        Assert.Equal("那菈绘梦", added.Name);
    }

    [Fact]
    public void LongTextIsTrimmedForTheLayerName()
    {
        (DocumentViewModel document, _) = OpenSurface();

        LayerViewModel? added = document.AddTextLayer(TextStyle.Default(new string('x', 60)), 20, 20);

        Assert.NotNull(added);
        Assert.Equal(25, added.Name.Length);
        Assert.EndsWith("…", added.Name, StringComparison.Ordinal);
    }

    private static (int First, int Last) LitColumns(PixelBuffer buffer)
    {
        int first = int.MaxValue, last = -1;
        for (int y = 0; y < buffer.Height; y++)
        {
            for (int x = 0; x < buffer.Width; x++)
            {
                if (buffer[x, y].A == 0) continue;
                if (x < first) first = x;
                if (x > last) last = x;
            }
        }

        return last < 0 ? (0, 0) : (first, last);
    }

    private static (int Top, int Bottom) LitRows(PixelBuffer buffer)
    {
        int top = int.MaxValue, bottom = -1;
        for (int y = 0; y < buffer.Height; y++)
        {
            for (int x = 0; x < buffer.Width; x++)
            {
                if (buffer[x, y].A == 0) continue;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }

        return bottom < 0 ? (0, 0) : (top, bottom);
    }

    private static (DocumentViewModel Document, LayerViewModel Layer) OpenSurface()
    {
        var codec = new ImageCodec();
        string path = Path.Combine(ScratchDirectory(), "text-layer.png");
        codec.Write(new PixelBuffer(Width, Height), path, new ImageSaveOptions(ImageFileFormat.Png));

        try
        {
            var document = new DocumentViewModel(new ImageImporter(codec), codec, new AdjustmentFilter(), new SelectionMaskBuilder());
            document.Open(path);
            document.SelectedLayer = document.Layers[0];
            document.History.Clear();
            return (document, document.SelectedLayer!);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string ScratchDirectory()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "text-layer-scratch");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
