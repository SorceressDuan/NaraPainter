using System.Collections.ObjectModel;
using System.Diagnostics;
using Compositor.App.Services;
using Compositor.Models.Adjustments;
using Compositor.Models.Documents;
using Compositor.Models.Layers;
using Compositor.Models.Pixels;
using Compositor.Models.Services;

namespace Compositor.App.ViewModels;

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
    private string _status = "Ready";
    private double _zoom = 1;

    public DocumentViewModel(ImageImporter importer, IImageCodec codec, IAdjustmentFilter filter, ISelectionMaskBuilder masks)
    {
        _importer = importer;
        _codec = codec;
        _filter = filter;
        _document = CreateDocument(DefaultCanvasWidth, DefaultCanvasHeight);
        Adjustment = new AdjustmentViewModel();
        Selection = new SelectionViewModel(this, masks);

        Layers.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(LayerCountLabel));
            OnPropertyChanged(nameof(HasLayers));
        };

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

    public ObservableCollection<LayerViewModel> Layers { get; } = [];

    public CanvasDocument Document => _document;

    public IImageCodec Codec => _codec;

    /// <summary>The filter adjustments run through. The canvas shares it so preview and export agree.</summary>
    public IAdjustmentFilter Filter => _filter;

    public bool CanUndo => History.CanUndo;

    public bool CanRedo => History.CanRedo;

    public bool HasLayers => Layers.Count > 0;

    public bool HasSelection => _selectedLayer is not null;

    public string Title => _document.FilePath is null ? "Untitled" : Path.GetFileName(_document.FilePath);

    public string WindowTitle => $"Compositor — {Title}{(_document.IsDirty ? " *" : string.Empty)}";

    public string SizeLabel => $"{_document.Width} × {_document.Height}";

    public string LayerCountLabel => Layers.Count == 1 ? "1 layer" : $"{Layers.Count} layers";

    public string ZoomLabel => $"{_zoom * 100:0}%";

    /// <summary>1.0 is 100%. Mirrors the canvas view, which owns the real zoom level.</summary>
    public double Zoom => _zoom;

    public string SuggestedExportName => $"{Path.GetFileNameWithoutExtension(_document.FilePath) ?? "Untitled"}-export";

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
        _document = document;
        History.Clear();
        ReloadLayers();
        Status = $"Opened {Path.GetFileName(path)} · {SizeLabel} · {stopwatch.ElapsedMilliseconds} ms";
    }

    public void NewDocument(int width, int height)
    {
        _document = CreateDocument(width, height);
        History.Clear();
        ReloadLayers();
        Status = $"New canvas · {SizeLabel}";
    }

    public LayerViewModel AddLayer()
    {
        Layer layer = _document.AddBlank($"Layer {_document.Layers.Count + 1}");
        var view = new LayerViewModel(layer, this, _filter);
        Layers.Insert(0, view);
        SelectedLayer = view;
        History.Push(new DelegateAction("New Layer", () => RemoveLayerCore(view), () => InsertLayerCore(view, 0)));
        NotifyChanged();
        Status = $"Added {layer.Name}";
        return view;
    }

    public LayerViewModel AddAdjustmentLayer(AdjustmentKind kind)
    {
        AdjustmentSettings settings = AdjustmentKinds.Create(kind);
        Layer layer = _document.Add(new Layer(settings.DisplayName) { Adjustment = settings });
        var view = new LayerViewModel(layer, this, _filter);
        Layers.Insert(0, view);
        SelectedLayer = view;
        History.Push(new DelegateAction($"New {settings.DisplayName} Layer", () => RemoveLayerCore(view), () => InsertLayerCore(view, 0)));
        NotifyChanged();
        Status = $"Added {settings.DisplayName} layer";
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
        History.Push(new DelegateAction($"Import {layer.Name}", () => RemoveLayerCore(view), () => InsertLayerCore(view, 0)));
        NotifyChanged();
        Status = $"Imported {Path.GetFileName(path)} as a layer";
        return view;
    }

    public LayerViewModel? DuplicateLayer(LayerViewModel? layer)
    {
        if (layer is null) return null;

        Layer clone = layer.Model.Clone();
        clone.Name = $"{layer.Model.Name} copy";

        // The copy keeps the original pixels and re-runs the same adjustment stack over them.
        clone.Pixels = layer.Source?.Clone();

        int panelIndex = Math.Max(Layers.IndexOf(layer), 0);
        var view = new LayerViewModel(clone, this, _filter);
        view.CopyAdjustmentsFrom(layer);

        _document.Add(clone);
        _document.Move(clone, DocumentIndex(panelIndex));
        Layers.Insert(panelIndex, view);
        SelectedLayer = view;
        History.Push(new DelegateAction($"Duplicate {layer.Name}", () => RemoveLayerCore(view), () => InsertLayerCore(view, panelIndex)));
        NotifyChanged();
        Status = $"Duplicated {layer.Name}";
        return view;
    }

    public void DeleteLayer(LayerViewModel? layer)
    {
        if (layer is null) return;

        int panelIndex = Layers.IndexOf(layer);
        if (panelIndex < 0) return;

        RemoveLayerCore(layer);
        History.Push(new DelegateAction($"Delete {layer.Name}", () => InsertLayerCore(layer, panelIndex), () => RemoveLayerCore(layer)));
        Status = $"Deleted {layer.Name}";
    }

    /// <summary>Takes the order the layers panel ended up in, top first, and records the move.</summary>
    public void ReorderLayers(IReadOnlyList<LayerViewModel> panelOrder)
    {
        var before = Layers.ToList();
        if (before.SequenceEqual(panelOrder)) return;

        var after = panelOrder.ToList();
        ApplyPanelOrder(after);
        History.Push(new DelegateAction("Reorder Layers", () => ApplyPanelOrder(before), () => ApplyPanelOrder(after)));
        Status = "Layers reordered";
    }

    public PixelBuffer Flatten() => _document.Flatten(RunAdjustment);

    public void Export(string path)
    {
        PixelBuffer pixels = Flatten();
        var format = _codec.FormatFromPath(path);
        _codec.Write(pixels, path, new ImageSaveOptions(format));

        _document.IsDirty = false;
        OnPropertyChanged(nameof(WindowTitle));
        Status = $"Exported {Path.GetFileName(path)} · {pixels.Width} × {pixels.Height}";
    }

    public void Undo() => History.Undo();

    public void Redo() => History.Redo();

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
        document.AddBlank("Layer 1");
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
        Selection.ResetToCanvas();

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
