using NaraPainter.Models.Layers;

namespace NaraPainter.Models.Documents;

/// <summary>
/// Works the layer tree out of the flat list. Nothing is stored twice: a layer's folder is its
/// <see cref="Layer.ParentId"/>, and the stack order plus that link is enough to draw and to list.
/// </summary>
/// <remarks>
/// Ported from <c>legacy/Compositor/Document/LayerGroups.swift</c>. A folder is pass-through: it is
/// never composited as a unit, so what it contributes is its opacity and visibility multiplied into
/// everything inside it. That is why <see cref="EffectiveOpacity"/> exists rather than a group buffer.
/// </remarks>
public static class LayerOrder
{
    /// <summary>How far folders may nest. Also what stops a malformed tree from spinning.</summary>
    public const int MaxDepth = 64;

    /// <summary>
    /// Walks the tree bottom up, parents before their children, calling <paramref name="visit"/> with
    /// each layer and what its ancestors make of it.
    /// </summary>
    /// <param name="visible">
    /// Whether the layer shows once its own flag and every folder above it are taken together.
    /// </param>
    /// <param name="opacity">The layer's own opacity times every folder above it.</param>
    public static void ForEach(
        IReadOnlyList<Layer> layers,
        Action<Layer, bool, double> visit)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(visit);

        // Children discovered while walking, popped from the end, so siblings come out in stack order.
        var pending = new Stack<(Layer Layer, bool Visible, double Opacity, int Depth)>();

        // Reverse so the first entry pushed is the first layer, which is the one drawn first.
        for (int i = layers.Count - 1; i >= 0; i--)
        {
            if (layers[i].ParentId is null) pending.Push((layers[i], true, 1, 0));
        }

        while (pending.Count > 0)
        {
            (Layer layer, bool visible, double opacity, int depth) = pending.Pop();
            bool shows = visible && layer.IsVisible;
            double effective = opacity * layer.Opacity;
            visit(layer, shows, effective);

            if (!layer.IsGroup || depth >= MaxDepth) continue;

            for (int i = layers.Count - 1; i >= 0; i--)
            {
                if (layers[i].ParentId == layer.Id) pending.Push((layers[i], shows, effective, depth + 1));
            }
        }
    }

    /// <summary>
    /// Every layer that shows, in draw order, folders left out. A hidden folder hides what is inside
    /// it without touching the layers' own flags.
    /// </summary>
    public static List<Layer> Drawn(IReadOnlyList<Layer> layers)
    {
        var drawn = new List<Layer>();
        ForEach(layers, (layer, visible, _) =>
        {
            if (visible && !layer.IsGroup) drawn.Add(layer);
        });
        return drawn;
    }

    /// <summary>
    /// What the folders above a layer contribute: their opacities multiplied together. The layer's own
    /// opacity is applied when it is composited, so it is not part of this. 50% inside 50% shows at
    /// 25% while the layer itself still reads 50% in the panel.
    /// </summary>
    public static double EffectiveOpacity(IReadOnlyList<Layer> layers, Layer layer)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(layer);

        double opacity = 1;
        var byId = new Dictionary<Guid, Layer>(layers.Count);
        foreach (Layer candidate in layers) byId[candidate.Id] = candidate;

        Guid? parent = layer.ParentId;
        for (int depth = 0; parent is { } id && depth < MaxDepth; depth++)
        {
            if (!byId.TryGetValue(id, out Layer? folder)) break;
            opacity *= folder.Opacity;
            parent = folder.ParentId;
        }

        return opacity;
    }

    /// <summary>
    /// True when the tree can be drawn: one entry per id, every folder link points at a folder, and no
    /// layer is its own ancestor. Called before a structural edit is committed, not on every frame.
    /// </summary>
    public static bool IsValid(IReadOnlyList<Layer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);

        var byId = new Dictionary<Guid, Layer>(layers.Count);
        foreach (Layer layer in layers)
        {
            if (!byId.TryAdd(layer.Id, layer)) return false;

            // A folder holds no pixels of its own; bailing out here keeps "has content" meaningful.
            if (layer.IsGroup && layer.Pixels is not null) return false;
        }

        foreach (Layer layer in layers)
        {
            var seen = new HashSet<Guid> { layer.Id };
            Guid? parent = layer.ParentId;
            for (int depth = 0; parent is { } id && depth <= MaxDepth; depth++)
            {
                if (!seen.Add(id)) return false;
                if (!byId.TryGetValue(id, out Layer? folder) || !folder.IsGroup) return false;
                parent = folder.ParentId;
            }

            if (parent is not null) return false;
        }

        return true;
    }
}
