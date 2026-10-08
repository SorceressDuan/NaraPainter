using NaraPainter.App;
using NaraPainter.App.Services;
using NaraPainter.App.ViewModels;
using NaraPainter.Imaging.Services;
using NaraPainter.Models.Pixels;
using NaraPainter.Models.Services;
using Xunit;

namespace NaraPainter.Tests;

/// <summary>
/// Folders: putting the selected layer into one, taking it out again, dragging a row into one, and
/// folding one away.
/// </summary>
/// <remarks>
/// Every structural one of these is a single undo step, recorded as the stack before and the stack
/// after, so the folder links come back with it.
/// </remarks>
[Collection(LocalizedState.Name)]
public class LayerGroupingTests
{
    [Fact]
    public void GroupingPutsTheSelectedLayerInANewFolder()
    {
        (DocumentViewModel document, LayerViewModel selected) = OpenStack();
        int before = document.Layers.Count;

        LayerViewModel? folder = document.GroupSelectedLayers();

        Assert.NotNull(folder);
        Assert.True(folder.IsGroup);
        Assert.Equal(before + 1, document.Layers.Count);
        Assert.Equal(folder.Model.Id, selected.Model.ParentId);
        Assert.Equal(1, selected.Depth);
        Assert.True(folder.HasChildren);
        Assert.Equal(Localization.Get("Undo_GroupLayers"), document.History.UndoName);
    }

    [Fact]
    public void GroupingUndoesAndRedoes()
    {
        (DocumentViewModel document, LayerViewModel selected) = OpenStack();
        int before = document.Layers.Count;

        LayerViewModel? folder = document.GroupSelectedLayers();

        document.Undo();

        Assert.Equal(before, document.Layers.Count);
        Assert.DoesNotContain(folder!, document.Layers);
        Assert.Null(selected.Model.ParentId);
        Assert.Equal(0, selected.Depth);

        document.Redo();

        Assert.Equal(before + 1, document.Layers.Count);
        Assert.Equal(folder!.Model.Id, selected.Model.ParentId);
        Assert.Equal(1, selected.Depth);
    }

