using NaraPainter.App;
using NaraPainter.App.Services;
using NaraPainter.App.ViewModels;
using NaraPainter.Imaging.Services;
using NaraPainter.Models.Pixels;
using NaraPainter.Models.Services;
using Xunit;

namespace NaraPainter.Tests;

/// <summary>
/// How the layer panel lays a stack out: how far each row is indented and which rows get a triangle.
/// </summary>
/// <remarks>
/// The panel shows the stack as one flat list, so nesting is carried by a depth number per row rather
/// than by a tree of controls. These check the number, which is what the indent and the triangle are
/// drawn from.
/// </remarks>
[Collection(LocalizedState.Name)]
public class LayerPanelTests
{
    [Fact]
    public void AFlatStackHasNoIndentation()
    {
        (DocumentViewModel document, _) = OpenStack();

        Assert.All(document.Layers, row => Assert.Equal(0, row.Depth));
        Assert.All(document.Layers, row => Assert.Equal(0, row.IndentWidth));
        Assert.All(document.Layers, row => Assert.False(row.HasChildren));
    }

    [Fact]
    public void ALayerInsideAFolderIsIndentedOneStep()
    {
        (DocumentViewModel document, _) = OpenStack();

        // Panel order is top first: [0] is the top layer, [2] is the bottom.
        document.Layers[0].Model.IsGroup = true;
        document.Layers[2].Model.ParentId = document.Layers[0].Model.Id;
        Recompute(document);

        Assert.Equal(0, document.Layers[0].Depth);
        Assert.Equal(1, document.Layers[2].Depth);
        Assert.Equal(20, document.Layers[2].IndentWidth);
    }

    [Fact]
    public void OnlyAFolderWithSomethingInsideItGetsATriangle()
    {
        (DocumentViewModel document, _) = OpenStack();

        document.Layers[0].Model.IsGroup = true;
        document.Layers[2].Model.ParentId = document.Layers[0].Model.Id;
        Recompute(document);

        Assert.True(document.Layers[0].HasChildren, "the layer holding another one should have a triangle");
        Assert.False(document.Layers[1].HasChildren);
        Assert.False(document.Layers[2].HasChildren);

        // And it says so in the panel rather than claiming to be an empty layer.
        Assert.Equal(Localization.Get("Layers_Group"), document.Layers[0].ContentLabel);
    }

    [Fact]
    public void AnEmptyFolderHasNoTriangleAndStillReadsAsAFolder()
    {
        (DocumentViewModel document, _) = OpenStack();

        document.Layers[0].Model.IsGroup = true;
        Recompute(document);

        Assert.False(document.Layers[0].HasChildren);
        Assert.Equal(Localization.Get("Layers_Group"), document.Layers[0].ContentLabel);
    }

    [Fact]
    public void NestingTwoDeepCountsBothSteps()
    {
        (DocumentViewModel document, _) = OpenStack();

        // Top holds Middle, Middle holds Bottom.
        document.Layers[0].Model.IsGroup = true;
        document.Layers[1].Model.IsGroup = true;
        document.Layers[1].Model.ParentId = document.Layers[0].Model.Id;
        document.Layers[2].Model.ParentId = document.Layers[1].Model.Id;

        Recompute(document);

        Assert.Equal(0, document.Layers[0].Depth);
        Assert.Equal(1, document.Layers[1].Depth);
        Assert.Equal(2, document.Layers[2].Depth);
        Assert.Equal(40, document.Layers[2].IndentWidth);
    }

    [Fact]
    public void ALinkToAFolderThatIsNotThereLaysOutAtTheTopLevel()
    {
        // A broken tree still has to draw, rather than throwing or indenting against nothing.
        (DocumentViewModel document, _) = OpenStack();

        document.Layers[1].Model.ParentId = Guid.NewGuid();
        Recompute(document);

        Assert.Equal(0, document.Layers[1].Depth);
    }

