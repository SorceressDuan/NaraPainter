using Compositor.Imaging.Services;
using Compositor.Models.Adjustments;
using Compositor.Models.Documents;
using Compositor.Models.Layers;
using Compositor.Models.Pixels;
using Xunit;

namespace Compositor.Tests;

public class DocumentModelTests
{
    [Fact]
    public void LayersStackUpwardsAndTheTopOneIsActive()
    {
        var document = new CanvasDocument(4, 4);
        var bottom = document.Add(new Layer("bottom"));
        var top = document.Add(new Layer("top"));

        Assert.Equal(new[] { bottom, top }, document.Layers);
        Assert.Same(top, document.ActiveLayer);
        Assert.Equal(0, document.IndexOf(bottom));
        Assert.Equal(1, document.IndexOf(top));
    }

    [Fact]
    public void MoveClampsTheTargetIndex()
    {
        var document = new CanvasDocument(1, 1);
        var bottom = document.Add(new Layer("bottom"));
        var middle = document.Add(new Layer("middle"));
        var top = document.Add(new Layer("top"));

        Assert.True(document.Move(bottom, 99));
        Assert.Equal(new[] { middle, top, bottom }, document.Layers);

        Assert.True(document.Move(bottom, -5));
        Assert.Equal(new[] { bottom, middle, top }, document.Layers);

        Assert.False(document.Move(middle, 1));
        Assert.Equal(new[] { bottom, middle, top }, document.Layers);
    }

    [Fact]
    public void MoveUpAndMoveDownStopAtTheEnds()
    {
        var document = new CanvasDocument(1, 1);
        var bottom = document.Add(new Layer("bottom"));
        var top = document.Add(new Layer("top"));

        Assert.False(document.MoveUp(top));
        Assert.False(document.MoveDown(bottom));
        Assert.True(document.MoveUp(bottom));
        Assert.Equal(new[] { top, bottom }, document.Layers);
    }

    [Fact]
    public void MoveReportsLayersThatAreNotInTheDocument()
    {
        var document = new CanvasDocument(1, 1);
        document.Add(new Layer("only"));

        Assert.False(document.Move(new Layer("stray"), 0));
        Assert.False(document.CanMove(new Layer("stray")));
    }

    [Fact]
    public void RemoveReportsWhetherTheLayerWasThere()
    {
        var document = new CanvasDocument(1, 1);
        var layer = document.Add(new Layer("layer"));

        Assert.True(document.Remove(layer));
        Assert.Empty(document.Layers);
        Assert.Null(document.ActiveLayer);
        Assert.False(document.Remove(layer));
    }

    [Fact]
    public void FlattenPutsUpperLayersOverLowerOnes()
    {
        var document = new CanvasDocument(1, 1);
        document.Add(new Layer("bottom") { Pixels = TestPixels.Solid(1, 1, 0, 0, 255, 255) });
        document.Add(new Layer("top") { Pixels = TestPixels.Solid(1, 1, 255, 0, 0, 255) });

        Assert.Equal(new Rgba32(255, 0, 0, 255), document.Flatten()[0, 0]);
    }

    [Fact]
    public void FlattenAppliesBlendModesBottomUp()
    {
        var document = new CanvasDocument(1, 1);
        var lower = new Layer("lower") { Pixels = TestPixels.Solid(1, 1, 64, 64, 64, 255) };
        var upper = new Layer("upper") { Pixels = TestPixels.Solid(1, 1, 191, 191, 191, 255), BlendMode = BlendMode.Overlay };
        document.Add(lower);
        document.Add(upper);

        // Overlay is not symmetric: 191 over 64 lands on 96, and swapping the stack lands on 159.
        Assert.Equal(96, document.Flatten()[0, 0].R);

        Assert.True(document.Move(upper, 0));
        Assert.Equal(159, document.Flatten()[0, 0].R);
    }

    [Fact]
    public void HiddenLayersDoNotContribute()
    {
        var document = new CanvasDocument(1, 1);
        document.Add(new Layer("bottom") { Pixels = TestPixels.Solid(1, 1, 0, 0, 255, 255) });
        document.Add(new Layer("top") { Pixels = TestPixels.Solid(1, 1, 255, 0, 0, 255), IsVisible = false });

        Assert.Equal(new Rgba32(0, 0, 255, 255), document.Flatten()[0, 0]);
    }

    [Fact]
    public void LayersAtZeroOpacityDoNotContribute()
    {
        var document = new CanvasDocument(1, 1);
        document.Add(new Layer("bottom") { Pixels = TestPixels.Solid(1, 1, 0, 0, 255, 255) });
        document.Add(new Layer("top") { Pixels = TestPixels.Solid(1, 1, 255, 0, 0, 255), Opacity = 0 });

        Assert.Equal(new Rgba32(0, 0, 255, 255), document.Flatten()[0, 0]);
    }

    [Fact]
    public void MaskCoverageScalesTheTopLayerIn()
    {
        var document = new CanvasDocument(1, 1);
        document.Add(new Layer("bottom") { Pixels = TestPixels.Solid(1, 1, 255, 255, 255, 255) });
        var top = document.Add(new Layer("top") { Pixels = TestPixels.Solid(1, 1, 255, 0, 0, 255) });

        top.Mask = [0];
        Assert.Equal(new Rgba32(255, 255, 255, 255), document.Flatten()[0, 0]);

        top.Mask = [128];
        Assert.Equal(new Rgba32(255, 127, 127, 255), document.Flatten()[0, 0]);

        top.Mask = [255];
        Assert.Equal(new Rgba32(255, 0, 0, 255), document.Flatten()[0, 0]);
    }

