using System.Globalization;
using System.Linq;

namespace Mjolnir.SDK.UI;

/// <summary>The rules of mod tabs in the game's own windows (<see cref="MenuTabs"/>), without Unity: unit-tested.</summary>
internal static class MenuTabRules
{
    /// <summary>
    /// The tab order of the first mod tab: the game sorts its tabs by it for Q/E and counts its own from 0, so mod tabs
    /// come after the game's, in the order they were made.
    /// </summary>
    public const int FirstOrder = 1000;

    /// <summary>What a tab without a picture shows: the header's first letter or digit, upper-case; "?" without one.</summary>
    public static string Initial(string? header)
    {
        char c = (header ?? "").FirstOrDefault(char.IsLetterOrDigit);
        return c == default ? "?" : char.ToUpper(c, CultureInfo.CurrentCulture).ToString();
    }

    /// <summary>The second line of a mod tab's hover card: the name of the mod that added it, else its assembly's.</summary>
    public static string Details(string? modName, string? assemblyName) =>
        !string.IsNullOrWhiteSpace(modName) ? modName : assemblyName ?? "";
}
