using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// The natural objects of the world: the trees, stones, bushes, nests and mushrooms the world grew, and the items lying on
/// the ground. Only the part of the world loaded around the player has them, and only the main thread may read or change
/// them (elsewhere the lists are empty).
/// </summary>
/// <example><code>
/// // The bushes of a yard taken away, the trees left standing on their new ground.
/// var yard = LandscapeArea.Circle(position, 8);
/// foreach (var found in NaturalObjects.In(yard))
///     if (found.Kind == NaturalObjectKind.Plant) found.Remove();
/// Landscape.Level(yard, Landscape.HeightAt(position.x, position.z), new LandscapeOptions { Objects = LandscapeObjects.Settle });
/// </code></example>
public static class NaturalObjects
{
    /// <summary>
    /// The natural objects standing in the area, nearest its middle first: every one whose place is at least half in it
    /// (the blend counted). Empty where no world is loaded, the area is not all loaded, or off the main thread.
    /// </summary>
    /// <param name="area">Where to look.</param>
    public static IReadOnlyList<NaturalObject> In(LandscapeArea area)
    {
        if (Landscape.LiveSource() is not { } source) return [];
        var bounds = area.Bounds;
        float radius = MathF.Sqrt(bounds.m_Width * bounds.m_Width + bounds.m_Height * bounds.m_Height) / 2 + 1f;
        return Found(source, area.Center.x, area.Center.z, radius, (x, z) => area.Weight(x, z) >= 0.5f);
    }

    /// <summary>
    /// The natural objects within <paramref name="radius"/> metres of a point, nearest first.
    /// </summary>
    /// <param name="center">The middle of the circle (its y is not used).</param>
    /// <param name="radius">How far round it to look, over 0 and at most <see cref="MaximumRadius"/> metres.</param>
    /// <exception cref="ArgumentOutOfRangeException">The radius is out of range or not a number.</exception>
    public static IReadOnlyList<NaturalObject> Around(Vector3 center, float radius)
    {
        if (!(radius > 0 && radius <= MaximumRadius)) throw new ArgumentOutOfRangeException(nameof(radius), radius, $"A radius is over 0 and at most {MaximumRadius} m.");
        if (Landscape.LiveSource() is not { } source) return [];
        return Found(source, center.x, center.z, radius, null);
    }

    /// <summary>
    /// The natural objects that reach into a circle, nearest first: those whose place lies in it, and those whose footprint
    /// on the ground does though their place lies outside — a large boulder or a cliff of the shore, whose place can stand
    /// several metres from the part of it a player points at. A tool that takes what is under it asks this.
    /// </summary>
    /// <param name="center">The middle of the circle (its y is not used).</param>
    /// <param name="radius">The circle's radius, over 0 and at most <see cref="MaximumRadius"/> metres.</param>
    /// <exception cref="ArgumentOutOfRangeException">The radius is out of range or not a number.</exception>
    public static IReadOnlyList<NaturalObject> Touching(Vector3 center, float radius)
    {
        if (!(radius > 0 && radius <= MaximumRadius)) throw new ArgumentOutOfRangeException(nameof(radius), radius, $"A radius is over 0 and at most {MaximumRadius} m.");
        if (Landscape.LiveSource() is not { } source) return [];
        return Found(source, center.x, center.z, radius, null, byFootprint: true);
    }

    /// <summary>The nearest natural object within <paramref name="radius"/> metres of a point; null for none.</summary>
    /// <param name="center">Where to look from (its y is not used).</param>
    /// <param name="radius">How far round it to look, over 0 and at most <see cref="MaximumRadius"/> metres.</param>
    /// <exception cref="ArgumentOutOfRangeException">The radius is out of range or not a number.</exception>
    public static NaturalObject? Nearest(Vector3 center, float radius = 8f)
    {
        var found = Around(center, radius);
        return found.Count > 0 ? found[0] : null;
    }

    /// <summary>The farthest the world's objects can be asked for round a point, in metres.</summary>
    public const float MaximumRadius = 64f;

    /// <summary>The largest scale an object is placed at: four times its kind's own size.</summary>
    public const float MaximumScale = 4f;

