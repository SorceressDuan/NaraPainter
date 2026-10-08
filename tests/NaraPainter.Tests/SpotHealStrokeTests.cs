using NaraPainter.App;
using NaraPainter.App.Services;
using NaraPainter.App.ViewModels;
using NaraPainter.Imaging.Services;
using NaraPainter.Models.Pixels;
using NaraPainter.Models.Services;
using Xunit;

namespace NaraPainter.Tests;

/// <summary>
/// Spot healing through the tool the window drives: a drag paints coverage, and letting go heals it as
/// exactly one history entry.
/// </summary>
/// <remarks>
/// The fixture is upstream's — <c>legacy/CompositorTests/SpotHealingTests.swift</c> — carried through
/// the real <see cref="DocumentViewModel"/> so the undo step and the status text are covered too.
/// </remarks>
[Collection(LocalizedState.Name)]
public class SpotHealStrokeTests
{
    [Fact]
    public void AHealingStrokeIsASingleUndoStep()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenSurface();
        byte[] before = (byte[])layer.Source!.Data.Clone();

        Drag(document, (60, 40), (61, 40));

        byte[] after = (byte[])layer.Source!.Data.Clone();
        Assert.NotEqual(before, after);
        Assert.True(layer.Source.Data.AsSpan().SequenceEqual(layer.Model.Pixels!.Data), "the layer did not take the healed pixels");

        Assert.Equal(Localization.Get("Undo_SpotHealing"), document.History.UndoName);

        document.Undo();
        Assert.Equal(before, layer.Source!.Data);
        Assert.False(document.CanUndo);