    [Fact]
    public void DeletingAFolderTakesItsRowAndLeavesTheRestFlat()
    {
        (DocumentViewModel document, _) = OpenStack();

        document.Layers[0].Model.IsGroup = true;
        document.Layers[2].Model.ParentId = document.Layers[0].Model.Id;
        Recompute(document);
        Assert.Equal(1, document.Layers[2].Depth);

        document.DeleteLayer(document.Layers[0]);

        Assert.Single(document.Layers);
        Assert.Equal(0, document.Layers[0].Depth);
        Assert.False(document.Layers[0].HasChildren);

        document.Undo();

        Assert.Equal(3, document.Layers.Count);
        Assert.Equal(1, document.Layers[2].Depth);
        Assert.True(document.Layers[0].HasChildren);
    }

    /// <summary>
    /// A folded row must leave the panel's list outright rather than being hidden inside it.
    /// </summary>
    /// <remarks>
    /// This is the shape the drag bug came from. A ListView that recycles containers while its items flip
    /// to a collapsed visibility parks those containers far outside the viewport, and after a drag the
    /// rows were left drawn on top of each other. Keeping folded rows out of the list leaves it nothing
    /// to recycle.
    /// </remarks>
    [Fact]
    public void FoldedRowsLeaveThePanelsListEntirely()
    {
        (DocumentViewModel document, LayerViewModel selected) = OpenStack();
        LayerViewModel folder = document.GroupSelectedLayers()!;

        Assert.Contains(selected, document.DisplayRows);

        document.ToggleCollapsed(folder);

        Assert.DoesNotContain(selected, document.DisplayRows);
        Assert.Contains(folder, document.DisplayRows);

        document.ToggleCollapsed(folder);

        Assert.Contains(selected, document.DisplayRows);
        Assert.Equal(document.Layers.Count, document.DisplayRows.Count);
    }

    [Fact]
    public void ThePanelsListStaysInStepThroughStructuralEdits()
    {
        (DocumentViewModel document, LayerViewModel selected) = OpenStack();

        void AssertInStep()
        {
            string[] panel = [.. document.Layers.Where(row => row.IsRowVisible).Select(row => row.Name)];
            string[] shown = [.. document.DisplayRows.Select(row => row.Name)];
            Assert.Equal(panel, shown);
        }

        AssertInStep();

        document.GroupSelectedLayers();
        AssertInStep();

        document.AddLayer();
        AssertInStep();

        document.DeleteLayer(selected);
        AssertInStep();

        document.Undo();
        AssertInStep();

        document.Undo();
        AssertInStep();
    }

    /// <summary>
    /// Makes the panel re-read the placement after a test has wired up folder links on the model.
    /// </summary>
    /// <remarks>
    /// Deleting a throwaway layer and undoing it twice is the least invasive operation that recomputes:
    /// the alternative, a reorder, would move the very rows the assertions then index into. It also puts
    /// the placement through the snapshot restore, which is where a nesting bug would actually show up.
    /// </remarks>
    private static void Recompute(DocumentViewModel document)
    {
        LayerViewModel throwaway = document.AddLayer();
        document.DeleteLayer(throwaway);
        document.Undo();
        document.Undo();
    }

    /// <summary>Three layers over a blank canvas, history cleared, panel order top first.</summary>
    private static (DocumentViewModel Document, LayerViewModel Top) OpenStack()
    {
        var codec = new ImageCodec();
        string path = Path.Combine(ScratchDirectory(), "layer-panel.png");
        codec.Write(new PixelBuffer(16, 12), path, new ImageSaveOptions(ImageFileFormat.Png));

        try
        {
            var document = new DocumentViewModel(new ImageImporter(codec), codec, new AdjustmentFilter(), new SelectionMaskBuilder());
            document.Open(path);
            document.AddLayer();
            document.AddLayer();
            document.Layers[0].Name = "Top";
            document.Layers[1].Name = "Middle";
            document.Layers[2].Name = "Bottom";
            document.SelectedLayer = document.Layers[0];
            document.History.Clear();
            return (document, document.Layers[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string ScratchDirectory()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "layer-panel-scratch");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
