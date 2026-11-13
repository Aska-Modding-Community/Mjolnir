using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK;
using Mjolnir.SDK.AI;
using Mjolnir.SDK.Player;
using Mjolnir.SDK.World;
using SandSailorStudio;
using SandSailorStudio.Storage;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.UI;
using UnityEngine;
using GamePauseMenu = SSSGame.UI.PauseMenu;

namespace Mjolnir.UiLab;

/// <summary>
/// The "stats" scenario (autopilot.once containing "stats"): CharacterStats against the game, for the player and a villager
/// at work. The characteristics next to the game's attributes, modifiers of each kind, a skill's level set and given back,
/// experience given and watched, status effects applied for a time and for good, their events, the console, then
/// Mjolnir.StatsExample's own effect: its texts, its changes, on the HUD and in the character's window; then what a save keeps:
/// the blessing, UiLab's lab_effect for good and the game's energized, back after the main menu and a load with the time they had
/// left. What it changes it puts back, but the scenario saves: back the save up first, put it back after.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> StatsScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        Log($"Stats: in the main menu {SDK.Effects.All.Count} effects, the player available {LocalPlayer.Sheet.IsAvailable} (false expected)");
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 6f;
        KeepDaytime(); // the settlement at work, not asleep
        yield return 4f;

        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        var stats = LocalPlayer.Sheet;
        if (character == null || !stats.IsAvailable)
        {
            Log($"Stats: no player (character {character != null}, stats {stats.IsAvailable})");
            yield break;
        }
        Log($"Stats: {SDK.Effects.All.Count} effects, {stats.Stats.Count} attributes of the player, the same object {ReferenceEquals(stats, LocalPlayer.Sheet)}");
        foreach (var stat in new[] { stats.Vitality, stats.Strength, stats.Dexterity, stats.Intelligence, stats.MaxStamina, stats.StaminaRegeneration })
            Log($"Stats: {stat.DisplayName} ({stat.AssetName}) {stat.Value:0.##}, base {stat.BaseValue:0.##}, pool {stat.IsPool}; {stat.Description}");
        var armor = stats.Find("Attribute_Armor");
        var food = stats.Find("VariableAttribute_Food");
        Log($"Stats: armor {armor?.Value}, food {food?.Value:0.#} in {food?.Minimum}..{food?.Maximum}, pool {food?.IsPool}; no such attribute {stats.Find("Attribute_Nothing") == null}");

        // Modifiers of each kind, and that they go.
        float strength = stats.Strength.Value, strengthBase = stats.Strength.BaseValue;
        var changes = new List<string>();
        EventHandler<ValueChangedEventArgs<float>> onChanged = (_, e) => changes.Add($"{e.OldValue:0.##}→{e.NewValue:0.##}");
        stats.Strength.Changed += onChanged;
        var add = stats.Strength.AddModifier(5);
        Log($"Stats: +5: {strength:0.##} → {stats.Strength.Value:0.##}, in the game {character.Attributes.GetAttribute(0xB).GetValue():0.##}");
        var multiply = stats.Strength.AddModifier(2, ModifierKind.Multiply);
        Log($"Stats: ×2: {stats.Strength.Value:0.##}");
        var percent = stats.Strength.AddModifier(0.5f, ModifierKind.Percent);
        Log($"Stats: +50 %: {stats.Strength.Value:0.##}");
        add.Amount = 1;
        Log($"Stats: the +5 made +1: {stats.Strength.Value:0.##}");
        add.Dispose();
        multiply.Dispose();
        percent.Dispose();
        stats.Strength.Changed -= onChanged;
        Log($"Stats: all taken off: {stats.Strength.Value:0.##} ({strength:0.##} expected), base {stats.Strength.BaseValue:0.##} ({strengthBase:0.##} expected); Changed [{string.Join(", ", changes)}]");
        yield return 0.5f;

        // Skills.
        var wood = stats.Skill(SkillKind.WoodHarvest);
        float level = wood.Level.BaseValue, experience = wood.Experience.Value;
        Log($"Stats: {string.Join(", ", stats.Skills.Where(s => s.Level.Value > 0).Select(s => $"{s.Kind} '{s.DisplayName}' {s.Level.Value:0.#} {s.Rank}, exp {s.Experience.Value:0.#} x{s.ExperienceMultiplier.Value:0.##}"))}");
        wood.Level.BaseValue = 60;
        Log($"Stats: wood harvest set to 60: {wood.Level.Value} rank {wood.Rank} (Adept expected)");
        wood.Level.BaseValue = level;
        var seen = new List<string>();
        EventHandler<ExperienceGainingEventArgs> onGaining = (_, e) =>
        {
            if (seen.Count < 12) seen.Add($"{(ReferenceEquals(e.Sheet, stats) ? "player" : "villager")} {e.Skill.Kind} {e.Amount:0.##}");
            if (ReferenceEquals(e.Sheet, stats) && e.Skill.Kind == SkillKind.WoodHarvest) e.Amount *= 2;
        };
        Skill.ExperienceGaining += onGaining;
        yield return 0.2f;
        Log($"Stats: wood experience {experience:0.##}, AddExperience(10) {wood.AddExperience(10)}: {wood.Experience.Value:0.##} ({experience + 20:0.##} expected: a handler doubles it)");
        float levelBefore = wood.Level.BaseValue;
        wood.AddExperience(2000);
        Log($"Stats: AddExperience(2000): level {levelBefore} → {wood.Level.BaseValue}, experience {wood.Experience.Value:0.##}");
        yield return 25f;
        Skill.ExperienceGaining -= onGaining;
        Log($"Stats: experience seen in 25 s: {string.Join("; ", seen)}");
        wood.Level.BaseValue = level;
        wood.Experience.BaseValue = experience;
        Log($"Stats: wood harvest put back: level {wood.Level.BaseValue}, experience {wood.Experience.Value:0.##}");

        // Status effects.
        var energized = SDK.Effects.Find("Effect_Energized");
        var cold = SDK.Effects.Find("Effect_Wet");
        if (energized == null || cold == null)
        {
            Log($"Stats: effects not found ({energized != null}, {cold != null})");
            yield break;
        }
        var events = new List<string>();
        EventHandler<EffectChangedEventArgs> onAdded = (_, e) => events.Add($"+{e.Effect.AssetName} {e.Stacks}");
        EventHandler<EffectChangedEventArgs> onRemoved = (_, e) => events.Add($"-{e.Effect.AssetName} {e.Stacks}");
        stats.Effects.Added += onAdded;
        stats.Effects.Removed += onRemoved;
        Log($"Stats: '{energized.DisplayName}' ({energized.AssetName}, id {energized.Id}, {energized.Kind}): {energized.Description}; on the player: {Effects(stats)}");
        float stamina = stats.MaxStamina.Value, regen = stats.StaminaRegeneration.Value;
        Log($"Stats: Apply for 20 s {stats.Effects.Apply(energized, TimeSpan.FromSeconds(20))}, has it {stats.Effects.Has(energized)}: {Effects(stats)}");
        yield return 2f;
        Log($"Stats: 2 s later: {Effects(stats)}; stamina {stamina:0.#} → {stats.MaxStamina.Value:0.#}, regen {regen:0.#} → {stats.StaminaRegeneration.Value:0.#}");
        Shot("stats-effect");
        yield return AfterShot;
        Log($"Stats: Remove {stats.Effects.Remove(energized)}, has it {stats.Effects.Has(energized)}; stamina {stats.MaxStamina.Value:0.#}; a second Remove {stats.Effects.Remove(energized)} (false expected)");
        Log($"Stats: Apply for good {stats.Effects.Apply(cold)}: {Effects(stats)}");
        yield return 1f;
        Log($"Stats: Remove {stats.Effects.Remove(cold)}, has it {stats.Effects.Has(cold)}");
        stats.Effects.Apply(energized, TimeSpan.FromSeconds(3));
        yield return 5f;
        Log($"Stats: a 3 s effect after 5 s: has it {stats.Effects.Has(energized)} (false expected)");
        stats.Effects.Added -= onAdded;
        stats.Effects.Removed -= onRemoved;
        Log($"Stats: events [{string.Join(", ", events)}]");

        // A villager at work.
        var villager = Residents.All.FirstOrDefault(r => r.Sheet.Skills.Any(s => s.Level.Value > 10));
        if (villager == null) Log("Stats: no villager with a skill");
        else
        {
            var its = villager.Sheet;
            Log($"Stats: villager {villager.Name}: strength {its.Strength.Value:0.##}, skills {string.Join(", ", its.Skills.Where(s => s.Level.Value > 0).Select(s => $"{s.Kind} {s.Level.Value:0.#} (base {s.Level.BaseValue:0.#})"))}");
            Log($"Stats: its effects: {Effects(its)}");
            Log($"Stats: Apply {its.Effects.Apply(energized, TimeSpan.FromSeconds(10))}, has it {its.Effects.Has(energized)}; Remove {its.Effects.Remove(energized)}");
            var skill = its.Skills.First(s => s.Level.Value > 10);
            float before = skill.Experience.Value;
            Log($"Stats: its {skill.Kind} AddExperience(5) {skill.AddExperience(5)}: {before:0.##} → {skill.Experience.Value:0.##}");
        }

        DevConsole.Execute("stats");
        DevConsole.Execute("skill Carpentry");
        DevConsole.Execute("effects Energized");
        DevConsole.Execute("effect Effect_Energized 30");
        yield return 1f;
        DevConsole.Execute("uneffect Effect_Energized");
        DevConsole.Execute("stats Nobody");
        DevConsole.Execute($"stats {villager?.Name}");
        yield return 0.5f;

        // A mod's own effect.
        var blessing = SDK.Effects.Find(BlessingAsset);
        if (blessing == null)
        {
            Log("Stats: the example's blessing is not among the effects");
            yield break;
        }
        Log($"Stats: '{blessing.DisplayName}' ({blessing.AssetName}, id {blessing.Id}, {blessing.Kind}): {blessing.Description}; {SDK.Effects.All.Count} effects; the ModEffect itself {blessing is Mjolnir.SDK.Content.ModEffect}");
        stats.Effects.Remove(blessing); // the example blesses the player at each level, and the levels above gave some
        yield return 1f;
        float strengthThen = stats.Strength.Value, health = LocalPlayer.Health.Value;
        Log($"Stats: Apply for 10 minutes {stats.Effects.Apply(blessing, TimeSpan.FromMinutes(10))}: {Effects(stats)}");
        yield return 3f;
        Log($"Stats: 3 s later strength {strengthThen:0.##} → {stats.Strength.Value:0.##} (3 more expected), health {health:0.#} → {LocalPlayer.Health.Value:0.#} (about 3 more expected)");
        Shot("stats-blessing");
        yield return AfterShot;

        var lab = SDK.Effects.Find(LabEffectAsset);
        var storage = GameManagers.Get<StorageManager>();
        if (storage != null && lab != null)
        {
            float dexterity = stats.Dexterity.Value;
            Log($"Stats: '{lab.DisplayName}' for good: {stats.Effects.Apply(lab)}, the energized for 5 minutes {stats.Effects.Apply(energized, TimeSpan.FromMinutes(5))}");
            yield return 1.5f;
            Log($"Stats: dexterity {dexterity:0.##} → {stats.Dexterity.Value:0.##}; {Effects(stats)}");
            var task = storage.SaveCurrentSession();
            foreach (float wait in WaitFor("the save", () => task.IsCompleted && !storage.IsSaving, 60)) yield return wait;
            Log($"Stats: saved, {storage.LastSaveOperationCode}");
            MainMenuReady = false;
            HudReady = false;
            Menu.Show<GamePauseMenu>(null);
            yield return 1.5f;
            Menu.GetInstance<GamePauseMenu>().BtnQuitToMainMenu();
            yield return 1.5f;
            if (Menu.GetInstance<ConfirmActionMenu>() is { } confirm) Click(confirm.ConfirmButton.gameObject);
            foreach (float wait in WaitFor("main menu again", () => MainMenuReady, 180)) yield return wait;
            yield return 3f;
            Log($"Stats: in the main menu the old stats are available {stats.IsAvailable} (false expected)");
            foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
            yield return 5f;
            stats = LocalPlayer.Sheet;
            Log($"Stats: loaded again, a new object, available {stats.IsAvailable}: the blessing {stats.Effects.Has(blessing)}, the lab's {stats.Effects.Has(lab)}, the game's energized {stats.Effects.Has(energized)} (all three expected, with the time they had left): {Effects(stats)}; strength {stats.Strength.Value:0.##}, dexterity {stats.Dexterity.Value:0.##}");
            stats.Effects.Remove(blessing);
            stats.Effects.Remove(lab);
            stats.Effects.Remove(energized);
            yield return 2f;
            Log($"Stats: taken off: strength {stats.Strength.Value:0.##}, dexterity {stats.Dexterity.Value:0.##}");
        }

        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private const string BlessingAsset = "Effect_mjolnir.statsexample_blessing";
    private const string LabEffectAsset = "Effect_mjolnir.test.uilab_lab_effect";

    private static string Effects(CharacterSheet stats) =>
        string.Join(", ", stats.Effects.Active.Select(a => $"{a.Effect.AssetName} x{a.Stacks} {(a.TimeLeft is { } left ? $"{left.TotalSeconds:0.#}/{a.Duration?.TotalSeconds:0.#} s" : "for good")}"));
}
