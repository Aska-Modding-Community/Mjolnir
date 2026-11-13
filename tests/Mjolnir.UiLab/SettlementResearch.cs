using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using Mjolnir.SDK;
using Mjolnir.SDK.Player;
using Mjolnir.SDK.World;
using SandSailorStudio;
using SandSailorStudio.Attributes;
using SandSailorStudio.Inventory;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.AI;
using SSSGame.UI;
using SSSGame.Weather;
using UnityEngine;
using GameAttribute = SandSailorStudio.Attributes.Attribute;
using Object = UnityEngine.Object;

namespace Mjolnir.UiLab;

/// <summary>
/// The "settlement" research (autopilot.once containing "settlement"): a real settlement, hosted. It loads the save,
/// dumps every attribute and status effect the game has (shots/attributes.txt, effects.txt), the player (player.txt), the
/// villagers (villagers-*.txt: attributes, effects, schedule, quests) and the structures with their workers and tasks
/// (structures.txt) and the movesets that award experience (movesets.txt), checks what the settlement's crafting tables know
/// of ItemsExample's recipes (it was built before the mods), walks the clock through every hour with Mjolnir.SDK.World's
/// WorldTime logging night and what the villagers are told to do, stops the clock and runs it again, and follows the
/// villagers through a morning and a night, logging which attributes grew, who earned experience and whose stamina cost
/// the SDK reported. It writes nothing on purpose, but the game saves by itself: back the save up first, put it back after.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> SettlementResearch()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        Log($"Settlement: in the main menu the clock is available {WorldTime.IsAvailable} (false expected), hour {WorldTime.Hour}");
        DevConsole.Execute("time");
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 8f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        var weather = WeatherSystem.Instance;
        if (character == null || weather == null)
        {
            Log($"Settlement: character {character != null}, weather {weather != null}");
            yield break;
        }
        Try("session", () => LogSession(weather));
        Try("time", () => LogTime(weather, "loaded"));
        var villagers = Object.FindObjectsOfType<Villager>().ToList();
        Log($"Settlement: {villagers.Count} villager(s)");
        Try("attributes", DumpAttributes);
        Try("effects", DumpEffects);
        Try("widgets", DumpWidgets);
        Try("player", () => File.WriteAllText(Path.Combine(ShotsDir, "player.txt"), Describe(character.Attributes, character.GetComponent<StatusEffectManager>())));
        Try("villagers", () => DumpVillagers(villagers, "loaded"));
        Try("structures", () => DumpStructures(villagers));
        Try("movesets", DumpMovesets);
        Try("tables", LogTables);
        Shot("settlement-loaded");
        yield return AfterShot;

        for (int hour = 0; hour < 24; hour++)
        {
            WorldTime.Hour = hour;
            yield return 1.5f;
            Log($"Settlement: hour {hour}: {Clock(weather)}; {Behaviours(villagers)}");
        }
        Log($"Settlement: the SDK's clock: available {WorldTime.IsAvailable}, hour {WorldTime.Hour:0.00}, night {WorldTime.IsNight}, days {WorldTime.DaysPassed}, running {WorldTime.IsRunning}");
        WorldTime.IsRunning = false;
        float stoppedAt = weather.TimeOfDay;
        yield return 5f;
        Log($"Settlement: stopped at {stoppedAt:0.000}, 5 s later {weather.TimeOfDay:0.000}, running {WorldTime.IsRunning} (the same, false expected)");
        DevConsole.Execute("time");
        DevConsole.Execute("time run");
        yield return 5f;
        Log($"Settlement: 5 s after it ran again {weather.TimeOfDay:0.000}, running {WorldTime.IsRunning} (0.08 later, true expected)");
        DevConsole.Execute("time night");
        yield return 1f;
        Log($"Settlement: time night: {Clock(weather)}");
        DevConsole.Execute("time 14:30");
        yield return 1f;
        Log($"Settlement: time 14:30: {Clock(weather)}");

        int costs = 0;
        void OnDraining(object? sender, StaminaDrainingEventArgs e) => costs++;
        LocalPlayer.Stamina.Draining += OnDraining;
        var before = Snapshot(villagers, character);
        DevConsole.Execute("time day");
        foreach (float wait in Watch(weather, villagers, "morning", 150, 15)) yield return wait;
        LocalPlayer.Stamina.Draining -= OnDraining;
        Log($"Settlement: in the morning the game drained stamina {SettlementProbes.Drains} times, the SDK reported {costs} (the idle player's only: 0 expected)");
        Shot("settlement-morning");
        yield return AfterShot;
        Try("villagers", () => DumpVillagers(villagers, "morning"));
        Try("growth", () => LogGrowth(before, Snapshot(villagers, character), villagers, character));
        Log($"Settlement: experience awarded: {string.Join("; ", SettlementProbes.Awards.OrderByDescending(a => a.Value).Select(a => $"{a.Key} x{a.Value}"))}");
        foreach (string award in SettlementProbes.Samples) Log($"Settlement: award {award}");
        DevConsole.Execute("time night");
        foreach (float wait in Watch(weather, villagers, "night", 60, 15)) yield return wait;
        Shot("settlement-night");
        yield return AfterShot;
        Try("villagers", () => DumpVillagers(villagers, "night"));

        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static void Try(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Log($"Settlement: {what} failed: {e}");
        }
    }

    private static void LogSession(WeatherSystem weather)
    {
        var runner = weather.Runner;
        Log($"Settlement: mode {runner.GameMode}, server {runner.IsServer}, single player {runner.IsSinglePlayer}");
    }

    private static string Clock(WeatherSystem weather) =>
        $"time {weather.TimeOfDay:0.00}, day {weather.DayOfYear} of {weather.DaysInOneYear} ({weather.GetDaysPassed()} passed), night {weather.IsNight}, " +
        $"dayNight {weather.DayNightValue:0.00}, normalized {weather.narmalizedDayTime:0.000}, dilated {weather.dilatedDayTime:0.000}, season '{weather.SeasonName}', weather '{weather.WeatherConditionName}'";

    private static void LogTime(WeatherSystem weather, string when) =>
        Log($"Settlement: time {when}: {Clock(weather)}; running {weather.TimeRunningEnabled}, speed {weather.TimeSpeedMultiplier}, day length {weather.dayLength} x {weather.GetCustomDayLengthMultiplier()}, schedule hours {Villager.scheduleMaxHourCount}");

    private static string Behaviours(List<Villager> villagers) =>
        string.Join(", ", villagers.Where(v => v != null).GroupBy(v => v.CurrentBehaviorType).Select(g => $"{g.Key} {g.Count()}"));

    // Logs what everyone does every few seconds.
    private static IEnumerable<float> Watch(WeatherSystem weather, List<Villager> villagers, string name, int seconds, int every)
    {
        for (int passed = 0; passed < seconds; passed += every)
        {
            yield return every;
            Log($"Settlement: {name} +{passed + every} s: {Clock(weather)}; {Behaviours(villagers)}");
            foreach (var villager in villagers.Where(v => v != null)) Log($"  {Line(villager)}");
        }
    }

    private static string Line(Villager villager)
    {
        var runner = villager._questRunner;
        var workstation = villager.GetWorkstation();
        return $"{villager.GetName()}: {villager.CurrentBehaviorType}, quest {runner?.ActiveQuest?.GetIl2CppType().Name ?? "none"}, state '{runner?.fsmController?._currentState?.name}', " +
               $"work '{workstation?.GetName()}', at {P(villager.transform.position)}";
    }

    private static void DumpAttributes()
    {
        var text = new StringBuilder();
        foreach (var config in GameManagers.Get<AttributesDatabase>().allAttributes)
            text.Append($"0x{config.attributeId:X} {config.name} '{config.GetName()}' type {config.attributeType?.name}, visible {config.visible}, format {config.displayFormat}: {config.GetDescription()}\n");
        File.WriteAllText(Path.Combine(ShotsDir, "attributes.txt"), text.ToString());
        Log("Settlement: attributes dumped");
    }

    private static void DumpEffects()
    {
        var text = new StringBuilder();
        var list = GameManagers.Get<StatusEffectDatabase>().CompleteStatusEffectList.StatusEffectList;
        foreach (var table in list)
        {
            text.Append($"{table.id} {table.name} [{table.GetIl2CppType().Name}] '{table.Name}' type {table.effectType?.name}, stacks {table.maxStacks} {table.stackingBehavior}: {table.Description}\n");
            foreach (var element in table.attrElements) text.Append($"    {element.targetAttribute?.name}: {element.modifier?.Operation} {element.modifier?.Value}\n");
            foreach (var element in table.vattrElements) text.Append($"    {element.targetAttribute?.name}: per tick {element.modifier?.mode} {element.modifier?.value}\n");
        }
        File.WriteAllText(Path.Combine(ShotsDir, "effects.txt"), text.ToString());
        Log($"Settlement: {list.Count} effects dumped");
    }

    private static void DumpWidgets()
    {
        foreach (var widget in Resources.FindObjectsOfTypeAll<AttributesWidget>())
        {
            var shown = new List<string>();
            foreach (var config in widget.attributesToShow) shown.Add($"{config.attributeType?.name} → {config.panelPrefab?.GetIl2CppType().Name}");
            Log($"Settlement: widget {widget.GetIl2CppType().Name} '{widget.name}': sort {widget.sortMode}, minimum {widget.useMinimumThreshold} {widget.minimumValueThreshold}, at most {widget.maxAttributesCount}, shows [{string.Join("; ", shown)}]");
        }
        var ranks = new List<string>();
        foreach (int value in Enumerable.Range(0, 21).Select(i => i * 5))
            ranks.Add($"{value}: '{ProficiencyPanel.rankNames.GetValue(value)}' tier {ProficiencyPanel.proficiencyTiers.GetValue(value)}");
        Log($"Settlement: proficiency ranks {string.Join(", ", ranks)}; villager max tier {Villager.c_MaxProficiencyTier}");
    }

    private static void DumpVillagers(List<Villager> villagers, string when)
    {
        var text = new StringBuilder();
        foreach (var villager in villagers.Where(v => v != null))
        {
            var schedule = Enumerable.Range(0, Villager.scheduleMaxHourCount).Select(h => ((ScheduleType)villager.GetScheduleModified(h)).ToString()[0]);
            text.Append($"=== {villager.GetName()} [{villager.GetIl2CppType().Name}] viking {villager.IsViking}, golem {villager.IsGolem}, tier {villager.GetProficiencyTier()}, happiness {villager.NormalizedHappiness:0.00}\n");
            text.Append($"  {Line(villager)}\n  home '{villager.GetHomestead()?.GetName()}', schedule {string.Concat(schedule)}\n");
            text.Append($"  quests: {Quests(villager)}\n");
            text.Append(Describe(villager.GetAttributes(), villager.GetStatusEffects()));
        }
        File.WriteAllText(Path.Combine(ShotsDir, $"villagers-{when}.txt"), text.ToString());
        Log($"Settlement: villagers dumped ({when})");
    }

    // Every quest the villager has with what it asks now, the highest first.
    private static string Quests(Villager villager)
    {
        var runner = villager._questRunner;
        if (runner == null) return "-";
        var asking = new List<(float Priority, string Name)>();
        var each = runner._questDataTable.GetEnumerator();
        while (each.MoveNext())
        {
            var pair = each._current; // Current reads as ambiguous in an IDE: the interop has two of that name
            float priority = float.NaN;
            try { priority = pair.Key.GetPriority(pair.Value); } catch (Exception) { }
            asking.Add((priority, pair.Key.GetIl2CppType().Name));
        }
        return string.Join(", ", asking.OrderByDescending(a => a.Priority).Select(a => $"{a.Name} {a.Priority:0.##}"));
    }

    private static string Describe(AttributeManager attributes, StatusEffectManager? effects)
    {
        var text = new StringBuilder();
        var database = GameManagers.Get<AttributesDatabase>();
        var properties = new Il2CppSystem.Collections.Generic.List<Property>();
        attributes.GetProperties(properties);
        foreach (var property in properties.ToArray().OrderBy(p => p.id))
        {
            database.TryGetAttributeConfig(property.id, out var config);
            string value = $"{property.GetValue():0.###}";
            if (property.TryCast<VariableAttribute>() is { } variable) value += $" of {variable.min:0.###}..{variable.max:0.###}";
            if (property.TryCast<GameAttribute>() is { } attribute)
            {
                value += $" base {attribute.baseValue:0.###}";
                if (attribute._modifiers != null) // made with the first modifier
                    foreach (var modifier in attribute._modifiers) value += $" {modifier.Operation} {modifier.Value:0.###}";
            }
            text.Append($"  0x{property.id:X} {config?.name} '{config?.GetName()}' [{property.GetIl2CppType().Name}] {config?.attributeType?.name}: {value}\n");
        }
        if (effects == null) return text.ToString();
        foreach (var effect in effects.statusEffects)
            text.Append($"  effect {effect.table?.name} '{effect.table?.Name}' type {effect.table?.effectType?.name}, {effect.stacks.Count} stack(s), left {effect.GetRemainingTime():0.#} of {effect.GetInitialDuration():0.#}\n");
        return text.ToString();
    }

    private static void DumpStructures(List<Villager> villagers)
    {
        var text = new StringBuilder();
        var settlement = villagers.FirstOrDefault(v => v != null)?.Settlement;
        if (settlement == null)
        {
            Log("Settlement: no settlement");
            return;
        }
        var structures = settlement.GetStructures();
        foreach (var structure in structures)
        {
            text.Append($"{structure.name} '{structure.Template?.name}'\n");
            foreach (var workstation in structure.GetComponentsInChildren<Workstation>(true))
            {
                text.Append($"    [{workstation.GetIl2CppType().Name}] '{workstation.GetName()}' workers {workstation.GetAgentsCount()} of {workstation.GetAgentsCapacity()}\n");
                foreach (var task in workstation.GetWorkstationTaskDatas())
                    text.Append($"        task [{task.GetIl2CppType().Name}] {task.itemInfoQuantity?.itemInfo?.name} x{task.itemInfoQuantity?.quantity}, priority {task.priority}, villagers {task.VillagersInCharge?.Count}\n");
            }
        }
        File.WriteAllText(Path.Combine(ShotsDir, "structures.txt"), text.ToString());
        Log($"Settlement: {structures.Count} structures dumped");
    }

    // Everything that awards experience: the attribute it fills, its reward and how far.
    private static void DumpMovesets()
    {
        var text = new StringBuilder();
        var movesets = Resources.FindObjectsOfTypeAll<InteractionMoveset>().OrderBy(m => m.name).ToList();
        foreach (var moveset in movesets)
        {
            var path = moveset.levelingPath;
            var curve = moveset.expertiseCapMultiplierCurve;
            string keys = curve == null ? "none" : string.Join(" ", curve.keys.Select(k => $"{k.time:0.##}:{k.value:0.##}"));
            text.Append($"{moveset.name} [{moveset.GetIl2CppType().Name}]: expertise {moveset.expertise?.name}, gain x{moveset.expertiseGainMultiplier}, cap {path?.expertiseCap}, reward {path?.reward?.name} up to {path?.rewardCap}, cap curve {keys}, stamina discount {moveset.staminaUsageDiscount}\n");
        }
        File.WriteAllText(Path.Combine(ShotsDir, "movesets.txt"), text.ToString());
        Log($"Settlement: {movesets.Count} movesets dumped");
    }

    // The settlement's crafting tables, built before the mods: what they know of ItemsExample's recipes.
    private static void LogTables()
    {
        var db = GameManagers.Get<ItemDatabaseManager>().Database;
        var spool = db.GetItemInfo(SpoolAsset);
        var twine = db.GetItemInfo(TwineAsset);
        foreach (var table in Object.FindObjectsOfType<CraftInteraction>().Where(t => t.localBlueprintsList != null))
            Log($"Settlement: table '{table.name}' of '{table.craftStationHost?.name}' list '{table.localBlueprintsList.name}' ({table.localBlueprintsList.itemInfoList.Count}): knows the spool {table.KnowsBlueprintForItem(spool, out CraftBlueprint _)}, the twine {table.KnowsBlueprintForItem(twine, out CraftBlueprint _)}");
    }

    private static Dictionary<(string Who, int Id), float> Snapshot(List<Villager> villagers, PlayerCharacter character)
    {
        var values = new Dictionary<(string, int), float>();
        void Take(string who, AttributeManager attributes)
        {
            var properties = new Il2CppSystem.Collections.Generic.List<Property>();
            attributes.GetProperties(properties);
            foreach (var property in properties.ToArray()) values[(who, property.id)] = property.GetValue();
        }
        Take("player", character.Attributes);
        foreach (var villager in villagers.Where(v => v != null)) Take(villager.GetName(), villager.GetAttributes());
        return values;
    }

    // What changed between two snapshots, leaving out what changes all the time.
    private static void LogGrowth(Dictionary<(string Who, int Id), float> before, Dictionary<(string Who, int Id), float> after, List<Villager> villagers, PlayerCharacter character)
    {
        var database = GameManagers.Get<AttributesDatabase>();
        var changed = after.Where(p => before.TryGetValue(p.Key, out float old) && Math.Abs(old - p.Value) > 0.0001f)
                           .GroupBy(p => p.Key.Id)
                           .OrderBy(g => g.Key);
        foreach (var attribute in changed)
        {
            database.TryGetAttributeConfig(attribute.Key, out var config);
            string changes = string.Join(", ", attribute.Take(6).Select(p => $"{p.Key.Who} {before[p.Key]:0.###} → {p.Value:0.###}"));
            Log($"Settlement: grew 0x{attribute.Key:X} {config?.name}: {attribute.Count()} changed: {changes}");
        }
    }
}

/// <summary>Probes for the "settlement" research: who is awarded experience, and who spends stamina.</summary>
[HarmonyPatch]
internal static class SettlementProbes
{
    public static readonly Dictionary<string, int> Awards = [];
    public static readonly List<string> Samples = [];
    public static int Drains;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(InteractionMoveset), nameof(InteractionMoveset.AwardProfficiencyPoints))]
    private static void Award(InteractionMoveset __instance, float __1)
    {
        Awards[__instance.name] = Awards.GetValueOrDefault(__instance.name) + 1;
        if (Samples.Count < 20 && Awards[__instance.name] == 1) Samples.Add($"{__instance.name}: base {__1:0.###}");
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Character), nameof(Character.DrainStamina))]
    private static void Drain() => Drains++;
}
