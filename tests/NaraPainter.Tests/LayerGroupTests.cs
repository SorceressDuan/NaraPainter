using NaraPainter.Models.Adjustments;
using NaraPainter.Models.Documents;
using NaraPainter.Models.Layers;
using NaraPainter.Models.Pixels;
using Xunit;

namespace NaraPainter.Tests;

/// <summary>
/// Folder nesting in the model: how a folder's visibility and opacity reach what is inside it, and
/// what the flatten does with a tree.
/// </summary>
/// <remarks>
/// The shape being checked is upstream's (<c>legacy/Compositor/Document/LayerGroups.swift</c>): a
/// folder is pass-through and is never composited as a unit, so its opacity multiplies into each
/// child rather than dimming a group buffer as a whole.
/// </remarks>
public class LayerGroupTests
{
    private const int Size = 8;

    [Fact]
    public void AFlatDocumentIsUnchangedByTheHierarchyWalk()
    {
        // The regression net for everything that was already working: no folder links means the walk
        // has to produce exactly the list order and the layers' own values.
        CanvasDocument document = Document();
        Layer bottom = Solid(document, 128, 0, 0, "bottom");
        Layer top = Solid(document, 0, 96, 0, "top");

        var seen = new List<(string Name, bool Visible, double Opacity)>();
        LayerOrder.ForEach(document.Layers, (layer, visible, opacity) => seen.Add((layer.Name, visible, opacity)));

        Assert.Equal(["bottom", "top"], seen.Select(entry => entry.Name));
        Assert.All(seen, entry => Assert.True(entry.Visible));
        Assert.All(seen, entry => Assert.Equal(1, entry.Opacity));
    }

    [Fact]
    public void AChildIsVisitedRightAfterItsFolder()
    {
        CanvasDocument document = Document();
        Layer child = Solid(document, 200, 0, 0, "child");
        Layer folder = Folder(document, "folder");
        Layer top = Solid(document, 0, 0, 200, "top");

        child.ParentId = folder.Id;

        var order = new List<string>();
        LayerOrder.ForEach(document.Layers, (layer, _, _) => order.Add(layer.Name));

        Assert.Equal(["folder", "child", "top"], order);
    }

    [Fact]
    public void AHiddenFolderHidesWhatIsInsideItButNotTheFlag()
    {
        CanvasDocument document = Document();
        Layer child = Solid(document, 200, 0, 0, "child");
        Layer folder = Folder(document, "folder");
        folder.IsVisible = false;
        child.ParentId = folder.Id;

        Assert.True(child.IsVisible, "the layer's own flag should not have been touched");

        Layer[] drawn = [.. LayerOrder.Drawn(document.Layers)];
        Assert.DoesNotContain(drawn, layer => layer.Name == "child");

        // And the flatten agrees: the hidden child contributes nothing.
        Assert.Equal(new PixelBuffer(Size, Size).Data, document.Flatten().Data);
    }

    [Fact]
    public void FolderOpacityMultipliesIntoItsChildren()
    {
        CanvasDocument document = Document();
        Layer folder = Folder(document, "folder");
        Layer child = Solid(document, 255, 255, 255, "child");
        child.ParentId = folder.Id;

        folder.Opacity = 0.5;
        child.Opacity = 0.5;

        // This is what the folders above a layer contribute; the layer's own value is applied when it
        // is composited, so it is not part of this number.
        Assert.Equal(0.5, LayerOrder.EffectiveOpacity(document.Layers, child), 3);

        // Folded in at draw time, 50% inside 50% shows at 25%: a quarter of white over nothing.
        PixelBuffer flattened = document.Flatten();
        Assert.InRange(flattened[0, 0].A, 62, 66);
    }

    [Fact]
    public void OpacityMultipliesThroughNestedFolders()
    {
        CanvasDocument document = Document();
        Layer outer = Folder(document, "outer");
        Layer inner = Folder(document, "inner");
        Layer child = Solid(document, 255, 255, 255, "child");

        inner.ParentId = outer.Id;
        child.ParentId = inner.Id;
        outer.Opacity = 0.5;
        inner.Opacity = 0.5;

        Assert.Equal(0.25, LayerOrder.EffectiveOpacity(document.Layers, child), 3);
    }

    [Fact]
    public void AFolderIsNeverCompositedAsAUnit()
    {
        // A folder draws nothing by itself: it holds no pixels, and the flatten has to skip it rather
        // than lay down an empty canvas-sized buffer.
        CanvasDocument document = Document();
        Layer theFolder = Folder(document, "folder");
        Layer child = Solid(document, 255, 0, 0, "child");
        child.ParentId = theFolder.Id;

        PixelBuffer flattened = document.Flatten();

        Assert.Equal(255, flattened[0, 0].R);
        Assert.Equal(255, flattened[0, 0].A);
        Assert.Null(theFolder.Pixels);
    }

