using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SandSailorStudio;
using SandSailorStudio.UI;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain harvest" scenario: felling with the game's loot through Mjolnir.SDK.Terrain. In the main menu nothing is
/// found. On the test save's wooded spot it harvests a tree as nobody (the player's wood experience stays), one as the
/// player without injuries (it grows), one without chopping its trunk and then the trunk on its own, runs the console's
/// <c>objects harvest</c>, and tries a tree far from the player. It changes the save's world: back the save up and put it
/// back after.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainHarvestScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        Log($"Harvest: in the main menu {NaturalObjects.Around(Vector3.zero, 16).Count} objects");
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log("Harvest: no world");
            yield break;
        }
        var home = character.transform.position;
        float Wood() => Mjolnir.SDK.Player.LocalPlayer.Sheet?.Skills.FirstOrDefault(s => s.Kind == SkillKind.WoodHarvest)?.Experience.Value ?? float.NaN;
        string Loot(Vector3 at) => string.Join(", ", NaturalObjects.Around(at, 24).Where(o => o.Kind == NaturalObjectKind.Item && !o.AssetName.Contains("Sapling")).GroupBy(o => o.AssetName).Select(g => $"{g.Key} ×{g.Count()}"));

        // The test save's wooded spot: young firs standing close together.
        var wood = new Vector3(-16.1f, 41.5f, 249.5f);
        var trees = NaturalObjects.Around(wood, 12).Where(o => o.Kind == NaturalObjectKind.Tree).ToList();
        Log($"Harvest: {trees.Count} trees within 12 m of the wooded spot");
        if (trees.Count < 4)
        {
            Log("Harvest: too few trees on the wooded spot");
            yield break;
        }
        var near = wood - (wood - home).normalized * 6f;
        var walk = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(near.x, near.z);
        foreach (float wait in WaitFor("the teleport", () => walk.IsCompleted, 30)) yield return wait;
        yield return 4f;
        var camera = Overhead(wood);
        camera.Position = wood + new Vector3(0, 14f, -14f);
        camera.Rotation = Quaternion.LookRotation(wood - camera.Position, Vector3.up);
        Shot("harvest-before");
        yield return AfterShot;

        // A. Nobody strikes: the loot falls, the player's experience stays.
        var a = NaturalObjects.Around(wood, 12).First(o => o.Kind == NaturalObjectKind.Tree);
        var aAt = a.Position;
        string lootBefore = Loot(aAt);
        float xp = Wood();
        bool aDone = a.Harvest();
        string felled = Loot(aAt);
        yield return 6f;
        Log($"Harvest: A '{a.AssetName}' at {P(aAt)} as nobody: {aDone}, gone {a.IsGone}; loot round it before [{lootBefore}], at once [{felled}], 6 s later [{Loot(aAt)}]; the place now {NaturalObjects.Nearest(aAt, 0.6f)?.AssetName ?? "empty"}; wood experience {xp:0.##} → {Wood():0.##}");

        // B. The player strikes, without injuries: the experience grows.
        var b = NaturalObjects.Around(wood, 12).First(o => o.Kind == NaturalObjectKind.Tree);
        var bAt = b.Position;
        xp = Wood();
        bool bDone = b.Harvest(new HarvestOptions { AsPlayer = true, AllowInjuries = false });
        yield return 6f;
        Log($"Harvest: B '{b.AssetName}' at {P(bAt)} as the player without injuries: {bDone}; wood experience {xp:0.##} → {Wood():0.##}; loot round it [{Loot(bAt)}]");

        // C. The trunk left lying, then chopped on its own.
        var c = NaturalObjects.Around(wood, 12).First(o => o.Kind == NaturalObjectKind.Tree);
        var cAt = c.Position;
        string resource = c.ResourceName;
        bool cDone = c.Harvest(new HarvestOptions { ChopTrunk = false });
        yield return 6f;
        var trunk = NaturalObjects.Around(cAt, 24).FirstOrDefault(o => o.AssetName == resource);
        Log($"Harvest: C '{c.AssetName}' at {P(cAt)} without its trunk: {cDone}; the trunk {trunk?.ToString() ?? "none"}; loot round it [{Loot(cAt)}]");
        if (trunk != null)
        {
            bool chopped = trunk.Harvest();
            Log($"Harvest: C's trunk chopped: {chopped}, gone {trunk.IsGone}; loot round it [{Loot(cAt)}]");
        }
        Shot("harvest-felled");
        yield return AfterShot;
        camera.Dispose();

        // D. The console round the player, next to a tree.
        var d = NaturalObjects.Around(wood, 12).FirstOrDefault(o => o.Kind == NaturalObjectKind.Tree);
        if (d != null)
        {
            var by = d.Position - (d.Position - wood).normalized * 2f;
            var go = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(by.x, by.z);
            foreach (float wait in WaitFor("the teleport", () => go.IsCompleted, 30)) yield return wait;
            yield return 3f;
            var player = character.transform.position;
            string round = string.Join(", ", NaturalObjects.Around(player, 4).Where(o => o.IsHarvestable && o.Kind != NaturalObjectKind.Item).Select(o => o.AssetName));
            DevConsole.Execute("objects harvest 4");
            yield return 6f;
            Log($"Harvest: the console's objects harvest at {P(player)}: harvestable within 4 m before [{round}], after [{string.Join(", ", NaturalObjects.Around(player, 4).Where(o => o.IsHarvestable && o.Kind != NaturalObjectKind.Item).Select(o => o.AssetName))}]; loot [{Loot(player)}]");
        }

        // E. A tree far from the player has no harvest to strike.
        NaturalObject? far = null;
        foreach (float distance in new[] { 160f, 200f, 240f })
        {
            for (int turn = 0; turn < 360 && far == null; turn += 30)
            {
                var spot = character.transform.position + Quaternion.Euler(0, turn, 0) * Vector3.forward * distance;
                far = NaturalObjects.Around(spot, 20).FirstOrDefault(o => o.Kind == NaturalObjectKind.Tree);
            }
            if (far != null) break;
        }
        Log($"Harvest: E a tree far away {far?.ToString() ?? "none"} ({(far != null ? (far.Position - character.transform.position).magnitude : 0):0} m): harvested {far?.Harvest()}, gone {far?.IsGone}");

        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
