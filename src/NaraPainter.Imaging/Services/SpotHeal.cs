namespace NaraPainter.Imaging.Services;

/// <summary>
/// Spot healing: rebuilds the pixels a brush stroke covered from the texture around them.
/// </summary>
/// <remarks>
/// A direct port of <c>legacy/Compositor/Rendering/HealPixels.c</c>. That file works on premultiplied
/// RGBA because it solves the alpha channel alongside the colours and clamps a colour to the alpha it
/// ends up with; <see cref="NaraPainter.Models.Pixels.PixelBuffer"/> is straight alpha, so the two
/// conversions bracket the solve rather than being folded into it. The cost is that a fully
/// transparent pixel has no colour to heal from, which is the same trade the original makes.
/// </remarks>
public static class SpotHeal
{
    /// <summary>Texture copied from the nearby patch whose surrounding ring best matches this spot.</summary>
    public const int ContentAware = 0;

    /// <summary>Filled smoothly from the spot's edges, with grain matched to the detail around it.</summary>
    public const int CreateTexture = 1;

    /// <summary>Like <see cref="ContentAware"/>, but only the closest patch is considered.</summary>
    public const int ProximityMatch = 2;

    private const int Outside = 0;
    private const int Ring = 1;
    private const int Hole = 2;

    /// <summary>Half-open bounds of the nonzero bytes in a coverage mask, or all zero when it is empty.</summary>
    // MIGRATION: heal_coverage_bounds (HealPixels.c)
    public static (int X0, int Y0, int X1, int Y1) CoverageBounds(ReadOnlySpan<byte> coverage, int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (coverage.Length < (long)width * height) throw new ArgumentException("The coverage is smaller than the dimensions.", nameof(coverage));

        int x0 = width, y0 = height, x1 = 0, y1 = 0;
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                if (coverage[row + x] == 0) continue;
                if (x < x0) x0 = x;
                if (x + 1 > x1) x1 = x + 1;
                if (y < y0) y0 = y;
                if (y + 1 > y1) y1 = y + 1;
            }
        }

        if (x1 <= x0 || y1 <= y0) return (0, 0, 0, 0);
        return (x0, y0, x1, y1);
    }

    /// <summary>True when no byte of the coverage is set, which is the same test <see cref="CoverageBounds"/> makes.</summary>
    public static bool IsEmpty(ReadOnlySpan<byte> coverage)
    {
        foreach (byte value in coverage)
        {
            if (value != 0) return false;
        }

        return true;
    }

    /// <summary>
    /// Heals <paramref name="rgba"/> in place, where <paramref name="coverage"/> marks what to heal
    /// (0 to 255, as a brush lays it down). <paramref name="opacity"/> scales the whole replacement on
    /// top of the per-pixel coverage. Returns false only when a work buffer cannot be allocated, which
    /// is the single failure the original reports.
    /// </summary>
    /// <remarks>
    /// The caller owns the premultiplied conversion: pass a premultiplied buffer and read back a
    /// premultiplied one.
    /// </remarks>
    // MIGRATION: spot_heal (HealPixels.c)
    public static bool Apply(byte[] rgba, byte[] coverage, int width, int height, float opacity, int mode, uint seed)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        ArgumentNullException.ThrowIfNull(coverage);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (rgba.Length < (long)width * height * 4) throw new ArgumentException("The buffer is smaller than the dimensions.", nameof(rgba));
        if (coverage.Length < (long)width * height) throw new ArgumentException("The coverage is smaller than the dimensions.", nameof(coverage));

        int w = width, h = height;
        (int bx0, int by0, int bx1, int by1) = CoverageBounds(coverage, width, height);
        if (bx1 <= bx0) return true;

        long boundsW = bx1 - bx0, boundsH = by1 - by0;
        long size = Math.Max(boundsW, boundsH);
        long ring = Math.Clamp(size / 8, 2, 16);

        // Work box: the spot plus its ring, clipped to the image.
        long wx0 = Math.Max(0, bx0 - ring), wy0 = Math.Max(0, by0 - ring);
        long wx1 = Math.Min(w, (long)bx1 + ring), wy1 = Math.Min(h, (long)by1 + ring);
        long ww = wx1 - wx0, wh = wy1 - wy0, wn = ww * wh;
        if (ww <= 0 || wh <= 0) return true;

        byte[] role = new byte[wn];
        byte[] near = new byte[wn];
        long[] prefix = new long[Math.Max(ww, wh) + 1];
        float[] value = new float[wn * 4];

        // HOLE where the brush covered a pixel, OUTSIDE everywhere else to start with.
        for (long y = 0; y < wh; y++)
        {
            long row = y * ww;
            long sourceRow = (wy0 + y) * width;
            for (long x = 0; x < ww; x++)
            {
                role[row + x] = coverage[sourceRow + wx0 + x] != 0 ? (byte)Hole : (byte)Outside;
            }
        }

        // The ring: pixels within `ring` of the spot. A square dilation done as a row pass, then a
        // column pass over what the row pass found.
        for (long y = 0; y < wh; y++)
        {
            prefix[0] = 0;
            long row = y * ww;
            for (long x = 0; x < ww; x++)
            {
                prefix[x + 1] = prefix[x] + (role[row + x] == Hole ? 1 : 0);
            }

            for (long x = 0; x < ww; x++)
            {
                long lo = Math.Max(0, x - ring);
                long hi = Math.Min(ww, x + ring + 1);
                near[row + x] = prefix[hi] - prefix[lo] > 0 ? (byte)1 : (byte)0;
            }
        }

        for (long x = 0; x < ww; x++)
        {
            prefix[0] = 0;
            for (long y = 0; y < wh; y++)
            {
                prefix[y + 1] = prefix[y] + near[(y * ww) + x];
            }

            for (long y = 0; y < wh; y++)
            {
                long lo = Math.Max(0, y - ring);
                long hi = Math.Min(wh, y + ring + 1);
                if (role[(y * ww) + x] == Outside && prefix[hi] - prefix[lo] > 0) role[(y * ww) + x] = Ring;
            }
        }

        long ringCount = 0;
        for (long p = 0; p < wn; p++)
        {
            if (role[p] == Ring) ringCount++;
        }

        if (ringCount == 0) return true;

        // Source patch for Content-Aware and Proximity Match. Create Texture has none, which is what
        // switches the rest of the routine over to a smooth fill plus grain.
        long ox = 0, oy = 0;
        bool haveSource = false;
        if (mode != CreateTexture)
        {
            double[] factors = [1.05, 1.35, 1.75, 2.25, 2.8];
            int count = mode == ProximityMatch ? 2 : 5;
            double best = double.PositiveInfinity;

            for (int f = 0; f < count; f++)
            {
                for (int a = 0; a < 24; a++)
                {
                    double angle = a * Math.PI / 12.0;
                    long dx = (long)Math.Round(Math.Cos(angle) * factors[f] * ww);
                    long dy = (long)Math.Round(Math.Sin(angle) * factors[f] * wh);
                    double score = Score(rgba, width, role, wx0, wy0, ww, wh, dx, dy, w, h);
                    if (double.IsInfinity(score)) continue;

                    // Nearer patches win ties, more so in Proximity Match.
                    score *= mode == ProximityMatch ? 1.0 + (0.6 * f) : 1.0 + (0.1 * f);
                    if (score < best)
                    {
                        best = score;
                        ox = dx;
                        oy = dy;
                    }
                }
            }

            if (!double.IsInfinity(best))
            {
                // Fine-tune the alignment so repeating texture lines up.
                long cx = ox, cy = oy;
                double refined = Score(rgba, width, role, wx0, wy0, ww, wh, cx, cy, w, h);
                for (long j = -3; j <= 3; j++)
                {
                    for (long i = -3; i <= 3; i++)
                    {
                        double score = Score(rgba, width, role, wx0, wy0, ww, wh, cx + i, cy + j, w, h);
                        if (score < refined)
                        {
                            refined = score;
                            ox = cx + i;
                            oy = cy + j;
                        }
                    }
                }

                haveSource = true;
            }
        }

        // Membrane: the edge difference between the original and the patch, spread across the spot.
        // Without a patch it is the original's own edge tone, and the detail around the spot becomes
        // grain instead.
        double[] mean = new double[4];
        double[] detail = new double[3];
        for (long y = 0; y < wh; y++)
        {
            long row = y * ww;
            long iy = wy0 + y;
            for (long x = 0; x < ww; x++)
            {
                long p = row + x;
                if (role[p] != Ring)
                {
                    Array.Clear(value, (int)(p * 4), 4);
                    continue;
                }

                long ix = wx0 + x;
                long t = ((iy * width) + ix) * 4;
                long s = haveSource ? ((((iy + oy) * width) + ix + ox) * 4) : -1;
                for (int c = 0; c < 4; c++)
                {
                    float delta = s >= 0 ? rgba[t + c] - rgba[s + c] : rgba[t + c];
                    value[(p * 4) + c] = delta;
                    mean[c] += delta;
                }

                if (haveSource) continue;

                // Fine detail around the spot: each pixel against the average of its neighbours.
                for (int c = 0; c < 3; c++)
                {
                    double around = 0;
                    int n = 0;
                    if (ix - 1 >= 0) { around += rgba[(((iy * width) + ix - 1) * 4) + c]; n++; }
                    if (ix + 1 < w) { around += rgba[(((iy * width) + ix + 1) * 4) + c]; n++; }
                    if (iy - 1 >= 0) { around += rgba[((((iy - 1) * width) + ix) * 4) + c]; n++; }
                    if (iy + 1 < h) { around += rgba[((((iy + 1) * width) + ix) * 4) + c]; n++; }

                    if (n > 0)
                    {
                        double d = rgba[t + c] - (around / n);
                        detail[c] += d * d;
                    }
                }
            }
        }

        for (int c = 0; c < 4; c++) mean[c] /= ringCount;
        for (long p = 0; p < wn; p++)
        {
            if (role[p] != Hole) continue;
            for (int c = 0; c < 4; c++) value[(p * 4) + c] = (float)mean[c];
        }

        Solve(value, role, ww, wh, 0);

        for (int c = 0; c < 3; c++) detail[c] = Math.Sqrt(detail[c] / ringCount) * 0.9;

        var outValue = new double[4];
        for (long y = 0; y < wh; y++)
        {
            long row = y * ww;
            long iy = wy0 + y;
            for (long x = 0; x < ww; x++)
            {
                long p = row + x;
                if (role[p] != Hole) continue;

                long ix = wx0 + x;
                long t = ((iy * width) + ix) * 4;
                long s = haveSource ? ((((iy + oy) * width) + ix + ox) * 4) : -1;
                double amount = coverage[(int)((iy * width) + ix)] / 255.0 * opacity;
                double grain = 0;
                if (!haveSource)
                {
                    uint key = Hash(seed ^ Hash((uint)((iy * w) + ix)));
                    double u1 = Unit(key), u2 = Unit(key ^ 0x68e31da4U);
                    grain = Math.Sqrt(-2.0 * Math.Log(1.0 - u1)) * Math.Cos(2.0 * Math.PI * u2);
                }

                for (int c = 0; c < 4; c++)
                {
                    double healed = (s >= 0 ? rgba[s + c] : 0) + value[(p * 4) + c] + (c < 3 ? grain * detail[c] : 0);
                    outValue[c] = rgba[t + c] + ((healed - rgba[t + c]) * amount);
                }

                double alpha = Math.Clamp(outValue[3], 0, 255);
                rgba[t + 3] = (byte)Math.Round(alpha, MidpointRounding.AwayFromZero);
                for (int c = 0; c < 3; c++)
                {
                    double channel = Math.Clamp(outValue[c], 0, rgba[t + 3]);
                    rgba[t + c] = (byte)Math.Round(channel, MidpointRounding.AwayFromZero);
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Mean squared difference between the ring around the spot and the ring around the patch offset
    /// by (<paramref name="dx"/>, <paramref name="dy"/>). Infinite when the patch would overlap the
    /// spot or leave the image.
    /// </summary>
    private static double Score(byte[] rgba, int width, byte[] role, long wx0, long wy0, long ww, long wh, long dx, long dy, long w, long h)
    {
        if (Math.Abs(dx) < ww && Math.Abs(dy) < wh) return double.PositiveInfinity;
        if (wx0 + dx < 0 || wy0 + dy < 0 || wx0 + ww + dx > w || wy0 + wh + dy > h) return double.PositiveInfinity;

        double sum = 0;
        long n = 0;
        for (long y = 0; y < wh; y++)
        {
            long row = y * ww;
            long t = (((wy0 + y) * width) + wx0) * 4;
            long s = ((((wy0 + y + dy) * width) + wx0 + dx)) * 4;
            for (long x = 0; x < ww; x++)
            {
                if (role[row + x] != Ring) continue;

                long ti = t + (x * 4);
                long si = s + (x * 4);
                for (int c = 0; c < 4; c++)
                {
                    double d = rgba[ti + c] - rgba[si + c];
                    sum += d * d;
                }

                n++;
            }
        }

        return n > 0 ? sum / n : double.PositiveInfinity;
    }

    /// <summary>
    /// Solves for smooth values over HOLE pixels, fixed to the RING values around them. A coarser copy
    /// is solved first and used as the starting point, so large spots settle in few passes.
    /// </summary>
    private static void Solve(float[] value, byte[] role, long w, long h, int depth)
    {
        int iterations = 300;
        if (w > 32 && h > 32 && depth < 16)
        {
            long cw = (w + 1) / 2, ch = (h + 1) / 2;
            var coarse = new float[cw * ch * 4];
            var downRole = new byte[cw * ch];

            var knownSum = new float[4];
            var holeSum = new float[4];
            for (long y = 0; y < ch; y++)
            {
                for (long x = 0; x < cw; x++)
                {
                    int known = 0, hole = 0;
                    Array.Clear(knownSum);
                    Array.Clear(holeSum);
                    for (long j = 0; j < 2; j++)
                    {
                        for (long i = 0; i < 2; i++)
                        {
                            long fx = (x * 2) + i, fy = (y * 2) + j;
                            if (fx >= w || fy >= h) continue;

                            long p = (fy * w) + fx;
                            if (role[p] == Ring)
                            {
                                known++;
                                for (int c = 0; c < 4; c++) knownSum[c] += value[(p * 4) + c];
                            }
                            else if (role[p] == Hole)
                            {
                                hole++;
                                for (int c = 0; c < 4; c++) holeSum[c] += value[(p * 4) + c];
                            }
                        }
                    }

                    long q = (y * cw) + x;
                    if (known > 0)
                    {
                        downRole[q] = Ring;
                        for (int c = 0; c < 4; c++) coarse[(q * 4) + c] = knownSum[c] / known;
                    }
                    else if (hole > 0)
                    {
                        downRole[q] = Hole;
                        for (int c = 0; c < 4; c++) coarse[(q * 4) + c] = holeSum[c] / hole;
                    }
                }
            }

            Solve(coarse, downRole, cw, ch, depth + 1);

            for (long y = 0; y < h; y++)
            {
                for (long x = 0; x < w; x++)
                {
                    long p = (y * w) + x, q = ((y / 2) * cw) + (x / 2);
                    if (role[p] == Hole && downRole[q] == Hole)
                    {
                        Array.Copy(coarse, q * 4, value, p * 4, 4);
                    }
                }
            }

            iterations = 40;
        }

        const float Omega = 1.8f;
        for (int it = 0; it < iterations; it++)
        {
            for (long y = 0; y < h; y++)
            {
                for (long x = 0; x < w; x++)
                {
                    long p = (y * w) + x;
                    if (role[p] != Hole) continue;

                    float sum0 = 0, sum1 = 0, sum2 = 0, sum3 = 0;
                    int n = 0;
                    if (x - 1 >= 0 && role[p - 1] != Outside) { sum0 += value[((p - 1) * 4)]; sum1 += value[((p - 1) * 4) + 1]; sum2 += value[((p - 1) * 4) + 2]; sum3 += value[((p - 1) * 4) + 3]; n++; }
                    if (x + 1 < w && role[p + 1] != Outside) { sum0 += value[((p + 1) * 4)]; sum1 += value[((p + 1) * 4) + 1]; sum2 += value[((p + 1) * 4) + 2]; sum3 += value[((p + 1) * 4) + 3]; n++; }
                    if (y - 1 >= 0 && role[p - w] != Outside) { sum0 += value[((p - w) * 4)]; sum1 += value[((p - w) * 4) + 1]; sum2 += value[((p - w) * 4) + 2]; sum3 += value[((p - w) * 4) + 3]; n++; }
                    if (y + 1 < h && role[p + w] != Outside) { sum0 += value[((p + w) * 4)]; sum1 += value[((p + w) * 4) + 1]; sum2 += value[((p + w) * 4) + 2]; sum3 += value[((p + w) * 4) + 3]; n++; }
                    if (n == 0) continue;

                    float inverse = 1f / n;
                    value[(p * 4)] += Omega * ((sum0 * inverse) - value[(p * 4)]);
                    value[(p * 4) + 1] += Omega * ((sum1 * inverse) - value[(p * 4) + 1]);
                    value[(p * 4) + 2] += Omega * ((sum2 * inverse) - value[(p * 4) + 2]);
                    value[(p * 4) + 3] += Omega * ((sum3 * inverse) - value[(p * 4) + 3]);
                }
            }
        }
    }

    /// <summary>The original's integer mixer, kept bit for bit so the grain matches.</summary>
    private static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7feb352dU;
        x ^= x >> 15;
        x *= 0x846ca68bU;
        x ^= x >> 16;
        return x;
    }

    private static double Unit(uint key) => (Hash(key) >> 8) / 16777216.0;
}
