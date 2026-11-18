using System;
using UnityEngine;

namespace Mjolnir.SDK.Terrain;

/// <summary>The sums of placed objects over time: a bush's mark while the game writes it, a tree's grown size.</summary>
internal static class Regrowth
{
    /// <summary>
    /// The mark a placed bush at rest holds while the game writes it: when it gives again, in hundredths of a world day,
    /// below -1 so that it is never 0 (the game writes no world item whose count is 0).
    /// </summary>
    public static int Encode(float due) => -1 - (int)Math.Clamp(MathF.Round(due * 100f), 0f, int.MaxValue - 1f);

    /// <summary>When a marked bush gives again, in world days.</summary>
    public static float Decode(int count) => (-1 - (long)count) / 100f;

    /// <summary>
    /// The scale a placed tree grows to: between its kind's smallest and largest, the same for the same place on every
    /// machine, so trees placed together do not all grow alike.
    /// </summary>
    public static float GrownScale(float minimum, float maximum, Vector3 position)
    {
        uint hash = Fnv1a.OfUtf8(FormattableString.Invariant($"{MathF.Round(position.x * 10f)},{MathF.Round(position.z * 10f)}"));
        return minimum + (maximum - minimum) * (hash / (float)uint.MaxValue);
    }
}
