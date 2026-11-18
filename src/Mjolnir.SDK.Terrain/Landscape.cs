using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mjolnir.SDK.Threading;
using UnityEngine;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// The ground of the world: its height, its kind and the water over it, and changes to it made with the game's own tool, as
/// the player's hoe, a villager's shovel or a building's footprint makes them. So a change is saved with the world, sent to
/// the other players, clears the grass and lets the villagers walk the new ground, as the game's own changes do; it stays in
/// the save without the mod.
/// </summary>
/// <remarks>
/// <para>Only the part of the world loaded around the player (512 metres across) can be read or changed; elsewhere the
/// reads give nothing and the changes <see cref="LandscapeResult.NotLoaded"/>.</para>
/// <para>In a multiplayer game every player's game sends its own changes to the others, and nobody's game checks them: make a
/// change on one machine only (the host's, by default), or every machine running the mod makes it once more.</para>
/// <para>Read and change it on the main thread. From another thread the reads give nothing (the water level, what the main
/// thread saw last) and the changes are made at the start of the next frame (<see cref="LandscapeResult.Queued"/>).</para>
/// <para>A rectangle's or a brush's raise, lower, level, paint or clearing is applied by the game later in the frame: a change
/// over the same ground in that frame through any other area, option or method reads the ground as it was before, and
/// writes that back. Make the next one a frame later (in the next <c>OnUpdate</c>, or posted with
/// <c>Dispatcher.MainThread.Post</c>).</para>
/// </remarks>
/// <example><code>
/// // A flat yard of 12 × 8 metres at the height of its middle, with a road across it.
/// var yard = LandscapeArea.Rectangle(position, 12, 8, rotation: 30, blend: 2);
/// if (Landscape.Level(yard, Landscape.HeightAt(position.x, position.z)) == LandscapeResult.Done)
///     Landscape.Paint(LandscapeArea.Rectangle(position, 3, 8, rotation: 30, blend: 0.5f), GroundKind.Road);
/// </code></example>
public static partial class Landscape
{
    /// <summary>The most a change raises or lowers the ground at once, in metres.</summary>
    public const float MaximumStep = 10f;

    /// <summary>The highest ground of the world, in metres (the lowest is 0).</summary>
    public const float MaximumHeight = 160f;

    private static ModHandlers<EventHandler<LandscapeChangingEventArgs>>? s_changing;
    private static ModHandlers<EventHandler<LandscapeChangedEventArgs>>? s_changed;
    private static float s_waterLevel;

    // The mod whose change the game's tool is making now, and whether a handler cancelled it (main thread).
    [ThreadStatic] private static string? s_editingMod;
    [ThreadStatic] private static bool s_editing;
    [ThreadStatic] private static bool s_cancelled;

    /// <summary>The game's ground, once the module started (a fake in the unit tests).</summary>
    internal static ITerrainSource? Source { get; set; }

    /// <summary>Whether a world is loaded. False in the main menu.</summary>
    public static bool IsAvailable => Live() != null;

    /// <summary>Whether the point is in the part of the world loaded around the player, so its ground can be read and changed.</summary>
    public static bool IsLoaded(float x, float z) => Live() is { } source && source.IsLoaded(x, z);

    /// <summary>The height of the ground at a point, in metres; NaN where it is not loaded (or off the main thread).</summary>
    public static float HeightAt(float x, float z) => TryGetHeight(x, z, out float height) ? height : float.NaN;

    /// <summary>The height of the ground at a point, in metres.</summary>
    /// <returns>Whether it is known: the point is loaded and asked on the main thread.</returns>
    public static bool TryGetHeight(float x, float z, out float height)
    {
        height = float.NaN;
        return Live() is { } source && source.IsLoaded(x, z) && source.TryGetHeight(x, z, out height);
    }

    /// <summary>Which way the ground faces at a point (length 1); (0, 0, 0) where it is not known.</summary>
    public static Vector3 NormalAt(float x, float z) => Live() is { } source && source.IsLoaded(x, z) ? source.Normal(x, z) : default;

    /// <summary>What the ground is at a point; <see cref="GroundKind.Natural"/> where it is not known.</summary>
    public static GroundKind KindAt(float x, float z) => Live() is { } source && source.IsLoaded(x, z) ? source.Kind(x, z) : GroundKind.Natural;

    /// <summary>Whether the grass and bushes at a point were cleared: by any change of the ground there, the game's or a mod's.</summary>
    public static bool IsCleared(float x, float z) => Live() is { } source && source.IsLoaded(x, z) && source.IsCleared(x, z);

