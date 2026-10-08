using System.Collections.ObjectModel;
using System.Diagnostics;
using NaraPainter.App.Services;
using NaraPainter.Compositing.Rendering;
using NaraPainter.Models.Adjustments;
using NaraPainter.Models.Blending;
using NaraPainter.Models.Documents;
using NaraPainter.Models.Layers;
using NaraPainter.Models.Pixels;
using NaraPainter.Models.Services;
using NaraPainter.Models.Text;

namespace NaraPainter.App.ViewModels;

/// <summary>
/// The open document and everything the shell binds to. Layer order differs on purpose: the panel
/// lists top first, the document stores bottom first, and the two are kept in step here.
/// </summary>
public sealed class DocumentViewModel : ObservableObject
{
    public const int DefaultCanvasWidth = 1280;
    public const int DefaultCanvasHeight = 800;

    private readonly ImageImporter _importer;
    private readonly IImageCodec _codec;
    private readonly IAdjustmentFilter _filter;

    private CanvasDocument _document;
    private LayerViewModel? _selectedLayer;
    private string _status = Strings.StatusReady;
    private double _zoom = 1;

    public DocumentViewModel(ImageImporter importer, IImageCodec codec, IAdjustmentFilter filter, ISelectionMaskBuilder masks)
    {
        _importer = importer;
        _codec = codec;
        _filter = filter;
        _document = CreateDocument(DefaultCanvasWidth, DefaultCanvasHeight);
        Adjustment = new AdjustmentViewModel();
        Selection = new SelectionViewModel(this, masks);
        MaskBrush = new MaskBrushViewModel(this, masks);
        SpotHeal = new SpotHealViewModel(this);
        ContentFill = new ContentAwareFillViewModel(this, masks);
        Transform = new TransformViewModel(this);
        ColorPicker = new ColorPickerViewModel(this);

        Layers.CollectionChanged += (_, _) => OnPropertyChanged(nameof(LayerCountLabel));

        History.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
        };

