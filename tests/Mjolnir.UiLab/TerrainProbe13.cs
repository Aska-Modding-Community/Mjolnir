using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SandSailorStudio;
using SandSailorStudio.UI;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain probe13" scenario: stones and deposits harvested with their loot. Round the settlement and a shore it
/// lists the stones, boulders and deposits of the world with what their harvest wants (each part's points and the tool
/// category its moveset needs), then harvests a few of different kinds through <c>NaturalObject.Harvest</c> and reads the
/// loot round them. It changes the save's world: back it up.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainProbe13()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log("Probe13: no world");
            yield break;
        }
        var home = character.transform.position;
        List<BiomeItemInstance> Biome(Vector3 p, float r) => Objects(p, r, null, InstanceActivationContext.ALL).Select(i => i.TryCast<BiomeItemInstance>()).Where(b => b != null && !b.Destroyed).Cast<BiomeItemInstance>().ToList();
        string Loot(Vector3 at) => string.Join(", ", NaturalObjects.Around(at, 16).Where(o => o.Kind == NaturalObjectKind.Item && !o.AssetName.Contains("Sapling")).GroupBy(o => o.AssetName).Select(g => $"{g.Key} ×{g.Count()}"));

        // Every stone-like kind within reach, one of each.
        var kinds = new Dictionary<string, NaturalObject>();
        foreach (float distance in new[] { 0f, 40f, 80f, 120f })
            for (int turn = 0; turn < 360; turn += distance == 0 ? 360 : 45)
            {
                var spot = home + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                foreach (var o in NaturalObjects.Around(spot, 40))
                    if (o.Kind is NaturalObjectKind.Object or NaturalObjectKind.LargeObject && !kinds.ContainsKey(o.AssetName)) kinds[o.AssetName] = o;
            }
        Log($"Probe13: {kinds.Count} kinds of things and large things: {string.Join("; ", kinds.Values.Select(o => $"{o.AssetName} ({o.Kind}, harvestable {o.IsHarvestable}, health {o.Health:0}, resource {o.ResourceName})"))}");

        int tried = 0;
        foreach (var found in kinds.Values.Where(o => o.IsHarvestable).OrderByDescending(o => o.Kind).ThenBy(o => (o.Position - home).magnitude))
        {
            if (tried >= 6) break;
            var at = found.Position;
            var to = at - (at - home).normalized * 3f;
            var walk = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(to.x, to.z);
            foreach (float wait in WaitFor("the teleport", () => walk.IsCompleted, 30)) yield return wait;
            yield return 3f;
            var instance = Biome(at, 0.6f).FirstOrDefault();
            var harvest = instance?.gameObject != null ? instance.gameObject.GetComponentInChildren<HarvestInteraction>(true) : null;
            var gather = instance?.gameObject != null ? instance.gameObject.GetComponentInChildren<GatherInteraction>(true) : null;
            string pieces = harvest == null ? "none" : string.Join("; ", harvest.harvestPieces.ToArray().Select(p => $"{p.pieceId} {p.pieceHitPoints} needs {p.attackMoveset?.requiredEqippmentCategory?.name ?? "nothing"}"));
            var again = NaturalObjects.Nearest(at, 0.6f);
            string before = Loot(at);
            bool done = again?.Harvest() ?? false;
            yield return 4f;
            Log($"Probe13: '{found.AssetName}' ({found.Kind}) at {P(at)}: object '{(instance?.gameObject != null ? instance.gameObject.name : "none")}', harvest [{pieces}], gather {gather != null}; Harvest {done}, gone {again?.IsGone}; loot before [{before}], after [{Loot(at)}]");
            tried++;
        }

        // The shoreline boulder beside the willow (the probe10 spot).
        var shore = NaturalObjects.Around(new Vector3(-203.8f, 34.5f, 392.3f), 14).FirstOrDefault(o => o.AssetName == "rock_shoreline_big");
        if (shore != null)
        {
            var at = shore.Position;
            var walk = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(at.x - 4, at.z - 4);
            foreach (float wait in WaitFor("the teleport", () => walk.IsCompleted, 30)) yield return wait;
            yield return 3f;
            var instance = Biome(at, 0.6f).FirstOrDefault();
            string parts = instance?.gameObject != null ? string.Join(", ", instance.gameObject.GetComponentsInChildren<Component>(true).Select(c => c != null ? c.GetIl2CppType().Name : "null").Distinct().Take(16)) : "no object";
            bool done = NaturalObjects.Nearest(at, 0.6f)?.Harvest() ?? false;
            yield return 3f;
            Log($"Probe13: the shoreline boulder: harvestable {shore.IsHarvestable}, parts [{parts}]; Harvest {done}; loot [{Loot(at)}]");
        }

        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
