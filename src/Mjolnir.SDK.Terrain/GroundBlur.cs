using System;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// The blur the game gives every change of heights it applies, and its undoing. The game copies the change's heights, then
/// makes each sample inside the square's edge the 3 × 3 kernel's sum of the copy round it, and applies the samples marked
/// changed: so a height written as it is comes out as an average of it and its neighbours (a snapshot put straight back
/// moved the ground by 9.5 mm on average, an undo over four tiles by up to 0.35 m, seen in game). <see cref="Compensate"/>
/// finds the heights to give the square so that, blurred, every applied sample comes out as asked: the samples not applied
/// are free to take any height, the applied ones are moved by Landweber's iteration until the blur gives them back. Every
/// machine applies the same blur, so the other players get the same ground. Pure.
/// </summary>
internal static class GroundBlur
{
    /// <summary>The kernel that changes nothing.</summary>
    public static readonly float[] Identity = [0, 0, 0, 0, 1, 0, 0, 0, 0];

    /// <summary>What the game makes of the square <paramref name="heights"/> (n × n, row by row) with the kernel (3 × 3, row by row).</summary>
    public static float[] Blur(float[] heights, int n, float[] kernel)
    {
        var blurred = (float[])heights.Clone();
        for (int r = 1; r < n - 1; r++)
            for (int c = 1; c < n - 1; c++)
                blurred[r * n + c] = At(heights, n, kernel, r, c);
        return blurred;
    }

    /// <summary>
    /// Gives <paramref name="heights"/> (n × n, row by row: the heights asked for where <paramref name="applied"/>, the
    /// ground's own elsewhere) the heights to write so that the blur gives every applied sample what was asked; how far the
    /// worst one is left from it, in the heights' own units.
    /// </summary>
    public static float Compensate(float[] heights, bool[] applied, int n, float[] kernel, int iterations = 200, float tolerance = 1e-7f)
    {
        if (IsIdentity(kernel)) return 0;
        // Least squares by conjugate gradients (CGLS): the heights moved by x, where x is free on every sample but the applied
        // ones on the square's edge (applied as written); the blur of the moved heights matched to what was asked on the
        // applied samples inside the edge. Doubles: the heights are a fraction of the world's 160 m, and a step is a millimetre.
        int count = n * n;
        var wanted = new double[count];
        var w = new double[count];
        for (int i = 0; i < count; i++) wanted[i] = w[i] = heights[i];
        var r = new double[count];
        for (int y = 1; y < n - 1; y++)
            for (int x = 1; x < n - 1; x++)
            {
                int i = y * n + x;
                if (applied[i]) r[i] = wanted[i] - At(w, n, kernel, y, x);
            }
        var s = Spread(r, applied, n, kernel);
        var p = (double[])s.Clone();
        double gamma = Dot(s, s);
        double worst = MaxAbs(r);
        for (int round = 0; round < iterations && worst > tolerance && gamma > 0; round++)
        {
            var q = new double[count];
            for (int y = 1; y < n - 1; y++)
                for (int x = 1; x < n - 1; x++)
                {
                    int i = y * n + x;
                    if (applied[i]) q[i] = At(p, n, kernel, y, x);
                }
            double qq = Dot(q, q);
            if (qq <= 0) break;
            double alpha = gamma / qq;
            for (int i = 0; i < count; i++)
            {
                w[i] += alpha * p[i];
                r[i] -= alpha * q[i];
            }
            worst = MaxAbs(r);
            s = Spread(r, applied, n, kernel);
            double next = Dot(s, s);
            double beta = next / gamma;
            gamma = next;
            for (int i = 0; i < count; i++) p[i] = s[i] + beta * p[i];
        }
        for (int i = 0; i < count; i++) heights[i] = (float)w[i];
        return (float)worst;
    }

    // What is left spread back through the kernel turned about its middle, onto the samples free to move.
    private static double[] Spread(double[] residual, bool[] applied, int n, float[] kernel)
    {
        var s = new double[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                if (applied[i] && IsEdge(y, x, n)) continue; // applied as written, not blurred
                double sum = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int yy = y - dy, xx = x - dx;
                        if (yy < 1 || yy >= n - 1 || xx < 1 || xx >= n - 1) continue;
                        sum += kernel[(dy + 1) * 3 + dx + 1] * residual[yy * n + xx];
                    }
                s[i] = sum;
            }
        return s;
    }

    private static double At(double[] heights, int n, float[] kernel, int r, int c)
    {
        double sum = 0;
        for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
                sum += kernel[(dr + 1) * 3 + dc + 1] * heights[(r + dr) * n + c + dc];
        return sum;
    }

    private static double Dot(double[] a, double[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++) sum += a[i] * b[i];
        return sum;
    }

    private static double MaxAbs(double[] a)
    {
        double worst = 0;
        foreach (double v in a) worst = Math.Max(worst, Math.Abs(v));
        return worst;
    }

    private static float At(float[] heights, int n, float[] kernel, int r, int c)
    {
        float sum = 0;
        for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
                sum += kernel[(dr + 1) * 3 + dc + 1] * heights[(r + dr) * n + c + dc];
        return sum;
    }

    private static bool IsEdge(int r, int c, int n) => r == 0 || c == 0 || r == n - 1 || c == n - 1;

    private static bool IsIdentity(float[] kernel)
    {
        for (int i = 0; i < 9; i++)
            if (MathF.Abs(kernel[i] - Identity[i]) > 1e-6f) return false;
        return true;
    }
}
