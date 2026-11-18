using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SSSGame.UI;
using SSSGame.Weather;
using SandSailorStudio;
using SandSailorStudio.Inventory;
using SandSailorStudio.Storage;
using SandSailorStudio.UI;
using UnityEngine;
using GamePauseMenu = SSSGame.UI.PauseMenu;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain regrowth" scenario: placed objects over the world's time and a road plan's width over a save. On a free
/// spot it places a berry bush and gathers it to the end, a young willow (scale 0.3) and a road plan 8 m wide (not paved),
/// runs the world's clock fast and reads the bush's count, the willow's scale and the plan's width; gathers the bush again,
/// saves, goes back to the main menu and loads, and reads them again; then gathers the bush once more, saves and quits.
/// "terrain regrowth check", run right after it without putting the save back, reads them after a fresh start of the game
/// (the spot is kept in a file beside UiLab), runs the clock fast once more and paves the plan, reading how wide it paved.
/// Both change the save's world and run its clock days ahead: back the save up and put it back after.
/// </summary>
internal static partial class Autopilot
{
    private const string BushKind = "bush_sloe_berries";
    private const float FastTime = 1000f;

    private static string RegrowthSpotFile => Path.Combine(Path.GetDirectoryName(typeof(Autopilot).Assembly.Location)!, "regrowth-spot.txt");

