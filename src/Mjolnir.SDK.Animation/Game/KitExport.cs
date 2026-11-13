using System;
using System.IO;

namespace Mjolnir.SDK.Animation;

/// <summary>
/// Where the game's own files for the Mjolnir kit go: <c>Mjolnir/ModKit/</c>, with the skeletons in <c>rigs/</c> (always), the
/// characters' models in <c>models/&lt;rig&gt;/</c> (<c>[Kit] Models</c>) and the game's clips in <c>clips/</c>
/// (<c>[Kit] Animations</c>), and a README saying what they are. The kit's Import from the Game reads that folder. A Mjolnir of
/// before wrote the same into <c>Mjolnir/rigs/</c>, which goes once the new folder is written.
/// </summary>
internal static class KitExport
{
    /// <summary>The kit's folder in the game, null without the loader's folder (unit tests).</summary>
    public static string? Root => PlatformPaths.Loader is { } loader ? Path.Combine(loader, "ModKit") : null;

    public static string? Rigs => Sub("rigs");

    public static string? Models => Sub("models");

    public static string? Clips => Sub("clips");

    private static string? Sub(string name) => Root is { } root ? Path.Combine(root, name) : null;

    private const string Readme =
        "Mjolnir kit: the game's own files for the Mjolnir kit (unity/Mjolnir.ModKit), written by Mjolnir.SDK.Animation.\r\n" +
        "In the kit: Mjolnir > Rigs > Import from the Game.\r\n\r\n" +
        "rigs/    the game's skeletons: bone names and poses (always written)\r\n" +
        "models/  the characters' models and textures (\"Characters' models for the kit\" on Mjolnir's settings page)\r\n" +
        "clips/   the game's animation clips of the people (\"The game's animations for the kit\" there)\r\n\r\n" +
        "These are the game's assets, for this computer only: do not share them or put them into a mod.\r\n" +
        "Deleting this folder is safe; the game writes it again.\r\n";

    /// <summary>Makes the folder with its README, and takes away the folder of an earlier Mjolnir. Logged; never throws.</summary>
    public static void Prepare()
    {
        if (Root is not { } root || PlatformPaths.Loader is not { } loader) return;
        try
        {
            Directory.CreateDirectory(root);
            string readme = Path.Combine(root, "README.txt");
            if (!File.Exists(readme) || File.ReadAllText(readme) != Readme) File.WriteAllText(readme, Readme);
            string old = Path.Combine(loader, "rigs");
            if (Directory.Exists(old))
            {
                Directory.Delete(old, true);
                SdkLog.Info(GameAnimations.Source, $"Animations: the kit's files of an earlier Mjolnir in {old} are gone; they are in {root} now");
            }
        }
        catch (Exception e)
        {
            SdkLog.Warn(GameAnimations.Source, $"Animations: the kit's folder {root} could not be made ready: {e.Message}");
        }
    }
}
