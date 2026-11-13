using Mjolnir.SDK.Config;

namespace Mjolnir.PlayerExample;

/// <summary>
/// The example's settings, Mjolnir/Config/mjolnir.playerexample.toml; the Mods window shows them too (main or pause menu
/// → Mjolnir → Player Example → Settings).
/// </summary>
internal sealed class PlayerSettings(ModConfig config)
{
    public ConfigEntry<float> StaminaCost { get; } =
        config.Bind("Stamina", "CostMultiplier", 0.5f, "What sprinting, harvesting and fighting cost in stamina, as a part of the game's cost (1 = the game's own, 0 = free).")
              .WithRange(0f, 2f).WithDisplayName("Stamina cost");

    public ConfigEntry<bool> ShowBars { get; } =
        config.Bind("HUD", "ShowBars", true, "Show the example's stat bars in the HUD.").WithDisplayName("Show the stat bars");
}