    /// <summary>
    /// How many of the world's days a placed tree takes to grow from nothing to its grown size, 32 by default, as long as
    /// the game's own birch sapling grows; 0 stops placed trees growing. A placed tree smaller than its grown size — a
    /// scale between its kind's <see cref="PlaceableKind.MinimumScale"/> and <see cref="PlaceableKind.MaximumScale"/>, the
    /// same for the same place on every machine — grows towards it by the world's clock while it stands (a stump does not),
    /// on the host, while it is near a player; the game saves its scale and tells the other players. A tree placed at its
    /// grown size or larger stays as it is. It holds for every mod's trees, so set it only when the player asks.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Below 0, or not a number.</exception>
    public static float TreeGrowthDays
    {
        get => s_treeGrowthDays;
        set
        {
            if (!(value >= 0) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value), value, "A tree's growth takes 0 days or more.");
            s_treeGrowthDays = value;
        }
    }

    private static volatile float s_treeGrowthDays = 32f;

    /// <summary>
    /// The kinds of the world a mod can place (<see cref="Place"/>), every kind the game grows but its grass. They are made
    /// as the game builds its item database, in the main menu already; empty before that and without the game.
    /// </summary>
    public static IReadOnlyList<PlaceableKind> Kinds
    {
        get
        {
            var made = Landscape.Source?.PlaceableKinds;
            if (made == null || made.Count == 0) return [];
            if (s_kinds is { } kinds && ReferenceEquals(s_from, made) && kinds.Count == made.Count) return kinds;
            var list = new PlaceableKind[made.Count];
            for (int i = 0; i < list.Length; i++) list[i] = new PlaceableKind(made[i]);
            s_kinds = list;
            s_from = made;
            return list;
        }
    }

    private static IReadOnlyList<PlaceableKind>? s_kinds;
    private static object? s_from;

    /// <summary>A kind by its name ("tree_willow") or its <see cref="PlaceableKind.AssetName"/>; null for none.</summary>
    public static PlaceableKind? FindKind(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (var kind in Kinds)
            if (string.Equals(kind.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(kind.AssetName, name, StringComparison.OrdinalIgnoreCase))
                return kind;
        return null;
    }

    /// <summary>
    /// Places an object of a kind of the world (<see cref="Kinds"/>) on the ground at a point of the loaded world: a tree, a
    /// bush, a stone, a boulder of the scenery. It is a world item of the SDK's own that looks as the kind does and is
    /// harvested or gathered as it is: a tree falls to its stump and drops the game's trunk, a bush gives its berries. The
    /// game saves it and tells the other players, who need the same SDK. A tree's scale is its age: the world's own trees
    /// stand between <see cref="PlaceableKind.MinimumScale"/> and <see cref="PlaceableKind.MaximumScale"/>, and a placed tree
    /// smaller than that grows (<see cref="TreeGrowthDays"/>). A bush comes up full; gathered to the end it stays, saved
    /// as it is, and gives again when the world's bushes of its kind do (the world's berry bushes at a season's start).
    /// </summary>
    /// <param name="kind">The kind's name ("tree_willow") or asset name.</param>
    /// <param name="position">Where it stands; its y is the ground's.</param>
    /// <param name="rotation">Its turn round the vertical, in degrees.</param>
    /// <param name="scale">Its size against the kind's own, over 0 and at most <see cref="MaximumScale"/>.</param>
    /// <returns>What was placed; null without a world, off the loaded part, off the main thread, or when the game took none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="kind"/> is null.</exception>
    /// <exception cref="ArgumentException">No such kind.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A scale out of its range, or a point that is not one.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static NaturalObject? Place(string kind, Vector3 position, float rotation = 0f, float scale = 1f)
    {
        ArgumentNullException.ThrowIfNull(kind);
        if (!(scale > 0 && scale <= MaximumScale)) throw new ArgumentOutOfRangeException(nameof(scale), scale, $"A scale is over 0 and at most {MaximumScale}.");
        if (!float.IsFinite(position.x) || !float.IsFinite(position.z) || !float.IsFinite(rotation)) throw new ArgumentOutOfRangeException(nameof(position), "A point is a point of the world.");
        var found = FindKind(kind) ?? throw new ArgumentException($"No kind '{kind}' to place (NaturalObjects.Kinds).", nameof(kind));
        string modId = Landscape.Owner(CallingMod.Of(Assembly.GetCallingAssembly()));
        if (Landscape.LiveSource() is not { } source) return null;
        if (!source.IsLoaded(position.x, position.z) || !source.TryGetHeight(position.x, position.z, out position.y)) return null;
        try
        {
            var placed = source.Place(found.Name, position, rotation, scale);
            return placed != null ? new NaturalObject(placed) : null;
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"{modId}: '{found.Name}' could not be placed: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Marks objects in the world in the game's own see-through red, the red it lays on what keeps a building from its
    /// place (<see cref="ObjectMarks"/>): a tool shows so what it is about to take. Empty until <see cref="ObjectMarks.Set"/>
    /// is given objects; it stays until disposed.
    /// </summary>
    /// <returns>The marks: set their objects, then dispose them to take them away.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ObjectMarks ShowMarks() => ObjectMarks.Add(CallingMod.Of(Assembly.GetCallingAssembly()));

    /// <summary>
    /// Keeps the world's items up round a point as the game keeps them round the character (<see cref="ActivationPoint"/>):
    /// what mods placed, the trees the villagers plant, what lies on the ground. The game brings them up only near the
    /// character, so a tool whose camera looks at the world far from the character sees none of them, and
    /// <see cref="Around"/> finds none of them there, without such a point under its camera.
    /// </summary>
    /// <param name="center">The point; move it with <see cref="ActivationPoint.Center"/>.</param>
    /// <returns>The point: dispose it to let the game forget it.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ActivationPoint KeepActive(Vector3 center) => ActivationPoint.Add(CallingMod.Of(Assembly.GetCallingAssembly()), center);

    /// <summary>
    /// Shows a kind's model standing on the ground at a point (<see cref="KindPreview"/>): the ghost of a tool that places
    /// one object at a time, moved with the cursor, turned and sized as <see cref="Place"/> would place it, red where it may
    /// not go. It stays until disposed. May be called from any thread: it shows from the next frame.
    /// </summary>
    /// <param name="kind">The kind's name ("tree_willow") or asset name.</param>
    /// <param name="position">Where it stands; its y is not used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="kind"/> is null.</exception>
    /// <exception cref="ArgumentException">No such kind.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static KindPreview ShowPreview(string kind, Vector3 position)
    {
        ArgumentNullException.ThrowIfNull(kind);
        var found = FindKind(kind) ?? throw new ArgumentException($"No kind '{kind}' (NaturalObjects.Kinds).", nameof(kind));
        return KindPreview.Add(CallingMod.Of(Assembly.GetCallingAssembly()), found.Name, position);
    }

    /// <summary>The largest side of a kind's picture, in pixels.</summary>
    public const int MaximumPictureSize = 1024;

    /// <summary>
    /// A picture of a kind (<see cref="Kinds"/>) for a palette or a list: its model in three quarters view on the build menu's
    /// dark, lit by lights of its own so the time of day hardly shows. Taken the first time it is asked for (a few
    /// milliseconds), then kept for the game's run; the same texture comes back each time.
    /// </summary>
    /// <param name="kind">The kind's name ("tree_willow") or asset name.</param>
    /// <param name="width">The picture's width in pixels, 16 to <see cref="MaximumPictureSize"/>.</param>
    /// <param name="height">Its height; the build menu's tiles are 4 : 3.</param>
    /// <returns>The picture; null before the game runs, off the main thread, or when the kind has no model.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="kind"/> is null.</exception>
    /// <exception cref="ArgumentException">No such kind.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A size out of its range.</exception>
    public static Texture2D? PictureOf(string kind, int width = 256, int height = 192)
    {
        ArgumentNullException.ThrowIfNull(kind);
        if (width < 16 || width > MaximumPictureSize) throw new ArgumentOutOfRangeException(nameof(width), width, $"A side is 16 to {MaximumPictureSize} pixels.");
        if (height < 16 || height > MaximumPictureSize) throw new ArgumentOutOfRangeException(nameof(height), height, $"A side is 16 to {MaximumPictureSize} pixels.");
        var found = FindKind(kind) ?? throw new ArgumentException($"No kind '{kind}' (NaturalObjects.Kinds).", nameof(kind));
        if (!GameLoop.CanUseUnity("NaturalObjects.PictureOf", Landscape.Platform) || Landscape.Source is not { } source) return null;
        try
        {
            return source.KindPicture(found.Name, width, height);
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"The picture of '{found.Name}' could not be taken: {e.Message}");
            return null;
        }
    }

    private static IReadOnlyList<NaturalObject> Found(ITerrainSource source, float x, float z, float radius, Func<float, float, bool>? where,
        bool byFootprint = false)
    {
        try
        {
            var objects = byFootprint ? source.ObjectsTouching(x, z, radius) : source.ObjectsIn(x, z, radius);
            var list = new List<(NaturalObject Object, float Distance)>(objects.Count);
            foreach (var found in objects)
            {
                if (found.IsGone) continue;
                var at = found.Position;
                float dx = at.x - x, dz = at.z - z;
                float distance = dx * dx + dz * dz;
                if (!byFootprint && distance > radius * radius) continue;
                if (where != null && !where(at.x, at.z)) continue;
                list.Add((new NaturalObject(found), distance));
            }
            list.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            var result = new NaturalObject[list.Count];
            for (int i = 0; i < list.Count; i++) result[i] = list[i].Object;
            return result;
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"The world's objects round ({x:0.#}, {z:0.#}) could not be read: {e.Message}");
            return [];
        }
    }
}