    [Fact]
    public void AChildDrawsOverWhatIsBelowItJustLikeAnyOtherLayer()
    {
        CanvasDocument document = Document();
        Layer bottom = Solid(document, 0, 0, 255, "bottom");
        Layer folder = Folder(document, "folder");
        Layer child = Solid(document, 255, 0, 0, "child");
        child.ParentId = folder.Id;

        PixelBuffer flattened = document.Flatten();

        // The child is above the blue layer in stack order, so red wins: a folder does not isolate it.
        Assert.Equal(255, flattened[0, 0].R);
        Assert.Equal(0, flattened[0, 0].B);
    }

    [Fact]
    public void AnEmptyFolderChangesNothing()
    {
        CanvasDocument document = Document();
        Layer bottom = Solid(document, 40, 60, 80);
        Folder(document, "empty");

        PixelBuffer flattened = document.Flatten();

        Assert.Equal(40, flattened[0, 0].R);
        Assert.Equal(60, flattened[0, 0].G);
        Assert.Equal(80, flattened[0, 0].B);
    }

    [Fact]
    public void ACloneKeepsItsFolder()
    {
        CanvasDocument document = Document();
        Layer folder = Folder(document, "folder");
        Layer child = Solid(document, 10, 20, 30, "child");
        child.ParentId = folder.Id;

        Layer copy = child.Clone();

        Assert.Equal(folder.Id, copy.ParentId);
        Assert.False(copy.IsGroup);
        Assert.Equal(child.Id, child.Id);
        Assert.NotEqual(child.Id, copy.Id);
    }

    [Fact]
    public void ACycleIsRejected()
    {
        CanvasDocument document = Document();
        Layer a = Folder(document, "a");
        Layer b = Folder(document, "b");
        a.ParentId = b.Id;
        b.ParentId = a.Id;

        Assert.False(LayerOrder.IsValid(document.Layers));
    }

    [Fact]
    public void ALayerCannotHangOffSomethingThatIsNotAFolder()
    {
        CanvasDocument document = Document();
        Layer plain = Solid(document, 1, 1, 1, "plain");
        Layer orphan = Solid(document, 2, 2, 2, "orphan");
        orphan.ParentId = plain.Id;

        Assert.False(LayerOrder.IsValid(document.Layers));
    }

    [Fact]
    public void ALinkToALayerThatIsNotThereIsRejected()
    {
        CanvasDocument document = Document();
        Layer child = Solid(document, 1, 1, 1, "child");
        child.ParentId = Guid.NewGuid();

        Assert.False(LayerOrder.IsValid(document.Layers));
    }

    [Fact]
    public void AWellFormedTreeIsAccepted()
    {
        CanvasDocument document = Document();
        Layer outer = Folder(document, "outer");
        Layer inner = Folder(document, "inner");
        Layer child = Solid(document, 1, 2, 3, "child");
        inner.ParentId = outer.Id;
        child.ParentId = inner.Id;
        Solid(document, 4, 5, 6, "top");

        Assert.True(LayerOrder.IsValid(document.Layers));
    }

    [Fact]
    public void AnAdjustmentInsideAHiddenFolderDoesNotForceTheCpuPath()
    {
        CanvasDocument document = Document();
        Layer folder = Folder(document, "folder");
        var adjustment = new Layer("adjust") { Adjustment = new BrightnessContrastSettings(), ParentId = folder.Id };
        document.Add(adjustment);

        Assert.True(NaraPainter.Compositing.Rendering.CpuCompositor.NeedsFullDocumentPass(document));

        folder.IsVisible = false;
        Assert.False(NaraPainter.Compositing.Rendering.CpuCompositor.NeedsFullDocumentPass(document));
    }

    private static CanvasDocument Document() => new(Size, Size);

    private static Layer Folder(CanvasDocument document, string name)
    {
        var folder = new Layer(name) { IsGroup = true };
        document.Add(folder);
        return folder;
    }

    private static Layer Solid(CanvasDocument document, byte red, byte green, byte blue, string? name = null)
    {
        var buffer = new PixelBuffer(Size, Size);
        for (int i = 0; i < buffer.Data.Length; i += 4)
        {
            buffer.Data[i] = red;
            buffer.Data[i + 1] = green;
            buffer.Data[i + 2] = blue;
            buffer.Data[i + 3] = 255;
        }

        var layer = new Layer(name ?? $"solid-{document.Layers.Count}") { Pixels = buffer };
        document.Add(layer);
        return layer;
    }
}
