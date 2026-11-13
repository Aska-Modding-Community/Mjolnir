using System;
using System.Globalization;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The tile a row shows in place of a missing picture (the Mods window, for a mod without an icon): a letter on a
/// colour of its own. The colour comes from a stable hash of an id, so a mod keeps it from one game start to the next
/// (string.GetHashCode changes per process). No Unity here: the view turns <see cref="Hue"/> into a colour.
/// </summary>
internal readonly record struct Monogram(string Letter, float Hue)
{
    /// <summary>The first letter or digit of <paramref name="name"/>, upper case ("?" without one), on the hue of <paramref name="id"/>.</summary>
    public static Monogram For(string name, string id)
    {
        string letter = "?";
        foreach (char c in name ?? "")
        {
            if (!char.IsLetterOrDigit(c)) continue;
            letter = char.ToUpper(c, CultureInfo.InvariantCulture).ToString();
            break;
        }
        return new Monogram(letter, HueOf(id ?? ""));
    }

    // FNV-1a over the id in lower case (mod ids compare case-insensitively), spread over the colour wheel.
    private static float HueOf(string id) => Fnv1a.OfChars(id.ToLowerInvariant()) % 360 / 360f;
}
