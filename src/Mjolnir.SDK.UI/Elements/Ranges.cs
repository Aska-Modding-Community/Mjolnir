using System;

namespace Mjolnir.SDK.UI;

/// <summary>What a slider and a progress bar share about their range.</summary>
internal static class Ranges
{
    /// <summary><paramref name="value"/> within the limits, whichever way round they are; NaN gives the lower one.</summary>
    public static double Clamp(double value, double a, double b)
    {
        double low = Math.Min(a, b), high = Math.Max(a, b);
        return double.IsNaN(value) ? low : Math.Clamp(value, low, high);
    }
}
