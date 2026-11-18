using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// The game's road plans: a road laid along points, which the villagers supply, then level and pave section by section,
/// as a player's own plan from the road maker; or the game's levelling plan along a line, which levels a strip of dirt. A mod lays one with <see cref="Place"/> and may build and level it
/// at once (<see cref="RoadPlan.LevelNow"/>). Only the part of the world loaded around the player has them, and only the
/// main thread may read or change them.
/// </summary>
/// <example><code>
/// // A road from the gate to the well, paved at once.
/// if (RoadPlans.Place([gate, bend, well]) is { } road)
///     road.LevelNow();
/// </code></example>
public static class RoadPlans
{
    /// <summary>The most points a plan has: what the game keeps of a plan for the other players.</summary>
    public const int MaximumPoints = 8;

    /// <summary>The most sections a plan has, each <see cref="SectionLength"/> metres of its line.</summary>
    public const int MaximumSections = 96;

    /// <summary>The metres of line each section of a plan covers, as the game cuts it.</summary>
    public const float SectionLength = 2f;

    /// <summary>The narrowest strip a plan of a mod's width levels (<see cref="RoadPlan.Width"/>), in metres.</summary>
    public const float MinimumWidth = 1f;

    /// <summary>The widest strip a plan of a mod's width levels (<see cref="RoadPlan.Width"/>), in metres.</summary>
    public const float MaximumWidth = 16f;

    /// <summary>The road plans of the loaded world.</summary>
    public static IReadOnlyList<RoadPlan> All
    {
        get
        {
            if (Landscape.LiveSource() is not { } source) return [];
            try
            {
                var plans = source.RoadPlans;
                var list = new RoadPlan[plans.Count];
                for (int i = 0; i < plans.Count; i++) list[i] = new RoadPlan(plans[i]);
                return list;
            }
            catch (Exception e)
            {
                SdkLog.Warn(Landscape.Platform, $"The road plans could not be read: {e.Message}");
                return [];
            }
        }
    }

    /// <summary>The road plan with a point nearest <paramref name="center"/>, within <paramref name="radius"/> metres; null for none.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The radius is out of range or not a number.</exception>
    public static RoadPlan? Nearest(Vector3 center, float radius = 32f)
    {
        if (!(radius > 0 && radius < 10000f)) throw new ArgumentOutOfRangeException(nameof(radius), radius, "A radius is over 0 m.");
        RoadPlan? best = null;
        float nearest = radius * radius;
        foreach (var plan in All)
            foreach (var at in plan.Points)
            {
                float dx = at.x - center.x, dz = at.z - center.z, distance = dx * dx + dz * dz;
                if (distance > nearest) continue;
                nearest = distance;
                best = plan;
            }
        return best;
    }

