using System;
using System.Collections.Generic;
using System.Linq;
using Mjolnir.SDK.Terrain;
using SSSGame;
using SSSGame.Combat;
using SandSailorStudio;
using SandSailorStudio.UI;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "terrain probe8" scenario: what is left of the terrain research (plan §5.11, step 5). Near the player it looks at
/// what the world's objects carry (their interactions, loot and resource parts), gathers a charge out of a gathering one
/// (the berries, flax and driftwood a player picks by hand) through the game's own interaction, strikes a tree with and
/// without the harvest tool flag to see whether anything drops. (Adding an object to the world's data by appending to the
/// vegetation buffer's native lists killed the game, so the probe no longer does it.) It changes the objects and the items
/// of the save: back it up.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> TerrainProbe8()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        yield return 10f;
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        if (character == null || !Landscape.IsAvailable)
        {
            Log("Probe8: no world");
            yield break;
        }
        var home = character.transform.position;
        List<BiomeItemInstance> Biome(Vector3 p, float r) => Objects(p, r, null, InstanceActivationContext.ALL).Select(i => i.TryCast<BiomeItemInstance>()).Where(b => b != null && !b.Destroyed).Cast<BiomeItemInstance>().ToList();
        List<InventoryItemInstance> Items(Vector3 p, float r) => Objects(p, r, null, InstanceActivationContext.ALL).Select(i => i.TryCast<InventoryItemInstance>()).Where(b => b != null && !b.Destroyed).Cast<InventoryItemInstance>().ToList();
        string ItemNames(Vector3 p, float r) => string.Join(", ", Items(p, r).Select(i => i.Descriptor?.itemInfo?.name ?? "?").GroupBy(n => n).Select(g => $"{g.Key} ×{g.Count()}"));
        string Kind(BiomeItemInstance b) => b.Descriptor?.TryCast<BiomeItemDescriptor>()?.GetBiomeInfo()?.Name ?? "?";

        // A. What the objects round the player carry: their own game objects and the parts on them.
        var near = Biome(home, 14f).Where(b => b.gameObject != null).ToList();
        Log($"Probe8: {near.Count} objects with a game object within 14 m of the player");
        foreach (var one in near.Take(6))
        {
            var go = one.gameObject;
            string parts = string.Join(", ", go.GetComponentsInChildren<Component>(true).Select(c => c != null ? c.GetIl2CppType().Name : "null").Distinct().Take(14));
            Log($"Probe8:   '{Kind(one)}' ({go.name}): harvest {go.GetComponentInChildren<HarvestInteraction>(true) != null}, gather {go.GetComponentInChildren<GatherInteraction>(true) != null}, loot spawner {go.GetComponentInChildren<LootSpawner>(true) != null}, harvest spawner {go.GetComponentsInChildren<HarvestSpawner>(true).Length}; [{parts}]");
        }
        var camera = Overhead(home);
        Shot("probe8-around");
        yield return AfterShot;

        // B. A charge gathered through the SDK, as a player picking by hand.
        var pick = NaturalObjects.Around(home, 14).FirstOrDefault(o => o.GatherCount > 0);
        if (pick != null)
        {
            var at = pick.Position;
            camera.Position = at + new Vector3(0, 5f, -6f);
            camera.Rotation = Quaternion.LookRotation(at + Vector3.up - camera.Position, Vector3.up);
            string before = ItemNames(at, 6f);
            int charges = pick.GatherCount;
            int taken = pick.Gather();
            yield return 2f;
            Log($"Probe8: '{pick.AssetName}' gathered through the SDK: {taken} item(s) from {charges} left, now {pick.GatherCount}, quantity {pick.Quantity}, gone {pick.IsGone}; items near before [{before}], after [{ItemNames(at, 6f)}]");
            Shot("probe8-gathered");
            yield return AfterShot;
            int rest = pick.Gather(10);
            yield return 2f;
            Log($"Probe8: the rest gathered: {rest} item(s), left {pick.GatherCount}, gone {pick.IsGone}; items near [{ItemNames(at, 6f)}]");
        }
        else Log("Probe8: nothing to gather by hand near the player");

        // C. A tree struck without a real tool, with and without the harvest tool ignored.
        // A standing tree, the player taken to it so the game spawns its object.
        var standing = NaturalObjects.Around(home, 60).FirstOrDefault(o => o.Kind == NaturalObjectKind.Tree && o.Health > 0);
        if (standing != null && Vector3.Distance(standing.Position, character.transform.position) > 4f)
        {
            var to = standing.Position + (home - standing.Position).normalized * 3f;
            var walk = Mjolnir.SDK.Player.LocalPlayer.TeleportToGroundAsync(to.x, to.z);
            foreach (float wait in WaitFor("the teleport", () => walk.IsCompleted, 30)) yield return wait;
            yield return 3f;
        }
        var tree = standing == null ? null : Biome(standing.Position, 1.5f).FirstOrDefault(b => b.gameObject != null && b.gameObject.GetComponentInChildren<HarvestInteraction>(true) != null && b.GetHealth() > 0);
        if (tree != null)
        {
            var harvest = tree.gameObject.GetComponentInChildren<HarvestInteraction>(true);
            var dealer = character.GetComponentInChildren<MeleeManager>(true)?.TryCast<IDamageDealer>();
            var at = tree.GetPosition();
            camera.Position = at + new Vector3(0, 6f, -8f);
            camera.Rotation = Quaternion.LookRotation(at + Vector3.up * 2 - camera.Position, Vector3.up);
            foreach (var (name, flags) in new[] { ("Harvest | Melee", DamageFlags.Harvest | DamageFlags.Melee), ("IgnoreHarvestTool", DamageFlags.Harvest | DamageFlags.Melee | DamageFlags.IgnoreHarvestTool) })
            {
                string before = ItemNames(at, 8f);
                int piece = Try2(() => harvest.GetCurrentPieceIndex());
                float health = tree.GetHealth();
                int blows = 0;
                for (; blows < 12 && harvest != null && !tree.Destroyed; blows++)
                {
                    string error = Try(() =>
                    {
                        harvest!.ForceTakeDamage(new DamageData
                        {
                            damageReceiver = harvest.TryCast<IDamageReceiver>(),
                            damageDealer = dealer,
                            baseDamage = 40,
                            damageMultiplier = 1,
                            damageFlags = flags,
                            hitPosition = harvest.transform.position + Vector3.up,
                            hitDirection = (harvest.transform.position - character.transform.position).normalized,
                        });
                        return "";
                    });
                    if (error.Length > 0)
                    {
                        Log($"Probe8: a blow with {name} failed: {error}");
                        break;
                    }
                    yield return 0.3f;
                }
                yield return 2f;
                Log($"Probe8: '{Kind(tree)}' struck {blows} times with {name}: health {health:0.#} → {Try(() => tree.GetHealth())}, piece {piece} → {Try(() => harvest?.GetCurrentPieceIndex())}, exhausted {Try(() => tree.IsExhausted())}, destroyed {tree.Destroyed}; items near before [{before}], after [{ItemNames(at, 8f)}]");
            }
            Shot("probe8-struck");
            yield return AfterShot;
        }
        else Log("Probe8: no tree with a harvest interaction near the player");

        camera.Dispose();
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }

    private static T? Try2<T>(Func<T?> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return default;
        }
    }
}
