namespace NaraPainter.Models.Adjustments;

public readonly record struct CurvePoint(double X, double Y);

/// <summary>
/// Curves, 0-255 on both axes. The RGB composite is applied after the per-channel curves, the same
/// order as Levels. Interpolation is monotone cubic Hermite, so the curve cannot overshoot between
/// two handles the way a plain spline would.
/// </summary>
public sealed class CurvesSettings : AdjustmentSettings
{
    public const int ChannelCount = 4;
    public const int MinPoints = 2;
    public const int MaxPoints = 32;

    private readonly CurvePoint[][] _channels;

    public CurvesSettings()
    {
        _channels = new CurvePoint[ChannelCount][];
        for (int c = 0; c < ChannelCount; c++)
        {
            _channels[c] = [new CurvePoint(0, 0), new CurvePoint(255, 255)];
        }
    }

    private CurvesSettings(CurvePoint[][] channels) => _channels = channels;

    public override string DisplayName => "Curves";

    public IReadOnlyList<CurvePoint> Points(LevelsChannel channel) => _channels[(int)channel];

    public bool IsIdentity
    {
        get
        {
            foreach (var points in _channels)
            {
                if (points.Length != 2 || points[0] != new CurvePoint(0, 0) || points[1] != new CurvePoint(255, 255))
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Replaces one channel's handles. Points must be sorted by X with X strictly increasing, and the
    /// first and last must stay pinned at 0 and 255 so the curve always covers the full range.
    /// </summary>
    public CurvesSettings With(LevelsChannel channel, IEnumerable<CurvePoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var list = points.OrderBy(p => p.X).ToArray();
        if (list.Length < MinPoints || list.Length > MaxPoints)
        {
            throw new ArgumentException($"A curve needs between {MinPoints} and {MaxPoints} points.", nameof(points));
        }

        for (int i = 1; i < list.Length; i++)
        {
            if (list[i].X <= list[i - 1].X)
            {
                throw new ArgumentException("Curve points must have strictly increasing x.", nameof(points));
            }
        }

        if (list[0].X != 0 || list[^1].X != 255)
        {
            throw new ArgumentException("A curve must start at 0 and end at 255.", nameof(points));
        }

        var copy = new CurvePoint[ChannelCount][];
        for (int c = 0; c < ChannelCount; c++)
        {
            copy[c] = c == (int)channel ? list : _channels[c];
        }
        return new CurvesSettings(copy);
    }

    /// <summary>The per-channel 256-entry tables this adjustment applies.</summary>
    public byte[][] Tables()
    {
        var rgb = _channels[(int)LevelsChannel.Rgb];
        var tables = new byte[3][];
        for (int c = 0; c < 3; c++)
        {
            var channel = _channels[c + 1];
            var table = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                double value = Evaluate(channel, i) / 255.0;
                value = Evaluate(rgb, value * 255) / 255.0;
                table[i] = value <= 0 ? (byte)0 : value >= 1 ? (byte)255 : (byte)Math.Round(value * 255, MidpointRounding.AwayFromZero);
            }
            tables[c] = table;
        }
        return tables;
    }

    public override AdjustmentSettings Clone()
    {
        var copy = new CurvePoint[ChannelCount][];
        for (int c = 0; c < ChannelCount; c++) copy[c] = (CurvePoint[])_channels[c].Clone();
        return new CurvesSettings(copy);
    }

    /// <summary>Monotone cubic Hermite (Fritsch-Carlson) through the handles.</summary>
    private static double Evaluate(CurvePoint[] points, double x)
    {
        if (points.Length == 2 && points[0].Y == 0 && points[1].Y == 255) return Math.Clamp(x, 0, 255);

        int i = 0;
        for (int k = points.Length - 2; k >= 0; k--)
        {
            if (points[k].X <= x)
            {
                i = k;
                break;
            }
        }

        double h = points[i + 1].X - points[i].X;
        if (h <= 0) return points[i].Y;

        double t = Math.Clamp((x - points[i].X) / h, 0, 1);
        double m0 = Slope(points, i);
        double m1 = Slope(points, i + 1);

        double t2 = t * t;
        double t3 = t2 * t;
        double y = ((2 * t3) - (3 * t2) + 1) * points[i].Y
            + (t3 - (2 * t2) + t) * h * m0
            + ((-2 * t3) + (3 * t2)) * points[i + 1].Y
            + (t3 - t2) * h * m1;
        return Math.Clamp(y, 0, 255);
    }

    private static double Slope(CurvePoint[] points, int index)
    {
        double[] delta = new double[points.Length - 1];
        for (int k = 0; k < delta.Length; k++)
        {
            delta[k] = (points[k + 1].Y - points[k].Y) / (points[k + 1].X - points[k].X);
        }

        if (index == 0) return delta[0];
        if (index == points.Length - 1) return delta[^1];

        // A sign change means a local extremum, and the tangent has to be flat there or the curve
        // bulges past the handle. Otherwise take the harmonic mean.
        if (delta[index - 1] * delta[index] <= 0) return 0;
        return 2 / ((1 / delta[index - 1]) + (1 / delta[index]));
    }
}
