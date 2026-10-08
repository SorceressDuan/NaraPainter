using NaraPainter.App;
using NaraPainter.App.Services;
using NaraPainter.App.ViewModels;
using NaraPainter.Imaging.Services;
using NaraPainter.Models.Documents;
using NaraPainter.Models.Pixels;
using NaraPainter.Models.Services;
using Xunit;

namespace NaraPainter.Tests;

/// <summary>
/// Where a dragged row lands when it is dropped on another row: above it, below it, or inside it when
/// that row is a folder.
/// </summary>
/// <remarks>
/// This is the placement half of the drag. The panel works out which of the three the pointer is
/// asking for; everything after that, including the tree staying walkable and the whole move coming
/// back with one undo, is decided here.
/// </remarks>
[Collection(LocalizedState.Name)]
public class LayerDropTests
{
    [Fact]
    public void DroppingAboveARowLandsItJustAbove()
    {
        (DocumentViewModel document, LayerViewModel top, LayerViewModel middle, _) = OpenStack();

        // Bottom is dragged onto the top row's upper edge.
        Assert.True(document.MoveLayerTo(document.Layers[2], top, DocumentViewModel.RowDrop.Above));

        Assert.Equal(["Bottom", "Top", "Middle"], Names(document));
        Assert.Equal(middle.Model.ParentId, document.Layers[0].Model.ParentId);
    }

    [Fact]
    public void DroppingBelowARowLandsItJustBelow()
    {
        (DocumentViewModel document, LayerViewModel top, _, _) = OpenStack();

        // Top is dragged onto the middle row's lower edge.
        Assert.True(document.MoveLayerTo(top, document.Layers[1], DocumentViewModel.RowDrop.Below));

        Assert.Equal(["Middle", "Top", "Bottom"], Names(document));
    }

    [Fact]
    public void DroppingOntoAFolderPutsTheRowInsideIt()
    {
        (DocumentViewModel document, LayerViewModel top, _, LayerViewModel folder) = WithFolder();

        Assert.True(document.MoveLayerTo(document.Layers[^1], folder, DocumentViewModel.RowDrop.Into));

        LayerViewModel moved = document.Layers.First(row => row.Name == "Bottom");
        Assert.Equal(folder.Model.Id, moved.Model.ParentId);
        Assert.Equal(1, moved.Depth);
        Assert.True(document.SelectedLayer == moved);
    }

    [Fact]
    public void AFolderCannotBeDroppedInsideItsOwnDescendant()
    {
        // The tree would stop being walkable, so it has to be refused and leave no undo step behind.
        (DocumentViewModel document, _, _, LayerViewModel folder) = WithFolder();
        document.History.Clear();

        LayerViewModel child = document.Layers.First(row => row.Model.ParentId == folder.Model.Id);

        Assert.False(document.MoveLayerTo(folder, child, DocumentViewModel.RowDrop.Into));
        Assert.False(document.MoveLayerTo(folder, child, DocumentViewModel.RowDrop.Above));
        Assert.False(document.MoveLayerTo(folder, child, DocumentViewModel.RowDrop.Below));

        Assert.False(document.CanUndo);
        Assert.True(LayerOrder.IsValid(document.Document.Layers));
    }

