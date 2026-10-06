using NaraDreamPainter.Models.Pixels;

namespace NaraDreamPainter.Models.Services;

public enum SelectionShape
{
    Rectangle,
    Ellipse
}

public readonly record struct SelectionRegion(SelectionShape Shape, int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>
/// Turns a selection shape into per-pixel coverage at document resolution: 255 fully selected,
/// 0 outside. Feather fades the boundary symmetrically, spreading inside and outside the edge the
/// way the original does, rather than only softening inwards.
/// </summary>
public interface ISelectionMaskBuilder
{
    byte[] Build(SelectionRegion region, int canvasWidth, int canvasHeight, double feather = 0);

    /// <summary>Coverage for the whole canvas, all 255, for operations with no active selection.</summary>
    byte[] Full(int canvasWidth, int canvasHeight);

    /// <summary>Applies coverage to a buffer: selected pixels change, the rest keep their original value.</summary>
    PixelBuffer BlendThrough(PixelBuffer adjusted, PixelBuffer original, byte[] coverage);
}
