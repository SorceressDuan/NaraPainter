using NaraPainter.App;
using NaraPainter.App.Services;
using NaraPainter.App.ViewModels;
using NaraPainter.Imaging.Services;
using NaraPainter.Models.Pixels;
using NaraPainter.Models.Services;
using Xunit;

namespace NaraPainter.Tests;

/// <summary>
/// The mask brush and the content-aware fill each record one history step per operation, so a whole
/// drag undoes in one go and a fill restores exactly the pixels it replaced.
/// </summary>
[Collection(LocalizedState.Name)]
public class UndoRedoTests
{
    [Fact]
    public void AMaskStrokeIsASingleUndoStep()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenPattern();
        Assert.False(layer.IsMasked);

        document.MaskBrush.Size = 24;
        document.MaskBrush.Hardness = 1;
        document.MaskBrush.Opacity = 1;
        document.MaskBrush.Erase = false;

        document.MaskBrush.BeginStroke(20, 20);
        document.MaskBrush.ContinueStroke(40, 20);
        document.MaskBrush.ContinueStroke(60, 40);
        document.MaskBrush.EndStroke();

        byte[] painted = (byte[])layer.Model.Mask!.Clone();
        Assert.True(layer.IsMasked);
        Assert.Contains(painted, value => value > 0);
        Assert.True(document.CanUndo);
        Assert.False(document.CanRedo);
        Assert.Equal(Localization.Get("Undo_PaintMask"), document.History.UndoName);

        document.Undo();

        // One undo covers the whole drag: the layer is back to having no mask at all.
        Assert.Null(layer.Model.Mask);
        Assert.False(document.CanUndo);
        Assert.True(document.CanRedo);

        document.Redo();

        Assert.Equal(painted, layer.Model.Mask);
        Assert.False(document.CanRedo);
    }

    [Fact]
    public void AContentAwareFillRestoresThePixelsItReplaced()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenPattern();

        EraseAHole(document, 48, 32, size: 20);
        document.History.Clear();

        Assert.True(layer.IsMasked);
        Assert.Contains(layer.Model.Mask!, value => value == 0);
        Assert.True(document.ContentFill.CanFill);

        byte[] before = (byte[])layer.Model.Pixels!.Data.Clone();
        document.ContentFill.Radius = 3;
        document.ContentFill.Fill();
        byte[] after = (byte[])layer.Model.Pixels!.Data.Clone();

        Assert.True(document.CanUndo, $"the fill recorded no history step; status was '{document.Status}'");
        Assert.NotEqual(before, after);
        Assert.True(document.CanUndo);
        Assert.Equal(Localization.Get("Undo_ContentFill"), document.History.UndoName);

        document.Undo();

        Assert.Equal(before, layer.Model.Pixels!.Data);
        Assert.False(document.CanUndo);
        Assert.True(document.CanRedo);

        document.Redo();

        Assert.Equal(after, layer.Model.Pixels!.Data);
        Assert.False(document.CanRedo);
    }

    [Fact]
    public void TwoOperationsUndoAndRedoInOrder()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenPattern();
        byte[] original = (byte[])layer.Model.Pixels!.Data.Clone();

        EraseAHole(document, 48, 32, size: 20);
        byte[] masked = (byte[])layer.Model.Mask!.Clone();

        document.ContentFill.Fill();
        byte[] filled = (byte[])layer.Model.Pixels!.Data.Clone();

        // Opening a document leaves no selection, so the fill works from the hole the brush erased.
        Assert.NotEqual(original, filled);

        document.Undo();

        // The fill's step touches pixels only, and leaves the mask the stroke produced alone.
        Assert.Equal(original, layer.Model.Pixels!.Data);
        Assert.Equal(masked, layer.Model.Mask);

        document.Undo();

        // The stroke's step takes the mask away and still leaves the pixels as they were.
        Assert.Null(layer.Model.Mask);
        Assert.Equal(original, layer.Model.Pixels!.Data);
        Assert.False(document.CanUndo);

        document.Redo();
        Assert.Equal(masked, layer.Model.Mask);

        document.Redo();
        Assert.Equal(filled, layer.Model.Pixels!.Data);
        Assert.False(document.CanRedo);
    }

    private static void EraseAHole(DocumentViewModel document, double x, double y, int size)
    {
        document.MaskBrush.Size = size;
        document.MaskBrush.Hardness = 1;
        document.MaskBrush.Opacity = 1;
        document.MaskBrush.Erase = true;
        document.MaskBrush.BeginStroke(x, y);
        document.MaskBrush.EndStroke();
    }

    private static (DocumentViewModel Document, LayerViewModel Layer) OpenPattern()
    {
        var codec = new ImageCodec();
        string path = Path.Combine(ScratchDirectory(), "undo-source.png");
        codec.Write(Pattern(96, 64), path, new ImageSaveOptions(ImageFileFormat.Png));

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

    private static PixelBuffer Pattern(int width, int height)
    {
        var buffer = new PixelBuffer(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = buffer.Offset(x, y);
                buffer.Data[i] = (byte)((x * 255) / (width - 1));
                buffer.Data[i + 1] = (byte)((y * 255) / (height - 1));
                buffer.Data[i + 2] = (byte)(((x / 8) + (y / 8)) % 2 == 0 ? 40 : 210);
                buffer.Data[i + 3] = 255;
            }
        }
        return buffer;
    }

    private static string ScratchDirectory()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "undo-scratch");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