    [Fact]
    public void ARowCannotBeDroppedOnItself()
    {
        (DocumentViewModel document, LayerViewModel top, _, _) = OpenStack();
        document.History.Clear();

        Assert.False(document.MoveLayerTo(top, top, DocumentViewModel.RowDrop.Above));
        Assert.False(document.MoveLayerTo(top, top, DocumentViewModel.RowDrop.Into));
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void DroppingInsideALayerThatIsNotAFolderIsRefused()
    {
        (DocumentViewModel document, LayerViewModel top, _, _) = OpenStack();
        document.History.Clear();

        Assert.False(top.IsGroup);
        Assert.False(document.MoveLayerTo(document.Layers[1], top, DocumentViewModel.RowDrop.Into));
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void ADropIsOneUndoStepThatPutsTheRowBack()
    {
        (DocumentViewModel document, _, _, LayerViewModel folder) = WithFolder();
        LayerViewModel moved = document.Layers[^1];
        Guid? wasParent = moved.Model.ParentId;
        string[] wasOrder = Names(document);

        Assert.True(document.MoveLayerTo(moved, folder, DocumentViewModel.RowDrop.Into));
        Assert.Equal(Localization.Get("Undo_ReorderLayers"), document.History.UndoName);

        document.Undo();

        Assert.Equal(wasOrder, Names(document));
        Assert.Equal(wasParent, moved.Model.ParentId);
        Assert.Equal(0, moved.Depth);

        document.Redo();

        Assert.Equal(folder.Model.Id, moved.Model.ParentId);
        Assert.Equal(1, moved.Depth);
    }

    [Fact]
    public void DroppingIntoAFoldedFolderOpensItSoTheRowCanBeSeen()
    {
        (DocumentViewModel document, LayerViewModel top, _, LayerViewModel folder) = WithFolder();

        // Put a row in, fold the folder away, and check the row really is out of sight.
        LayerViewModel moved = document.Layers[^1];
        Assert.True(document.MoveLayerTo(moved, folder, DocumentViewModel.RowDrop.Into));

        document.ToggleCollapsed(folder);
        Assert.True(folder.IsCollapsed);
        Assert.DoesNotContain(moved, document.DisplayRows);

        // Now fold it again with a different row coming in: the drop has to open it, or the row lands
        // somewhere the user cannot see.
        document.SelectedLayer = top;
        Assert.True(document.MoveLayerTo(top, folder, DocumentViewModel.RowDrop.Into));

        Assert.False(folder.IsCollapsed);
        Assert.Contains(top, document.DisplayRows);
    }

    [Fact]
    public void ADropLeavesTheTreeWalkable()
    {
        (DocumentViewModel document, LayerViewModel top, _, LayerViewModel folder) = WithFolder();

        document.MoveLayerTo(document.Layers[^1], folder, DocumentViewModel.RowDrop.Into);
        document.MoveLayerTo(top, document.Layers[^1], DocumentViewModel.RowDrop.Below);
        document.MoveLayerTo(document.Layers[^1], folder, DocumentViewModel.RowDrop.Above);

        Assert.True(LayerOrder.IsValid(document.Document.Layers));
        Assert.Equal(document.Layers.Count, document.Document.Layers.Count);
    }

    [Fact]
    public void ARowDroppedOutOfAFolderMatchesTheRowItLandedAgainst()
    {
        // A row that kept the level it came from would sit at a depth its neighbours do not have.
        (DocumentViewModel document, _, _, LayerViewModel folder) = WithFolder();
        LayerViewModel inside = document.Layers.First(row => row.Model.ParentId == folder.Model.Id);
        LayerViewModel outside = document.Layers.First(row => row.Name == "Bottom");

        Assert.True(document.MoveLayerTo(inside, outside, DocumentViewModel.RowDrop.Above));

        Assert.Null(inside.Model.ParentId);
        Assert.Equal(0, inside.Depth);
        Assert.True(LayerOrder.IsValid(document.Document.Layers));
    }

    private static string[] Names(DocumentViewModel document) =>
        [.. document.Layers.Select(row => row.Name)];

    /// <summary>Three layers over a blank canvas, history cleared, panel order top first.</summary>
    private static (DocumentViewModel Document, LayerViewModel Top, LayerViewModel Middle, LayerViewModel Bottom) OpenStack()
    {
        var codec = new ImageCodec();
        string path = Path.Combine(ScratchDirectory(), "layer-drop.png");
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
            return (document, document.Layers[0], document.Layers[1], document.Layers[2]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>The three-layer stack with the middle one already inside a folder.</summary>
    private static (DocumentViewModel Document, LayerViewModel Top, LayerViewModel Middle, LayerViewModel Folder) WithFolder()
    {
        (DocumentViewModel document, LayerViewModel top, LayerViewModel middle, _) = OpenStack();
        document.SelectedLayer = middle;
        LayerViewModel folder = document.GroupSelectedLayers()!;
        document.History.Clear();
        return (document, top, middle, folder);
    }

    private static string ScratchDirectory()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "layer-drop-scratch");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
