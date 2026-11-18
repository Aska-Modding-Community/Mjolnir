using System;
using System.Collections.Generic;
using HarmonyLib;
using SandSailorStudio;
using SSSGame;
using SSSGame.Network;
using UnityEngine;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// The game's road plans for the model: found by their terraforming splines in the loaded world, laid through the
/// settlement as the road maker lays one, given their points through the layout's network state (a placement made in code
/// does not carry them), built by finishing their build part, and levelled section by section as a villager levels them.
/// </summary>
internal static class GameRoads
{
    /// <summary>The game's templates of its two plans along a line: a road, and a levelled strip of dirt.</summary>
    private const string RoadTemplate = "Item_Structures_TerrainSplineRoad";
    private const string LevelTemplate = "Item_Structures_TerrainSplineLevel";

    private static SplineStructureTemplate? s_road;
    private static SplineStructureTemplate? s_level;

    /// <summary>
    /// The widths mods gave plans, by the plan's layout state: the template's own field, half the strip a section levels,
    /// put in its place while one of that plan's sections is levelled (<see cref="RoadPatches"/>). Holding the state keeps
    /// its pointer from being given to another object.
    /// </summary>
    private static readonly Dictionary<IntPtr, (NetworkStructureSplineLayoutState State, SplineStructureTemplate Template, float Half)> s_widths = [];

    /// <summary>The template's half width to level a plan's section with, where a mod gave the plan its own.</summary>
    internal static bool TryGetWidth(NetworkStructureSplineLayoutState state, out SplineStructureTemplate template, out float half)
    {
        if (s_widths.TryGetValue(state.Pointer, out var entry))
        {
            template = entry.Template;
            half = entry.Half;
            return true;
        }
        template = null!;
        half = 0;
        return false;
    }

    /// <summary>Every road plan of the loaded world.</summary>
    public static IReadOnlyList<IRoadPlanView> All()
    {
        // Every spline of the loaded scenes, its component switched off too: a plan's spline stays off until the plan is
        // built, and the plain search for live objects passes it by (seen in game).
        var splines = Resources.FindObjectsOfTypeAll<TerraformingSpline>();
        var list = new List<IRoadPlanView>(splines.Length);
        foreach (var spline in splines)
        {
            if (spline == null || !spline.gameObject.scene.IsValid()) continue;
            var plan = new GameRoadPlan(spline);
            if (!plan.IsGone) list.Add(plan);
        }
        return list;
    }

    /// <summary>Lays a plan through the settlement, then gives its layout the points.</summary>
    public static IRoadPlanView? Place(IReadOnlyList<Vector3> points, GroundKind kind)
    {
        var settlement = GameManagers.Get<SettlementManager>()?.worldSettlement;
        var template = Template(kind == GroundKind.Dirt ? LevelTemplate : RoadTemplate);
        if (settlement == null || template == null)
        {
            SdkLog.Info(Landscape.Platform, $"A road plan was not laid: settlement {settlement != null}, template {template != null}");
            return null;
        }
        var placement = new PolyLinePlacement();
        var list = new Il2CppSystem.Collections.Generic.List<PointPlacement>();
        foreach (var at in points)
        {
            var point = new PointPlacement();
            point.position = at;
            point.rotation = Quaternion.identity;
            list.Add(point);
        }
        placement.points = list;
        var structure = settlement.CreateStructure(template, placement, null, StructureTemplate.Options.SpawnedByTool, 0);
        if (structure == null) return null;
        var state = structure.GetComponentInChildren<NetworkStructureSplineLayoutState>(true);
        var spline = structure.GetComponentInChildren<TerraformingSpline>(true);
        if (state == null || spline == null)
        {
            SdkLog.Warn(Landscape.Platform, "A road plan was laid without its line");
            structure.Dismantle(true);
            return null;
        }
        // The structure comes up with no points, whatever the placement carried: the road maker gives them to the layout's
        // state from its preview. The state sends them to the other players.
        foreach (var at in points) state.AddPoint(at);
        state.Rebuild();
        return new GameRoadPlan(spline);
    }

    private static SplineStructureTemplate? Template(string name)
    {
        ref var cached = ref name == RoadTemplate ? ref s_road : ref s_level;
        if (cached != null) return cached;
        foreach (var template in Resources.FindObjectsOfTypeAll<NodeTemplate>())
            if (template != null && template.name == name && template.TryCast<SplineStructureTemplate>() is { } spline)
                return cached = spline;
        return null;
    }

    /// <summary>One road plan, read and changed through its spline, layout and build part.</summary>
    private sealed class GameRoadPlan(TerraformingSpline spline) : IRoadPlanView
    {
        private bool _taken;

        private Structure? Owner() => spline != null ? spline.GetComponentInParent<Structure>() : null;

