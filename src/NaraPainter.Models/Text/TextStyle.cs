using NaraPainter.Models.Pixels;

namespace NaraPainter.Models.Text;

/// <summary>
/// How a text layer was drawn. Kept so a layer can say what it is; the pixels are ordinary layer
/// pixels and nothing re-renders from this after the layer is made.
/// </summary>
/// <param name="Content">The text itself, as typed.</param>
/// <param name="FontFamily">A family name DirectWrite resolves, falling back per glyph when absent.</param>
/// <param name="FontSize">Cap height in canvas pixels.</param>
/// <param name="Colour">Straight-alpha colour, the same shape <see cref="PixelBuffer"/> uses.</param>
/// <param name="Alignment">Where the run sits relative to the point it was placed at.</param>
public sealed record TextStyle(
    string Content,
    string FontFamily = "Microsoft YaHei UI",
    float FontSize = 64,
    Rgba32? Colour = null,
    TextAlignment Alignment = TextAlignment.Left)
{
    /// <summary>What the tool uses when the dialog is opened without a previous choice.</summary>
    public static TextStyle Default(string content) => new(content);

    /// <summary>The colour actually drawn, defaulting to the near-black the rest of the app uses.</summary>
    public Rgba32 Ink => Colour ?? new Rgba32(26, 26, 26, 255);

    /// <summary>False when there is nothing to draw, which is how a blank dialog is refused.</summary>
    public bool IsDrawable => !string.IsNullOrWhiteSpace(Content) && FontSize >= 1 && FontFamily.Length > 0;
}

/// <summary>Where a run of text sits relative to the point it was placed at.</summary>
public enum TextAlignment
{
    Left,
    Center,
    Right
}