        ReloadLayers();
    }

    /// <summary>Raised when pixels, layer settings or the stack changed: the canvas repaints.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when a different document is loaded, so the canvas can take the new one.</summary>
    public event EventHandler<CanvasDocument>? DocumentReplaced;

    public UndoStack History { get; } = new();

    /// <summary>Crop, rotate, flip and canvas resize. Created with the document, like the other tools.</summary>
    public TransformViewModel Transform { get; }

    /// <summary>Reads the colour under a point on the canvas.</summary>
    public ColorPickerViewModel ColorPicker { get; }

    public AdjustmentViewModel Adjustment { get; }

    public SelectionViewModel Selection { get; }

    /// <summary>Paints the selected layer's mask; the canvas routes pointer drags here when active.</summary>
    public MaskBrushViewModel MaskBrush { get; }

    /// <summary>Rebuilds the pixels under a drag from the texture around them.</summary>
    public SpotHealViewModel SpotHeal { get; }

    /// <summary>Fills the selection, or the masked-out pixels, from what surrounds them.</summary>
    public ContentAwareFillViewModel ContentFill { get; }

    public ObservableCollection<LayerViewModel> Layers { get; } = [];

    /// <summary>
    /// The rows the layer panel shows: <see cref="Layers"/> minus whatever is inside a folded folder.
    /// </summary>
    /// <remarks>
    /// Folded rows are kept out of the list rather than hidden with a collapsed visibility, because a
    /// ListView that recycles containers while its items flip to <c>Collapsed</c> ends up parking them
    /// far outside the viewport; after a drag, rows were left drawn on top of each other. Not being in
    /// the list means there is nothing to recycle.
    /// </remarks>
    public ObservableCollection<LayerViewModel> DisplayRows { get; } = [];

    public CanvasDocument Document => _document;

    public IImageCodec Codec => _codec;

    /// <summary>The filter adjustments run through. The canvas shares it so preview and export agree.</summary>
    public IAdjustmentFilter Filter => _filter;

    public bool CanUndo => History.CanUndo;

    public bool CanRedo => History.CanRedo;

    public bool HasSelection => _selectedLayer is not null;

    public string Title => _document.FilePath is null ? Strings.AppUntitled : Path.GetFileName(_document.FilePath);

    public string WindowTitle => Localization.Interpolate(Strings.AppWindowTitle, Strings.AppTitle, Title)
        + (_document.IsDirty ? Strings.AppModifiedMark : string.Empty);

    public string SizeLabel => $"{_document.Width} × {_document.Height}";

    public string LayerCountLabel => Layers.Count == 1
        ? Strings.StatusOneLayer
        : Localization.Interpolate(Strings.StatusLayerCount, Layers.Count);

    public string ZoomLabel => $"{_zoom * 100:0}%";

    /// <summary>1.0 is 100%. Mirrors the canvas view, which owns the real zoom level.</summary>
    public double Zoom => _zoom;

    public string SuggestedExportName =>
        $"{Path.GetFileNameWithoutExtension(_document.FilePath) ?? Strings.AppUntitled}-export";

    public string Status
    {
        get => _status;
        internal set => SetProperty(ref _status, value);
    }

    public LayerViewModel? SelectedLayer
    {
        get => _selectedLayer;
        set
        {
            if (!SetProperty(ref _selectedLayer, value)) return;

            Adjustment.Target = value;
            Selection.Refresh();
            MaskBrush.Refresh();
            SpotHeal.Refresh();
            ContentFill.Refresh();
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    /// <summary>
    /// Puts the selected layer into a new folder. The folder takes the selected layer's place among its
    /// siblings, so grouping does not move anything up or down the stack.
    /// </summary>
    public LayerViewModel? GroupSelectedLayers()
    {
        if (_selectedLayer is not { } selected) return null;

        LayerStackSnapshot before = LayerStackSnapshot.Capture(this);
        var folder = new Layer(UniqueFolderName()) { IsGroup = true, ParentId = selected.Model.ParentId };

        // Folders hold no pixels of their own; what they contribute is what is inside them.
        _document.Add(folder);
        var view = new LayerViewModel(folder, this, _filter);

        int panelIndex = Math.Max(Layers.IndexOf(selected), 0);
        Layers.Insert(panelIndex, view);
        selected.Model.ParentId = folder.Id;
        _collapsed.Remove(folder.Id);

        RefreshPlacement();
        SelectedLayer = view;
        PushStructuralStep(Strings.UndoGroupLayers, before);
        NotifyChanged();
        Status = Localization.Interpolate(Strings.StatusLayersGrouped, folder.Name);
        return view;
    }

    /// <summary>
    /// Dissolves the selected folder: what was inside it takes its place among its own siblings, in the
    /// order it had, and the folder goes. Its opacity and blend mode go with it, as Photoshop's Ungroup
    /// does.
    /// </summary>
    public bool UngroupSelectedFolder()
    {
        if (_selectedLayer is not { } folder || !folder.IsGroup) return false;
        if (!Layers.Contains(folder)) return false;

        LayerStackSnapshot before = LayerStackSnapshot.Capture(this);
        Guid folderId = folder.Model.Id;
        Guid? grandparent = folder.Model.ParentId;

        // Collected before anything moves: the folder is about to leave the list, and its children have
        // to come out in the order they were in.
        var children = new List<LayerViewModel>();
        foreach (LayerViewModel row in Layers)
        {
            if (row.Model.ParentId == folderId) children.Add(row);
        }

        int slot = Layers.IndexOf(folder);
        Layers.Remove(folder);
        _document.Remove(folder.Model);

        // Each child takes the folder's place, so they end up where it sat, in their own order. They are
        // still in the list, so they move rather than go back in.
        for (int i = 0; i < children.Count; i++)
        {
            LayerViewModel child = children[i];
            child.Model.ParentId = grandparent;
            Layers.Move(Layers.IndexOf(child), slot + i);
            _document.Move(child.Model, DocumentIndex(slot + i));
        }

        _collapsed.Remove(folderId);
        RefreshPlacement();
        SelectedLayer = children.Count > 0 ? children[0] : Layers.FirstOrDefault();
        PushStructuralStep(Strings.UndoUngroupLayers, before);
        NotifyChanged();
        Status = Localization.Interpolate(Strings.StatusLayersUngrouped, folder.Name);
        return true;
    }

    /// <summary>
    /// Moves a layer into a folder, or out to the top level when <paramref name="parent"/> is null. Used
    /// by a drag in the layers panel.
    /// </summary>
    public bool MoveLayerInto(LayerViewModel? row, LayerViewModel? parent)
    {
        if (row is null || ReferenceEquals(row, parent)) return false;
        if (parent is not null && !parent.IsGroup) return false;

        Guid? target = parent?.Model.Id;
        if (row.Model.ParentId == target) return false;

        // A folder cannot be dropped inside itself or anything it already holds, or the tree would
        // stop being walkable.
        if (parent is not null && IsDescendantOf(parent, row)) return false;

        LayerStackSnapshot before = LayerStackSnapshot.Capture(this);
        row.Model.ParentId = target;
        if (parent is not null) _collapsed.Remove(parent.Model.Id);

        RefreshPlacement();
        SelectedLayer = row;
        PushStructuralStep(Strings.UndoMoveLayer, before);
        NotifyChanged();
        Status = Localization.Interpolate(Strings.StatusLayerMoved, row.Name);
        return true;
    }

    /// <summary>True when <paramref name="candidate"/> sits somewhere inside <paramref name="folder"/>.</summary>
    private bool IsDescendantOf(LayerViewModel candidate, LayerViewModel folder)
    {
        Guid folderId = folder.Model.Id;
        Guid? parent = candidate.Model.ParentId;
        for (int depth = 0; parent is { } id && depth <= LayerOrder.MaxDepth; depth++)
        {
            if (id == folderId) return true;
            parent = FindRowModel(id)?.ParentId;
        }

        return false;
    }

    private Layer? FindRowModel(Guid id)
    {
        foreach (LayerViewModel row in Layers)
        {
            if (row.Model.Id == id) return row.Model;
        }

        return null;
    }

    /// <summary>"Folder 1", "Folder 2"… skipping the numbers already in use.</summary>
    private string UniqueFolderName()
    {
        var taken = new HashSet<string>();
        foreach (LayerViewModel row in Layers) taken.Add(row.Name);

        for (int number = 1; ; number++)
        {
            string name = Localization.Interpolate(Strings.LayersFolderNameFormat, number);
            if (!taken.Contains(name)) return name;
        }
    }

    /// <summary>
    /// Folds a folder away, or opens it again. Folded folders are kept by id rather than in the model:
    /// what is folded is how the panel is being looked at, not something the document is.
    /// </summary>
    public void ToggleCollapsed(LayerViewModel? folder)
    {
        if (folder is not { IsGroup: true }) return;

        if (!_collapsed.Remove(folder.Model.Id)) _collapsed.Add(folder.Model.Id);

        // Selecting something that just went out of sight would leave the panel with no highlighted row.
        if (_selectedLayer is not null && !IsRowVisible(_selectedLayer))
        {
            SelectedLayer = folder;
        }

        RefreshPlacement();
        FolderCollapsed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when a folder is folded or opened, so the panel can redraw its rows.</summary>
    public event EventHandler? FolderCollapsed;

    /// <summary>
    /// The row a drag started on, remembered for the length of the drag. The list moves rows around
    /// itself but has no notion of dropping one inside another, so the panel needs to know which row is
    /// in flight to turn a drop onto a folder into a reparent.
    /// </summary>
    public LayerViewModel? DraggedRow { get; set; }

    /// <summary>
    /// Whether a row should be shown: false when any folder above it is folded.
    /// </summary>
    private bool IsRowVisible(LayerViewModel row)
    {
        Guid? parent = row.Model.ParentId;
        for (int depth = 0; parent is { } id && depth <= LayerOrder.MaxDepth; depth++)
        {
            if (_collapsed.Contains(id)) return false;
            parent = FindRowModel(id)?.ParentId;
        }

        return true;
    }

    public void SetZoom(double zoom)
    {
        if (!double.IsFinite(zoom) || zoom <= 0) return;
        if (!SetProperty(ref _zoom, zoom, nameof(Zoom))) return;

        OnPropertyChanged(nameof(ZoomLabel));
    }

    public void Open(string path)
    {
        var stopwatch = Stopwatch.StartNew();
        CanvasDocument document = _importer.ReadDocument(path);

        // Replacing the picture is undoable once there is a picture to go back to. The blank canvas
        // the window opens with is not one: nothing has been done to it yet, so recording an undo
        // step for the first import would mean the first Ctrl+Z after opening emptied the window.
        bool replacesSomething = !_isStartupCanvas;
        TakeDocument(document, stopwatch, Path.GetFileName(path), withUndo: replacesSomething);
        _isStartupCanvas = false;
    }

    /// <summary>
    /// Swaps in a document read from disk. Shared by the open dialog and the drop target so both
    /// behave the same, including leaving one undo step behind.
    /// </summary>
    private void TakeDocument(CanvasDocument document, Stopwatch stopwatch, string fileName, bool withUndo)
    {
        CanvasDocument previous = _document;
        _document = document;

        // The history belongs to the document that was just replaced.
        History.Clear();

        if (withUndo)
        {
            History.Push(new DelegateAction(
                Localization.Interpolate(Strings.UndoOpenImage, fileName),
                () => SwapDocument(previous),
                () => SwapDocument(document)));
        }

        ReloadLayers();
        Status = Localization.Interpolate(Strings.StatusOpened, fileName, SizeLabel, stopwatch.ElapsedMilliseconds);
    }

    internal void SwapDocument(CanvasDocument document)
    {
        _document = document;
        ReloadLayers();
    }

    /// <summary>True until the first picture is imported, which is when undo starts to mean something.</summary>
    private bool _isStartupCanvas = true;

    /// <summary>
    /// Folders the panel is currently showing folded, by layer id. Deliberately not part of the model:
    /// what is folded is a way of looking at the document, so it stays out of the undo stack and out of
    /// anything that gets saved.
    /// </summary>
    private readonly HashSet<Guid> _collapsed = [];

    public LayerViewModel AddLayer()
    {
        LayerStackSnapshot before = LayerStackSnapshot.Capture(this);
        Layer layer = _document.AddBlank(Localization.Interpolate(Strings.LayersNameFormat, _document.Layers.Count + 1));
        var view = new LayerViewModel(layer, this, _filter);
        Layers.Insert(0, view);
        RefreshPlacement();
        SelectedLayer = view;
        PushStructuralStep(Strings.UndoNewLayer, before);
        NotifyChanged();
        Status = Localization.Interpolate(Strings.StatusLayerAdded, layer.Name);
        return view;
    }

    public LayerViewModel AddAdjustmentLayer(AdjustmentKind kind)
    {
        LayerStackSnapshot before = LayerStackSnapshot.Capture(this);
        AdjustmentSettings settings = AdjustmentKinds.Create(kind);
        string name = AdjustmentKinds.Name(kind);
        Layer layer = _document.Add(new Layer(name) { Adjustment = settings });
        var view = new LayerViewModel(layer, this, _filter);
        Layers.Insert(0, view);
        RefreshPlacement();
        SelectedLayer = view;
        PushStructuralStep(Localization.Interpolate(Strings.UndoNewAdjustmentLayer, name), before);
        NotifyChanged();
        Status = Localization.Interpolate(Strings.StatusAdjustmentLayerAdded, name);
        return view;
    }

    public LayerViewModel ImportLayer(string path)
    {
        LayerStackSnapshot before = LayerStackSnapshot.Capture(this);
        Layer layer = _importer.ReadLayer(path);

        // Layers live at canvas resolution; the canvas scales an image that came in at another size.
        if (layer.Pixels is not null) layer.Pixels = _document.Fit(layer.Pixels);

        _document.Add(layer);
        var view = new LayerViewModel(layer, this, _filter);
        Layers.Insert(0, view);
        RefreshPlacement();
        SelectedLayer = view;
        PushStructuralStep(Strings.UndoImportLayer, before);
        NotifyChanged();
        Status = Localization.Interpolate(Strings.StatusImported, Path.GetFileName(path));
        return view;
    }

    /// <summary>
    /// Draws a line of text into a new layer whose top-left sits at the point given, in document
    /// pixels. The pixels are canvas-sized, so nothing resamples them on the way to the canvas or the
    /// export. Returns null when there is nothing to draw or the rasterizer produced no pixels.
    /// </summary>
    public LayerViewModel? AddTextLayer(TextStyle style, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(style);
        if (!style.IsDrawable) return null;

        LayerStackSnapshot before = LayerStackSnapshot.Capture(this);
        PixelBuffer? pixels = TextRasterizer.Render(style, _document.Width, _document.Height, x, y);
        if (pixels is null) return null;

        var layer = new Layer(TextLayerName(style.Content)) { Pixels = pixels };
        _document.Add(layer);
        var view = new LayerViewModel(layer, this, _filter);
        Layers.Insert(0, view);
        RefreshPlacement();
        SelectedLayer = view;
        PushStructuralStep(Strings.UndoAddTextLayer, before);
        NotifyChanged();
        Status = Strings.TextAdded;
        return view;
    }

    /// <summary>The layer's name: the first few characters as typed, or plain "Text" when it is blank.</summary>
    private static string TextLayerName(string content)
    {
        string flattened = content.ReplaceLineEndings(" ").Trim();
        if (flattened.Length == 0) return Strings.TextLayerDefaultName;

        int take = Math.Min(flattened.Length, 24);
        string name = flattened[..take];
        return take < flattened.Length ? name + "…" : name;
    }

    public LayerViewModel? DuplicateLayer(LayerViewModel? layer)
    {
        if (layer is null) return null;

        LayerStackSnapshot before = LayerStackSnapshot.Capture(this);
        Layer clone = layer.Model.Clone();
        clone.Name = Localization.Interpolate(Strings.LayersCopyNameFormat, layer.Model.Name);

        // The copy keeps the original pixels and re-runs the same adjustment stack over them.
        clone.Pixels = layer.Source?.Clone();

        int panelIndex = Math.Max(Layers.IndexOf(layer), 0);
        var view = new LayerViewModel(clone, this, _filter);
        view.CopyAdjustmentsFrom(layer);

        _document.Add(clone);
        _document.Move(clone, DocumentIndex(panelIndex));
        Layers.Insert(panelIndex, view);
        RefreshPlacement();
        SelectedLayer = view;
        PushStructuralStep(Strings.UndoDuplicateLayer, before);
        NotifyChanged();
        Status = Localization.Interpolate(Strings.StatusLayerDuplicated, layer.Name);
        return view;
    }

    /// <summary>
    /// Removes a layer, or a whole folder with everything inside it. Taking the children along is the
    /// point: a child left behind would keep a link to a folder that is gone, which stops it being drawn
    /// while it still sits in the document and the panel.
    /// </summary>
    public void DeleteLayer(LayerViewModel? layer)
    {
        if (layer is null) return;
        if (!Layers.Contains(layer)) return;

        LayerStackSnapshot before = LayerStackSnapshot.Capture(this);
        int removed = 0;
        foreach (LayerViewModel row in Subtree(layer))
        {
            if (Layers.Remove(row)) removed++;
            _document.Remove(row.Model);
        }

        // The children are gone from the document, so nothing should still point at the folder.
        layer.Model.ParentId = null;

        RefreshPlacement();
        SelectedLayer = Layers.FirstOrDefault();
        PushStructuralStep(Strings.UndoDeleteLayer, before);
        NotifyChanged();
        Status = Localization.Interpolate(Strings.StatusLayerDeleted, layer.Name);
    }

    /// <summary>The layer and everything under it, deepest last.</summary>
    private IEnumerable<LayerViewModel> Subtree(LayerViewModel root)
    {
        var found = new List<LayerViewModel> { root };
        for (int i = 0; i < found.Count; i++)
        {
            Guid id = found[i].Model.Id;
            foreach (LayerViewModel candidate in Layers)
            {
                if (candidate.Model.ParentId == id && !found.Contains(candidate)) found.Add(candidate);
            }
        }

        return found;
    }

    /// <summary>
    /// Takes the order the layers panel ended up in, top first, and records the move. The panel only
    /// lists what is on show, so rows inside a folded folder keep the place they already had.
    /// </summary>
    public void ReorderLayers(IReadOnlyList<LayerViewModel> panelOrder)
    {
        var before = Layers.ToList();
        if (VisibleInPanel(before).SequenceEqual(panelOrder)) return;

        List<LayerViewModel> after = ApplyPanelOrder(panelOrder);
        RefreshPlacement();
        History.Push(new DelegateAction(
            Strings.UndoReorderLayers,
            () => ApplyPanelOrder(VisibleInPanel(before)),
            () => ApplyPanelOrder(VisibleInPanel(after))));
        Status = Strings.StatusLayersReordered;
    }

    private static List<LayerViewModel> VisibleInPanel(IReadOnlyList<LayerViewModel> rows)
    {
        var visible = new List<LayerViewModel>(rows.Count);
        foreach (LayerViewModel row in rows)
        {
            if (row.IsRowVisible) visible.Add(row);
        }

        return visible;
    }

    /// <summary>
    /// Merges the selected layer with the one directly below it, in the same folder. Refused when there
    /// is nothing below it, which is what makes the command do nothing rather than swallow the document.
    /// </summary>
    public bool MergeDown() => Merge(Strings.UndoMergeDown, PlanMergeDown());

    /// <summary>Merges a folder and everything inside it into one layer in the folder's place.</summary>
    public bool MergeGroup() => Merge(Strings.UndoMergeGroup, PlanMergeGroup());

    /// <summary>
    /// Composites the planned layers into one and swaps them for it, as a single undo step.
    /// </summary>
    /// <remarks>
    /// The result is what the canvas showed for those layers: blend modes, opacity, masks and the
    /// adjustment layers among them are all baked in. What was below them is not, so a merge can never
    /// change how the rest of the document looks.
    /// </remarks>
    private bool Merge(string name, IReadOnlyList<Layer>? plan)
    {
        // A folder on its own has nothing to composite; one child is still a real merge, because the
        // folder's own opacity and blend mode are baked into the result.
        if (plan is not { Count: >= 1 }) return false;
        if (plan.Count == 1 && !plan[0].IsGroup) return false;

        LayerStackSnapshot before = LayerStackSnapshot.Capture(this);
        Layer top = plan[^1];
        Layer folder = top.IsGroup ? top : plan[0];
        Layer merged = new(top.Name) { Pixels = Composite(plan), ParentId = top.ParentId };
        bool mergingFolder = plan.Any(layer => layer.IsGroup);

        // A merge down takes exactly the two layers planned. A folder merge also takes away every folder
        // nested inside it, which holds no pixels but would otherwise be left on the panel with nothing
        // in it.
        var dying = new HashSet<Layer>(plan);
        if (mergingFolder)
        {
            foreach (Layer layer in _document.Layers)
            {
                if (layer.IsGroup && layer.Id != folder.Id && IsInside(layer, folder)) dying.Add(layer);
            }
        }
        var view = new LayerViewModel(merged, this, _filter);
        int slot = int.MaxValue;
        for (int i = 0; i < Layers.Count; i++)
        {
            if (dying.Contains(Layers[i].Model) && i < slot) slot = i;
        }

        for (int i = Layers.Count - 1; i >= 0; i--)
        {
            if (dying.Contains(Layers[i].Model)) Layers.RemoveAt(i);
        }

        foreach (Layer layer in dying) _document.Remove(layer);

        _document.Add(merged);
        Layers.Insert(Math.Clamp(slot, 0, Layers.Count), view);
        SortDocumentLikeRows();

        RefreshPlacement();
        SelectedLayer = view;
        PushStructuralStep(name, before);
        NotifyChanged();
        Status = Localization.Interpolate(Strings.StatusLayersMerged, merged.Name);
        return true;
    }

    /// <summary>Composites a planned set of layers the way the canvas draws them.</summary>
    private PixelBuffer Composite(IReadOnlyList<Layer> layers)
    {
        var wanted = new HashSet<Layer>(layers);
        var canvas = new PixelBuffer(_document.Width, _document.Height);

        // Walks the whole stack so a folder's opacity and visibility still reach what is inside it, but
        // only the planned layers contribute.
        LayerOrder.ForEach(_document.Layers, (layer, visible, opacity) =>
        {
            if (!wanted.Contains(layer) || layer.IsGroup || !visible || opacity <= 0) return;

            PixelBuffer contribution;
            if (layer.IsAdjustment)
            {
                contribution = RunAdjustment(canvas, layer);
            }
            else
            {
                contribution = layer.Pixels is null ? new PixelBuffer(_document.Width, _document.Height) : _document.Fit(layer.Pixels);
            }

            canvas = BlendCompositor.Composite(canvas, contribution, layer.BlendMode, opacity, layer.CoverageFor(_document.Width, _document.Height));
        });

        return canvas;
    }

    /// <summary>The selected layer and the first one below it that shares its folder, or null.</summary>
    private IReadOnlyList<Layer>? PlanMergeDown()
    {
        if (_selectedLayer is not { } selected) return null;
        if (selected.Model.IsAdjustment) return null;

        Guid? parent = selected.Model.ParentId;
        int index = IndexOfModel(selected.Model);

        // The document is kept bottom first, so the layer after this one is the one underneath it.
        if (index < 0 || index + 1 >= _document.Layers.Count) return null;

        Layer below = _document.Layers[index + 1];
        if (below.ParentId != parent || below.IsAdjustment || below.IsGroup) return null;

        return new List<Layer> { below, selected.Model };
    }

    /// <summary>
    /// A folder and everything inside it, or null when the selection is not a folder. Nested folders are
    /// listed too: they hold no pixels of their own, but the merge has to take them away with it, or a
    /// folder with nothing left inside it stays on the panel.
    /// </summary>
    private IReadOnlyList<Layer>? PlanMergeGroup()
    {
        if (_selectedLayer is not { IsGroup: true } folder) return null;

        var inside = CollectSubtree(folder.Model);
        return inside.Count < 2 ? null : inside;
    }

    /// <summary>
    /// A folder's descendants, bottom first, and the folder itself last so it names the result. Nested
    /// folders are left out of the walk: they contribute no pixels, and the merge removes them anyway.
    /// </summary>
    private List<Layer> CollectSubtree(Layer folder)
    {
        var painted = new List<Layer>();

        LayerOrder.ForEach(_document.Layers, (layer, _, _) =>
        {
            if (layer.IsGroup || layer.Id == folder.Id) return;
            if (!IsInside(layer, folder)) return;
            if (layer.IsAdjustment || layer.Pixels is not null) painted.Add(layer);
        });

        painted.Add(folder);
        return painted;
    }

    /// <summary>True when <paramref name="layer"/> sits somewhere under <paramref name="folder"/>.</summary>
    private bool IsInside(Layer layer, Layer folder)
    {
        Guid folderId = folder.Id;

        // A tree this deep is already rejected elsewhere, so the bound is only here to stop a link that
        // points back on itself from spinning.
        for (int depth = 0; depth <= LayerOrder.MaxDepth; depth++)
        {
            if (layer.ParentId is not { } parent) return false;
            if (parent == folderId) return true;
            if (FindModel(parent) is not { } ancestor) return false;
            layer = ancestor;
        }

        return false;
    }

    private Layer? FindModel(Guid id)
    {
        foreach (Layer layer in _document.Layers)
        {
            if (layer.Id == id) return layer;
        }

        return null;
    }

    private int IndexOfModel(Layer layer)
    {
        for (int i = 0; i < Layers.Count; i++)
        {
            if (ReferenceEquals(Layers[i].Model, layer)) return i;
        }

        return -1;
    }

    /// <summary>Puts the document's stack back in step with the rows the panel is showing.</summary>
    private void SortDocumentLikeRows()
    {
        var ordered = Layers.Select(row => row.Model).ToList();
        foreach (Layer layer in ordered) _document.Remove(layer);

        // The document is kept bottom first, so it takes the panel's rows the other way round.
        for (int i = ordered.Count - 1; i >= 0; i--) _document.Add(ordered[i]);
    }

    public PixelBuffer Flatten() => _document.Flatten(RunAdjustment);

    /// <summary>
    /// Writes the flattened document. A size may be given to export at a different resolution than
    /// the canvas; that resamples the flattened copy only, so the document itself is untouched.
    /// </summary>
    public void Export(string path, int? width = null, int? height = null, int quality = 90)
    {
        PixelBuffer pixels = Flatten();
        if (width is > 0 && height is > 0 && (width != pixels.Width || height != pixels.Height))
        {
            pixels = Imaging.Services.Geometry.Resize(pixels, width.Value, height.Value);
        }

        var format = _codec.FormatFromPath(path);
        _codec.Write(pixels, path, new ImageSaveOptions(format, Math.Clamp(quality, 1, 100)));

        _document.IsDirty = false;
        OnPropertyChanged(nameof(WindowTitle));
        Status = Localization.Interpolate(Strings.StatusExported, Path.GetFileName(path), pixels.Width, pixels.Height);
    }

    public void Undo() => History.Undo();

    public void Redo() => History.Redo();

    /// <summary>
    /// Re-reads every label that comes from the resources. Nothing calls this yet: the app follows the
    /// system language at startup and has no language menu, but the hook is what a switch would need.
    /// </summary>
    public void RefreshLocalization()
    {
        foreach (LayerViewModel layer in Layers) layer.RefreshLocalization();

        Adjustment.RefreshLocalization();
        Selection.RefreshLocalization();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(LayerCountLabel));
    }

    internal void NotifyChanged()
    {
        _document.IsDirty = true;
        OnPropertyChanged(nameof(WindowTitle));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adjustment layers run over everything composited below them.</summary>
    private PixelBuffer RunAdjustment(PixelBuffer canvas, Layer layer) =>
        layer.Adjustment is null ? canvas : _filter.Apply(canvas, layer.Adjustment);

    private static CanvasDocument CreateDocument(int width, int height)
    {
        var document = new CanvasDocument(width, height);
        document.AddBlank(Localization.Interpolate(Strings.LayersNameFormat, 1));
        document.IsDirty = false;
        return document;
    }

    private void ReloadLayers()
    {
        Layers.Clear();
        foreach (Layer layer in _document.Layers.Reverse())
        {
            Layers.Add(new LayerViewModel(layer, this, _filter));
        }

        RefreshPlacement();
        SelectedLayer = Layers.FirstOrDefault();

        // A freshly opened document has no selection. Defaulting it to the whole canvas made every
        // fill treat the entire image as its target, which reads as "the button does nothing".
        Selection.Clear();
        Selection.Refresh();

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(SizeLabel));
        OnPropertyChanged(nameof(SuggestedExportName));
        DocumentReplaced?.Invoke(this, _document);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Re-reads how deep each row sits and whether it has anything inside it. Depth is a property of the
    /// stack, not of the order rows happen to be listed in, so it is worked out from the folder links
    /// before anything is assigned to a row.
    /// </summary>
    private void RefreshPlacement()
    {
        var rowsById = new Dictionary<Guid, LayerViewModel>(Layers.Count);
        var hasChildren = new HashSet<Guid>();
        foreach (LayerViewModel row in Layers)
        {
            rowsById[row.Model.Id] = row;
            if (row.Model.ParentId is { } parent) hasChildren.Add(parent);
        }

        var depthOf = new Dictionary<Guid, int>(Layers.Count);
        foreach (LayerViewModel row in Layers) depthOf[row.Model.Id] = DepthOf(row.Model, rowsById, depthOf);

        foreach (LayerViewModel row in Layers)
        {
            row.SetPlacement(
                depthOf[row.Model.Id],
                hasChildren.Contains(row.Model.Id),
                IsRowVisible(row),
                _collapsed.Contains(row.Model.Id));
        }

        RefreshDisplayRows();
    }

    /// <summary>Rebuilds the panel's rows from the stack, leaving out what a folded folder hides.</summary>
    private void RefreshDisplayRows()
    {
        DisplayRows.Clear();
        foreach (LayerViewModel row in Layers)
        {
            if (row.IsRowVisible) DisplayRows.Add(row);
        }
    }

    /// <summary>
    /// Steps from a layer to the top, counting folders. The result is cached per layer, so a deep chain
    /// is walked once rather than once per layer under it. A link to a folder that is not there counts
    /// as the top level, so a broken tree still lays out instead of failing to draw.
    /// </summary>
    private static int DepthOf(Layer model, Dictionary<Guid, LayerViewModel> rowsById, Dictionary<Guid, int> known)
    {
        var chain = new List<Layer>();
        Layer current = model;
        int depth = 0;

        while (current.ParentId is { } parent)
        {
            if (known.TryGetValue(parent, out int parentDepth))
            {
                depth = parentDepth + 1;
                break;
            }

            if (!rowsById.TryGetValue(parent, out LayerViewModel? folder)) break;

            chain.Add(current);
            current = folder.Model;
            if (chain.Count > LayerOrder.MaxDepth) break;
        }

        for (int i = chain.Count - 1; i >= 0; i--)
        {
            known[chain[i].Id] = depth;
            depth++;
        }

        return depth;
    }

    internal void ReplaceRows(IReadOnlyList<LayerViewModel> rows, LayerViewModel? selected)
    {
        Layers.Clear();
        foreach (LayerViewModel row in rows) Layers.Add(row);

        RefreshPlacement();
        SelectedLayer = selected is not null && Layers.Contains(selected) ? selected : Layers.FirstOrDefault();
        NotifyChanged();    }

    /// <summary>
    /// Puts the given panel rows into the order the panel shows them, leaving rows that are folded out of
    /// sight where they were. <paramref name="panelOrder"/> is top first and holds only visible rows,
    /// which is the order <see cref="Layers"/> is kept in.
    /// </summary>
    private List<LayerViewModel> ApplyPanelOrder(IReadOnlyList<LayerViewModel> panelOrder)
    {
        var incoming = new HashSet<LayerViewModel>(panelOrder);
        var result = new List<LayerViewModel>(Layers.Count);

        for (int i = 0; i < Layers.Count; i++)
        {
            LayerViewModel row = Layers[i];
            if (!incoming.Contains(row))
            {
                // Folded out of sight: it keeps the place it already had.
                result.Add(row);
                continue;
            }

            // Where this row's slot falls among the visible rows decides which panel row fills it.
            int slot = 0;
            for (int k = 0; k < i; k++)
            {
                if (incoming.Contains(Layers[k])) slot++;
            }

            result.Add(panelOrder[slot]);
        }

        Layers.Clear();
        foreach (LayerViewModel row in result)
        {
            Layers.Add(row);
            _document.Remove(row.Model);
        }

        // The document is kept bottom first, so it takes the panel's rows the other way round.
        for (int i = result.Count - 1; i >= 0; i--) _document.Add(result[i].Model);

        NotifyChanged();
        return result;
    }

    /// <summary>
    /// Records a structural edit as the two stacks it moved between. Replaying a whole stack is what
    /// makes folders work: a layer that moved into a folder, or a folder removed with its children, is
    /// not expressible as "row N of the flat list".
    /// </summary>
    private void PushStructuralStep(string name, LayerStackSnapshot before)
        => PushStructuralStep(name, before, LayerStackSnapshot.Capture(this));

    private void PushStructuralStep(string name, LayerStackSnapshot before, LayerStackSnapshot after)
        => History.Push(new DelegateAction(
            name,
            () => before.Restore(this),
            () => after.Restore(this)));


    private int DocumentIndex(int panelIndex)
    {
        int last = Math.Max(_document.Layers.Count - 1, 0);
        return Math.Clamp(last - panelIndex, 0, last);
    }
}
