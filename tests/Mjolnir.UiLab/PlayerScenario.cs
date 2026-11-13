using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Mjolnir.SDK.Player;
using SandSailorStudio;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.UI;
using UnityEngine;
using GamePauseMenu = SSSGame.UI.PauseMenu;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "player" scenario (autopilot.once containing "player"): Mjolnir.SDK.Player against the game itself.
/// <list type="bullet">
/// <item>LocalPlayer before, in and after the world: Spawned once, the stats live inside it, the values next to the
/// character's own attributes (read here directly, by the ids the game's HUD uses), Changed counted while standing;</item>
/// <item>Stamina.Draining through the game's own Character.DrainStamina, called here: the cost the handlers saw, a set
/// amount, a cancel, a villager's cost not reported; a stat set through the facade;</item>
/// <item>the HUD with examples/Mjolnir.PlayerExample's bars (screenshot), then the game's "quit to main menu": Despawned.</item>
/// </list>
/// <see cref="PlayerProbes"/> (installed only for this scenario) logs when PlayerManager sets and unsets players.
/// </summary>
internal static partial class Autopilot
{
    private static readonly (string Name, int Id)[] s_playerAttributes =
        [("health", 0x898), ("stamina", 0x899), ("warmth", 0x8A2), ("water", 0x8A3), ("food", 0x8A4)];

    private static readonly List<string> s_playerEvents = [];
    private static readonly List<string> s_drains = [];
    private static Func<StaminaDrainingEventArgs, bool>? s_drainAnswer;