/// <summary>
/// One natural object of the world (<see cref="NaturalObjects.In"/>): a tree, a stone, a bush, a nest. It stands for the
/// game's own object, so what it tells changes as the game changes it, and it is <see cref="IsGone"/> once the object is
/// taken. Read and change it on the main thread.
/// </summary>
public sealed class NaturalObject
{
    private readonly IWorldObject _object;

    internal NaturalObject(IWorldObject found)
    {
        _object = found;
        AssetName = Read(() => found.AssetName, "");
        Kind = Read(() => found.Kind, NaturalObjectKind.Other);
        ResourceName = Read(() => found.ResourceName, "");
        IsRemovable = Read(() => found.IsRemovable, false);
        IsHarvestable = Read(() => found.IsHarvestable, false);
        IsClearable = Read(() => found.IsClearable, false);
    }

    /// <summary>The game's name of what it is ("Harvest_Wood_birch1", "Harvest_Stone_BiomeIronDeposit").</summary>
    public string AssetName { get; }

    /// <summary>What kind of thing it is.</summary>
    public NaturalObjectKind Kind { get; }

    /// <summary>The game's name of the item gathering it gives ("Item_Wood_birch1"), or "" where it gives none.</summary>
    public string ResourceName { get; }

    /// <summary>Whether a change of the ground takes it: a bush or a small stone does, a tree or a boulder does not.</summary>
    public bool IsRemovable { get; }

