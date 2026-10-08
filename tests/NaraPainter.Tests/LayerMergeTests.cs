using NaraPainter.App;
using NaraPainter.App.Services;
using NaraPainter.App.ViewModels;
using NaraPainter.Imaging.Services;
using NaraPainter.Models.Blending;
using NaraPainter.Models.Layers;
using NaraPainter.Models.Pixels;
using NaraPainter.Models.Services;
using Xunit;

namespace NaraPainter.Tests;

/// <summary>
/// Merging layers down and merging a folder into one layer.
/// </summary>
/// <remarks>
/// A merge has to leave the picture exactly as it was: everything inside the merged set is baked in,
/// anything below it is not touched, and the whole thing comes back with one undo step.
/// </remarks>
[Collection(LocalizedState.Name)]
public class LayerMergeTests
{
    private const int Size = 8;

    [Fact]
    public void MergingDownReplacesBothLayersWithOne()
    {
        (DocumentViewModel document, LayerViewModel top, LayerViewModel below) = OpenStack();
        int before = document.Layers.Count;

        Assert.True(document.MergeDown());

        // Both rows go; one row takes their place, carrying the upper layer's name.
        Assert.Equal(before - 1, document.Layers.Count);
        Assert.DoesNotContain(below, document.Layers);
        Assert.DoesNotContain(top, document.Layers);
        Assert.Equal("Top", document.SelectedLayer!.Name);

        Assert.Equal(Localization.Get("Undo_MergeDown"), document.History.UndoName);
        Assert.NotNull(document.SelectedLayer.Source);
    }

    [Fact]
    public void AMergeLeavesThePictureAlone()
    {
        // The merged layer has to look like the two did, so flattening afterwards is unchanged.
        (DocumentViewModel document, _, _) = OpenStack();
        byte[] was = (byte[])document.Flatten().Data.Clone();

        document.MergeDown();

        Assert.Equal(was, document.Flatten().Data);
    }

    [Fact]
    public void AMergeBakesInOpacityAndBlendMode()
    {
        (DocumentViewModel document, LayerViewModel top, _) = OpenStack();
        top.Opacity = 50;
        top.Model.BlendMode = BlendMode.Multiply;
        byte[] was = (byte[])document.Flatten().Data.Clone();

        document.MergeDown();

        Assert.Equal(was, document.Flatten().Data);
    }

    [Fact]
    public void MergingDownUndoesAndRedoes()
    {
        (DocumentViewModel document, LayerViewModel top, LayerViewModel below) = OpenStack();
        int before = document.Layers.Count;
        byte[] was = (byte[])document.Flatten().Data.Clone();

        document.MergeDown();
        document.Undo();

        Assert.Equal(before, document.Layers.Count);
        Assert.Contains(top, document.Layers);
        Assert.Contains(below, document.Layers);
        Assert.Equal(was, document.Flatten().Data);

        document.Redo();

        Assert.Equal(before - 1, document.Layers.Count);
        Assert.Equal(was, document.Flatten().Data);
    }

