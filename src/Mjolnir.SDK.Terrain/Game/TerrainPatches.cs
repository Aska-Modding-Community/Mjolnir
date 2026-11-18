using System;
using System.Collections.Generic;
using HarmonyLib;
using SSSGame;
using UnityEngine;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// Where the game changes its ground (each method its own code, not folded with others', Mjolnir.CodeMap):
/// <list type="bullet">
/// <item><c>HeightmapTool.Run</c> and <c>RunOnArea</c>, every local change but a road plan's: <see cref="Landscape.GroundChanging"/>
/// is raised there, a cancel answering the game as a refused placement;</item>
/// <item><c>HeightmapTool.TerraformSpline</c>, a road plan's levelling: remembered, for whom <see cref="Landscape.GroundChanged"/>
/// says made it;</item>
/// <item><c>TerrainSurfaceManager.ApplyTerrainToolData</c>, which queues every change, local ones and those from the network;
/// <c>_ApplyTerrainToolDataInternal</c>, which the game's routine runs as it applies it: <see cref="Landscape.GroundChanged"/>.</item>
/// </list>
/// </summary>
// Harmony skips a class without a class-level [HarmonyPatch], even when its methods carry their own.
[HarmonyPatch]
internal static class TerrainPatches
{
    private const int MaximumPending = 64;

    // The change the game's tool is making now (main thread), and the changes queued for the game's routine.
    [ThreadStatic] private static Made? s_making;
    private static readonly List<(IntPtr Data, Made Made)> s_pending = [];

    private sealed record Made(Rect Area, LandscapeOperation? Operation, LandscapeSource Source, string? ModId);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(HeightmapTool), nameof(HeightmapTool.Run))]
    private static bool BeforeRun(HeightmapTool __instance, TerraformingToolOperation operation, Vector3 position, ref TerraformingToolResult __result, out Made? __state)
    {
        float half = Math.Max(__instance.radius, 1.75f);
        return Before(__instance, operation, LandscapeArea.MakeRect(position.x - half, position.z - half, 2 * half, 2 * half), ref __result, out __state);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(HeightmapTool), nameof(HeightmapTool.RunOnArea))]
    private static bool BeforeRunOnArea(HeightmapTool __instance, TerraformingToolOperation operation, float width, float height, float blendDistance, Vector3 position, float rotation, ref TerraformingToolResult __result, out Made? __state) =>
        Before(__instance, operation, LandscapeArea.BoundsOf(position.x, position.z, width, height, rotation, blendDistance), ref __result, out __state);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(HeightmapTool), nameof(HeightmapTool.TerraformSpline))]
    private static void BeforeSpline(HeightmapTool __instance, TerraformingToolOperation operation, out Made? __state)
    {
        __state = s_making;
        s_making = new Made(default, GameTerrain.OperationOf(operation, __instance.setTerrainType), LandscapeSource.Building, null);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(HeightmapTool), nameof(HeightmapTool.Run))]
    [HarmonyPatch(typeof(HeightmapTool), nameof(HeightmapTool.RunOnArea))]
    [HarmonyPatch(typeof(HeightmapTool), nameof(HeightmapTool.TerraformSpline))]
    private static void AfterTool(Made? __state) => s_making = __state;

    /// <summary>
    /// Marks the change the SDK writes itself, as a mod's, until <see cref="End"/>: the data it queues is reported as made by
    /// that mod. Returns what was marked before.
    /// </summary>
    internal static object? Begin(Rect area, LandscapeOperation operation)
    {
        var previous = s_making;
        s_making = new Made(area, operation, LandscapeSource.Mod, Landscape.EditingMod);
        return previous;
    }

    internal static void End(object? previous) => s_making = previous as Made;

    private static bool Before(HeightmapTool tool, TerraformingToolOperation operation, Rect area, ref TerraformingToolResult result, out Made? state)
    {
        state = s_making;
        bool cancelled = false;
        try
        {
            var source = SourceOf(tool);
            var op = GameTerrain.OperationOf(operation, tool.setTerrainType);
            // A mod's call that sets a game's tool going (a field it levels) is that mod's, whoever holds the tool.
            string? modId = Landscape.EditingMod;
            s_making = new Made(area, op, source, modId);
            cancelled = Landscape.RaiseChanging(area, op, source);
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"Before a change to the ground: {e.Message}");
        }
        if (!cancelled) return true;
        result = TerraformingToolResult.ERROR_PLACEMENT;
        s_making = state;
        return false;
    }

    // Who holds the tool: the SDK's own, a building's (its footprint, a field, a farm, a plan), a villager's or a player's
    // (a tool in their hands, under their bones), else the game's (a spell, a sapling).
    private static LandscapeSource SourceOf(HeightmapTool tool)
    {
        if (tool.Pointer == GameTerrain.ToolPointer && Landscape.IsEditing) return LandscapeSource.Mod;
        if (tool.GetComponentInParent<Structure>() != null) return LandscapeSource.Building;
        if (tool.GetComponentInParent<Villager>() != null) return LandscapeSource.Resident;
        if (tool.GetComponentInParent<PlayerCharacter>() != null) return LandscapeSource.Player;
        return LandscapeSource.Other;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TerrainSurfaceManager), nameof(TerrainSurfaceManager.ApplyTerrainToolData))]
    private static void Queued(TerrainToolData data)
    {
        if (!Landscape.IsChangedWatched || data == null) return;
        try
        {
            var made = s_making is { } local
                ? local with { Area = data.worldArea }
                : new Made(data.worldArea, null, LandscapeSource.Remote, null);
            if (s_pending.Count >= MaximumPending) s_pending.RemoveAt(0);
            s_pending.Add((data.Pointer, made));
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"A change to the ground queued: {e.Message}");
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TerrainSurfaceManager), nameof(TerrainSurfaceManager._ApplyTerrainToolDataInternal))]
    private static void Applied(TerrainToolData data)
    {
        if (data == null || s_pending.Count == 0) return;
        try
        {
            int index = s_pending.FindIndex(p => p.Data == data.Pointer);
            if (index < 0) return;
            var made = s_pending[index].Made;
            s_pending.RemoveAt(index);
            Landscape.RaiseChanged(made.Area, made.Operation, made.Source, made.ModId);
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"A change to the ground applied: {e.Message}");
        }
    }
}
