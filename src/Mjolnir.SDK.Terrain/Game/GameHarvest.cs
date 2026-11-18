using System;
using System.Collections.Generic;
using HarmonyLib;
using SandSailorStudio;
using SSSGame;
using SSSGame.Combat;
using SSSGame.Deeds;
using UnityEngine;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// Harvesting a natural object as the game's own harvest does: blows through its harvest interaction, the villagers' and
/// the player's way, so each part falls with the game's loot (a tree its trunk, a stump its firewood, a trunk its logs) and
/// the game tells the other players. A blow carries no tool (the flag that lets it through without one) and the local
/// player's melee as its dealer, which the game needs; the patches below take back what the game would give that player
/// unless the mod asked for it.
/// </summary>
internal static class GameHarvest
{
    /// <summary>The most blows given to one object: a tree's three parts take one each.</summary>
    private const int MostBlows = 12;

    /// <summary>How far round a felled tree its trunk is looked for, in metres: a tall tree's trunk falls its own length away.</summary>
    private const float TrunkReach = 24f;

    /// <summary>How long a felled tree's trunk is waited for, in seconds: the game takes no blow just after it falls.</summary>
    private const float TrunkWait = 10f;

    /// <summary>How often a trunk that is not there yet, or not to be struck yet, is tried again, in seconds.</summary>
    private const float TrunkRetry = 0.5f;

    /// <summary>How long objects to be harvested after a change of the ground wait for the drawn ground, in seconds.</summary>
    private const float GroundWait = 0.3f;

    private static readonly List<Trunk> s_trunks = [];
    private static readonly List<(List<IWorldObject> Objects, HarvestOptions Options, float At)> s_later = [];
    private static bool s_ticking;

    /// <summary>The options of the blow the SDK is giving now; null while the game strikes for itself.</summary>
    internal static HarvestOptions? Current { get; private set; }

    /// <summary>
    /// Strikes until nothing is left of it. True when it is used up; false when the game turned a blow away (too soon after
    /// it appeared) or there was no one to strike with.
    /// </summary>
    public static bool Strike(HarvestInteraction harvest, HarvestOptions options)
    {
        var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
        var dealer = character != null ? character.GetComponentInChildren<MeleeManager>(true)?.TryCast<IDamageDealer>() : null;
        if (character == null || dealer == null) return false;
        var receiver = harvest.TryCast<IDamageReceiver>();
        for (int blow = 0; blow < MostBlows; blow++)
        {
            var at = harvest.transform.position;
            var direction = at - character.transform.position;
            direction.y = 0;
            var data = new DamageData
            {
                damageDealer = dealer,
                damageReceiver = receiver,
                baseDamage = Math.Max(harvest.GetHarvestHitPoints(), 0f) + 1f,
                damageMultiplier = 1f,
                damageFlags = DamageFlags.IgnoreHarvestTool,
                hitPosition = at + Vector3.up,
                hitDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward,
            };
            Current = options;
            try
            {
                harvest.TakeDamage(data);
            }
            finally
            {
                Current = null;
            }
            // A blow on what is used up comes back Invalid; one too soon after it appeared, Ignore.
            if ((data.damageFlags & DamageFlags.Invalid) != 0) return true;
            if ((data.damageFlags & DamageFlags.Ignore) != 0) return false;
        }
        return harvest.GetTotalRemainingHitPoints() <= 0f;
    }

    /// <summary>The trunks lying round a tree about to be felled, so the one it drops can be told from them.</summary>
    public static HashSet<IntPtr> TrunksAround(Vector3 tree, string resource)
    {
        var lying = new HashSet<IntPtr>();
        foreach (var found in GameObjects.Items(tree, TrunkReach, resource)) lying.Add(found.Pointer);
        return lying;
    }