    /// <summary>The height of the sea, in metres (the world has one); what the main thread saw last elsewhere.</summary>
    public static float WaterLevel
    {
        get
        {
            if (Live() is { } source) s_waterLevel = source.WaterLevel;
            return s_waterLevel;
        }
    }

    /// <summary>Whether the ground at a point lies under the water.</summary>
    public static bool IsUnderwater(float x, float z) => Live() is { } source && source.IsLoaded(x, z) && source.IsUnderwater(x, z);

    /// <summary>
    /// Raised on this machine before the ground changes: by a player's tool, a villager, a building, a mod or the game. A
    /// handler may cancel it. Changes that come over the network from another player are not raised here (their game made
    /// them); <see cref="GroundChanged"/> reports them. Road plans' levelling is reported only by <see cref="GroundChanged"/>.
    /// </summary>
    public static event EventHandler<LandscapeChangingEventArgs>? GroundChanging
    {
        add => ModHandlers<EventHandler<LandscapeChangingEventArgs>>.Of(ref s_changing, "Landscape.GroundChanging").Add(value);
        remove => s_changing?.Remove(value);
    }

    /// <summary>Raised once the ground changed, here or on another player's machine, as its new heights show.</summary>
    public static event EventHandler<LandscapeChangedEventArgs>? GroundChanged
    {
        add => ModHandlers<EventHandler<LandscapeChangedEventArgs>>.Of(ref s_changed, "Landscape.GroundChanged").Add(value);
        remove => s_changed?.Remove(value);
    }

    /// <summary>Raises the ground in the area by <paramref name="height"/> metres (blended at its edge).</summary>
    /// <param name="area">Where.</param>
    /// <param name="height">How much, over 0 and at most <see cref="MaximumStep"/>.</param>
    /// <param name="options">How; the game's own way by default.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="height"/> is out of range.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LandscapeResult Raise(LandscapeArea area, float height, LandscapeOptions? options = null)
    {
        CheckStep(height, nameof(height));
        return Basic(Owner(CallingMod.Of(Assembly.GetCallingAssembly())), new LandscapeEdit(area, LandscapeOperation.Raise, height, null, options ?? LandscapeOptions.Default));
    }

    /// <summary>Lowers (digs) the ground in the area by <paramref name="depth"/> metres (blended at its edge).</summary>
    /// <param name="area">Where.</param>
    /// <param name="depth">How much, over 0 and at most <see cref="MaximumStep"/>.</param>
    /// <param name="options">How; the game's own way by default.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="depth"/> is out of range.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LandscapeResult Lower(LandscapeArea area, float depth, LandscapeOptions? options = null)
    {
        CheckStep(depth, nameof(depth));
        return Basic(Owner(CallingMod.Of(Assembly.GetCallingAssembly())), new LandscapeEdit(area, LandscapeOperation.Lower, depth, null, options ?? LandscapeOptions.Default));
    }

    /// <summary>Brings the ground in the area to <paramref name="height"/>, the world's height in metres, its edge blending into the ground around.</summary>
    /// <param name="area">Where.</param>
    /// <param name="height">The height, from 0 to <see cref="MaximumHeight"/>.</param>
    /// <param name="options">How; the game's own way by default.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="height"/> is out of range.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LandscapeResult Level(LandscapeArea area, float height, LandscapeOptions? options = null)
    {
        if (!(height >= 0 && height <= MaximumHeight)) throw new ArgumentOutOfRangeException(nameof(height), height, $"A height is from 0 to {MaximumHeight} m.");
        return Basic(Owner(CallingMod.Of(Assembly.GetCallingAssembly())), new LandscapeEdit(area, LandscapeOperation.Level, height, null, options ?? LandscapeOptions.Default));
    }

