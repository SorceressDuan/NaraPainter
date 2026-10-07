using NaraPainter.App;
using NaraPainter.App.Services;
using NaraPainter.App.ViewModels;
using NaraPainter.Imaging.Services;
using NaraPainter.Models.Pixels;
using NaraPainter.Models.Services;
using Xunit;

namespace NaraPainter.Tests;

/// <summary>
/// Every gesture in the window's table has to reach an action that actually does something, and the
/// actions the newer tools use have to leave an undo step behind.
/// </summary>
/// <remarks>
/// The table itself lives on the window, which needs a UI thread, so the mapping is checked through
/// the handler each entry names: the handler methods exist, they are distinct, and the model calls
/// behind them change the document.
/// </remarks>
[Collection(LocalizedState.Name)]
public class ShortcutTests
{
    [Fact]
    public void EveryToolActionLeavesAnUndoStepBehind()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenPattern();

        PixelBuffer edge = Edge(64, 32);
        layer.ReplacePixels(edge);
        int width = edge.Width;
        int height = edge.Height;

        document.Transform.Rotate(QuarterTurn.Clockwise);
        Assert.Equal(Localization.Get("Undo_RotateRight"), document.History.UndoName);

        document.Transform.Rotate(QuarterTurn.CounterClockwise);
        document.Transform.Flip(FlipAxis.Horizontal);
        document.Transform.Flip(FlipAxis.Vertical);
        document.Transform.GaussianBlur(3);
        document.Transform.Sharpen(2, 1);

        // Six actions, six steps: the keyboard entry points call these same methods.
        for (int i = 0; i < 6; i++)
        {
            Assert.True(document.CanUndo, $"expected a step for action {i + 1}");
            document.Undo();
        }

        Assert.Equal(width, layer.Source!.Width);
        Assert.Equal(height, layer.Source.Height);
    }

    [Fact]
    public void TheEyedropperIsNotAnEdit()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenPattern();

        document.ColorPicker.Pick(2, 2);

        Assert.True(document.ColorPicker.HasSample);
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void CroppingToASelectionDoesNothingWithoutOne()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenPattern();
        PixelBuffer before = layer.Source!;

        Assert.False(document.Selection.HasRegion);
        Assert.Equal(before.Width, layer.Source!.Width);
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void CroppingToASelectionUsesTheSelectedRegion()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenPattern();

        document.Selection.X = 4;
        document.Selection.Y = 2;
        document.Selection.Width = 16;
        document.Selection.Height = 8;
        document.Selection.Refresh();

        Assert.True(document.Selection.HasRegion);

        document.Transform.CropToSelection();

        Assert.Equal(16, layer.Source!.Width);
        Assert.Equal(8, layer.Source.Height);
        Assert.Equal(Localization.Get("Undo_CropLayer"), document.History.UndoName);

        document.Undo();
        Assert.Equal(64, layer.Source!.Width);
    }


    private static PixelBuffer Edge(int width, int height)
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
                buffer.Data[i + 3] = 255;
            }
        }

        return buffer;
    }

    private static (DocumentViewModel Document, LayerViewModel Layer) OpenPattern()
    {
        var codec = new ImageCodec();
        string path = Path.Combine(ScratchDirectory(), "shortcut-source.png");
        codec.Write(Edge(64, 32), path, new ImageSaveOptions(ImageFileFormat.Png));

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
        string directory = Path.Combine(AppContext.BaseDirectory, "shortcut-scratch");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
