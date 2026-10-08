using NaraPainter.App;
using NaraPainter.App.Services;
using NaraPainter.App.ViewModels;
using NaraPainter.Imaging.Services;
using NaraPainter.Models.Pixels;
using NaraPainter.Models.Services;
using Xunit;

namespace NaraPainter.Tests;

/// <summary>
/// Undo and redo for the operations that change the layer stack rather than its pixels: add,
/// duplicate, delete and reorder.
/// </summary>
/// <remarks>
/// These describe what the structural undo does, which until now had no coverage at all; the
/// pixel-level steps in <see cref="UndoRedoTests"/> were the only ones tested. They were written
/// against the index-based steps and were meant to stay exactly as they were through the move to
/// whole-stack snapshots, which is what makes them evidence that the move changed no behaviour.
/// </remarks>
[Collection(LocalizedState.Name)]
public class StructuralUndoTests
{
    [Fact]
    public void AddingALayerUndoesAndRedoes()
    {
        (DocumentViewModel document, _) = OpenStack();
        int before = document.Layers.Count;

        LayerViewModel added = document.AddLayer();

        Assert.Equal(before + 1, document.Layers.Count);
        Assert.Same(added, document.SelectedLayer);
        Assert.Equal(Localization.Get("Undo_NewLayer"), document.History.UndoName);

        document.Undo();

        Assert.Equal(before, document.Layers.Count);
        Assert.DoesNotContain(added, document.Layers);
        Assert.False(document.CanUndo);

        document.Redo();

        Assert.Equal(before + 1, document.Layers.Count);
        Assert.Contains(added, document.Layers);
        Assert.False(document.CanRedo);
    }

    [Fact]
    public void AddingAnAdjustmentLayerUndoesAndRedoes()
    {
        (DocumentViewModel document, _) = OpenStack();
        int before = document.Layers.Count;

        LayerViewModel added = document.AddAdjustmentLayer(AdjustmentKind.HueSaturation);

        Assert.Equal(before + 1, document.Layers.Count);
        Assert.True(added.IsAdjustment);
        Assert.Equal(
            Localization.Interpolate(Localization.Get("Undo_NewAdjustmentLayer"), Localization.Get("Adjust_HueSaturation")),
            document.History.UndoName);

        document.Undo();
        Assert.Equal(before, document.Layers.Count);

        document.Redo();
        Assert.Equal(before + 1, document.Layers.Count);
    }

    [Fact]
    public void DuplicatingALayerUndoesAndRedoes()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenStack();
        int before = document.Layers.Count;

        LayerViewModel? copy = document.DuplicateLayer(layer);

        Assert.NotNull(copy);
        Assert.Equal(before + 1, document.Layers.Count);
        Assert.Equal(Localization.Get("Undo_DuplicateLayer"), document.History.UndoName);

        // The copy is its own layer with its own pixels, not the same object twice.
        Assert.NotSame(layer.Model, copy.Model);
        Assert.False(ReferenceEquals(layer.Source, copy.Source));

        document.Undo();

        Assert.Equal(before, document.Layers.Count);
        Assert.DoesNotContain(copy, document.Layers);
        Assert.Contains(layer, document.Layers);

        document.Redo();

