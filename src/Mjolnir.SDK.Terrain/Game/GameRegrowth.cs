using System;
using System.Collections.Generic;
using HarmonyLib;
using SandSailorStudio.Inventory;
using SSSGame;
using SSSGame.Weather;
using UnityEngine;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// What the world's own objects do over time, for placed ones: a bush gathered to the end gives its berries again after its
/// world kind's time (the vegetation system does that for the world's bushes, so a world item needs the SDK), and a tree
/// smaller than its grown size grows (<see cref="NaturalObjects.TreeGrowthDays"/>). Both go by the world's clock, on the
/// machine that has the object's authority (the host), while the object is up near a player; the game saves and sends what
/// they change, as it does for its saplings.
/// <para>A placed bush comes up full (its gathering's whole quantity). A gathered world item keeps what is left on it in its stack's count, and the game neither saves nor loads a world
/// item whose count is 0, so a bush gathered to the end would be gone after a load. While the game writes its world items,
/// such a bush holds a mark instead: a negative count that carries when it gives again, which the game writes and reads as
/// any count but 0; it holds 0 again once written, and the mark read from a save is taken off before its gathering reads it.</para>
/// </summary>
internal static class GameRegrowth
{
    /// <summary>How often the placed objects are looked at, in seconds of real time.</summary>
    private const float Interval = 1f;

    /// <summary>The days a bush whose world kind gives no time of its own waits to give again.</summary>
    private const float DefaultReplenishDays = 3f;

    /// <summary>The least growth a tree is given at once, against its scale: smaller steps wait, so the game sends few.</summary>
    private const float GrowthStep = 0.01f;

    private static readonly object s_lock = new();
    private static readonly Dictionary<IntPtr, (Item Item, float Due)> s_resting = [];
    private static readonly Dictionary<IntPtr, GatherInteraction> s_bushes = [];
    private static readonly Dictionary<IntPtr, HarvestInteraction> s_trees = [];
    private static readonly Dictionary<IntPtr, float> s_grownAt = [];
    private static readonly List<Item> s_marked = [];
    private static float s_next;
    private static bool s_ticking;

    /// <summary>A placed bush's gathering takes its world instance: a mark read from a save comes off first.</summary>
    public static void BeforeGatherSetup(WorldItemInstance? instance)
    {
        var item = instance?.TryCast<InventoryItemInstance>()?.GetItem();
        if (item == null || item.count >= 0 || GamePlacement.Find(item.info?.name) == null) return;
        float due = Regrowth.Decode(item.count);
        item.count = 0;
        lock (s_lock) s_resting[item.Pointer] = (item, due);
    }

    /// <summary>A placed bush's gathering is set up: it is looked at from now on.</summary>
    public static void AfterGatherSetup(GatherInteraction gather)
    {
        if (!IsOurs(gather)) return;
        s_bushes[gather.Pointer] = gather;
        StartTicking();
    }

    /// <summary>A placed tree is set up: it is looked at from now on.</summary>
    public static void AfterTreeSetup(HarvestInteraction harvest)
    {
        if (GamePlacement.Find(NameOf(harvest)) is not { Data.Kind: NaturalObjectKind.Tree }) return;
        s_trees[harvest.Pointer] = harvest;
        StartTicking();
    }

    /// <summary>The game is about to write world items: every bush at rest holds its mark.</summary>
    public static void BeforeWrite()
    {
        lock (s_lock)
        {
            s_marked.Clear();
            foreach (var (item, due) in s_resting.Values)
            {
                if (item.count != 0) continue;
                item.count = Regrowth.Encode(due);
                s_marked.Add(item);
            }
        }
    }

    /// <summary>The game wrote them: the bushes hold 0 again.</summary>
    public static void AfterWrite()
    {
        lock (s_lock)
        {
            foreach (var item in s_marked)
                if (item.count < 0) item.count = 0;
            s_marked.Clear();
        }
    }

    private static void StartTicking()
    {
        if (s_ticking) return;
        s_ticking = true;
        GameLoop.Update += () => GameGuard.RunLogOnce(Landscape.Platform, "regrowing the placed objects", Tick);
    }

    private static void Tick()
    {
        if (Time.realtimeSinceStartup < s_next) return;
        s_next = Time.realtimeSinceStartup + Interval;
        var weather = WeatherSystem.Instance;
        if (weather == null) return;
        float now = weather.NetworkedCurrentGameTime;
        Bushes(weather, now);
        Trees(now);
    }

    private static void Bushes(WeatherSystem weather, float now)
    {
        List<IntPtr>? gone = null;
        foreach (var (key, gather) in s_bushes)
        {
            var instance = gather == null ? null : gather._worldInstance?.TryCast<InventoryItemInstance>();
            if (gather == null || instance == null || instance.Destroyed)
            {
                (gone ??= []).Add(key);
                continue;
            }
            var item = instance.GetItem();
            var made = GamePlacement.Find(item?.info?.name);
            if (item == null || made == null) continue;
            lock (s_lock)
            {
                if (item.count > 0)
                {
                    s_resting.Remove(item.Pointer);
                    continue;
                }
                if (!s_resting.TryGetValue(item.Pointer, out var resting))
                {
                    s_resting[item.Pointer] = (item, ReplenishedAt(made, weather, now));
                    continue;
                }
                if (now < resting.Due || !instance.HasAuthority()) continue;
            }
            // It stays at rest until its count is up (the next look), so a save made meanwhile still keeps it.
            Replenish(gather, instance);
        }
        if (gone != null)
            foreach (var key in gone) s_bushes.Remove(key);
    }

