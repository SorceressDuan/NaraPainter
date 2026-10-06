using NaraDreamPainter.Models.Adjustments;
using NaraDreamPainter.Models.Blending;
using NaraDreamPainter.Models.Documents;
using NaraDreamPainter.Models.Layers;
using NaraDreamPainter.Models.Pixels;

namespace Compositor.Tools.Check;

// Spot checks for the model layer, independent of the xUnit suite: useful when only
// NaraDreamPainter.Models is buildable, and as a second opinion on the numbers.
internal static class Program
{
    private static int _failures;

    private static void Main()
    {
        LevelsIdentity();
        CurvesIdentity();
        ClipColorEdges();
        ClipColorKnownValues();
        MaskSurvivesPartialOpacity();
        LayerStackOrder();

        Console.WriteLine(_failures == 0 ? "all checks passed" : $"{_failures} check(s) failed");
        Environment.Exit(_failures == 0 ? 0 : 1);
    }

    private static void LevelsIdentity()
    {
        var settings = new LevelsSettings();
        Check("LevelsSettings() is identity", settings.IsIdentity);

        var range = LevelRange.Identity;
        Check("LevelRange.Identity.Gamma == 1", range.Gamma == 1);
        Check("LevelRange.Identity.White == 255", range.White == 255);

        // A default levels layer used to map everything to black: White fell back to 0, which
        // collapsed the input range to [0, 1] and then sent it all to the black output point.
        var tables = settings.Tables();
        Check("Levels identity table keeps 100", tables[0][100] == 100);
        Check("Levels identity table keeps 200", tables[2][200] == 200);
        Check("Levels identity table keeps 0 and 255", tables[1][0] == 0 && tables[1][255] == 255);

        var custom = new LevelsSettings(new LevelRange(0, 1, 128, 0, 255));
        Check("Levels custom rgb range leaves the per-channel ranges alone", custom[LevelsChannel.Red] == LevelRange.Identity);
        Check("Levels custom rgb range maps the white point", custom.Tables()[0][255] == 255 && custom.Tables()[0][64] == 128);
    }

    private static void CurvesIdentity()
    {
        var settings = new CurvesSettings();
        Check("CurvesSettings() is identity", settings.IsIdentity);
        var tables = settings.Tables();
        Check("Curves identity table keeps 100", tables[0][100] == 100);
        Check("Curves identity table keeps 255", tables[2][255] == 255);
    }

    private static void ClipColorEdges()
    {
        // Black backdrop under Color: the result has to be black, not a half-clipped color.
        var onBlack = BlendFunctions.Color(BlendMode.Color, (0, 0, 0), (1, 0, 0));
        Check("Color over black is black", Close(onBlack, (0, 0, 0)));

        var greenOnBlack = BlendFunctions.Color(BlendMode.Color, (0, 0, 0), (0, 1, 0));
        Check("Green in Color over black is black", Close(greenOnBlack, (0, 0, 0)));

        // Luminosity takes the backdrop's color and the source's brightness, so a black source
        // under a white backdrop comes out black.
        var onWhite = BlendFunctions.Color(BlendMode.Luminosity, (1, 1, 1), (0, 0, 0));
        Check("Luminosity of black under white is black", Close(onWhite, (0, 0, 0)));

        var whiteUnderColor = BlendFunctions.Color(BlendMode.Luminosity, (0.2, 0.4, 0.6), (1, 1, 1));
        Check("Luminosity of white keeps the backdrop hue", Math.Abs(Lum(whiteUnderColor) - 1) < 0.002);
    }

    private static void ClipColorKnownValues()
    {
        // Worked through the compositing-1 ClipColor definition by hand.
        var color = BlendFunctions.Color(BlendMode.Color, (0.2, 0.4, 0.6), (1, 0, 0.5));
        Check("Color (0.2,0.4,0.6) <- (1,0,0.5)", Close(color, (1.0, 0.0109, 0.5055), 0.002));

        var hue = BlendFunctions.Color(BlendMode.Hue, (0.2, 0.4, 0.6), (0.9, 0.1, 0.3));
        Check("Hue keeps the backdrop luminance", Math.Abs(Lum(hue) - Lum((0.2, 0.4, 0.6))) < 0.002);

        var saturation = BlendFunctions.Color(BlendMode.Saturation, (0.2, 0.4, 0.6), (0.9, 0.1, 0.3));
        Check("Saturation keeps the backdrop luminance", Math.Abs(Lum(saturation) - Lum((0.2, 0.4, 0.6))) < 0.002);
    }

    private static void MaskSurvivesPartialOpacity()
    {
        var document = new CanvasDocument(1, 1);
        var white = document.Add(new Layer("white") { Pixels = Solid(255, 255, 255, 255) });
        var red = document.Add(new Layer("red")
        {
            Pixels = Solid(255, 0, 0, 255),
            Mask = [0],
            Opacity = 0.5
        });

        var flattened = document.Flatten();
        var pixel = flattened[0, 0];
        Check("mask 0 hides the layer even at opacity 0.5", pixel is { R: 255, G: 255, B: 255 });

        red.Mask = [255];
        var half = document.Flatten()[0, 0];
        Check("mask 255 at opacity 0.5 blends halfway", Close((half.R / 255.0, half.G / 255.0, half.B / 255.0), (1, 0.5, 0.5), 0.01));

        Check("CoverageFor returns the mask at opacity 0.5", red.CoverageFor(1, 1) is not null);
        Check("CoverageFor ignores a wrong-sized mask", red.CoverageFor(2, 2) is null);
    }

    private static void LayerStackOrder()
    {
        var document = new CanvasDocument(1, 1);
        var bottom = document.Add(new Layer("bottom") { Pixels = Solid(0, 0, 255, 255) });
        var top = document.Add(new Layer("top") { Pixels = Solid(255, 0, 0, 255) });

        Check("top layer wins by default", document.Flatten()[0, 0] is { R: 255, G: 0, B: 0 });

        document.Move(top, 0);
        Check("moving the top layer down puts the blue on top", document.Flatten()[0, 0] is { R: 0, G: 0, B: 255 });

        bottom.IsVisible = false;
        Check("hiding a layer drops it from the stack", document.Flatten()[0, 0] is { R: 255, G: 0, B: 0 });
    }

    private static PixelBuffer Solid(byte r, byte g, byte b, byte a)
    {
        var buffer = new PixelBuffer(1, 1);
        buffer.Data[0] = r;
        buffer.Data[1] = g;
        buffer.Data[2] = b;
        buffer.Data[3] = a;
        return buffer;
    }

    private static double Lum((double R, double G, double B) c) => (0.3 * c.R) + (0.59 * c.G) + (0.11 * c.B);

    private static bool Close((double R, double G, double B) actual, (double R, double G, double B) expected, double tolerance = 1.0 / 255)
        => Math.Abs(actual.R - expected.R) <= tolerance
            && Math.Abs(actual.G - expected.G) <= tolerance
            && Math.Abs(actual.B - expected.B) <= tolerance;

    private static void Check(string name, bool condition)
    {
        if (condition)
        {
            Console.WriteLine($"  ok   {name}");
            return;
        }
        _failures++;
        Console.WriteLine($"  FAIL {name}");
    }
}
