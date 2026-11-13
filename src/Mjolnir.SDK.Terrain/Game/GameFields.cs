using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SandSailorStudio;
using SSSGame;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// The game's levelling fields for the model: found by their grids in the loaded world, put down through the settlement as
/// a player's own plan is, and levelled tile by tile as a villager's hoe levels them.
/// </summary>
internal static class GameFields
{
    /// <summary>The game's template of the plain levelling field, the one a player sets down.</summary>
    private const string FieldTemplate = "Item_Structures_TerrainLevelField";

    private static NodeTemplate? s_template;

    /// <summary>Every levelling field of the loaded world.</summary>
    public static IReadOnlyList<IFieldView> All()
    {
        var grids = Object.FindObjectsOfType<TerraformingGrid>();
        var list = new List<IFieldView>(grids.Length);
        foreach (var grid in grids)
        {
            if (grid == null) continue;
            var field = new GameField(grid);
            if (!field.IsGone) list.Add(field);
        }
        return list;
    }

    /// <summary>Puts a field down as a player's plan, then gives it its grid of tiles.</summary>
    public static IFieldView? Place(float x, float z, int tilesX, int tilesZ, float tileSize)
    {
        var settlement = GameManagers.Get<SettlementManager>()?.worldSettlement;
        var template = Template();
        if (settlement == null || template == null)
        {
            SdkLog.Info(Landscape.Platform, $"A levelling field was not placed: settlement {settlement != null}, template {template != null}");
            return null;
        }
        var corner = At(x, Height(x, z), z);
        var far = At(x + tilesX * tileSize, Height(x + tilesX * tileSize, z + tilesZ * tileSize), z + tilesZ * tileSize);
        var placement = new TerraformingPlacement
        {
            points = new Il2CppReferenceArray<PointPlacement>(new[] { Point(corner), Point(far) }),
            gridSize = new Vector3Int(tilesX, 1, tilesZ),
            tileSize = tileSize,
        };
        var structure = settlement.CreateStructure(template, placement, null, StructureTemplate.Options.SpawnedByTool, 0);
        if (structure == null) return null;

        // The structure comes up one tile wide whatever the placement asked for: its grid is given its size here, as the
        // game's own tool does while a player drags a plan out.
        var building = structure.GetComponentInChildren<DynamicDimensionBuilding>(true);
        if (building != null) building.SetDimensions(tileSize, new Vector3Int(tilesX, 1, tilesZ), true);
        var grid = structure.GetComponentInChildren<TerraformingGrid>(true);
        if (grid == null)
        {
            SdkLog.Warn(Landscape.Platform, "A levelling field was placed without a grid of tiles");
            return null;
        }
        grid.RefreshCellsHeights(false, true);
        return new GameField(grid);
    }

    private static NodeTemplate? Template()
    {
        if (s_template != null) return s_template;
        foreach (var template in Resources.FindObjectsOfTypeAll<NodeTemplate>())
            if (template != null && template.name == FieldTemplate) return s_template = template;
        return null;
    }

    private static PointPlacement Point(Vector3 at)
    {
        var point = new PointPlacement();
        point.position = at;
        point.rotation = Quaternion.identity;
        return point;
    }

    private static float Height(float x, float z) => Landscape.TryGetHeight(x, z, out float height) ? height : 0f;

    private static Vector3 At(float x, float y, float z)
    {
        Vector3 v = default;
        v.x = x;
        v.y = y;
        v.z = z;
        return v;
    }

    /// <summary>One levelling field, read and changed through its grid.</summary>
    private sealed class GameField(TerraformingGrid grid) : IFieldView
    {
        private bool _taken;

        public Vector3 Center
        {
            get
            {
                var area = Area();
                return At(area.m_XMin + area.m_Width / 2, Height(area.m_XMin + area.m_Width / 2, area.m_YMin + area.m_Height / 2), area.m_YMin + area.m_Height / 2);
            }
        }

        public float Width => Area().m_Width;

        public float Depth => Area().m_Height;

        public int Tiles => IsGone ? 0 : grid.GetCellsCount();

        public int LevelledTiles
        {
            get
            {
                if (IsGone) return 0;
                int count = 0, tiles = grid.GetCellsCount();
                for (int i = 0; i < tiles; i++)
                    if (grid.IsCellLeveled(i)) count++;
                return count;
            }
        }

        public float Height => IsGone ? float.NaN : grid.ReferenceLevel;

        public float HeightDifference => IsGone ? float.NaN : grid.LevelDifference;

        // A plan is gone once its grid or its building is destroyed, or its structure was dismantled or turned into what it
        // builds: the game leaves the grid behind for a frame or two, with its building already taken away.
        public bool IsGone
        {
            get
            {
                if (_taken || grid == null || grid.dynamicBuilding == null) return true;
                var structure = grid.GetComponentInParent<Structure>();
                return structure == null || structure.Dismantled || !structure.gameObject.activeInHierarchy;
            }
        }

        public int LevelNow()
        {
            if (IsGone) return 0;
            int tiles = grid.GetCellsCount();
            // The villagers' own interaction levels a tile where there is one; the grid itself does it otherwise.
            var interaction = grid.terraformingInteraction;
            for (int i = 0; i < tiles; i++)
            {
                if (grid.IsCellLeveled(i)) continue;
                if (interaction != null) interaction.TryLevelTile(i);
                else grid.TryLevelTile(i);
            }
            // The game applies a tile's levelling later in the frame, so the heights its cells show are still the old ones
            // here: how many are levelled is right, the unevenness follows a frame later.
            if (!IsGone) grid.RefreshCellsHeights(false, true);
            return IsGone ? tiles : LevelledTiles;
        }

        public bool Finish()
        {
            if (IsGone) return true;
            if (grid._state is not { } state) return false;
            state.Rpc_DebugCompleteField();
            _taken = true;
            return true;
        }

        public bool Remove()
        {
            if (IsGone) return true;
            var structure = grid.GetComponentInParent<Structure>();
            if (structure == null) return false;
            structure.Dismantle(true);
            _taken = true;
            return true;
        }

        // The ground the field covers: its building's own area (the grid has none of its own).
        private Rect Area() => grid != null && grid.dynamicBuilding != null ? grid.dynamicBuilding.WorldArea : default;
    }
}
