using NaraDreamPainter.Models.Layers;
using Microsoft.Graphics.Canvas.Effects;
using Windows.Graphics.Effects;

namespace NaraDreamPainter.Compositing.Blending;

/// <summary>
/// Maps a <see cref="BlendMode"/> onto what Win2D can actually do with it.
///
/// Win2D's own <c>CanvasBlend</c> enum only has SourceOver, Copy, Min and Add, so it covers Normal
/// and nothing else. A <c>PixelShaderEffect</c> would cover the rest, but it needs HLSL that has
/// already been compiled to bytecode, and this toolchain has no shader compiler, so that route is
/// closed. Direct2D's blend effect implements the W3C compositing formulas for the same mode list
/// Photoshop uses, so the other 23 modes go through <see cref="BlendEffect"/>. Nothing else here
/// blends: the CPU path in <c>NaraDreamPainter.Models.Blending</c> takes over when the device refuses an
/// effect, and it agrees with these formulas.
/// </summary>
public static class BlendEffectFactory
{
    /// <summary>
    /// The effect's counterpart of a mode. Null means Normal, which the blend effect enum does not
    /// list because plain source-over drawing already is that mode.
    /// </summary>
    public static BlendEffectMode? Map(BlendMode mode) => mode switch
    {
        BlendMode.Normal => null,
        BlendMode.Darken => BlendEffectMode.Darken,
        BlendMode.Multiply => BlendEffectMode.Multiply,
        BlendMode.ColorBurn => BlendEffectMode.ColorBurn,
        BlendMode.LinearBurn => BlendEffectMode.LinearBurn,
        BlendMode.Lighten => BlendEffectMode.Lighten,
        BlendMode.Screen => BlendEffectMode.Screen,
        BlendMode.ColorDodge => BlendEffectMode.ColorDodge,
        BlendMode.LinearDodge => BlendEffectMode.LinearDodge,
        BlendMode.Overlay => BlendEffectMode.Overlay,
        BlendMode.SoftLight => BlendEffectMode.SoftLight,
        BlendMode.HardLight => BlendEffectMode.HardLight,
        BlendMode.VividLight => BlendEffectMode.VividLight,
        BlendMode.LinearLight => BlendEffectMode.LinearLight,
        BlendMode.PinLight => BlendEffectMode.PinLight,
        BlendMode.HardMix => BlendEffectMode.HardMix,
        BlendMode.Difference => BlendEffectMode.Difference,
        BlendMode.Exclusion => BlendEffectMode.Exclusion,
        BlendMode.Subtract => BlendEffectMode.Subtract,
        BlendMode.Divide => BlendEffectMode.Division,
        BlendMode.Hue => BlendEffectMode.Hue,
        BlendMode.Saturation => BlendEffectMode.Saturation,
        BlendMode.Color => BlendEffectMode.Color,
        BlendMode.Luminosity => BlendEffectMode.Luminosity,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown blend mode.")
    };

    /// <summary>
    /// Builds the effect that composites <paramref name="foreground"/> onto <paramref name="background"/>,
    /// or null when the mode has no GPU route and the caller should draw it as source-over.
    /// </summary>
    public static BlendEffect? Create(BlendMode mode, IGraphicsEffectSource background, IGraphicsEffectSource foreground)
    {
        BlendEffectMode? mapped = Map(mode);
        if (mapped is null) return null;

        // Both inputs are premultiplied render targets, which is what the effect expects; the layer
        // bitmaps were uploaded with straight alpha and premultiplied when they were drawn into one.
        return new BlendEffect
        {
            Background = background,
            Foreground = foreground,
            Mode = mapped.Value
        };
    }
}
