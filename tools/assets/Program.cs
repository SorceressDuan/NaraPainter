using OpenCvSharp;

namespace Compositor.Tools.Assets;

internal static class Program
{
    // Generates the test images and the application icon. From the repository root:
    //   dotnet run --project tools/assets -- .
    // The icon is drawn here rather than reused from the original project: this is an unofficial
    // port, so it should not carry the upstream app's identity.
    private static void Main(string[] args)
    {
        string root = args.Length > 0 ? args[0] : ".";
        string images = Path.Combine(root, "assets", "testimages");
        string icons = Path.Combine(root, "assets", "icon");
        Directory.CreateDirectory(images);
        Directory.CreateDirectory(icons);

        WriteGradient(Path.Combine(images, "gradient.png"), 640, 480);
        WriteShapes(Path.Combine(images, "shapes.png"), 512, 512);
        WritePhoto(Path.Combine(images, "photo.jpg"), 800, 600);
        WriteTransparent(Path.Combine(images, "transparent.webp"), 400, 300);
        WriteLarge(Path.Combine(images, "large-4000x3000.png"));
        WriteIcon(icons);

        Console.WriteLine("assets written to " + Path.GetFullPath(images));
    }

    // A smooth ramp plus a vertical alpha ramp, so a codec round trip has something to measure on
    // every channel.
    private static void WriteGradient(string path, int width, int height)
    {
        using var mat = new Mat(height, width, MatType.CV_8UC4);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte r = (byte)(x * 255 / Math.Max(1, width - 1));
                byte g = (byte)(y * 255 / Math.Max(1, height - 1));
                byte b = (byte)(((x / 32) + (y / 32)) % 2 == 0 ? 200 : 60);
                mat.Set(y, x, new Vec4b(b, g, r, 255));
            }
        }
        Cv2.ImWrite(path, mat);
    }

    // Hard edges and flat fills, which is what the blend modes are easiest to eyeball on.
    private static void WriteShapes(string path, int width, int height)
    {
        using var mat = new Mat(height, width, MatType.CV_8UC4, new Scalar(40, 40, 48, 255));
        Cv2.Rectangle(mat, new Rect(0, 0, width, height / 3), new Scalar(30, 90, 200, 255), -1);
        Cv2.Circle(mat, new Point(width / 2, height / 2), height / 5, new Scalar(60, 200, 120, 255), -1);
        Cv2.Rectangle(mat, new Rect(width / 8, height * 5 / 8, width / 3, height / 4), new Scalar(220, 180, 40, 255), -1);
        Cv2.Line(mat, new Point(0, height - 1), new Point(width - 1, 0), new Scalar(255, 255, 255, 255), 3);
        Cv2.ImWrite(path, mat);
    }

    // Noise over a gradient, so JPEG has something to lose and the blur paths have detail to work on.
    private static void WritePhoto(string path, int width, int height)
    {
        using var mat = new Mat(height, width, MatType.CV_8UC3);
        var random = new Random(1234);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double baseValue = 90 + (120.0 * x / width) + (40.0 * Math.Sin(y / 40.0));
                int noise = random.Next(-12, 13);
                byte b = (byte)Math.Clamp((baseValue * 0.7) + noise, 0, 255);
                byte g = (byte)Math.Clamp((baseValue * 0.9) + noise, 0, 255);
                byte r = (byte)Math.Clamp((baseValue * 1.1) + noise, 0, 255);
                mat.Set(y, x, new Vec3b(b, g, r));
            }
        }
        Cv2.ImWrite(path, mat, new ImageEncodingParam(ImwriteFlags.JpegQuality, 92));
    }

    private static void WriteTransparent(string path, int width, int height)
    {
        using var mat = new Mat(height, width, MatType.CV_8UC4, new Scalar(0, 0, 0, 0));
        Cv2.Rectangle(mat, new Rect(20, 20, width - 40, height - 40), new Scalar(180, 120, 60, 220), -1);
        Cv2.Circle(mat, new Point(width / 2, height / 2), height / 4, new Scalar(40, 40, 200, 128), -1);
        Cv2.ImWrite(path, mat);
    }

    // The awkward size from the acceptance criteria, mostly flat so the file stays small.
    private static void WriteLarge(string path)
    {
        using var mat = new Mat(3000, 4000, MatType.CV_8UC4);
        for (int y = 0; y < 3000; y++)
        {
            for (int x = 0; x < 4000; x++)
            {
                byte r = (byte)(x * 255 / 3999);
                byte g = (byte)(y * 255 / 2999);
                byte b = (byte)((x + y) % 256);
                mat.Set(y, x, new Vec4b(b, g, r, 255));
            }
        }
        Cv2.ImWrite(path, mat);
    }

    private static void WriteIcon(string directory)
    {
        foreach (int size in new[] { 16, 32, 48, 64, 128, 256 })
        {
            using var mat = new Mat(size, size, MatType.CV_8UC4, new Scalar(0, 0, 0, 0));
            Cv2.Rectangle(mat, new Rect(0, 0, size, size), new Scalar(38, 34, 30, 255), -1);

            // Two offset cards, standing in for a layer stack.
            int inset = Math.Max(1, size / 8);
            Cv2.Rectangle(mat, new Rect(inset, inset, size - (inset * 2), size - (inset * 2)), new Scalar(90, 150, 240, 255), -1);
            Cv2.Rectangle(mat, new Rect(inset * 2, inset * 2, size - (inset * 3), size - (inset * 3)), new Scalar(220, 200, 120, 255), -1);

            Cv2.ImWrite(Path.Combine(directory, $"compositor-{size}.png"), mat);
        }
    }
}
