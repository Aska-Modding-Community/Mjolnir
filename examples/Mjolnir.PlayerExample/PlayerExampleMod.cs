using Mjolnir.PlayerExample.Views;
using Mjolnir.SDK;
using Mjolnir.SDK.Player;
using Mjolnir.SDK.UI;

namespace Mjolnir.PlayerExample;

/// <summary>
/// Example mod: the local player through Mjolnir.SDK.Player, without a single game type or Harmony patch of its own.
/// <list type="bullet">
/// <item><see cref="StatBars"/>: health, stamina, food, water and warmth as HUD bars bound to the player's stats;</item>
/// <item>stamina costs less (<see cref="PlayerSettings.StaminaCost"/>, a slider in the Mods window): each cost passes
/// <see cref="StaminaStat.Draining"/> before the game spends it;</item>
/// <item>the character's spawning and death in the log and on the screen.</item>
/// </list>
/// For "never spend stamina" with raw Harmony patches instead, see Mjolnir.InfiniteStamina.
/// </summary>
[ModManifest(
    "mjolnir.playerexample",
    "Player Example",
    "1.0.0",
    Description = "The local player through Mjolnir.SDK.Player: stat bars and cheaper stamina.",
    Author = "Mjolnir")]
public sealed class PlayerExampleMod : Mod
{
    public override void OnLoad()
    {
        var settings = new PlayerSettings(Config);

        Hud.Add(new StatBars(settings), HudAnchor.TopLeft); // the game's own panels take the right side
        LocalPlayer.Stamina.Draining += (s, e) => e.Amount *= settings.StaminaCost.Value;

        // The stats are still being filled in from the save at this point: the bars follow them through their bindings.
        LocalPlayer.Spawned += (s, e) => Log.Info($"{LocalPlayer.Name ?? "The player"} is in the world");
        LocalPlayer.Despawned += (s, e) => Log.Info("The player left the world");
        LocalPlayer.Died += (s, e) => Notify.Banner("Player Example", Localization.Get("banner.died"));
        LocalPlayer.Revived += (s, e) => Notify.Banner("Player Example", Localization.Get("banner.revived"));
    }
}