        document.Redo();
        Assert.Equal(after, layer.Source!.Data);
        Assert.False(document.CanRedo);
    }

    [Fact]
    public void TheBlemishGoesAndTheRestOfTheLayerDoesNot()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenSurface();
        byte[] before = (byte[])layer.Source!.Data.Clone();

        Drag(document, (60, 40), (61, 40));

        // Upstream's three witnesses inside the brush: the red has to be gone and the level has to
        // land back in the stripe's own range.
        foreach ((int x, int y) in new[] { (60, 40), (56, 36), (64, 44) })
        {
            Rgba32 healed = layer.Source![x, y];
            Assert.True(healed.R - healed.G < 30, $"({x}, {y}) is still red at {healed}");
            Assert.InRange(healed.G, 80, 130);
            Assert.Equal(255, healed.A);
        }

        // And upstream's five witnesses outside it, byte for byte.
        foreach ((int x, int y) in new[] { (10, 10), (90, 40), (30, 40), (60, 10), (60, 70) })
        {
            int i = layer.Source!.Offset(x, y);
            Assert.Equal(before[i], layer.Source.Data[i]);
            Assert.Equal(before[i + 1], layer.Source.Data[i + 1]);
            Assert.Equal(before[i + 2], layer.Source.Data[i + 2]);
            Assert.Equal(before[i + 3], layer.Source.Data[i + 3]);
        }
    }

    [Fact]
    public void AStrokeThatPaintsNothingLeavesNoHistoryEntry()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenSurface();
        byte[] before = (byte[])layer.Source!.Data.Clone();

        // No Begin, so there is no layer or coverage to heal.
        document.SpotHeal.EndStroke();

        Assert.False(document.CanUndo);
        Assert.Equal(before, layer.Source!.Data);
    }

    [Fact]
    public void CancellingAStrokeWritesNothing()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenSurface();
        byte[] before = (byte[])layer.Source!.Data.Clone();

        document.SpotHeal.BeginStroke(60, 40);
        document.SpotHeal.ContinueStroke(61, 40);
        document.SpotHeal.CancelStroke();
        document.SpotHeal.EndStroke();

        Assert.False(document.CanUndo);
        Assert.Equal(before, layer.Source!.Data);
    }

    [Fact]
    public void TurningTheToolOffDoesNotCommitTheStroke()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenSurface();
        byte[] before = (byte[])layer.Source!.Data.Clone();

        document.SpotHeal.IsActive = true;
        document.SpotHeal.BeginStroke(60, 40);
        document.SpotHeal.IsActive = false;

        Assert.False(document.CanUndo);
        Assert.Equal(before, layer.Source!.Data);
    }

    [Fact]
    public void TurningTheToolOnTurnsTheOtherPointerToolsOff()
    {
        (DocumentViewModel document, _) = OpenSurface();

        document.MaskBrush.IsActive = true;
        document.ColorPicker.IsActive = true;
        document.SpotHeal.IsActive = true;

        Assert.True(document.SpotHeal.IsActive);
        Assert.False(document.MaskBrush.IsActive);
        Assert.False(document.ColorPicker.IsActive);
    }

    [Fact]
    public void AnAdjustmentLayerCannotBeHealed()
    {
        (DocumentViewModel document, _) = OpenSurface();
        document.AddAdjustmentLayer(AdjustmentKind.BrightnessContrast);

        // Adding the layer is itself a step; the point here is that healing adds none.
        document.History.Clear();

        Assert.False(document.SpotHeal.CanHeal);

        document.SpotHeal.BeginStroke(60, 40);
        document.SpotHeal.EndStroke();

        Assert.False(document.CanUndo);
        Assert.Equal(Localization.Get("SpotHeal_SelectPixelLayer"), document.Status);
    }

    [Fact]
    public void AllThreeModesHealAndNameThemselves()
    {
        foreach ((int index, string key) in new[] { (0, "SpotHeal_ModeContentAware"), (1, "SpotHeal_ModeCreateTexture"), (2, "SpotHeal_ModeProximityMatch") })
        {
            (DocumentViewModel document, LayerViewModel layer) = OpenSurface();
            document.SpotHeal.ModeIndex = index;

            Drag(document, (60, 40), (61, 40));

            Assert.True(document.CanUndo, $"mode {index} recorded no step; status was '{document.Status}'");
            Assert.Equal(Localization.Get(key), document.SpotHeal.ModeNames[index]);
            Rgba32 healed = layer.Source![60, 40];
            Assert.True(healed.R - healed.G < 30, $"mode {index}: (60, 40) is still red at {healed}");
        }
    }

    [Fact]
    public void AnOutOfRangeModeIsRefused()
    {
        (DocumentViewModel document, _) = OpenSurface();

        document.SpotHeal.ModeIndex = 7;
        Assert.Equal(0, document.SpotHeal.ModeIndex);

        document.SpotHeal.ModeIndex = -1;
        Assert.Equal(0, document.SpotHeal.ModeIndex);
    }

    [Fact]
    public void AStrokeOverALayerSmallerThanTheCanvasIsSkippedRatherThanSmeared()
    {
        // Crop works in the layer's own pixels and leaves the canvas alone, so after one the two
        // coordinate spaces disagree. A document-space drag no longer says where on the layer it
        // landed, and the honest answer is to do nothing rather than pile the stroke on the edge.
        (DocumentViewModel document, LayerViewModel layer) = OpenSurface();

        document.Transform.Crop(40, 30, 32, 24);
        document.History.Clear();

        Assert.Equal(32, layer.Source!.Width);
        byte[] before = (byte[])layer.Source.Data.Clone();

        Drag(document, (8, 8), (9, 9));

        Assert.False(document.CanUndo);
        Assert.Equal(before, layer.Source!.Data);
        Assert.Equal(Localization.Get("SpotHeal_Nothing"), document.Status);
    }

    private static void Drag(DocumentViewModel document, (double X, double Y) from, (double X, double Y) to)
    {
        document.SpotHeal.IsActive = true;
        document.SpotHeal.Size = 24;
        document.SpotHeal.Hardness = 1;
        document.SpotHeal.Opacity = 1;
        document.SpotHeal.BeginStroke(from.X, from.Y);
        document.SpotHeal.ContinueStroke(to.X, to.Y);
        document.SpotHeal.EndStroke();
    }

    /// <summary>Upstream's fixture: 2px gray stripes at 100/112 with a red blemish over the middle.</summary>
    private static PixelBuffer Surface(int width, int height)
    {
        var buffer = new PixelBuffer(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool red = x is >= 55 and < 65 && y is >= 35 and < 45;
                byte gray = x % 4 < 2 ? (byte)100 : (byte)112;
                int i = buffer.Offset(x, y);
                buffer.Data[i] = red ? (byte)230 : gray;
                buffer.Data[i + 1] = red ? (byte)20 : gray;
                buffer.Data[i + 2] = red ? (byte)20 : gray;
                buffer.Data[i + 3] = 255;
            }
        }

        return buffer;
    }

    private static (DocumentViewModel Document, LayerViewModel Layer) OpenSurface()
    {
        var codec = new ImageCodec();
        string path = Path.Combine(ScratchDirectory(), "spot-heal.png");
        codec.Write(Surface(120, 80), path, new ImageSaveOptions(ImageFileFormat.Png));

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
        string directory = Path.Combine(AppContext.BaseDirectory, "spot-heal-scratch");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