    /// <summary>
    /// Makes the ground in the area <paramref name="kind"/>, its height left as it is: a road, a path, dirt, or
    /// <see cref="GroundKind.Natural"/> to give it back its own look.
    /// </summary>
    /// <param name="area">Where.</param>
    /// <param name="kind">What it becomes.</param>
    /// <param name="options">How; <see cref="LandscapeOptions.Kind"/> is not used.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is none of the kinds.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LandscapeResult Paint(LandscapeArea area, GroundKind kind, LandscapeOptions? options = null)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind), kind, "No such kind of ground.");
        return Basic(Owner(CallingMod.Of(Assembly.GetCallingAssembly())), new LandscapeEdit(area, LandscapeOperation.Paint, 0, kind, options ?? PaintDefault));
    }

    /// <summary>Clears the grass, bushes and saplings in the area, as the game's rake does; the ground's height and kind stay.</summary>
    /// <param name="area">Where.</param>
    /// <param name="options">How; only <see cref="LandscapeOptions.KeepOffBuildings"/> counts.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LandscapeResult ClearVegetation(LandscapeArea area, LandscapeOptions? options = null) =>
        Basic(Owner(CallingMod.Of(Assembly.GetCallingAssembly())), new LandscapeEdit(area, LandscapeOperation.ClearVegetation, 0, null, options ?? LandscapeOptions.Default));

    /// <summary>
    /// Whether a change could be made in the area now, without making it: the world, the loaded ground, the buildings (with
    /// the default <see cref="LandscapeOptions.KeepOffBuildings"/>) and, for a change the game's tool makes (one of the five
    /// basic changes over a rectangle or a brush), the game's own check at the area's middle.
    /// </summary>
    /// <returns><see cref="LandscapeResult.Done"/> when it could; <see cref="LandscapeResult.Blocked"/> also where another
    /// mod's protected zone keeps the ground; <see cref="LandscapeResult.NotAvailable"/> off the main thread.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LandscapeResult Check(LandscapeArea area, LandscapeOperation operation)
    {
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation), operation, "No such change.");
        string modId = Owner(CallingMod.Of(Assembly.GetCallingAssembly()));
        var edit = new LandscapeEdit(area, operation, operation == LandscapeOperation.Level ? area.Center.y : 0.5f, null, LandscapeOptions.Default);
        if (Live() is not { } source) return LandscapeResult.NotAvailable;
        if (Refused(source, area, edit.Options, out _) is { } refused) return refused;
        if (LandscapeZone.Refusing(area.Bounds, modId) != null) return LandscapeResult.Blocked;
        return area.IsGameShape && operation <= LandscapeOperation.ClearVegetation ? Guarded(() => source.Check(edit)) : LandscapeResult.Done;
    }

    // A change of kind keeps the vegetation unless asked: painting a road does not mow the meadow beside it.
    private static readonly LandscapeOptions PaintDefault = new() { ClearVegetation = false };

    // The five basic changes: a rectangle or a brush through the game's own tool, any other area written by the SDK, and so
    // are those the game's tool cannot make as asked: limited to one kind of ground, or keeping or settling the objects it
    // would take itself.
    private static LandscapeResult Basic(string modId, LandscapeEdit edit)
    {
        if (!edit.Area.IsGameShape || edit.Options.OnlyOn != null || edit.Options.Objects != LandscapeObjects.Remove) return WriteBasic(modId, edit);
        if (!Dispatcher.IsMainThread)
        {
            Dispatcher.Run(() => Basic(modId, edit));
            return LandscapeResult.Queued;
        }
        if (Live() is not { } source) return LandscapeResult.NotAvailable;
        if (Refused(source, edit.Area, edit.Options, out string? building) is { } refused)
        {
            if (building != null) SdkLog.Info(Platform, $"{edit.Operation} over {edit.Area} refused: '{building}' stands there");
            return refused;
        }
        if (Zoned(modId, edit.Area, edit.Operation)) return LandscapeResult.Blocked;
        s_editingMod = modId;
        s_editing = true;
        s_cancelled = false;
        try
        {
            var result = Guarded(() => source.Apply(edit));
            return s_cancelled ? LandscapeResult.Cancelled : result;
        }
        finally
        {
            s_editing = false;
            s_editingMod = null;
        }
    }

    private static LandscapeResult? Refused(ITerrainSource source, LandscapeArea area, LandscapeOptions options, out string? building)
    {
        building = null;
        var bounds = area.Bounds;
        float x0 = bounds.m_XMin, z0 = bounds.m_YMin, x1 = x0 + bounds.m_Width, z1 = z0 + bounds.m_Height;
        if (!source.IsLoaded(x0, z0) || !source.IsLoaded(x1, z0) || !source.IsLoaded(x0, z1) || !source.IsLoaded(x1, z1))
            return LandscapeResult.NotLoaded;
        if (options.KeepOffBuildings && (building = source.BuildingIn(area)) != null) return LandscapeResult.Blocked;
        return null;
    }

    private static LandscapeResult Guarded(Func<LandscapeResult> run)
    {
        try
        {
            return run();
        }
        catch (Exception e)
        {
            SdkLog.Warn(Platform, $"A change to the ground failed: {e.Message}");
            return LandscapeResult.Failed;
        }
    }

    private static void CheckStep(float amount, string name)
    {
        if (!(amount > 0 && amount <= MaximumStep)) throw new ArgumentOutOfRangeException(name, amount, $"A step is over 0 and at most {MaximumStep} m.");
    }

    /// <summary>The id the module knows a mod's (or the platform's) changes by.</summary>
    internal static string Owner(Assembly mod) => DevConsole.OwnerOf(mod);

    /// <summary>
    /// Runs <paramref name="run"/> as <paramref name="modId"/>'s change on this thread: the game's tools it sets going (a
    /// field's own, which a mod levels) report that mod, so its own protected zones let them through.
    /// </summary>
    internal static T Editing<T>(string modId, Func<T> run)
    {
        string? mod = s_editingMod;
        bool editing = s_editing, cancelled = s_cancelled;
        s_editingMod = modId;
        s_editing = true;
        s_cancelled = false;
        try
        {
            return run();
        }
        finally
        {
            s_editingMod = mod;
            s_editing = editing;
            s_cancelled = cancelled;
        }
    }

    /// <summary>The source for the module's log lines.</summary>
    internal const string Platform = "SDK.Terrain";

    /// <summary>The mod whose change the game's tool is making now on this thread, or null when the game makes one.</summary>
    internal static string? EditingMod => s_editing ? s_editingMod : null;

    /// <summary>Whether a handler cancelled the mod's change being made now (the game then answers a refused place).</summary>
    internal static bool WasCancelled => s_editing && s_cancelled;

    /// <summary>Whether the change being made now is a mod's, through <see cref="Landscape"/>.</summary>
    internal static bool IsEditing => s_editing;

    /// <summary>
    /// Raises <see cref="GroundChanging"/> for a change the game's tool is about to make (the game's patch; the fake in tests),
    /// once the protected zones let it: a change a zone refuses is cancelled before any handler sees it.
    /// </summary>
    /// <returns>Whether a zone or a handler cancelled it.</returns>
    internal static bool RaiseChanging(Rect area, LandscapeOperation operation, LandscapeSource source)
    {
        var handlers = s_changing is { Count: > 0 } listening ? listening : null;
        bool zones = LandscapeZone.Active.Count > 0;
        if (handlers == null && !zones) return false;
        var args = new LandscapeChangingEventArgs(area, operation, source, EditingMod);
        if (!(zones && LandscapeZone.Refuse(args))) handlers?.Raise(h => h(null, args));
        if (args.Cancel && s_editing) s_cancelled = true;
        return args.Cancel;
    }

    /// <summary>
    /// Whether a protected zone of another mod refuses a mod's change over <paramref name="area"/> before it is made: then
    /// the zone raises its refusal and the change is <see cref="LandscapeResult.Blocked"/> (logged).
    /// </summary>
    internal static bool Zoned(string modId, LandscapeArea area, LandscapeOperation operation)
    {
        if (LandscapeZone.Active.Count == 0) return false;
        var args = new LandscapeChangingEventArgs(area.Bounds, operation, LandscapeSource.Mod, modId);
        if (!LandscapeZone.Refuse(args)) return false;
        SdkLog.Info(Platform, $"{operation} over {area} by {modId} refused: a protected zone keeps the ground");
        return true;
    }

    /// <summary>Whether anyone listens to <see cref="GroundChanged"/>: the game's side keeps nothing for it otherwise.</summary>
    internal static bool IsChangedWatched => s_changed is { Count: > 0 };

    /// <summary>Raises <see cref="GroundChanged"/> for a change the game applied.</summary>
    internal static void RaiseChanged(Rect area, LandscapeOperation? operation, LandscapeSource source, string? modId)
    {
        if (s_changed is not { Count: > 0 } handlers) return;
        var args = new LandscapeChangedEventArgs(area, operation, source, modId);
        handlers.Raise(h => h(null, args));
    }

    private static ITerrainSource? Live() => Dispatcher.IsMainThread && Source is { IsAvailable: true } source ? source : null;

    /// <summary>The game's ground while a world is loaded and this is the main thread (the world's objects ask too).</summary>
    internal static ITerrainSource? LiveSource() => Live();

    /// <summary>Whether this is the game's main thread, where the ground and its objects may be read and changed.</summary>
    internal static bool IsMainThread => Dispatcher.IsMainThread;

    /// <summary>Forgets the handlers and what was seen, between unit tests.</summary>
    internal static void ResetForTests()
    {
        TerrainCommands.ResetForTests();
        LandscapeOutline.ResetForTests();
        LandscapeZone.ResetForTests();
        Source = null;
        s_changing = null;
        s_changed = null;
        s_waterLevel = 0;
        s_editing = false;
        s_editingMod = null;
        s_cancelled = false;
    }
}
