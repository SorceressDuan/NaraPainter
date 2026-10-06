namespace NaraPainter.Imaging.Services;

/// <summary>
/// Resamples the points a pointer reports into the evenly spaced positions a brush stamp needs.
/// </summary>
public static class BrushStroke
{
    /// <summary>
    /// Inserts points so no two neighbours end up farther apart than <paramref name="spacing"/>.
    /// Without this a quick drag between two samples leaves a gap in the stroke.
    /// </summary>
    public static List<(int X, int Y)> Interpolate(IReadOnlyList<(int X, int Y)> points, double spacing)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (spacing <= 0) throw new ArgumentOutOfRangeException(nameof(spacing));

        var samples = new List<(int X, int Y)>(points.Count);
        if (points.Count == 0) return samples;

        // Integer points cannot sit closer than one pixel, so a finer spacing changes nothing.
        double step = Math.Max(spacing, 1);
        samples.Add(points[0]);

        for (int i = 1; i < points.Count; i++)
        {
            (int X, int Y) from = points[i - 1];
            (int X, int Y) to = points[i];
            double dx = to.X - from.X;
            double dy = to.Y - from.Y;
            int segments = (int)Math.Ceiling(Math.Sqrt((dx * dx) + (dy * dy)) / step);

            for (int s = 1; s < segments; s++)
            {
                double t = (double)s / segments;
                var sample = ((int)Math.Round(from.X + (t * dx)), (int)Math.Round(from.Y + (t * dy)));
                if (sample != samples[^1]) samples.Add(sample);
            }

            if (to != samples[^1]) samples.Add(to);
        }

        return samples;
    }

    /// <summary>Spacing that leaves no visible scalloping between stamps of the given radius.</summary>
    public static double SpacingFor(int radius) => Math.Max(1, radius / 4.0);
}