        private NetworkStructureSplineLayoutState? State() => Owner()?.GetComponentInChildren<NetworkStructureSplineLayoutState>(true);

        private StructureSplineLayoutBuildPart? Part() => Owner()?.GetComponentInChildren<StructureSplineLayoutBuildPart>(true);

        public IReadOnlyList<Vector3> Points
        {
            get
            {
                var state = State();
                if (state == null) return [];
                int count = state.GetPointCount();
                var points = new Vector3[count];
                for (int i = 0; i < count; i++) points[i] = state.GetPointPosition(i);
                return points;
            }
        }

        // What the plan leaves: a road's tool levels with DIRT and then paints the road over it (overpaint ROAD); the
        // levelling plan only levels (DIRT). Seen in game.
        public GroundKind Kind => spline.splineInteraction is { } tool
            ? GameTerrain.KindOf(tool.overpaint ? tool.overpaintTerrainType : tool.levelTerrainType)
            : GroundKind.Road;

        // The strip a section levels is twice the template's width field (the road's 1.75 levels 3.5 m and paves about
        // 2.5 m, seen in game); a mod's width stands in for it while one of the plan's sections is levelled.
        public float Width
        {
            get
            {
                if (State() is { } state && s_widths.TryGetValue(state.Pointer, out var entry)) return entry.Half * 2f;
                return Template() is { } template ? template.width * 2f : 0f;
            }
            set
            {
                var state = State();
                var template = Template();
                if (state == null || template == null) return;
                var dead = new List<IntPtr>();
                foreach (var (pointer, entry) in s_widths)
                    if (entry.State == null) dead.Add(pointer);
                foreach (var pointer in dead) s_widths.Remove(pointer);
                s_widths[state.Pointer] = (state, template, value / 2f);
            }
        }

        private SplineStructureTemplate? Template() =>
            Owner()?.GetComponentInChildren<StructureSplineLayout>(true)?.GetStructureTemplate()?.TryCast<SplineStructureTemplate>();

        public int Sections => IsGone ? 0 : spline.GetCellsCount();

        public int LevelledSections
        {
            get
            {
                if (IsGone) return 0;
                int count = 0, sections = spline.GetCellsCount();
                for (int i = 0; i < sections; i++)
                    if (spline.IsCellLeveled(i)) count++;
                return count;
            }
        }

        public bool IsBuilt => Part() is not { } part || part.IsBuilt();

        // The plan is gone once its spline or structure is, or its structure was dismantled (the game dismantles a road
        // whose last section is done) or switched off.
        public bool IsGone
        {
            get
            {
                if (_taken || spline == null) return true;
                var structure = Owner();
                return structure == null || structure.Dismantled || !structure.gameObject.activeInHierarchy;
            }
        }

        public bool Build()
        {
            if (IsGone) return false;
            if (Part() is { } part && !part.IsBuilt()) part.FinishPart();
            return IsBuilt;
        }

        public int LevelNow(Func<Rect, bool>? skip)
        {
            if (IsGone) return 0;
            int sections = spline.GetCellsCount();
            float reach = RoadPlans.SectionLength + Width;
            for (int i = 0; i < sections; i++)
            {
                if (IsGone) break;
                if (spline.IsCellLeveled(i)) continue;
                if (skip != null)
                {
                    var middle = spline.GetCellPosition(i);
                    if (skip(new Rect(middle.x - reach / 2, middle.z - reach / 2, reach, reach))) continue;
                }
                spline.TryLevelTile(i);
            }
            // The game dismantles a road whose last section is done: then every section is.
            return IsGone ? sections : LevelledSections;
        }

        public bool Remove()
        {
            if (IsGone) return true;
            var structure = Owner();
            if (structure == null) return false;
            structure.Dismantle(true);
            _taken = true;
            return true;
        }
    }
}

/// <summary>
/// A plan's own width: while one of its sections is levelled (by the SDK or a villager), its template's width is the one
/// a mod gave it, and the template's own comes back after. The method is the game's own (its code is shared with no other).
/// </summary>
[HarmonyPatch]
internal static class RoadPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TerraformingSplineInteraction), nameof(TerraformingSplineInteraction.LevelSection))]
    private static void Widen(TerraformingSplineInteraction __instance, out float __state)
    {
        __state = float.NaN;
        var state = __instance.splineLayoutState;
        if (state == null || !GameRoads.TryGetWidth(state, out var template, out float half)) return;
        __state = template.width;
        template.width = half;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TerraformingSplineInteraction), nameof(TerraformingSplineInteraction.LevelSection))]
    private static void Restore(TerraformingSplineInteraction __instance, float __state)
    {
        if (float.IsNaN(__state)) return;
        var state = __instance.splineLayoutState;
        if (state != null && GameRoads.TryGetWidth(state, out var template, out _)) template.width = __state;
    }
}
