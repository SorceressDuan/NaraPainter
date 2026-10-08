using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using NaraPainter.Models.Pixels;
using NaraPainter.Models.Text;

namespace NaraPainter.Compositing.Rendering;

/// <summary>
/// Draws a string into a layer's pixels, once. There is no reflow and no editing afterwards: what
/// comes back is an ordinary buffer, which is what keeps text layers out of the document model.
/// </summary>
/// <remarks>
/// The buffer is the size of the canvas rather than the size of the text. A layer smaller than the
/// canvas is stretched across it when it is composited or exported, so a tight text-sized buffer
/// would come back resampled and the glyphs would be wrong; a canvas-sized one is drawn as it is.
/// </remarks>
public static class TextRasterizer
{
    private const float Dpi = 96;

    /// <summary>
    /// Renders <paramref name="style"/> onto a transparent canvas-sized buffer, with the run's
    /// top-left at (<paramref name="x"/>, <paramref name="y"/>). Null when there is nothing to draw.
    /// </summary>
    public static PixelBuffer? Render(TextStyle style, int canvasWidth, int canvasHeight, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(style);
        if (canvasWidth <= 0) throw new ArgumentOutOfRangeException(nameof(canvasWidth));
        if (canvasHeight <= 0) throw new ArgumentOutOfRangeException(nameof(canvasHeight));
        if (!style.IsDrawable) return null;

        // Not disposed on the way out: this is the process-wide device the canvas control draws with as
        // well, and letting the scope take it down left the next frame drawing into a dead device.
        CanvasDevice device = CanvasDevice.GetSharedDevice();
        using var format = new CanvasTextFormat
        {
            FontFamily = style.FontFamily,
            FontSize = style.FontSize,
            WordWrapping = CanvasWordWrapping.NoWrap
        };

        // The requested size is a wrapping bound, not a measurement request: asking for zero width
        // makes DirectWrite break after every character, which stacks a CJK run one glyph per line.
        // A bound wide enough for the font's widest glyph leaves it as the single run it is.
        float bound = (style.Content.Length + 2) * style.FontSize * 2;
        using var layout = new CanvasTextLayout(device, style.Content, format, bound, bound);
        double runWidth = layout.LayoutBounds.Width;
        if (!(runWidth > 0) || !(layout.LayoutBounds.Height > 0)) return null;

        float left = style.Alignment switch
        {
            TextAlignment.Center => x - ((float)runWidth / 2),
            TextAlignment.Right => x - (float)runWidth,
            _ => x
        };

        var canvas = new PixelBuffer(canvasWidth, canvasHeight);
        using var target = new CanvasRenderTarget(device, canvasWidth, canvasHeight, Dpi);
        using (CanvasDrawingSession session = target.CreateDrawingSession())
        {
            session.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            Rgba32 ink = style.Ink;
            session.DrawTextLayout(
                layout,
                left,
                y,
                Windows.UI.Color.FromArgb(ink.A, ink.R, ink.G, ink.B));
        }

        ToStraightRgba(target.GetPixelBytes(), canvas.Data);
        return canvas;
    }

    /// <summary>
    /// A Win2D surface is premultiplied BGRA and a <see cref="PixelBuffer"/> is straight RGBA, so this
    /// swaps the colour channels and divides the alpha back out. The rounding matches the one-way
    /// conversion in <c>DocumentRenderer.ToPremultipliedBgra</c>.
    /// </summary>
    internal static void ToStraightRgba(byte[] bgra, byte[] rgba)
    {
        if (bgra.Length < rgba.Length) throw new ArgumentException("The surface is smaller than the buffer.", nameof(bgra));

        for (int i = 0; i < rgba.Length; i += 4)
        {
            byte alpha = bgra[i + 3];
            rgba[i + 3] = alpha;
            if (alpha == 0)
            {
                rgba[i] = 0;
                rgba[i + 1] = 0;
                rgba[i + 2] = 0;
                continue;
            }

            rgba[i] = Unpremultiply(bgra[i + 2], alpha);
            rgba[i + 1] = Unpremultiply(bgra[i + 1], alpha);
            rgba[i + 2] = Unpremultiply(bgra[i], alpha);
        }
    }

    private static byte Unpremultiply(byte channel, byte alpha) =>
        (byte)Math.Min(255, ((channel * 255) + (alpha / 2)) / alpha);
}