    /// <summary>Whether the player can gather or fell it.</summary>
    public bool IsHarvestable { get; }

    /// <summary>Whether <see cref="Clear"/> takes it: a tree and a boulder too, as the game's own level allows.</summary>
    public bool IsClearable { get; }

    /// <summary>
    /// How many items are left to gather out of it by hand (a bush's fibres, a spruce's sticks); 0 for a thing the player
    /// does not gather that way, a tree among them.
    /// </summary>
    public int GatherCount => Read(() => _object.GatherCount, 0);

    /// <summary>Where it stands now; (0, 0, 0) once it is gone or off the main thread.</summary>
    public Vector3 Position => Read(() => _object.Position, default);

    /// <summary>What a mark draws of it: its kind and where it stands, turned and sized; null once it is gone.</summary>
    internal MarkedObject? Placement() =>
        IsGone ? null : Read<MarkedObject?>(() => new MarkedObject(AssetName, _object.Position, _object.Rotation, _object.Scale), null);

    /// <summary>What is left of it, as the game counts a tree's or a deposit's; 0 for a thing with no health of its own.</summary>
    public float Health => Read(() => _object.Health, 0f);

    /// <summary>How much of its resource is left (a bush's berries); 0 for a thing that gives none.</summary>
    public int Quantity => Read(() => _object.Quantity, 0);

    /// <summary>
    /// Whether it is no longer in the world: taken by this mod, by a change of the ground or by the game. Ask for the
    /// objects again rather than keeping one for long: the game gives a taken object's place to another.
    /// </summary>
    public bool IsGone => Read(() => _object.IsGone, true);

    /// <summary>How far above (or below) the ground under it its place lies, in metres; NaN where that is not known.</summary>
    public float HeightOverGround
    {
        get
        {
            var at = Position;
            return Landscape.TryGetHeight(at.x, at.z, out float ground) ? at.y - ground : float.NaN;
        }
    }

