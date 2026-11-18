using System;
using System.Collections.Generic;
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

        public float Width => Owner()?.GetComponentInChildren<StructureSplineLayout>(true)?.SplineWidth ?? 0f;

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
