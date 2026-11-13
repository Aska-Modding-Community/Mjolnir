namespace Mjolnir.SDK.UI;

/// <summary>The ids of counting notifications (<see cref="Notify.Counter"/>; pure, unit-tested).</summary>
internal static class NotifyIds
{
    /// <summary>
    /// The game's stacking id for a mod's key: FNV-1a over "&lt;mod assembly&gt;/&lt;key&gt;", so the same key of two mods never
    /// shares a notification and the id is the same in every session (string.GetHashCode changes per process).
    /// </summary>
    public static int Of(string owner, string key) => unchecked((int)Fnv1a.OfChars(owner + "/" + key));
}