    /// <summary>
    /// Lays a road plan along <paramref name="points"/>, as a player sets one down with the road maker: the villagers then
    /// bring what it needs, level its sections and pave them, and the game saves it and tells the other players. The line
    /// runs smoothly through the points on the ground; the game's own checks of the road maker (its turns, its slope, the
    /// water) are not made.
    /// </summary>
    /// <param name="points">Where it runs: 2 to <see cref="MaximumPoints"/> points of the loaded world (their y is not
    /// used), together at most <see cref="MaximumSections"/> × <see cref="SectionLength"/> metres long.</param>
    /// <param name="kind">What it lays: <see cref="GroundKind.Road"/> (the default), or <see cref="GroundKind.Dirt"/> for the
    /// game's levelling plan along a line, which levels the strip and leaves it bare earth.</param>
    /// <param name="width">How wide a strip its sections level, from <see cref="MinimumWidth"/> to
    /// <see cref="MaximumWidth"/> metres (<see cref="RoadPlan.Width"/>); 0 (the default) for the game's own.</param>
    /// <returns>The plan, or null where the world will not take one (logged).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Too few or too many points, one not a point of the world, two at one
    /// place, a line too long, a kind that is neither a road nor dirt, or a width out of its range.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static RoadPlan? Place(IEnumerable<Vector3> points, GroundKind kind = GroundKind.Road, float width = 0f)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (width != 0f) RoadPlan.CheckWidth(width, nameof(width));
        var list = points.ToArray();
        if (list.Length < 2 || list.Length > MaximumPoints) throw new ArgumentOutOfRangeException(nameof(points), list.Length, $"A road plan has 2 to {MaximumPoints} points.");
        if (kind is not (GroundKind.Road or GroundKind.Dirt)) throw new ArgumentOutOfRangeException(nameof(kind), kind, "A road plan lays a road, or levels a strip of dirt.");
        float length = 0;
        for (int i = 0; i < list.Length; i++)
        {
            if (!float.IsFinite(list[i].x) || !float.IsFinite(list[i].z)) throw new ArgumentOutOfRangeException(nameof(points), "A point is a point of the world.");
            if (i == 0) continue;
            float dx = list[i].x - list[i - 1].x, dz = list[i].z - list[i - 1].z, step = MathF.Sqrt(dx * dx + dz * dz);
            if (step < SectionLength / 2) throw new ArgumentOutOfRangeException(nameof(points), "Two points of a road plan stand at one place.");
            length += step;
        }
        if (length > MaximumSections * SectionLength) throw new ArgumentOutOfRangeException(nameof(points), length, $"A road plan is at most {MaximumSections * SectionLength} m long.");
        string modId = Landscape.Owner(CallingMod.Of(Assembly.GetCallingAssembly()));
        if (Landscape.LiveSource() is not { } source) return null;
        for (int i = 0; i < list.Length; i++)
        {
            if (!source.IsLoaded(list[i].x, list[i].z)) return null;
            source.TryGetHeight(list[i].x, list[i].z, out list[i].y);
        }
        try
        {
            var placed = source.PlaceRoadPlan(list, kind);
            if (placed == null) return null;
            if (width != 0f) placed.Width = width;
            SdkLog.Info(Landscape.Platform, FormattableString.Invariant($"{modId} laid a {kind.ToString().ToLowerInvariant()} plan of {list.Length} points, {length:0.#} m"));
            return new RoadPlan(placed);
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"A road plan could not be laid: {e.Message}");
            return null;
        }
    }
}

/// <summary>
/// One of the game's road plans (<see cref="RoadPlans"/>): a line of sections the villagers level and pave. It stands for
/// the game's own plan, so what it tells follows the game, and it is <see cref="IsGone"/> once the road is finished or the
/// plan taken away.
/// </summary>
public sealed class RoadPlan
{
    private readonly IRoadPlanView _plan;

    internal RoadPlan(IRoadPlanView plan) => _plan = plan;

    /// <summary>The points it runs through, on the ground.</summary>
    public IReadOnlyList<Vector3> Points => Read(() => _plan.Points, []);

    /// <summary>What it lays: <see cref="GroundKind.Road"/>, or <see cref="GroundKind.Dirt"/> for a levelling plan.</summary>
    public GroundKind Kind => Read(() => _plan.Kind, GroundKind.Road);

    /// <summary>
    /// How wide a strip each of its sections levels, in metres: the game's road levels 3.5 m and paves about a metre less
    /// in its middle. Set, it is the plan's width on this machine, whoever levels a section here — <see cref="LevelNow"/> or
    /// the villagers — from <see cref="RoadPlans.MinimumWidth"/> to <see cref="RoadPlans.MaximumWidth"/>. The game keeps
    /// no width of its own for a plan, so it is not saved and not sent: once the world is loaded again, or on another
    /// player's machine, the plan levels the game's width until it is set again; the ground already paved stays as it is.
    /// Set off the main thread, it is applied on the main thread's next frame; 0 is read there and once it is gone.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A width out of its range.</exception>
    public float Width
    {
        get => Read(() => _plan.Width, 0f);
        set
        {
            CheckWidth(value, nameof(value));
            if (Landscape.IsMainThread) ApplyWidth(value);
            else Mjolnir.SDK.Threading.Dispatcher.MainThread.Post(() => ApplyWidth(value));
        }
    }