    /// <summary>
    /// Gathers from it as a player picking by hand does: the items of each charge fall on the ground beside it, its
    /// quantity goes down, and the game tells the other players. A tree, which is felled with a tool instead, gives
    /// nothing (<see cref="GatherCount"/> is 0 for it).
    /// </summary>
    /// <param name="charges">How many of the game's charges to take, at least 1; one charge may give several items, and
    /// more than it has takes what is left.</param>
    /// <returns>How many items it gave; 0 off the main thread, once it is gone, or when it is not gathered by hand.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="charges"/> is below 1.</exception>
    public int Gather(int charges = 1)
    {
        if (charges < 1) throw new ArgumentOutOfRangeException(nameof(charges), charges, "A charge count is at least 1.");
        if (!Landscape.IsMainThread || IsGone) return 0;
        try
        {
            return _object.Gather(charges);
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"'{AssetName}' could not be gathered: {e.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Takes it out of the world as a change of the ground does, with everything the game does for it (the other players'
    /// games told by the game itself). A tree or a boulder, which the game never takes that way, stays
    /// (<see cref="IsRemovable"/>); <see cref="Clear"/> takes it.
    /// </summary>
    /// <returns>Whether it is gone now.</returns>
    public bool Remove()
    {
        if (!Landscape.IsMainThread) return false;
        try
        {
            return _object.Remove();
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"'{AssetName}' could not be taken out of the world: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Harvests it as a woodcutter or a miner of the game does, blow after blow until nothing is left: a tree falls and
    /// leaves its trunk and its stump's firewood, the trunk is chopped into logs, sticks and bark
    /// (<see cref="HarvestOptions.ChopTrunk"/>), a stump or a trunk on its own gives its own. The loot is the game's,
    /// dropped beside it, and the game tells the other players. No tool is needed. By default nobody strikes, so no one
    /// gains experience, is injured or has the harvest counted; <see cref="HarvestOptions.AsPlayer"/> strikes as the local
    /// player. Only an object the game has brought up round the player can be harvested (a tree 160 m away was).
    /// </summary>
    /// <param name="options">Who strikes and what the game gives them; null for <see cref="HarvestOptions.Default"/>.</param>
    /// <returns>
    /// Whether it is used up now; false off the main thread, beyond what the game has brought up, for a thing picked by hand
    /// (<see cref="Gather"/>) or one the game does not harvest at all.
    /// </returns>
    public bool Harvest(HarvestOptions? options = null)
    {
        if (!Landscape.IsMainThread || IsGone) return false;
        try
        {
            return _object.Harvest(options ?? HarvestOptions.Default);
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"'{AssetName}' could not be harvested: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Takes it out of the world whatever it is — a tree or a boulder too — at the game's own level, as the game takes what
    /// it clears itself: nothing is dropped (no wood, no stone), and the game tells the other players. It does not grow
    /// back: the save keeps it gone.
    /// </summary>
    /// <returns>Whether it is gone now.</returns>
    public bool Clear()
    {
        if (!Landscape.IsMainThread) return false;
        try
        {
            return _object.Clear();
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"'{AssetName}' could not be cleared: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Puts it on the ground under it, so a tree left standing in the air (or sunk) after a change of the ground stands on
    /// it again. Its place and its look move; the other players' games are not told (their own will show it where it stood
    /// until they load that part of the world again).
    /// </summary>
    /// <returns>Whether it was moved.</returns>
    public bool Settle()
    {
        if (!Landscape.IsMainThread) return false;
        var at = Position;
        if (!Landscape.TryGetHeight(at.x, at.z, out float ground)) return false;
        if (Math.Abs(at.y - ground) <= 0.001f) return true;
        at.y = ground;
        return MoveTo(at);
    }

    /// <summary>
    /// Moves it to a place in the world, its look with it. As with <see cref="Settle"/>, the other players' games are not
    /// told. Use it to stand an object on the ground elsewhere; the game's own checks of where a thing may stand are not made.
    /// </summary>
    /// <param name="position">Where it goes.</param>
    /// <returns>Whether it was moved.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The place is not a point of the world.</exception>
    public bool MoveTo(Vector3 position)
    {
        if (!float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z))
            throw new ArgumentOutOfRangeException(nameof(position), "A place is a point of the world.");
        if (!Landscape.IsMainThread) return false;
        try
        {
            return _object.MoveTo(position);
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"'{AssetName}' could not be moved: {e.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        var at = Position;
        return FormattableString.Invariant($"{(AssetName.Length > 0 ? AssetName : "object")} ({Kind}) at ({at.x:0.#}, {at.z:0.#})");
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
