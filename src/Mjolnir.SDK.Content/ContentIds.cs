using System;
using System.Linq;

namespace Mjolnir.SDK.Content;

/// <summary>
/// The identity of a mod's content in the game (no Unity: unit-tested). An id is a hash of the mod id and the name,
/// never of the load order, so it is the same for every player and every start; the game's own ids are 999 to
/// 126 607 971, so mod ids are negative. The asset name is what the game keys an item's texts by and finds it by name.
/// </summary>
internal static class ContentIds
{
    private const int FirstEffectId = 20000;
    private const uint EffectIds = 12768;

    /// <summary>The id of a mod's item.</summary>
    public static int ItemId(string modId, string name) => Hash($"{modId}/item/{name}");

    /// <summary>The id of a mod's recipe (a blueprint is an item of the game too, with an id of its own).</summary>
    public static int RecipeId(string modId, string name) => Hash($"{modId}/recipe/{name}");

    /// <summary>The id of a mod's building (a structure template is an item of the game too).</summary>
    public static int BuildingId(string modId, string name) => Hash($"{modId}/building/{name}");

    /// <summary>
    /// A building's network prefab's guid (Fusion's <c>NetworkObjectGuid</c>, 32 hex digits): the MD5 of
    /// <c>&lt;mod id&gt;/building/&lt;name&gt;</c>, the same on every machine; the game's guids are random.
    /// </summary>
    public static string BuildingGuid(string modId, string name) =>
        Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes($"{modId}/building/{name}")));

    /// <summary>
    /// The id of a mod's status effect. The network carries an effect's id in 16 bits, so it is shorter than an item's: from
    /// 20000 up to 32767, above the game's own (up to 481) and positive, a hash of the mod id and the name as well.
    /// </summary>
    public static int EffectId(string modId, string name) => FirstEffectId + (int)((uint)Hash($"{modId}/effect/{name}") % EffectIds);

    /// <summary>The asset name of a mod's status effect, as the game's are named (Effect_&lt;name&gt;).</summary>
    public static string EffectAssetName(string modId, string name) => $"Effect_{modId}_{name}";

    /// <summary>The asset name of a mod's item, as the game's are named (Item_&lt;group&gt;_&lt;name&gt;).</summary>
    public static string ItemAssetName(string modId, string name) => $"Item_{modId}_{name}";

    /// <summary>
    /// The asset name of a mod's building, named as its items are: a mod's items and buildings share their names.
    /// </summary>
    public static string BuildingAssetName(string modId, string name) => $"Item_{modId}_{name}";

    /// <summary>The asset name of a mod's recipe.</summary>
    public static string RecipeAssetName(string modId, string name) => $"Item_{modId}_{name}_Recipe";

    /// <summary>A name a mod gives its content: letters, digits and underscores, so it fits keys and asset names.</summary>
    public static bool IsValidName(string? name) =>
        !string.IsNullOrEmpty(name) && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    // FNV-1a over the text's chars, then the sign bit set: deterministic across machines and processes (string.GetHashCode
    // is not), and apart from the game's ids, which are all non-negative.
    private static int Hash(string text) => (int)(Fnv1a.OfChars(text) | 0x80000000);
}