    internal static void CheckWidth(float width, string name)
    {
        if (!(width >= RoadPlans.MinimumWidth && width <= RoadPlans.MaximumWidth))
            throw new ArgumentOutOfRangeException(name, width, FormattableString.Invariant($"A road plan's width is from {RoadPlans.MinimumWidth} to {RoadPlans.MaximumWidth} m."));
    }

    private void ApplyWidth(float width)
    {
        try
        {
            if (!_plan.IsGone) _plan.Width = width;
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"The {this} could not be made {width} m wide: {e.Message}");
        }
    }

    /// <summary>How many sections it has, each <see cref="RoadPlans.SectionLength"/> metres of its line.</summary>
    public int Sections => Read(() => _plan.Sections, 0);

    /// <summary>How many sections are levelled and paved already; 0 until it is built.</summary>
    public int LevelledSections => Read(() => _plan.IsBuilt ? _plan.LevelledSections : 0, 0);

    /// <summary>
    /// Whether the villagers have brought what it needs and built it, so its sections can be levelled (or a mod built it,
    /// <see cref="Build"/>).
    /// </summary>
    public bool IsBuilt => Read(() => _plan.IsBuilt, false);

    /// <summary>Whether every section is levelled and paved.</summary>
    public bool IsLevelled => IsBuilt && Sections > 0 && LevelledSections >= Sections;

    /// <summary>Whether the plan is no longer in the world: the road finished, the plan taken away, or out of the loaded part.</summary>
    public bool IsGone => Read(() => _plan.IsGone, true);

    /// <summary>
    /// Builds it at once, as if the villagers had brought what it needs: its sections are ready to be levelled, by the
    /// villagers or by <see cref="LevelNow"/>.
    /// </summary>
    /// <returns>Whether it is built now.</returns>
    public bool Build() => Act(() => _plan.Build(), "built");

    /// <summary>
    /// Builds it if it is not built yet and levels and paves every section that is not done, as the villagers do: the
    /// ground is changed by the game, saved and sent to the other players. Once the last section is done the game finishes
    /// the road itself, so the plan is <see cref="IsGone"/> right after. A section another mod's protected zone keeps is
    /// left as it is (<see cref="Landscape.Protect"/>); the mod's own zones let it be paved.
    /// </summary>
    /// <returns>How many sections are done now; 0 off the main thread or once the plan is gone.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LevelNow()
    {
        string modId = Landscape.Owner(CallingMod.Of(Assembly.GetCallingAssembly()));
        if (!Landscape.IsMainThread || IsGone) return 0;
        try
        {
            if (!_plan.IsBuilt && !_plan.Build()) return 0;
            return Landscape.Editing(modId, () => _plan.LevelNow(rect => LandscapeZone.Refusing(rect, modId) != null));
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"The {this} could not be levelled: {e.Message}");
            return 0;
        }
    }

    /// <summary>Takes the plan away as a player dismantles it, leaving the ground as it is.</summary>
    /// <returns>Whether it is gone now.</returns>
    public bool Remove() => Act(() => _plan.Remove(), "taken away");

    /// <inheritdoc/>
    public override string ToString()
    {
        var points = Points;
        string from = points.Count > 0 ? FormattableString.Invariant($" from ({points[0].x:0.#}, {points[0].z:0.#})") : "";
        return FormattableString.Invariant($"{Kind.ToString().ToLowerInvariant()} plan of {points.Count} points{from}, {LevelledSections} of {Sections} sections done");
    }

    private bool Act(Func<bool> act, string what)
    {
        if (!Landscape.IsMainThread) return false;
        if (IsGone) return what == "taken away";
        try
        {
            return act();
        }
        catch (Exception e)
        {
            SdkLog.Warn(Landscape.Platform, $"The {this} could not be {what}: {e.Message}");
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
