using HarmonyLib;
using Mjolnir.SDK;
using SSSGame.Controllers;

namespace Mjolnir.InfiniteStamina;

/// <summary>
/// Example mod: stamina is never spent. It shows the parts every mod has: the [ModManifest] class deriving from
/// Mod, a config value (Mjolnir/Config/mjolnir.infinitestamina.toml), the mod's log and raw Harmony patches
/// applied in OnLoad. (The SDK's player module offers the same through a C# event, LocalPlayer.Stamina.Draining: see
/// PlayerExample.)
/// </summary>
[ModManifest("mjolnir.infinitestamina", "Infinite Stamina", "1.0.0", Description = "Stamina is never spent.", Author = "Mjolnir")]
public sealed class InfiniteStaminaMod : Mod
{
    internal static ModLogger? Logger;

    public override void OnLoad()
    {
        // Read once, here: the patches are applied or not for the whole game, so a change needs a restart.
        var enabled = Config.Bind("General", "Enabled", true,
                                  "Set to false to play with normal stamina without removing the mod.")
                            .WithDisplayName("Infinite stamina")
                            .WithRestartRequired();
        if (!enabled.Value)
        {
            Log.Info("Disabled in the config");
            return;
        }

        Logger = Log;
        Harmony.PatchAll(typeof(InfiniteStaminaMod).Assembly);
        Log.Info("Stamina is no longer spent");
    }
}

// While the player sprints or rolls, CharacterMovement.MoveCommand/TryRollCommand call both methods below every
// simulation tick. DrainStamina spends; TryDrainStamina spends nothing, it only decides whether
// the player may sprint, so letting it answer true keeps sprinting possible at zero stamina.

[HarmonyPatch(typeof(CharacterMovement), nameof(CharacterMovement.TryDrainStamina))]
internal static class TryDrainStaminaPatch
{
    private static bool s_logged;

    private static bool Prefix(ref bool __result)
    {
        if (!s_logged)
        {
            s_logged = true;
            InfiniteStaminaMod.Logger?.Info("First stamina cost skipped");
        }
        __result = true;   // "there was enough stamina"
        return false;      // skip the original: nothing is spent
    }
}

[HarmonyPatch(typeof(CharacterMovement), nameof(CharacterMovement.DrainStamina))]
internal static class DrainStaminaPatch
{
    private static bool Prefix() => false;
}
