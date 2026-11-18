using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// The game's levelling fields: the plans a player sets down for the villagers to level, tile by tile, to one height. A mod
/// puts one down with <see cref="Place"/> and may level it at once (<see cref="LevellingField.LevelNow"/>) instead of
/// waiting for the villagers. Only the part of the world loaded around the player has them, and only the main thread may
/// read or change them.
/// </summary>
/// <example><code>
/// // A field over the yard, levelled at once.
/// if (LevellingFields.Place(LandscapeArea.Rectangle(position, 8, 8)) is { } field)
///     field.LevelNow();
/// </code></example>
public static class LevellingFields
{
    /// <summary>The metres across one tile of a field, as the game lays its grid.</summary>
    public const float TileSize = 2f;

    /// <summary>The most tiles a field has, as the game's own plan allows.</summary>
    public const int MaximumTiles = 256;

    /// <summary>The levelling fields of the loaded world, nearest the player's own place in the list's order of the game.</summary>
    public static IReadOnlyList<LevellingField> All
    {
        get
        {
            if (Landscape.LiveSource() is not { } source) return [];
            try
            {
                var fields = source.Fields;
                var list = new LevellingField[fields.Count];
                for (int i = 0; i < fields.Count; i++) list[i] = new LevellingField(fields[i]);
                return list;
            }
            catch (Exception e)
            {
                SdkLog.Warn(Landscape.Platform, $"The levelling fields could not be read: {e.Message}");
                return [];
            }
        }
    }

    /// <summary>The levelling field nearest a point within <paramref name="radius"/> metres of it; null for none.</summary>
    /// <param name="center">Where to look from (its y is not used).</param>
    /// <param name="radius">How far round it to look, over 0 metres.</param>
    /// <exception cref="ArgumentOutOfRangeException">The radius is out of range or not a number.</exception>
    public static LevellingField? Nearest(Vector3 center, float radius = 16f)
    {
        if (!(radius > 0 && radius < 10000f)) throw new ArgumentOutOfRangeException(nameof(radius), radius, "A radius is over 0 m.");
        LevellingField? best = null;
        float nearest = radius * radius;
        foreach (var field in All)
        {
            var at = field.Center;
            float dx = at.x - center.x, dz = at.z - center.z;
            float distance = dx * dx + dz * dz;
            if (distance > nearest) continue;
            nearest = distance;
            best = field;
        }
        return best;
    }

    /// <summary>
    /// Puts a levelling field over the area, as a player sets one down: a grid of whole tiles of <see cref="TileSize"/>
    /// metres, laid along the world's own axes (an area's turn is not used) and fitted to the area's middle. The villagers
    /// work it as they work the player's own plans, and it is saved and sent to the other players by the game.
    /// </summary>
    /// <param name="area">Where it goes: its own sides (a circle's diameter, a polygon's box; the blend is not counted)
    /// rounded to whole tiles, at least one and at most <see cref="MaximumTiles"/> in all.</param>
    /// <returns>The field, or null where the world will not take one (logged).</returns>
    public static LevellingField? Place(LandscapeArea area)
    {
        if (Landscape.LiveSource() is not { } source) return null;
        int tilesX = Math.Max(1, (int)MathF.Round(area.Width / TileSize, MidpointRounding.AwayFromZero));
        int tilesZ = Math.Max(1, (int)MathF.Round(area.Depth / TileSize, MidpointRounding.AwayFromZero));
        if (tilesX * tilesZ > MaximumTiles)
        {
            SdkLog.Info(Landscape.Platform, $"A levelling field over {area} would need {tilesX * tilesZ} tiles, more than the {MaximumTiles} the game allows");
            return null;
        }
        float x = area.Center.x - tilesX * TileSize / 2, z = area.Center.z - tilesZ * TileSize / 2;
        if (!source.IsLoaded(x, z) || !source.IsLoaded(x + tilesX * TileSize, z + tilesZ * TileSize)) return null;
        try
        {
            var placed = source.PlaceField(x, z, tilesX, tilesZ, TileSize);
            return placed == null ? null : new LevellingField(placed);
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"A levelling field over {area} could not be placed: {e.Message}");
            return null;
        }
    }
}