    private static IEnumerator<float> PlayerScenario()
    {
        var stats = new (string Name, PlayerStat Stat)[]
        {
            ("health", LocalPlayer.Health), ("stamina", LocalPlayer.Stamina), ("warmth", LocalPlayer.Warmth),
            ("water", LocalPlayer.Water), ("food", LocalPlayer.Food),
        };
        var changes = new int[stats.Length];
        var reported = new bool[stats.Length];
        int spawnFrame = -1;
        for (int i = 0; i < stats.Length; i++)
        {
            int index = i;
            var (name, stat) = stats[i];
            stat.Changed += (_, e) =>
            {
                changes[index]++;
                // When the save's values reach the character: the first change of each stat after Spawned.
                if (spawnFrame < 0 || reported[index]) return;
                reported[index] = true;
                Log($"Player first Changed after Spawned: {name} {e.OldValue:0.###} → {e.NewValue:0.###} (max {stat.Maximum:0.###}), " +
                    $"{Time.frameCount - spawnFrame} frame(s) later");
            };
        }
        LocalPlayer.Spawned += (_, _) => spawnFrame = Time.frameCount;
        LocalPlayer.Spawned += (_, _) => PlayerEvent($"Spawned at frame {Time.frameCount}: {LocalPlayer.Name}, {LocalPlayer.Health}, " +
                                                     $"{LocalPlayer.Stamina}, {LocalPlayer.Food}, dead {LocalPlayer.IsDead}");
        LocalPlayer.Despawned += (_, _) => PlayerEvent($"Despawned at frame {Time.frameCount}: spawned {LocalPlayer.IsSpawned}, {LocalPlayer.Health}");
        LocalPlayer.Died += (_, _) => PlayerEvent("Died");
        LocalPlayer.Revived += (_, _) => PlayerEvent("Revived");
        // Added last, so it sees what the other mods' handlers (PlayerExample's multiplier) left.
        LocalPlayer.Stamina.Draining += (_, e) =>
        {
            s_drains.Add($"{e.Amount:0.###} ({e.Use})");
            if (s_drainAnswer != null) e.Cancel = !s_drainAnswer(e);
        };

        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 120)) yield return wait;
        yield return 2f;
        Log($"Player, main menu: spawned {LocalPlayer.IsSpawned}, name {LocalPlayer.Name ?? "null"}, {Describe(stats)}; game: {DescribeLocalPlayer()}");
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        Log($"Player, HUD ready (frame {Time.frameCount}): events [{string.Join("; ", s_playerEvents)}]");
        yield return 3f;

        var character = LocalCharacter();
        if (character == null)
        {
            Log("Player: no local character in the game");
            if (QuitWhenDone) Application.Quit();
            yield break;
        }
        Log($"Player: spawned {LocalPlayer.IsSpawned}, name '{LocalPlayer.Name}', dead {LocalPlayer.IsDead} (game {character.IsDead}), " +
            $"health {LocalPlayer.Health.Value:0.###}/{LocalPlayer.Health.Maximum:0.###} (game {character.CurrentHealth:0.###}/{character.MaxHealth:0.###})");
        foreach (var (name, stat) in stats)
        {
            var attribute = Attribute(character, name);
            Log($"Player stat {name}: facade {stat.Value:0.###} of {stat.Maximum:0.###} ({stat.Fraction:0.###}), " +
                $"game {attribute?.GetValue():0.###} of {attribute?.max:0.###}");
        }
        // PlayerStat reads the character's sheet: its pools, by their asset names, should show the same values.
        Log("Player sheet pools: " + string.Join(", ", LocalPlayer.Sheet.Stats.Where(p => p.IsPool && p.Maximum > 0)
            .Select(p => FormattableString.Invariant($"{p.AssetName} {p.Value:0.###} of {p.Maximum:0.###}"))));

        // Standing still: food and water change every frame.
        Array.Clear(changes);
        int frame = Time.frameCount;
        yield return 5f;
        Log($"Player Changed over {Time.frameCount - frame} frames: " +
            string.Join(", ", stats.Select((s, i) => $"{s.Name} {changes[i]}")));

        // The game's own stamina method, as sprinting calls it: what the handlers saw and what was spent.
        Log($"Player Draining: PlayerExample config {ConfigValues("mjolnir.playerexample")}");
        Drain(character, "no change by this handler", null);
        Drain(character, "this handler sets 7", e =>
        {
            e.Amount = 7f;
            return true;
        });
        Drain(character, "this handler cancels", _ => false);
        var villager = Object.FindObjectsByType<Villager>(FindObjectsSortMode.None).FirstOrDefault();
        if (villager == null)
        {
            Log("Player Draining, a villager: none in the world");
        }
        else
        {
            s_drains.Clear();
            villager.DrainStamina(0f, DrainStaminaUsage.Default); // 0: the villager's stamina stays as it is
            Log($"Player Draining, villager '{villager.name}': handler saw [{string.Join(", ", s_drains)}] (nothing expected)");
        }

        int staminaChanges = changes[1];
        LocalPlayer.Stamina.Value = 50f;
        Log($"Player set stamina 50: facade {LocalPlayer.Stamina.Value:0.###}, game {Attribute(character, "stamina")?.GetValue():0.###}, " +
            $"Changed {changes[1] - staminaChanges} time(s)");

        yield return 1f;
        Shot("player-hud");
        yield return AfterShot;

        // The game's "quit to main menu" (pause menu → confirm): the character leaves the world.
        MainMenuReady = false;
        Menu.Show<GamePauseMenu>(null);
        yield return 1.5f;
        Menu.GetInstance<GamePauseMenu>().BtnQuitToMainMenu();
        yield return 1.5f;
        var confirm = Menu.GetInstance<ConfirmActionMenu>();
        if (confirm == null)
        {
            Log("Player: no quit dialog");
        }
        else
        {
            Click(confirm.ConfirmButton.gameObject);
            foreach (float wait in WaitFor("main menu again", () => MainMenuReady, 120)) yield return wait;
            yield return 2f;
        }
        Log($"Player, back in the main menu: spawned {LocalPlayer.IsSpawned}, {Describe(stats)}; events [{string.Join("; ", s_playerEvents)}]");
        Log("Player scenario done");
        if (QuitWhenDone) Application.Quit();
    }

    private static void Drain(PlayerCharacter character, string what, Func<StaminaDrainingEventArgs, bool>? answer)
    {
        s_drains.Clear();
        s_drainAnswer = answer;
        float before = LocalPlayer.Stamina.Value;
        character.DrainStamina(20f, DrainStaminaUsage.Default);
        float after = LocalPlayer.Stamina.Value;
        s_drainAnswer = null;
        Log($"Player Draining 20, {what}: handler saw [{string.Join(", ", s_drains)}], spent {before - after:0.###} ({before:0.###} → {after:0.###})");
    }

    private static void PlayerEvent(string text)
    {
        s_playerEvents.Add(text);
        Log($"Player event: {text}");
    }

    private static string Describe((string Name, PlayerStat Stat)[] stats) =>
        string.Join(", ", stats.Select(s => $"{s.Name} {s.Stat.Value:0.##}/{s.Stat.Maximum:0.##}"));

    private static PlayerCharacter? LocalCharacter()
    {
        try
        {
            return GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static SandSailorStudio.Attributes.VariableAttribute? Attribute(PlayerCharacter character, string name)
    {
        int id = s_playerAttributes.First(a => a.Name == name).Id;
        return character.Attributes.GetVariableAttribute(ref id);
    }

    internal static string DescribeLocalPlayer()
    {
        try
        {
            var manager = GameManagers.Get<PlayerManager>();
            if (manager == null) return "no PlayerManager registered";
            var local = manager.LocalPlayer;
            string character = local?.playerCharacter != null ? $"'{local.playerCharacter.name}'" : "none";
            return $"PlayerManager '{manager.name}', joined {manager.JoinedPlayerCount}, local player {(local == null ? "none" : $"ref {local.reference}, character {character}")}";
        }
        catch (Exception e)
        {
            return $"{e.GetType().Name}: {e.Message}";
        }
    }
}

/// <summary>Logging-only probes for the "player" scenario: when the game sets and unsets a player's character.</summary>
// Harmony skips a class without a class-level [HarmonyPatch], even when its methods carry their own.
[HarmonyPatch]
internal static class PlayerProbes
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.SetPlayer))]
    private static void SetPlayer(object[] __args) =>
        UiLab.Instance.Log.Info($"Probe PlayerManager.SetPlayer({__args[0]}, {Name(__args[1])}) at frame {Time.frameCount}: {Autopilot.DescribeLocalPlayer()}");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.UnsetPlayer))]
    private static void UnsetPlayer(object[] __args) =>
        UiLab.Instance.Log.Info($"Probe PlayerManager.UnsetPlayer({__args[0]}) at frame {Time.frameCount}: {Autopilot.DescribeLocalPlayer()}");

    private static string Name(object? value) => value is Object o && o != null ? o.name : "null";
}