        Assert.Equal(before + 1, document.Layers.Count);
        Assert.Contains(copy, document.Layers);
    }

    [Fact]
    public void DeletingALayerUndoesAndRedoes()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenStack();
        int before = document.Layers.Count;

        document.DeleteLayer(layer);

        Assert.Equal(before - 1, document.Layers.Count);
        Assert.DoesNotContain(layer, document.Layers);
        Assert.Equal(Localization.Get("Undo_DeleteLayer"), document.History.UndoName);

        document.Undo();

        Assert.Equal(before, document.Layers.Count);
        Assert.Contains(layer, document.Layers);

        document.Redo();

        Assert.Equal(before - 1, document.Layers.Count);
        Assert.DoesNotContain(layer, document.Layers);
        Assert.False(document.CanRedo);
    }

    [Fact]
    public void ReorderingTheStackUndoesAndRedoes()
    {
        (DocumentViewModel document, _) = OpenStack();

        // The panel lists the top layer first, so reversing it flips the stack.
        var reversed = document.Layers.Reverse().ToList();
        string[] before = [.. document.Layers.Select(view => view.Name)];
        string[] after = [.. reversed.Select(view => view.Name)];

        document.ReorderLayers(reversed);

        Assert.Equal(after, document.Layers.Select(view => view.Name));
        Assert.Equal(Localization.Get("Undo_ReorderLayers"), document.History.UndoName);

        document.Undo();

        Assert.Equal(before, document.Layers.Select(view => view.Name));

        document.Redo();

        Assert.Equal(after, document.Layers.Select(view => view.Name));
    }

    [Fact]
    public void ReorderingWithNothingToDoRecordsNoStep()
    {
        (DocumentViewModel document, _) = OpenStack();

        document.ReorderLayers(document.Layers.ToList());

        Assert.False(document.CanUndo);
    }

    [Fact]
    public void SeveralStructuralStepsUndoInReverseOrder()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenStack();
        int original = document.Layers.Count;

        LayerViewModel added = document.AddLayer();
        document.DeleteLayer(layer);

        Assert.Equal(original, document.Layers.Count);

        document.Undo();
        Assert.Contains(layer, document.Layers);

        document.Undo();
        Assert.DoesNotContain(added, document.Layers);
        Assert.Equal(original, document.Layers.Count);
        Assert.False(document.CanUndo);

        document.Redo();
        Assert.Contains(added, document.Layers);

        document.Redo();
        Assert.DoesNotContain(layer, document.Layers);
    }

    [Fact]
    public void DeletingTheSelectedLayerMovesTheSelection()
    {
        (DocumentViewModel document, LayerViewModel layer) = OpenStack();
        document.SelectedLayer = layer;

        document.DeleteLayer(layer);

        Assert.NotSame(layer, document.SelectedLayer);
        Assert.Equal(document.Layers.FirstOrDefault(), document.SelectedLayer);
    }

    [Fact]
    public void ReplacingTheStackKeepsTheSameRowObjects()
    {
        // Whole-stack snapshots are only safe while a restore puts the same rows back: a step that
        // records against one particular row would otherwise edit an object the document no longer
        // holds, and undoing it would look like it did nothing.
        (DocumentViewModel document, LayerViewModel bottom) = OpenStack();
        LayerViewModel[] original = [.. document.Layers];

        document.ReorderLayers(document.Layers.Reverse().ToList());
        document.Undo();

        Assert.Equal(original.Length, document.Layers.Count);
        for (int i = 0; i < original.Length; i++)
        {
            Assert.Same(original[i], document.Layers[i]);
        }

        Assert.Contains(bottom, document.Layers);
    }

    /// <summary>
    /// A structural undo must not undo, or break, an unrelated property edit made after it.
    /// </summary>
    /// <remarks>
    /// The rename records its change against one particular row, so a restore that built fresh rows or
    /// wrote stale values back would leave the rename either reverted or pointing at an orphan. It sits
    /// underneath the add on the stack, so it is reached by undoing twice.
    /// </remarks>
    [Fact]
    public void AStructuralUndoLeavesAnEarlierRenameAlone()
    {
        (DocumentViewModel document, LayerViewModel top) = OpenStack();

        // Named so the first undo can be told from the second: the rename is the step underneath.
        top.Name = "Renamed";
        LayerViewModel added = document.AddLayer();

        Assert.Equal(Localization.Get("Undo_NewLayer"), document.History.UndoName);

        // Undo the add. The rename has to survive that untouched.
        document.Undo();
        Assert.DoesNotContain(added, document.Layers);
        Assert.Contains(top, document.Layers);
        Assert.Equal("Renamed", top.Name);

        // And it has to survive the redo, which restores the other stack snapshot.
        document.Redo();
        Assert.Contains(added, document.Layers);
        Assert.Equal("Renamed", top.Name);

        // The rename is still its own step underneath, and undoing it reaches the same row.
        document.Undo();
        Assert.Equal(Localization.Get("Undo_RenameLayer"), document.History.UndoName);

        document.Undo();
        Assert.Equal("Top", top.Name);
    }

    // Not covered here yet, on purpose: deleting a folder has no test because there is no way to make
    // one. The model takes a ParentId and an IsGroup, but nothing in the app creates a folder and
    // ReloadLayers is private, so a folder cannot be reached from a test without adding production code
    // just for it. docs/UNDO_REDESIGN.md records the behaviour that is missing: delete removes a
    // single row and a single layer, so a folder's children survive with a link to a folder that is
    // gone, and stop being drawn while still sitting in the document and the panel. That test belongs
    // with the grouping work, which is what makes a folder reachable.

    /// <summary>
    /// Three layers over a blank canvas with the history cleared. Returns the top row: the panel lists
    /// the stack top first, so <c>Layers[0]</c> is the top and the last row is the bottom.
    /// </summary>
    private static (DocumentViewModel Document, LayerViewModel Top) OpenStack()
    {
        var codec = new ImageCodec();
        string path = Path.Combine(ScratchDirectory(), "structural-undo.png");
        codec.Write(new PixelBuffer(16, 12), path, new ImageSaveOptions(ImageFileFormat.Png));

        try
        {
            var document = new DocumentViewModel(new ImageImporter(codec), codec, new AdjustmentFilter(), new SelectionMaskBuilder());
            document.Open(path);

            // Each add lands on top, so the panel ends up reading top first.
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
        string directory = Path.Combine(AppContext.BaseDirectory, "structural-undo-scratch");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
