using NaraPainter.App;
using NaraPainter.App.Services;
using NaraPainter.App.ViewModels;
using NaraPainter.Imaging.Services;
using NaraPainter.Models.Pixels;
using NaraPainter.Models.Services;
using Xunit;

namespace NaraPainter.Tests;

/// <summary>
/// Importing another picture replaces the document, so undoing it has to put the previous one back
/// rather than edit the current one. The blank canvas the window opens with is not a previous
/// picture, so the first import is not recorded as undoable.
/// </summary>
public class ImportUndoTests
{
    [Fact]
    public void TheFirstImportIsNotAnUndoStep()
    {
        (DocumentViewModel document, string path) = Import("gradient.png");

        // Nothing has been done to the document yet, so there is nothing to go back to.
        Assert.False(document.CanUndo);
        Assert.False(document.CanRedo);

        File.Delete(path);
    }

    [Fact]
    public void ImportingASecondPictureIsOneUndoStep()
    {
        (DocumentViewModel document, string first) = Import("gradient.png");
        string firstSize = document.SizeLabel;

        string second = Path.Combine(ScratchDirectory(), "second.png");
        var codec = new ImageCodec();
        codec.Write(Pattern(70, 90), second, new ImageSaveOptions(ImageFileFormat.Png));

        document.Open(second);
        Assert.NotEqual(firstSize, document.SizeLabel);
        Assert.True(document.CanUndo);
        Assert.Equal(Localization.Interpolate(Strings.UndoOpenImage, "second.png"), document.History.UndoName);

        document.Undo();

        // Back to the picture that was open before, canvas size included.
        Assert.Equal(firstSize, document.SizeLabel);
        Assert.False(document.CanUndo);
        Assert.True(document.CanRedo);

        document.Redo();

        Assert.NotEqual(firstSize, document.SizeLabel);
        Assert.True(document.CanUndo);

        File.Delete(first);
        File.Delete(second);
    }

    [Fact]
    public void AdjustmentsAfterAnImportStillUndoBeforeIt()
    {
        (DocumentViewModel document, string first) = Import("gradient.png");

        string second = Path.Combine(ScratchDirectory(), "after-adjust.png");
        var codec = new ImageCodec();
        codec.Write(Pattern(70, 90), second, new ImageSaveOptions(ImageFileFormat.Png));
        document.Open(second);

        LayerViewModel layer = document.SelectedLayer!;
        layer.Opacity = 40;
        document.Undo();

        // The opacity step comes off first, and the import step is still there behind it.
        Assert.Equal(100, layer.Opacity);
        Assert.True(document.CanUndo);

        document.Undo();
        Assert.True(document.CanRedo);

        File.Delete(first);
        File.Delete(second);
    }

    private static (DocumentViewModel Document, string Path) Import(string name)
    {
        var codec = new ImageCodec();
        string source = Path.Combine(AppContext.BaseDirectory, "testimages", name);
        string copy = Path.Combine(ScratchDirectory(), name);
        File.Copy(source, copy, overwrite: true);

        var document = new DocumentViewModel(new ImageImporter(codec), codec, new AdjustmentFilter(), new SelectionMaskBuilder());
        document.Open(copy);
        return (document, copy);
    }

    private static PixelBuffer Pattern(int width, int height)
    {
        var buffer = new PixelBuffer(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = buffer.Offset(x, y);
                buffer.Data[i] = (byte)((x * 255) / Math.Max(1, width - 1));
                buffer.Data[i + 1] = 90;
                buffer.Data[i + 2] = 200;
                buffer.Data[i + 3] = 255;
            }
        }

        return buffer;
    }

    private static string ScratchDirectory()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "import-scratch");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