    private static IEnumerator<float> TerrainRegrowthScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log("Regrowth: no world");
            yield break;
        }
        var spot = PlaceSpot(character.transform.position);
        if (float.IsNaN(spot.x))
        {
            Log("Regrowth: no free spot");
            yield break;
        }
        File.WriteAllText(RegrowthSpotFile, Inv($"{spot.x} {spot.z}"));
        var weatherNow = WeatherSystem.Instance;
        var berries = Try(() => GameManagers.Get<ItemDatabaseManager>()?.Database?.GetItemInfo("Item_Food_BiomeBerries")?.TryCast<BiomeResourceInfo>()?.GetReplenishData() is { } data && weatherNow != null
            ? Inv($"{data.replenishFrequencyDays} days, {data.replenishSeasonStart?.Count ?? 0} season(s), next {data.GetNextReplenishTime(weatherNow):0.00}, now {weatherNow.NetworkedCurrentGameTime:0.00}, {weatherNow.DaysInOneYear} days a year")
            : "none");
        Log(Inv($"Regrowth: the spot {P(spot)}, growth days {NaturalObjects.TreeGrowthDays}; the world's berry bushes give again: {berries}"));
        foreach (float wait in RegrowthWalk(spot)) yield return wait;

        var bushAt = spot;
        var willowAt = spot + new Vector3(6, 0, 0);
        var bush = NaturalObjects.Place(BushKind, bushAt);
        var willow = NaturalObjects.Place("tree_willow", willowAt, 0f, 0.3f);
        var road = RoadPlans.Place([spot + new Vector3(-12, 0, 8), spot + new Vector3(0, 0, 8), spot + new Vector3(12, 0, 8)], GroundKind.Road, 8f);
        yield return 3f;
        Log(Inv($"Regrowth: placed the bush {bush != null}, the willow {willow != null} at {RegrowthScale(willowAt)}, the road {road != null} {road?.Width:0.##} m wide"));
        foreach (float wait in RegrowthGather(bushAt, "the first time")) yield return wait;
        foreach (float wait in RegrowthFast(bushAt, willowAt, 14)) yield return wait;
        foreach (float wait in RegrowthGather(bushAt, "again before the save")) yield return wait;

        foreach (float wait in RegrowthSaveAndLoad()) yield return wait;
        foreach (float wait in RegrowthWalk(spot)) yield return wait;
        Log(Inv($"Regrowth: after loading again the bush {RegrowthBush(bushAt)}, the willow {RegrowthScale(willowAt)}, the road {RoadPlans.Nearest(spot + new Vector3(0, 0, 8), 6)?.Width:0.##} m wide"));
        foreach (float wait in RegrowthFast(bushAt, willowAt, 14)) yield return wait;
        foreach (float wait in RegrowthGather(bushAt, "once more before quitting")) yield return wait;
        var storage = GameManagers.Get<StorageManager>();
        if (storage != null)
        {
            var task = storage.SaveCurrentSession();
            foreach (float wait in WaitFor("the save", () => task.IsCompleted && !storage.IsSaving, 60)) yield return wait;
            Log($"Regrowth: saved to quit, {storage.LastSaveOperationCode}");
        }
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static IEnumerator<float> TerrainRegrowthCheckScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var parts = File.Exists(RegrowthSpotFile) ? File.ReadAllText(RegrowthSpotFile).Split(' ') : [];
        if (parts.Length != 2 || !Landscape.IsAvailable)
        {
            Log("Regrowth check: no spot or no world");
            yield break;
        }
        var spot = new Vector3(float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), 0, float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
        foreach (float wait in RegrowthWalk(spot)) yield return wait;
        Landscape.TryGetHeight(spot.x, spot.z, out spot.y);
        var bushAt = spot;
        var willowAt = spot + new Vector3(6, 0, 0);
        var road = RoadPlans.Nearest(spot + new Vector3(0, 0, 8), 6);
        Log(Inv($"Regrowth check: after a fresh start the bush {RegrowthBush(bushAt)}, the willow {RegrowthScale(willowAt)}, the road {road?.Width:0.##} m wide"));
        foreach (float wait in RegrowthFast(bushAt, willowAt, 3)) yield return wait;
        int paved = road?.LevelNow() ?? -1;
        yield return 3f;
        var kinds = Enumerable.Range(0, 37).Select(k => Landscape.KindAt(spot.x, spot.z + 8f - 9f + k * 0.5f)).ToList();
        Log(Inv($"Regrowth check: the road paved {paved}: {string.Concat(kinds.Select(k => k.ToString()[0]))} (road {kinds.Count(k => k == GroundKind.Road) * 0.5f:0.#} m, dirt {kinds.Count(k => k == GroundKind.Dirt) * 0.5f:0.#} m)"));
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static IEnumerable<float> RegrowthWalk(Vector3 spot)
    {
        var walk = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(spot.x, spot.z - 6);
        foreach (float wait in WaitFor("the teleport", () => walk.IsCompleted, 30)) yield return wait;
        yield return 4f;
    }

    private static string RegrowthBush(Vector3 at)
    {
        var item = PlacedItem(at, "Mjolnir_World_" + BushKind);
        if (item == null) return "gone";
        var gather = item.gameObject != null ? item.gameObject.GetComponentInChildren<GatherInteraction>(true) : null;
        return Inv($"there, count {Try(() => gather?.CheckAvailableItemCount())} (stack {item.GetItem()?.count})");
    }

    private static string RegrowthScale(Vector3 at) => PlacedItem(at, "Mjolnir_World_tree_willow") is { } item ? Inv($"{item.GetScale().x:0.###}") : "gone";

    private static IEnumerable<float> RegrowthGather(Vector3 at, string when)
    {
        var bush = NaturalObjects.Nearest(at, 1.5f);
        int got = 0;
        for (int charge = 0; charge < 8 && bush != null && bush.GatherCount > 0; charge++)
        {
            got += bush.Gather(1);
            yield return 1f;
        }
        Log($"Regrowth: the bush gathered {when}: {got} item(s), now {RegrowthBush(at)}");
    }

    private static IEnumerable<float> RegrowthFast(Vector3 bushAt, Vector3 willowAt, int steps)
    {
        var weather = WeatherSystem.Instance;
        if (weather == null) yield break;
        float speed = weather.TimeSpeedMultiplier;
        weather.TimeSpeedMultiplier = FastTime;
        for (int i = 0; i < steps; i++)
        {
            yield return 10f;
            Log(Inv($"Regrowth: fast, day {weather.NetworkedCurrentGameTime:0.00}: the bush {RegrowthBush(bushAt)}, the willow {RegrowthScale(willowAt)}"));
            if (PlacedItem(bushAt, "Mjolnir_World_" + BushKind)?.GetItem()?.count > 0) break;
        }
        weather.TimeSpeedMultiplier = speed;
    }

    private static IEnumerable<float> RegrowthSaveAndLoad()
    {
        var storage = GameManagers.Get<StorageManager>();
        if (storage == null) yield break;
        var task = storage.SaveCurrentSession();
        foreach (float wait in WaitFor("the save", () => task.IsCompleted && !storage.IsSaving, 60)) yield return wait;
        Log($"Regrowth: saved, {storage.LastSaveOperationCode}");
        MainMenuReady = false;
        HudReady = false;
        Menu.Show<GamePauseMenu>(null);
        yield return 1.5f;
        Menu.GetInstance<GamePauseMenu>().BtnQuitToMainMenu();
        yield return 1.5f;
        if (Menu.GetInstance<ConfirmActionMenu>() is { } confirm) Click(confirm.ConfirmButton.gameObject);
        foreach (float wait in WaitFor("main menu again", () => MainMenuReady, 180)) yield return wait;
        yield return 3f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
    }
}