    [Fact]
    public void MergingTheBottomLayerDownIsRefused()
    {
        (DocumentViewModel document, _, _) = OpenStack();

        // Panel order is top first, so the last row is the bottom of the stack.
        document.SelectedLayer = document.Layers[^1];

        Assert.False(document.MergeDown());
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void MergingDownRefusesWhenWhatIsBelowIsNotASibling()
    {
        // A folder is not something to merge a layer into, so the command refuses rather than reaching
        // past the boundary it cannot cross.
        (DocumentViewModel document, _, _, LayerViewModel folder) = Nested();
        LayerViewModel top = document.Layers[0];

        // The folder sits directly under the top layer in the stack.
        document.SelectedLayer = top;
        Assert.False(document.MergeDown());
        Assert.Contains(folder, document.Layers);
    }

    [Fact]
    public void MergingAFolderKeepsItsPlaceAndName()
    {
        // Middle and Bottom sit in a folder; Top stays outside it.
        (DocumentViewModel document, _, _, LayerViewModel folder) = Nested();

        int before = document.Layers.Count;
        int slot = document.Layers.IndexOf(folder);
        byte[] was = (byte[])document.Flatten().Data.Clone();

        document.SelectedLayer = folder;
        Assert.True(document.MergeGroup());

        Assert.Equal(before - 2, document.Layers.Count);
        Assert.DoesNotContain(folder, document.Layers);
        Assert.Equal(slot, document.Layers.IndexOf(document.SelectedLayer!));
        Assert.Equal(folder.Name, document.SelectedLayer!.Name);
        Assert.Equal(Localization.Get("Undo_MergeGroup"), document.History.UndoName);
        Assert.Equal(was, document.Flatten().Data);
    }

    [Fact]
    public void MergingAFolderUndoesAndRedoes()
    {
        (DocumentViewModel document, _, _) = OpenStack();
        LayerViewModel folder = document.GroupSelectedLayers()!;
        int before = document.Layers.Count;
        byte[] was = (byte[])document.Flatten().Data.Clone();

        document.SelectedLayer = folder;
        Assert.True(document.MergeGroup());

        document.Undo();

        Assert.Equal(before, document.Layers.Count);
        Assert.Contains(folder, document.Layers);
        Assert.Equal(was, document.Flatten().Data);

        document.Redo();

        Assert.Equal(before - 1, document.Layers.Count);
    }

    [Fact]
    public void MergingAFolderHoldingOneLayerRemovesTheFolder()
    {
        // The folder wrapper has an opacity and a blend mode of its own, so folding it into the layer is
        // a real operation rather than a no-op: the layer comes out of the folder, flattened.
        (DocumentViewModel document, LayerViewModel top, _) = OpenStack();
        LayerViewModel folder = document.GroupSelectedLayers()!;
        folder.Opacity = 50;

        int before = document.Layers.Count;
        byte[] was = (byte[])document.Flatten().Data.Clone();

        Assert.True(document.MergeGroup());

        Assert.Equal(before - 1, document.Layers.Count);
        Assert.DoesNotContain(folder, document.Layers);
        Assert.DoesNotContain(top, document.Layers);

        // The result carries the folder's name, so the row reads as the thing that was merged away.
        Assert.Equal("文件夹 1", document.SelectedLayer!.Name);
        Assert.Null(document.SelectedLayer.Model.ParentId);
        Assert.Equal(0, document.SelectedLayer.Depth);
        Assert.Equal(was, document.Flatten().Data);
    }

    [Fact]
    public void MergingSomethingThatIsNotAFolderIsRefused()
    {
        (DocumentViewModel document, LayerViewModel top, _) = OpenStack();
        document.SelectedLayer = top;

        Assert.False(document.MergeGroup());
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void AFolderWithNestedLayersMergesIntoOneLayer()
    {
        // Two levels deep, so the merge has to gather a whole subtree rather than one level of it.
        (DocumentViewModel document, LayerViewModel outer, LayerViewModel inner) = TwoDeep();
        int before = document.Layers.Count;
        byte[] was = (byte[])document.Flatten().Data.Clone();

        document.SelectedLayer = outer;
        Assert.True(document.MergeGroup());

        // Every folder the merge swallowed is gone, and the result is a single painted layer.
        Assert.DoesNotContain(document.Layers, row => row.IsGroup);
        Assert.DoesNotContain(document.Layers, row => row.Model.Id == inner.Model.Id);
        Assert.Single(document.Layers);
        Assert.NotNull(document.SelectedLayer!.Source);
        Assert.Equal(outer.Name, document.SelectedLayer.Name);

        // And the picture is exactly what it was.
        Assert.Equal(was, document.Flatten().Data);

        // One step puts the whole subtree back.
        document.Undo();
        Assert.Equal(before, document.Layers.Count);
        Assert.Contains(inner, document.Layers);
        Assert.Equal(was, document.Flatten().Data);
    }

    /// <summary>An outer folder holding an inner one, which holds a layer.</summary>
    private static (DocumentViewModel Document, LayerViewModel Outer, LayerViewModel Inner) TwoDeep()
    {
        var codec = new ImageCodec();
        string path = Path.Combine(ScratchDirectory(), "layer-merge-deep.png");
        codec.Write(new PixelBuffer(Size, Size), path, new ImageSaveOptions(ImageFileFormat.Png));

        try
        {
            var document = new DocumentViewModel(new ImageImporter(codec), codec, new AdjustmentFilter(), new SelectionMaskBuilder());
            document.Open(path);
            document.DeleteLayer(document.Layers[0]);

            Paint(document.AddLayer(), 200, 0, 0);
            LayerViewModel inner = document.GroupSelectedLayers()!;
            LayerViewModel outer = document.GroupSelectedLayers()!;

            Assert.Equal(outer.Model.Id, inner.Model.ParentId);
            Assert.True(NaraPainter.Models.Documents.LayerOrder.IsValid(document.Document.Layers));
            return (document, outer, inner);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Middle and Bottom inside a folder, with Top left outside it. Built with the real grouping
    /// operation so the panel's rows and the document stay in step.
    /// </summary>
    private static (DocumentViewModel Document, LayerViewModel Top, LayerViewModel Middle, LayerViewModel Folder) Nested()
    {
        (DocumentViewModel document, LayerViewModel top, LayerViewModel middle) = OpenStack();
        LayerViewModel bottom = document.Layers[2];

        document.SelectedLayer = middle;
        LayerViewModel folder = document.GroupSelectedLayers()!;

        // The second layer joins the same folder, which is what makes it a real group of two.
        bottom.Model.ParentId = folder.Model.Id;
        document.AddLayer();
        document.DeleteLayer(document.SelectedLayer);
        document.Undo();
        document.Undo();

        return (document, top, middle, folder);
    }

    /// <summary>Three stacked solid layers over a blank canvas, history cleared, panel order top first.</summary>
    private static (DocumentViewModel Document, LayerViewModel Top, LayerViewModel Below) OpenStack()
    {
        var codec = new ImageCodec();
        string path = Path.Combine(ScratchDirectory(), "layer-merge.png");
        codec.Write(new PixelBuffer(Size, Size), path, new ImageSaveOptions(ImageFileFormat.Png));

        try
        {
            var document = new DocumentViewModel(new ImageImporter(codec), codec, new AdjustmentFilter(), new SelectionMaskBuilder());
            document.Open(path);

            // The import leaves a layer of its own behind; the fixture is the three added here.
            document.DeleteLayer(document.Layers[0]);

            // Painted so a wrong composite shows up: three bands that overlap.
            Paint(document.AddLayer(), 200, 0, 0);
            Paint(document.AddLayer(), 0, 180, 0);
            Paint(document.AddLayer(), 0, 0, 160);

            document.Layers[0].Name = "Top";
            document.Layers[1].Name = "Middle";
            document.Layers[2].Name = "Bottom";
            document.SelectedLayer = document.Layers[0];
            document.History.Clear();
            return (document, document.Layers[0], document.Layers[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void Paint(LayerViewModel layer, byte red, byte green, byte blue)
    {
        if (layer.Source is not { } source) return;

        PixelBuffer pixels = source.Clone();
        for (int i = 0; i < pixels.Data.Length; i += 4)
        {
            pixels.Data[i] = red;
            pixels.Data[i + 1] = green;
            pixels.Data[i + 2] = blue;
            pixels.Data[i + 3] = 255;
        }

        layer.ReplacePixels(pixels);
    }

    private static string ScratchDirectory()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "layer-merge-scratch");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
