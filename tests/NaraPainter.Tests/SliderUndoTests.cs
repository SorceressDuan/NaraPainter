using NaraPainter.App;
using NaraPainter.App.Services;
using NaraPainter.App.ViewModels;
using NaraPainter.Imaging.Services;
using NaraPainter.Models.Adjustments;
using NaraPainter.Models.Pixels;
using NaraPainter.Models.Services;
using Xunit;

namespace NaraPainter.Tests;

/// <summary>
/// One drag of a slider has to leave one undo step behind, from the value it started at to the value
/// it ended at. The history merges by key, so a drag only has to share one key across its values.
/// </summary>
public class SliderUndoTests
{
    [Fact]
    public void DraggingOpacityFromOneHundredToOneStepUndoesInOneGo()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenPattern();
        layer.BeginOpacityEdit();

        // What a drag produces: a value for every fraction of travel.
        for (int value = 99; value >= 81; value--) layer.Opacity = value;

        Assert.Equal(81, layer.Opacity);

        document.Undo();

        Assert.Equal(100, layer.Opacity);
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void RedoPutsTheDraggedValueBack()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenPattern();
        layer.BeginOpacityEdit();
        for (int value = 99; value >= 81; value--) layer.Opacity = value;
        document.Undo();

        document.Redo();

        Assert.Equal(81, layer.Opacity);
    }

    [Fact]
    public void TwoSeparateDragsUndoOneAtATime()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenPattern();

        layer.BeginOpacityEdit();
        layer.Opacity = 90;
        layer.Opacity = 80;

        layer.BeginOpacityEdit();
        layer.Opacity = 40;
        layer.Opacity = 30;

        Assert.Equal(30, layer.Opacity);

        document.Undo();
        Assert.Equal(80, layer.Opacity);

        document.Undo();
        Assert.Equal(100, layer.Opacity);
    }

    [Fact]
    public void DraggingAnAdjustmentSliderUndoesInOneGo()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenPattern();
        document.AddAdjustmentLayer(AdjustmentKind.BrightnessContrast);
        double start = document.Adjustment.Brightness;

        document.Adjustment.BeginEdit();
        for (double value = start - 1; value >= start - 40; value--) document.Adjustment.Brightness = value;

        Assert.Equal(start - 40, document.Adjustment.Brightness);

        document.Undo();

        Assert.Equal(start, document.Adjustment.Brightness);
    }

    [Fact]
    public void EachNudgeOfASliderValueIsItsOwnStep()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenPattern();

        // No BeginEdit in front of these: an arrow key or a typed value stands on its own.
        layer.Opacity = 90;
        layer.Opacity = 80;

        document.Undo();
        Assert.Equal(90, layer.Opacity);

        document.Undo();
        Assert.Equal(100, layer.Opacity);
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void OpeningASessionTwiceStillCountsAsOneDrag()
    {
        // Pressing the thumb hands the slider focus a moment after the press, so the control saw a
        // second session start right behind the first and the drag split apart. It tolerates that now.
        AdjustmentSliderGuard.AssertASecondBeginIsIgnored();
    }

    private static (DocumentViewModel Document, LayerViewModel Layer) OpenPattern()
    {
        var codec = new ImageCodec();
        string path = Path.Combine(ScratchDirectory(), "slider-undo.png");
        codec.Write(Pattern(48, 32), path, new ImageSaveOptions(ImageFileFormat.Png));

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
                buffer.Data[i + 2] = 128;
                buffer.Data[i + 3] = 255;
            }
        }

        return buffer;
    }

    private static string ScratchDirectory()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "slider-scratch");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
