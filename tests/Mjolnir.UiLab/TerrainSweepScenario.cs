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
/// The "terrain sweep" scenario: changes of the ground that take what stands there with its loot
/// (<c>LandscapeObjects.Harvest</c>). On the test save's shore it levels a square round a willow, its boulder and the reeds
/// as nobody; by two stone clumps it raises a circle as the player without injuries; each spot's ground is put back after.
/// It changes the save's world: back the save up and put it back after.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainSweepScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log("Sweep: no world");
            yield break;
        }
        var home = character.transform.position;
        string Standing(LandscapeArea area) => string.Join(", ", NaturalObjects.In(area).Where(o => o.Kind != NaturalObjectKind.Item).GroupBy(o => o.AssetName).Select(g => $"{g.Key} ×{g.Count()}"));
        string Loot(Vector3 at, float r) => string.Join(", ", NaturalObjects.Around(at, r).Where(o => o.Kind == NaturalObjectKind.Item && !o.AssetName.Contains("Sapling")).GroupBy(o => o.AssetName).Select(g => $"{g.Key} ×{g.Count()}"));
        float Wood() => Mjolnir.SDK.Player.LocalPlayer.Sheet?.Skills.FirstOrDefault(s => s.Kind == SkillKind.WoodHarvest)?.Experience.Value ?? float.NaN;
        float Stone() => Mjolnir.SDK.Player.LocalPlayer.Sheet?.Skills.FirstOrDefault(s => s.Kind == SkillKind.StoneHarvest)?.Experience.Value ?? float.NaN;

        var spots = new (string Name, Vector3 At, Func<LandscapeArea> Area, bool Level, HarvestOptions? Who)[]
        {
            ("the willow's shore", new Vector3(-202.5f, 34.5f, 392f), () => LandscapeArea.Rectangle(new Vector3(-202.5f, 0, 392f), 10, 10, blend: 2), true, null),
            ("the stone clumps", new Vector3(-46.8f, 37f, 313.2f), () => LandscapeArea.Circle(new Vector3(-46.8f, 0, 313.2f), 14), false, new HarvestOptions { AsPlayer = true, AllowInjuries = false }),
        };
        foreach (var (name, at, makeArea, level, who) in spots)
        {
            var to = at - (at - home).normalized * 14f;
            var walk = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(to.x, to.z);
            foreach (float wait in WaitFor("the teleport", () => walk.IsCompleted, 30)) yield return wait;
            yield return 4f;
            var area = makeArea();
            var camera = Overhead(at);
            camera.Position = at + new Vector3(0, 16f, -16f);
            camera.Rotation = Quaternion.LookRotation(at - camera.Position, Vector3.up);
            var kept = Landscape.Capture(area);
            var heights = Landscape.Measure(area);
            string before = Standing(area);
            string lootBefore = Loot(at, 20);
            float wood = Wood(), stone = Stone();
            Shot($"sweep-{(level ? "shore" : "stones")}-before");
            yield return AfterShot;
            var options = new LandscapeOptions { Objects = LandscapeObjects.Harvest, Harvesting = who, KeepOffBuildings = false };
            var result = level ? Landscape.Level(area, heights.Average, options) : Landscape.Raise(area, 0.5f, options);
            yield return 8f;
            Log(Inv($"Sweep: {name} {(level ? $"levelled to {heights.Average:0.00}" : "raised 0.5 m")} with Objects = Harvest as {(who?.AsPlayer == true ? "the player" : "nobody")}: {result}; standing before [{before}], after [{Standing(area)}]; loot round it before [{lootBefore}], after [{Loot(at, 20)}]; wood experience {wood:0.##} → {Wood():0.##}, stone {stone:0.##} → {Stone():0.##}"));
            Shot($"sweep-{(level ? "shore" : "stones")}-after");
            yield return AfterShot;
            var back = kept?.Restore(new LandscapeOptions { KeepOffBuildings = false, Objects = LandscapeObjects.Keep });
            yield return 2f;
            Log($"Sweep: {name}: the ground put back: {back?.ToString() ?? "no snapshot"}");
            camera.Dispose();
        }

        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