/// <summary>
/// One of the game's levelling fields (<see cref="LevellingFields"/>): a grid of tiles the villagers level to one height.
/// It stands for the game's own plan, so what it tells follows the game, and it is <see cref="IsGone"/> once the plan is
/// finished or taken away.
/// </summary>
public sealed class LevellingField
{
    private readonly IFieldView _field;

    internal LevellingField(IFieldView field) => _field = field;

    /// <summary>The middle of the field on the ground.</summary>
    public Vector3 Center => Read(() => _field.Center, default);

    /// <summary>Its size along x, in metres.</summary>
    public float Width => Read(() => _field.Width, 0f);

    /// <summary>Its size along z, in metres.</summary>
    public float Depth => Read(() => _field.Depth, 0f);

    /// <summary>How many tiles it has.</summary>
    public int Tiles => Read(() => _field.Tiles, 0);

    /// <summary>How many of them are levelled already.</summary>
    public int LevelledTiles => Read(() => _field.LevelledTiles, 0);

    /// <summary>Whether every tile is levelled.</summary>
    public bool IsLevelled => Tiles > 0 && LevelledTiles >= Tiles;

    /// <summary>The height it levels its ground to, in metres.</summary>
    public float Height => Read(() => _field.Height, float.NaN);

    /// <summary>
    /// How far its ground still stands from that height at its worst, in metres. The game works it out from the ground as
    /// it was drawn, so after <see cref="LevelNow"/> it follows only on the next frame.
    /// </summary>
    public float HeightDifference => Read(() => _field.HeightDifference, float.NaN);

    /// <summary>Whether the plan is no longer in the world: finished, taken away, or out of the loaded part.</summary>
    public bool IsGone => Read(() => _field.IsGone, true);

    /// <summary>
    /// Levels every tile that is not levelled yet, as the villagers level them one by one with their hoes: the ground is
    /// changed by the game, saved and sent to the other players. Once the last tile is levelled the game finishes the plan
    /// itself, so the field is <see cref="IsGone"/> right after (there is nothing left to <see cref="Finish"/>). A tile
    /// another mod's protected zone keeps is left as it is (<see cref="Landscape.Protect"/>); the mod's own zones let it
    /// level.
    /// </summary>
    /// <returns>How many tiles are levelled now; 0 off the main thread or once the field is gone.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LevelNow()
    {
        string modId = Landscape.Owner(CallingMod.Of(Assembly.GetCallingAssembly()));
        if (!Landscape.IsMainThread || IsGone) return 0;
        try
        {
            // The field's own tool levels it, as the mod's change: the mod's own zones let it through, and a tile another's
            // zone keeps is left untried, since the game would mark it worked though its ground stayed.
            return Landscape.Editing(modId, () => _field.LevelNow(rect => LandscapeZone.Refusing(rect, modId) != null));
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"The levelling field at {Center} could not be levelled: {e.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Turns the plan into what it builds (the field goes and its result structure stands there), as the game does once the
    /// villagers have levelled it. A plain levelling field builds nothing, so it only goes. A field levelled to the last
    /// tile is finished by the game itself, so this is for a plan that is not levelled yet.
    /// </summary>
    /// <returns>Whether the game took it.</returns>
    public bool Finish() => Act(() => _field.Finish(), "finished");

    /// <summary>Takes the plan away as a player dismantles it, leaving the ground as it is.</summary>
    /// <returns>Whether it is gone now.</returns>
    public bool Remove() => Act(() => _field.Remove(), "taken away");

    /// <inheritdoc/>
    public override string ToString()
    {
        var at = Center;
        return FormattableString.Invariant($"levelling field of {Width:0.#} × {Depth:0.#} m at ({at.x:0.#}, {at.z:0.#}), {LevelledTiles} of {Tiles} tiles levelled");
    }

    private bool Act(Func<bool> act, string what)
    {
        if (!Landscape.IsMainThread) return false;
        if (IsGone) return true;
        try
        {
            return act();
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"The levelling field at {Center} could not be {what}: {e.Message}");
            return false;
        }
    }

    private static T Read<T>(Func<T> read, T none)
    {
        if (!Landscape.IsMainThread) return none;
        try
        {
            return read();
        }
        catch (Exception)
        {
            return none;
        }
    }
}