    [Fact]
    public void MaskCoverageSurvivesPartialOpacity()
    {
        var document = new CanvasDocument(1, 1);
        document.Add(new Layer("bottom") { Pixels = TestPixels.Solid(1, 1, 255, 255, 255, 255) });
        var top = document.Add(new Layer("top") { Pixels = TestPixels.Solid(1, 1, 255, 0, 0, 255), Opacity = 0.5 });

        // A mask and the layer opacity multiply: a black mask still hides the layer completely, and a
        // white mask leaves the half-opacity blend in place.
        top.Mask = [0];
        Assert.Equal(new Rgba32(255, 255, 255, 255), document.Flatten()[0, 0]);

        top.Mask = [255];
        Assert.Equal(new Rgba32(255, 128, 128, 255), document.Flatten()[0, 0]);
    }

    [Fact]
    public void CoverageForIgnoresMasksOfTheWrongSize()
    {
        var layer = new Layer("masked") { Mask = [1, 2, 3] };

        Assert.Null(layer.CoverageFor(2, 1));
        Assert.Null(layer.CoverageFor(1, 1));

        layer.Mask = [4, 5];
        Assert.Equal(new byte[] { 4, 5 }, layer.CoverageFor(2, 1));

        layer.Opacity = 0.5;
        Assert.Equal(new byte[] { 4, 5 }, layer.CoverageFor(2, 1));

        layer.Mask = null;
        Assert.Null(layer.CoverageFor(2, 1));
    }

    [Fact]
    public void FlattenIgnoresAMaskThatDoesNotMatchTheDocument()
    {
        var document = new CanvasDocument(2, 1);
        document.Add(new Layer("bottom") { Pixels = TestPixels.Solid(2, 1, 0, 0, 255, 255) });
        document.Add(new Layer("top") { Pixels = TestPixels.Solid(2, 1, 255, 0, 0, 255), Mask = [0, 0, 0] });

        PixelBuffer flat = document.Flatten();

        Assert.Equal(new Rgba32(255, 0, 0, 255), flat[0, 0]);
        Assert.Equal(new Rgba32(255, 0, 0, 255), flat[1, 0]);
    }

    [Fact]
    public void AdjustmentLayersRunOverWhatIsBelowThem()
    {
        var document = new CanvasDocument(1, 1);
        document.Add(new Layer("base") { Pixels = TestPixels.Solid(1, 1, 100, 100, 100, 255) });

        var settings = new BrightnessContrastSettings(25);
        var adjustment = document.Add(new Layer("brightness") { Adjustment = settings });

        PixelBuffer? handedToTheRunner = null;
        PixelBuffer flat = document.Flatten((below, layer) =>
        {
            Assert.Same(adjustment, layer);
            handedToTheRunner = below.Clone();
            return new AdjustmentFilter().Apply(below, layer.Adjustment!);
        });

        Assert.Equal(new Rgba32(100, 100, 100, 255), handedToTheRunner![0, 0]);
        Assert.Equal(settings.Map(100), flat[0, 0].R);
    }

    [Fact]
    public void AdjustmentLayersWithoutARunnerAreSkipped()
    {
        var document = new CanvasDocument(1, 1);
        document.Add(new Layer("base") { Pixels = TestPixels.Solid(1, 1, 100, 100, 100, 255) });
        document.Add(new Layer("brightness") { Adjustment = new BrightnessContrastSettings(25) });

        Assert.Equal(new Rgba32(100, 100, 100, 255), document.Flatten()[0, 0]);
    }

    [Fact]
    public void FlatteningAnEmptyDocumentGivesATransparentCanvas()
    {
        var document = new CanvasDocument(2, 2);

        PixelBuffer flat = document.Flatten();

        Assert.Equal(new Rgba32(0, 0, 0, 0), flat[0, 0]);
        Assert.Equal(new Rgba32(0, 0, 0, 0), flat[1, 1]);
    }

    [Fact]
    public void AddBlankCreatesADocumentSizedLayer()
    {
        var document = new CanvasDocument(3, 2);
        Layer layer = document.AddBlank("Layer 1");

        Assert.Equal("Layer 1", layer.Name);
        Assert.NotNull(layer.Pixels);
        Assert.Equal(3, layer.Pixels!.Width);
        Assert.Equal(2, layer.Pixels.Height);
        Assert.True(document.IsDirty);
    }

    [Fact]
    public void FitStretchesSmallerLayersAcrossTheCanvas()
    {
        var document = new CanvasDocument(4, 4);
        var source = new PixelBuffer(2, 2, [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 0, 255]);

        PixelBuffer fitted = document.Fit(source);

        Assert.Equal(new Rgba32(255, 0, 0, 255), fitted[0, 0]);
        Assert.Equal(new Rgba32(0, 255, 0, 255), fitted[3, 0]);
        Assert.Equal(new Rgba32(0, 0, 255, 255), fitted[0, 3]);
        Assert.Equal(new Rgba32(255, 255, 0, 255), fitted[3, 3]);
    }

    [Fact]
    public void DocumentsRejectNonPositiveSizes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CanvasDocument(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CanvasDocument(10, -1));
    }
}
