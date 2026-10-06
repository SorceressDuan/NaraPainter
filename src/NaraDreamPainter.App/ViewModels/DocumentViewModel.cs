using System.Collections.ObjectModel;
using System.Diagnostics;
using NaraDreamPainter.App.Services;
using NaraDreamPainter.Models.Adjustments;
using NaraDreamPainter.Models.Documents;
using NaraDreamPainter.Models.Layers;
using NaraDreamPainter.Models.Pixels;
using NaraDreamPainter.Models.Services;

namespace NaraDreamPainter.App.ViewModels;

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
        ContentFill = new ContentAwareFillViewModel(this, masks);

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

    public AdjustmentViewModel Adjustment { get; }

    public SelectionViewModel Selection { get; }

    /// <summary>Paints the selected layer's mask; the canvas routes pointer drags here when active.</summary>
    public MaskBrushViewModel MaskBrush { get; }

    /// <summary>Fills the selection, or the masked-out pixels, from what surrounds them.</summary>
    public ContentAwareFillViewModel ContentFill { get; }

    public ObservableCollection<LayerViewModel> Layers { get; } = [];

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
            ContentFill.Refresh();
            OnPropertyChanged(nameof(HasSelection));
        }
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

    private void SwapDocument(CanvasDocument document)
    {
        _document = document;
        ReloadLayers();
    }

    /// <summary>True until the first picture is imported, which is when undo starts to mean something.</summary>
    private bool _isStartupCanvas = true;

    public LayerViewModel AddLayer()
    {
        Layer layer = _document.AddBlank(Localization.Interpolate(Strings.LayersNameFormat, _document.Layers.Count + 1));
        var view = new LayerViewModel(layer, this, _filter);
        Layers.Insert(0, view);
        SelectedLayer = view;
        History.Push(new DelegateAction(Strings.UndoNewLayer, () => RemoveLayerCore(view), () => InsertLayerCore(view, 0)));
        NotifyChanged();
        Status = Localization.Interpolate(Strings.StatusLayerAdded, layer.Name);
        return view;
    }

    public LayerViewModel AddAdjustmentLayer(AdjustmentKind kind)
    {
        AdjustmentSettings settings = AdjustmentKinds.Create(kind);
        string name = AdjustmentKinds.Name(kind);
        Layer layer = _document.Add(new Layer(name) { Adjustment = settings });
        var view = new LayerViewModel(layer, this, _filter);
        Layers.Insert(0, view);
        SelectedLayer = view;
        History.Push(new DelegateAction(
            Localization.Interpolate(Strings.UndoNewAdjustmentLayer, name),
            () => RemoveLayerCore(view),
            () => InsertLayerCore(view, 0)));
        NotifyChanged();
        Status = Localization.Interpolate(Strings.StatusAdjustmentLayerAdded, name);
        return view;
    }

    public LayerViewModel ImportLayer(string path)
    {
        Layer layer = _importer.ReadLayer(path);

        // Layers live at canvas resolution; the canvas scales an image that came in at another size.
        if (layer.Pixels is not null) layer.Pixels = _document.Fit(layer.Pixels);

        _document.Add(layer);
        var view = new LayerViewModel(layer, this, _filter);
        Layers.Insert(0, view);
        SelectedLayer = view;
        History.Push(new DelegateAction(Strings.UndoImportLayer, () => RemoveLayerCore(view), () => InsertLayerCore(view, 0)));
        NotifyChanged();
        Status = Localization.Interpolate(Strings.StatusImported, Path.GetFileName(path));
        return view;
    }

    public LayerViewModel? DuplicateLayer(LayerViewModel? layer)
    {
        if (layer is null) return null;

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
        SelectedLayer = view;
        History.Push(new DelegateAction(Strings.UndoDuplicateLayer, () => RemoveLayerCore(view), () => InsertLayerCore(view, panelIndex)));
        NotifyChanged();
        Status = Localization.Interpolate(Strings.StatusLayerDuplicated, layer.Name);
        return view;
    }

    public void DeleteLayer(LayerViewModel? layer)
    {
        if (layer is null) return;

        int panelIndex = Layers.IndexOf(layer);
        if (panelIndex < 0) return;

        RemoveLayerCore(layer);
        History.Push(new DelegateAction(Strings.UndoDeleteLayer, () => InsertLayerCore(layer, panelIndex), () => RemoveLayerCore(layer)));
        Status = Localization.Interpolate(Strings.StatusLayerDeleted, layer.Name);
    }

    /// <summary>Takes the order the layers panel ended up in, top first, and records the move.</summary>
    public void ReorderLayers(IReadOnlyList<LayerViewModel> panelOrder)
    {
        var before = Layers.ToList();
        if (before.SequenceEqual(panelOrder)) return;

        var after = panelOrder.ToList();
        ApplyPanelOrder(after);
        History.Push(new DelegateAction(Strings.UndoReorderLayers, () => ApplyPanelOrder(before), () => ApplyPanelOrder(after)));
        Status = Strings.StatusLayersReordered;
    }

    public PixelBuffer Flatten() => _document.Flatten(RunAdjustment);

    public void Export(string path)
    {
        PixelBuffer pixels = Flatten();
        var format = _codec.FormatFromPath(path);
        _codec.Write(pixels, path, new ImageSaveOptions(format));

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

    private void RemoveLayerCore(LayerViewModel view)
    {
        if (!Layers.Remove(view)) return;

        _document.Remove(view.Model);
        if (ReferenceEquals(SelectedLayer, view)) SelectedLayer = Layers.FirstOrDefault();
        NotifyChanged();
    }

    private void InsertLayerCore(LayerViewModel view, int panelIndex)
    {
        _document.Add(view.Model);
        _document.Move(view.Model, DocumentIndex(panelIndex));
        Layers.Insert(Math.Clamp(panelIndex, 0, Layers.Count), view);
        SelectedLayer = view;
        NotifyChanged();
    }

    private void ApplyPanelOrder(IReadOnlyList<LayerViewModel> panelOrder)
    {
        int count = panelOrder.Count;
        for (int documentIndex = 0; documentIndex < count; documentIndex++)
        {
            _document.Move(panelOrder[count - 1 - documentIndex].Model, documentIndex);
        }

        for (int i = 0; i < panelOrder.Count; i++)
        {
            int current = Layers.IndexOf(panelOrder[i]);
            if (current != i) Layers.Move(current, i);
        }

        NotifyChanged();
    }

    /// <summary>Document index for a panel row: the panel lists top first, the document bottom first.</summary>
    private int DocumentIndex(int panelIndex)
    {
        int last = Math.Max(_document.Layers.Count - 1, 0);
        return Math.Clamp(last - panelIndex, 0, last);
    }
}
