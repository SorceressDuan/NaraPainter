using NaraPainter.App.Services;
using NaraPainter.Models.Layers;

namespace NaraPainter.App.ViewModels;

/// <summary>
/// Everything a structural edit needs to put the layer stack back: which rows the panel showed, in what
/// order, how they hung together, and which one was selected.
/// </summary>
/// <remarks>
/// <para>
/// <b>It holds references, never copies.</b> A snapshot records the pointers to the same
/// <see cref="LayerViewModel"/> and <see cref="Layer"/> objects the document already uses, plus a few
/// small values. Copying a layer's pixels here would cost 48 MB for one 4000 x 3000 buffer and 960 MB
/// for twenty of them, so an undo step could exhaust memory on its own. Keep it to references and
/// values.
/// </para>
/// <para>
/// Reusing the same objects is also what keeps older undo steps working. A rename records a change
/// against one particular <see cref="LayerViewModel"/>; if a restore built fresh view models, that step
/// would edit an object the document no longer holds and Ctrl+Z would look like it did nothing.
/// </para>
/// <para>
/// The fields kept here are only the ones a structural edit can disturb. Opacity, name and visibility
/// are deliberately left out: they have undo steps of their own, and writing them back from a stale
/// snapshot would let undoing a layer move silently revert a rename that happened after it.
/// </para>
/// </remarks>
internal sealed class LayerStackSnapshot
{
    private readonly List<LayerViewModel> _rows;
    private readonly List<LayerLink> _links;

    private LayerStackSnapshot(List<LayerViewModel> rows, List<LayerLink> links, LayerViewModel? selected)
    {
        _rows = rows;
        _links = links;
        Selected = selected;
    }

    /// <summary>The selected row when the snapshot was taken, or null when nothing was selected.</summary>
    public LayerViewModel? Selected { get; }

    /// <summary>How many rows the stack had. The panel should show this many again after a restore.</summary>
    public int Count => _rows.Count;

    /// <summary>Records the current stack. Call it with no edit in flight, or the state is a mixture.</summary>
    public static LayerStackSnapshot Capture(DocumentViewModel owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var rows = new List<LayerViewModel>(owner.Layers);
        var links = new List<LayerLink>(rows.Count);
        foreach (LayerViewModel row in rows) links.Add(new LayerLink(row));
        return new LayerStackSnapshot(rows, links, owner.SelectedLayer);
    }

    /// <summary>
    /// Puts the stack back. The same rows and the same model objects end up in the document, in the
    /// order they were, hanging together the way they did.
    /// </summary>
    public void Restore(DocumentViewModel owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var expected = new HashSet<LayerViewModel>(_rows);

        // Detach the rows this edit added before anything is removed: dropping a folder while its
        // children still point at it would leave them unwalkable, and the document is rebuilt below.
        foreach (LayerViewModel row in owner.Layers)
        {
            if (!expected.Contains(row)) row.Model.ParentId = null;
        }

        foreach (LayerViewModel row in owner.Layers) owner.Document.Remove(row.Model);

        foreach (LayerLink link in _links) link.Restore();

        // Rebuilt in snapshot order. The panel is top first, the document bottom first, so walking the
        // rows backwards hands the document its layers in the order it draws them.
        for (int i = _rows.Count - 1; i >= 0; i--)
        {
            owner.Document.Add(_rows[i].Model);
        }

        owner.ReplaceRows(_rows, Selected);
    }

    /// <summary>How one layer hung in the tree when the snapshot was taken.</summary>
    private readonly record struct LayerLink(
        LayerViewModel Row,
        Guid? ParentId,
        bool IsGroup,
        BlendMode BlendMode,
        byte[]? Mask)
    {
        public LayerLink(LayerViewModel row) : this(
            row,
            row.Model.ParentId,
            row.Model.IsGroup,
            row.Model.BlendMode,
            row.Model.Mask) { }

        public void Restore()
        {
            Layer model = Row.Model;
            model.ParentId = ParentId;

            // Not tree fields, but a stale value here would be visible the moment the stack comes back:
            // these are what the panel and the compositor read straight off the layer.
            model.IsGroup = IsGroup;
            model.BlendMode = BlendMode;
            model.Mask = Mask;
        }
    }
}