    [Fact]
    public void GroupingWithNothingSelectedDoesNothing()
    {
        (DocumentViewModel document, _) = OpenStack();
        document.SelectedLayer = null;
        int before = document.Layers.Count;

        Assert.Null(document.GroupSelectedLayers());
        Assert.Equal(before, document.Layers.Count);
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void FoldersAreNumberedWithoutReusingAName()
    {
        (DocumentViewModel document, _) = OpenStack();

        LayerViewModel first = document.GroupSelectedLayers()!;
        LayerViewModel second = document.GroupSelectedLayers()!;

        Assert.Equal(Localization.Interpolate(Localization.Get("Layers_FolderNameFormat"), 1), first.Name);
        Assert.Equal(Localization.Interpolate(Localization.Get("Layers_FolderNameFormat"), 2), second.Name);
    }

    [Fact]
    public void UngroupingPutsTheChildrenWhereTheFolderSat()
    {
        (DocumentViewModel document, LayerViewModel selected) = OpenStack();
        LayerViewModel folder = document.GroupSelectedLayers()!;
        int before = document.Layers.Count;
        int folderIndex = document.Layers.IndexOf(folder);

        Assert.True(document.UngroupSelectedFolder());

        Assert.Equal(before - 1, document.Layers.Count);
        Assert.DoesNotContain(folder, document.Layers);
        Assert.Null(selected.Model.ParentId);
        Assert.Equal(0, selected.Depth);

        // It takes the folder's own place rather than jumping to the top or the bottom.
        Assert.Equal(folderIndex, document.Layers.IndexOf(selected));
        Assert.Equal(Localization.Get("Undo_UngroupLayers"), document.History.UndoName);
    }

    [Fact]
    public void UngroupingUndoesAndRedoes()
    {
        (DocumentViewModel document, LayerViewModel selected) = OpenStack();
        LayerViewModel folder = document.GroupSelectedLayers()!;
        int withFolder = document.Layers.Count;

        document.UngroupSelectedFolder();
        document.Undo();

        Assert.Equal(withFolder, document.Layers.Count);
        Assert.Equal(folder.Model.Id, selected.Model.ParentId);
        Assert.Equal(1, selected.Depth);

        document.Redo();

        Assert.Equal(withFolder - 1, document.Layers.Count);
        Assert.Null(selected.Model.ParentId);
    }

    [Fact]
    public void UngroupingSomethingThatIsNotAFolderIsRefused()
    {
        (DocumentViewModel document, LayerViewModel selected) = OpenStack();
        document.SelectedLayer = selected;

        Assert.False(document.UngroupSelectedFolder());
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void ARowCanBeDroppedIntoAFolder()
    {
        (DocumentViewModel document, LayerViewModel selected) = OpenStack();
        LayerViewModel folder = document.GroupSelectedLayers()!;

        // The bottom row, which is outside the folder.
        LayerViewModel outside = document.Layers[^1];
        Assert.Null(outside.Model.ParentId);

        Assert.True(document.MoveLayerInto(outside, folder));

        Assert.Equal(folder.Model.Id, outside.Model.ParentId);
        Assert.Equal(1, outside.Depth);
        Assert.Equal(Localization.Get("Undo_MoveLayer"), document.History.UndoName);

        document.Undo();

        Assert.Null(outside.Model.ParentId);
        Assert.Equal(0, outside.Depth);
    }

    [Fact]
    public void AFolderCannotBeDroppedIntoItselfOrItsOwnChild()
    {
        (DocumentViewModel document, LayerViewModel selected) = OpenStack();
        LayerViewModel outer = document.GroupSelectedLayers()!;

        Assert.False(document.MoveLayerInto(outer, outer), "a folder cannot hold itself");

        // Nest one folder inside the other, then try to close the loop.
        document.SelectedLayer = selected;
        LayerViewModel inner = document.GroupSelectedLayers()!;
        Assert.Equal(outer.Model.Id, inner.Model.ParentId);

        Assert.False(document.MoveLayerInto(outer, inner), "a folder cannot be moved inside its own child");
        Assert.Null(outer.Model.ParentId);
    }

    [Fact]
    public void ARowCannotBeDroppedIntoAPlainLayer()
    {
        (DocumentViewModel document, LayerViewModel selected) = OpenStack();
        LayerViewModel plain = document.Layers[^1];

        Assert.False(document.MoveLayerInto(selected, plain));
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void MovingARowToTheTopLevelIsRefusedWhenItIsAlreadyThere()
    {
        (DocumentViewModel document, LayerViewModel selected) = OpenStack();

        Assert.False(document.MoveLayerInto(selected, null));
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void FoldingAFolderHidesWhatIsInsideItWithoutRemovingIt()
    {
        (DocumentViewModel document, LayerViewModel selected) = OpenStack();
        LayerViewModel folder = document.GroupSelectedLayers()!;

        Assert.True(selected.IsRowVisible);

        document.ToggleCollapsed(folder);

        Assert.True(folder.IsCollapsed);
        Assert.False(selected.IsRowVisible, "the row inside a folded folder should be hidden");

        // The row is hidden, not gone: the document still has it.
        Assert.Contains(selected, document.Layers);
        Assert.Equal(folder.Model.Id, selected.Model.ParentId);

        document.ToggleCollapsed(folder);

        Assert.False(folder.IsCollapsed);
        Assert.True(selected.IsRowVisible);
    }

    [Fact]
    public void FoldingIsNotAnUndoStep()
    {
        // What is folded is how the document is being looked at, so it stays out of the history.
        (DocumentViewModel document, _) = OpenStack();
        LayerViewModel folder = document.GroupSelectedLayers()!;
        string? undoName = document.History.UndoName;

        document.ToggleCollapsed(folder);

        Assert.Equal(undoName, document.History.UndoName);
    }

    [Fact]
    public void FoldingAFolderMovesTheSelectionOutOfSight()
    {
        (DocumentViewModel document, LayerViewModel selected) = OpenStack();
        LayerViewModel folder = document.GroupSelectedLayers()!;

        // Grouping selects the folder; select the row inside it, then fold.
        document.SelectedLayer = selected;
        document.ToggleCollapsed(folder);

        Assert.Same(folder, document.SelectedLayer);
    }

    [Fact]
    public void FoldingSomethingThatIsNotAFolderDoesNothing()
    {
        (DocumentViewModel document, LayerViewModel selected) = OpenStack();

        document.ToggleCollapsed(selected);

        Assert.False(selected.IsCollapsed);
        Assert.True(selected.IsRowVisible);
    }

    [Fact]
    public void AFoldedFoldersChildrenAreDrawnFlatAndShowAgainAfterUndo()
    {
        // Hiding rows must not disturb what the document draws: the layers are still composited.
        (DocumentViewModel document, LayerViewModel selected) = OpenStack();
        LayerViewModel folder = document.GroupSelectedLayers()!;

        document.ToggleCollapsed(folder);

        PixelBuffer flattened = document.Flatten();
        Assert.NotNull(flattened);

        document.Undo();

        Assert.DoesNotContain(folder, document.Layers);
        Assert.Equal(0, selected.Depth);
    }

    /// <summary>Three layers over a blank canvas, history cleared, panel order top first.</summary>
    private static (DocumentViewModel Document, LayerViewModel Top) OpenStack()
    {
        var codec = new ImageCodec();
        string path = Path.Combine(ScratchDirectory(), "layer-grouping.png");
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
        string directory = Path.Combine(AppContext.BaseDirectory, "layer-grouping-scratch");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