    /// <summary>Chops the trunk a felled tree dropped, once it is there and the game takes a blow on it.</summary>
    public static void ChopTrunk(Vector3 tree, string resource, HashSet<IntPtr> lying, HarvestOptions options)
    {
        s_trunks.Add(new Trunk(tree, resource, lying, options, Time.realtimeSinceStartup));
        StartTicking();
    }

    /// <summary>Takes the objects a change of the ground reached with their loot, once the drawn ground has followed.</summary>
    public static void Later(List<IWorldObject> objects, HarvestOptions options)
    {
        s_later.Add((objects, options, Time.realtimeSinceStartup + GroundWait));
        StartTicking();
    }

    private static void StartTicking()
    {
        if (s_ticking) return;
        s_ticking = true;
        GameLoop.Update += () => GameGuard.RunLogOnce(Landscape.Platform, "harvesting what the ground's changes reached", Tick);
    }

    private static void Tick()
    {
        float now = Time.realtimeSinceStartup;
        for (int i = 0; i < s_later.Count;)
        {
            var (objects, options, at) = s_later[i];
            if (now < at)
            {
                i++;
                continue;
            }
            s_later.RemoveAt(i);
            ObjectSweep.TakeAll(objects, options);
        }
        if (s_trunks.Count == 0) return;
        for (int i = s_trunks.Count - 1; i >= 0; i--)
        {
            var trunk = s_trunks[i];
            if (now < trunk.Next) continue;
            trunk.Next = now + TrunkRetry;
            bool done = false;
            try
            {
                foreach (var found in GameObjects.Items(trunk.Tree, TrunkReach, trunk.Resource))
                {
                    if (trunk.Lying.Contains(found.Pointer) || found.gameObject == null) continue;
                    var harvest = found.gameObject.GetComponentInChildren<HarvestInteraction>(true);
                    if (harvest == null) continue;
                    done = Strike(harvest, trunk.Options);
                    break;
                }
            }
            catch (Exception e)
            {
                SdkLog.Warn(Landscape.Platform, $"A felled trunk '{trunk.Resource}' could not be chopped: {e.Message}");
                done = true;
            }
            if (done) s_trunks.RemoveAt(i);
            else if (now - trunk.Felled > TrunkWait)
            {
                SdkLog.Info(Landscape.Platform, $"A felled trunk '{trunk.Resource}' was not chopped: it did not come up within {TrunkWait:0} s");
                s_trunks.RemoveAt(i);
            }
        }
    }

    private sealed class Trunk(Vector3 tree, string resource, HashSet<IntPtr> lying, HarvestOptions options, float felled)
    {
        public Vector3 Tree { get; } = tree;
        public string Resource { get; } = resource;
        public HashSet<IntPtr> Lying { get; } = lying;
        public HarvestOptions Options { get; } = options;
        public float Felled { get; } = felled;
        public float Next { get; set; } = felled + TrunkRetry;
    }
}

/// <summary>
/// What the game gives the dealer of a blow, taken back while the SDK strikes for nobody or for a player who should not
/// have it: the work's experience, the work injury, the harvest in the deeds and the game's statistics. Each method is the
/// game's own (none of them shares its code), and the game's own blows pass untouched.
/// </summary>
[HarmonyPatch]
internal static class HarvestPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(InteractionMoveset), nameof(InteractionMoveset.AwardProfficiencyPoints))]
    private static bool Experience() => GameHarvest.Current is not { } options || (options.AsPlayer && options.AwardExperience);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(InteractionMoveset), nameof(InteractionMoveset.HandleWorkInjury))]
    private static bool Injury() => GameHarvest.Current is not { } options || (options.AsPlayer && options.AllowInjuries);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(DeedsDatabase), nameof(DeedsDatabase.SendDeedEvent))]
    private static bool Deed() => GameHarvest.Current is not { } options || options.AsPlayer;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(DeedsDatabase.GameStatistics), nameof(DeedsDatabase.GameStatistics.AddHarvest))]
    private static bool Statistics() => GameHarvest.Current is not { } options || options.AsPlayer;
}