    private static void Trees(float now)
    {
        float days = NaturalObjects.TreeGrowthDays;
        List<IntPtr>? gone = null;
        foreach (var (key, harvest) in s_trees)
        {
            var instance = harvest == null ? null : harvest._worldInstance?.TryCast<InventoryItemInstance>();
            if (harvest == null || instance == null || instance.Destroyed)
            {
                (gone ??= []).Add(key);
                continue;
            }
            var made = GamePlacement.Find(instance.GetItem()?.info?.name);
            if (made == null || days <= 0 || harvest.GetCurrentPieceIndex() != 0 || !instance.HasAuthority())
            {
                s_grownAt.Remove(key);
                continue;
            }
            float scale = instance.GetScale().x;
            float grown = Regrowth.GrownScale(made.Data.MinimumScale, made.Data.MaximumScale, instance.GetPosition());
            if (scale >= grown)
            {
                s_grownAt.Remove(key);
                continue;
            }
            if (!s_grownAt.TryGetValue(key, out float since) || now < since)
            {
                s_grownAt[key] = now;
                continue;
            }
            float growth = (now - since) * grown / days;
            if (growth < GrowthStep) continue;
            var size = Vector3.one * Math.Min(grown, scale + growth);
            bool attached = true, notify = true;
            instance.SetScale(ref size, ref attached, ref notify);
            instance.GetHandler()?.NetworkDataManager?.UpdateWorldItemInstance(instance);
            s_grownAt[key] = now;
        }
        if (gone != null)
            foreach (var key in gone)
            {
                s_trees.Remove(key);
                s_grownAt.Remove(key);
            }
    }

    // A placed bush's object has no networked counter, so the gathering's own replenish does nothing (it changes that
    // counter): its quantity is set as the gathering reads it, and the game told of the item.
    private static void Replenish(GatherInteraction gather, InventoryItemInstance instance)
    {
        int full = gather.CheckMaximumItemCount();
        var modifier = gather.GetGatherModifier();
        if (full <= 0 || modifier == null) return;
        modifier.SetQuantity(full);
        gather.RefreshInteractionData();
        instance.GetHandler()?.NetworkDataManager?.UpdateInventoryItemInstanceItemOnly(instance);
    }

    // When a bush gathered to the end gives again: its world kind's time (the world's settings, its seasons), else a few days.
    // The game's own answer counts the world's settings and the seasons a kind gives again at (the world's berry bushes have
    // no days of their own, only seasons); one more than a year ahead, or none, falls back to its days or a few.
    private static float ReplenishedAt(GamePlacement.Made made, WeatherSystem weather, float now)
    {
        var data = made.World?.GetReplenishData();
        if (data == null) return now + DefaultReplenishDays;
        float at = data.GetNextReplenishTime(weather);
        int year = Math.Max(weather.DaysInOneYear, 1);
        if (float.IsFinite(at) && at > now && at - now <= year) return at;
        return now + (data.replenishFrequencyDays > 0 ? data.replenishFrequencyDays : DefaultReplenishDays);
    }

    private static bool IsOurs(Component part) => GamePlacement.Find(NameOf(part)) != null;

    private static string? NameOf(Component part) => part.GetComponentInParent<ResourceObject>()?.data?.itemInfo?.name;
}

/// <summary>
/// The hooks of placed objects over time, each the game's own method: a gathering taking its world instance (before, a
/// save's mark comes off; after, a placed bush is looked at) and the world items' writing (around it, bushes at rest hold
/// their mark).
/// </summary>
[HarmonyPatch]
internal static class RegrowthPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(GatherInteraction), nameof(GatherInteraction.SetWorldInstance))]
    private static void BeforeGatherSetup(WorldItemInstance instance)
    {
        try
        {
            GameRegrowth.BeforeGatherSetup(instance);
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"A placed bush's rest could not be read: {e.Message}");
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GatherInteraction), nameof(GatherInteraction.SetWorldInstance))]
    private static void AfterGatherSetup(GatherInteraction __instance)
    {
        try
        {
            GameRegrowth.AfterGatherSetup(__instance);
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"A placed bush will not give again: {e.Message}");
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryItemDataHandler), nameof(InventoryItemDataHandler.Write))]
    private static void BeforeWrite()
    {
        try
        {
            GameRegrowth.BeforeWrite();
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"Placed bushes at rest may not be saved: {e.Message}");
        }
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(InventoryItemDataHandler), nameof(InventoryItemDataHandler.Write))]
    private static Exception? AfterWrite(Exception? __exception)
    {
        try
        {
            GameRegrowth.AfterWrite();
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"Placed bushes at rest could not be put back: {e.Message}");
        }
        return __exception;
    }
}
