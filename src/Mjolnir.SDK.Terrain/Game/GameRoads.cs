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

    /// <summary>The step a plan's width is kept to, in metres: a template of the SDK's own per step.</summary>
    internal const float WidthStep = 0.5f;

    /// <summary>
    /// The SDK's own templates of the two plans, one per width (half the strip in the template's field, as the game's): a
    /// plan laid from one, or switched to one, keeps its width, since the game saves and sends a structure by its template.
    /// Made once per game as the game builds its item database, by name: "Mjolnir_RoadPlan_Road_80" for a road 8 m wide.
    /// </summary>
    private static readonly Dictionary<string, SplineStructureTemplate> s_widths = new(StringComparer.Ordinal);
    private static bool s_widthsMade;

    /// <summary>The width templates go into the database's list (made the first time), before the game reads it.</summary>
    public static void BeforeDatabase(Il2CppSystem.Collections.Generic.List<SandSailorStudio.Inventory.ItemInfo> list)
    {
        if (!s_widthsMade)
        {
            s_widthsMade = true;
            var ids = new HashSet<int>();
            SplineStructureTemplate? road = null, level = null;
            for (int i = 0; i < list.Count; i++)
            {
                var info = list[i];
                if (info == null) continue;
                ids.Add(info.id);
                if (info.name == RoadTemplate) road = info.TryCast<SplineStructureTemplate>();
                else if (info.name == LevelTemplate) level = info.TryCast<SplineStructureTemplate>();
            }
            int made = 0;
            foreach (var (kind, template) in new[] { ("Road", road), ("Level", level) })
            {
                if (template == null) continue;
                for (int step = (int)(RoadPlans.MinimumWidth / WidthStep); step <= (int)(RoadPlans.MaximumWidth / WidthStep); step++)
                {
                    string name = WidthName(kind, step * WidthStep);
                    int id = (int)(Fnv1a.OfUtf8("mjolnir.terrain/" + name) & 0x7FFFFFFF) | 0x40000000;
                    if (!ids.Add(id))
                    {
                        SdkLog.Warn(Landscape.Platform, $"The road plan template '{name}' is not made: its id {id} is taken");
                        continue;
                    }
                    var copy = UnityEngine.Object.Instantiate(template);
                    copy.name = name;
                    copy.id = id;
                    copy.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    copy.width = step * WidthStep / 2f;
                    s_widths[name] = copy;
                    made++;
                }
            }
            SdkLog.Info(Landscape.Platform, $"{made} road plan templates of a mod's width");
        }
        foreach (var template in s_widths.Values)
        {
            bool listed = false;
            for (int i = list.Count - 1; i >= 0 && !listed; i--)
                listed = list[i] != null && list[i].Pointer == template.Pointer;
            if (!listed) list.Add(template);
        }
    }

    private static string WidthName(string kind, float width) => FormattableString.Invariant($"Mjolnir_RoadPlan_{kind}_{Math.Round(width * 10f):0}");

    /// <summary>The template of a plan's kind and width: the game's own at its own width, else the SDK's.</summary>
    private static SplineStructureTemplate? Template(GroundKind kind, float width)
    {
        var game = Template(kind == GroundKind.Dirt ? LevelTemplate : RoadTemplate);
        if (width <= 0 || game == null) return game;
        float rounded = MathF.Round(width / WidthStep) * WidthStep;
        if (Math.Abs(rounded - game.width * 2f) < 0.01f) return game;
        return s_widths.TryGetValue(WidthName(kind == GroundKind.Dirt ? "Level" : "Road", rounded), out var mine) ? mine : null;
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
    public static IRoadPlanView? Place(IReadOnlyList<Vector3> points, GroundKind kind, float width)
    {
        var settlement = GameManagers.Get<SettlementManager>()?.worldSettlement;
        var template = Template(kind, width);
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

        // The strip a section levels is twice the width field of the layout's template (the road's 1.75 levels 3.5 m and
        // paves about 2.5 m, seen in game). A width of a mod's is the template of the SDK's for it: set on the structure,
        // which the game saves and sends by its template, and on the layout, which levels with its own reference.
        public float Width
        {
            get => Template() is { } template ? template.width * 2f : 0f;
            set
            {
                var structure = Owner();
                var layout = structure?.GetComponentInChildren<StructureSplineLayout>(true);
                if (structure == null || layout == null) return;
                var template = GameRoads.Template(Kind, value);
                if (template == null) return;
                structure.Template = template;
                layout._structureTemplate = template;
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
