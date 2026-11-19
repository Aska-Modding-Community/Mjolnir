using System;
using Mjolnir.SDK.Terrain;
using Xunit;

namespace Mjolnir.SDK.Terrain.Tests;

public class GroundBlurTests
{
    private const int N = 30;

    private static readonly float[] Binomial = [1 / 16f, 2 / 16f, 1 / 16f, 2 / 16f, 4 / 16f, 2 / 16f, 1 / 16f, 2 / 16f, 1 / 16f];

    private static readonly float[] Box = [1 / 9f, 1 / 9f, 1 / 9f, 1 / 9f, 1 / 9f, 1 / 9f, 1 / 9f, 1 / 9f, 1 / 9f];

    // A slope with a hill on it, in metres, every half metre.
    private static float[] Ground()
    {
        var h = new float[N * N];
        for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
                h[r * N + c] = 40 + 0.1f * r * 0.5f - 0.07f * c * 0.5f + 1.5f * MathF.Exp(-((r - 12) * (r - 12) + (c - 17) * (c - 17)) / 40f);
        return h;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void GroundPutStraightBackComesOutAsItWas(int which)
    {
        var kernel = which == 0 ? Binomial : Box;
        var ground = Ground();
        var applied = new bool[N * N];
        for (int r = 2; r < N - 2; r++)
            for (int c = 2; c < N - 2; c++) applied[r * N + c] = true;

        var naive = GroundBlur.Blur(ground, N, kernel);
        Assert.True(MaxOff(naive, ground, applied) > 0.005f); // written as it is, the blur moves it

        var written = (float[])ground.Clone();
        GroundBlur.Compensate(written, applied, N, kernel);
        Assert.True(MaxOff(GroundBlur.Blur(written, N, kernel), ground, applied) < 0.0005f);
    }

    [Fact]
    public void ARaisedDiscComesOutAsAskedAndTheRestIsNotApplied()
    {
        var ground = Ground();
        var asked = (float[])ground.Clone();
        var applied = new bool[N * N];
        for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
            {
                float d = MathF.Sqrt((r - 15) * (r - 15) + (c - 15) * (c - 15)) * 0.5f;
                if (d > 5) continue;
                applied[r * N + c] = true;
                float t = Math.Clamp(5 - d, 0, 2) / 2;
                asked[r * N + c] += t * t * (3 - 2 * t) * 1.2f;
            }
        var written = (float[])asked.Clone();
        float left = GroundBlur.Compensate(written, applied, N, Binomial);
        float off = MaxOff(GroundBlur.Blur(written, N, Binomial), asked, applied);
        Assert.True(off < 0.002f, $"{off} off, {left} left"); // within 2 mm: a kernel whose finest ripple it cannot keep
        float moved = 0;
        for (int i = 0; i < written.Length; i++) moved = MathF.Max(moved, MathF.Abs(written[i] - asked[i]));
        // A sharpening, most of it on the samples round the disc that are not applied: bounded, not a runaway.
        Assert.True(moved < 1.5f, $"the heights written move by up to {moved}");
    }

    [Fact]
    public void TheIdentityKernelLeavesTheHeightsAlone()
    {
        var ground = Ground();
        var written = (float[])ground.Clone();
        Assert.Equal(0, GroundBlur.Compensate(written, new bool[N * N], N, GroundBlur.Identity));
        Assert.Equal(ground, written);
    }

    private static float MaxOff(float[] got, float[] wanted, bool[] where)
    {
        float worst = 0;
        for (int i = 0; i < got.Length; i++)
            if (where[i]) worst = MathF.Max(worst, MathF.Abs(got[i] - wanted[i]));
        return worst;
    }
}
