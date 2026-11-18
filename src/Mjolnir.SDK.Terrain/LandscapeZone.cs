using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using UnityEngine;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// A protected zone of the ground (<see cref="Landscape.Protect"/>): no change of this machine reaches into it — not the
/// player's tools, the villagers', a building's footprint or field, the game's own, nor another mod's — while the mod that
/// made it still changes the ground there. It lives in the mod's memory, not in the save, and lasts until it is disposed
/// or the mod is disabled after an error.
/// </summary>
/// <remarks>
/// <para>A change is refused when the rectangle it touches (its blend included) reaches the zone's own shape: where the
/// zone's area is at least half (its blend's inner half). The game then answers the player's or a villager's tool as it
/// answers a place it will not dig, and a mod's change is <see cref="LandscapeResult.Blocked"/>.</para>
/// <para>Only this machine's changes are seen: another player's game makes and sends its own, and a road plan's levelling
/// is not seen at all. In a multiplayer game every player's game would need the zone.</para>
/// </remarks>
public sealed class LandscapeZone : IDisposable
{
    private static readonly List<LandscapeZone> s_all = [];
    private static readonly object s_gate = new();

    // The farthest apart two points of the zone's shape are looked at, in metres: half a sample of the ground's own grid.
    private const float Step = 0.5f;

    private readonly Assembly _owner;
    private readonly object _lock = new();
    private LandscapeArea _area;
    private bool _disposed;
    private ModHandlers<EventHandler<LandscapeChangingEventArgs>>? _refused;

    private LandscapeZone(Assembly owner, string modId, LandscapeArea area)
    {
        _owner = owner;
        ModId = modId;
        _area = area;
    }

    /// <summary>The ground it protects; setting it moves the zone.</summary>
    public LandscapeArea Area
    {
        get { lock (_lock) return _area; }
        set { lock (_lock) _area = value; }
    }

    /// <summary>The mod that made it, whose own changes it lets through.</summary>
    public string ModId { get; }

    /// <summary>Whether it still protects its ground: false once disposed, or once its mod was disabled.</summary>
    public bool IsActive
    {
        get
        {
            lock (_lock)
                if (_disposed) return false;
            return !ModCall.IsDisabled(_owner);
        }
    }

    /// <summary>
    /// Raised when the zone refused a change, with the change it refused (its <see cref="CancelEventArgs.Cancel"/> already
    /// set): a mod tells its player why the hoe does nothing there.
    /// </summary>
    public event EventHandler<LandscapeChangingEventArgs>? Refused
    {
        add => ModHandlers<EventHandler<LandscapeChangingEventArgs>>.Of(ref _refused, "LandscapeZone.Refused").Add(value);
        remove => _refused?.Remove(value);
    }

    /// <summary>Lets the ground of the zone be changed again.</summary>
    public void Dispose()
    {
        lock (_lock) _disposed = true;
        lock (s_gate) s_all.Remove(this);
    }

    /// <inheritdoc/>
    public override string ToString() => $"zone of {Area} kept by {ModId}";

    internal static LandscapeZone Add(Assembly owner, string modId, LandscapeArea area)
    {
        var zone = new LandscapeZone(owner, modId, area);
        lock (s_gate) s_all.Add(zone);
        return zone;
    }

    /// <summary>The zones that protect ground now.</summary>
    internal static IReadOnlyList<LandscapeZone> Active
    {
        get
        {
            LandscapeZone[] all;
            lock (s_gate) all = s_all.ToArray();
            return Array.FindAll(all, z => z.IsActive);
        }
    }

    /// <summary>
    /// The first active zone a change over <paramref name="rect"/> by <paramref name="modId"/> (null for the game) may not
    /// reach, or null when every zone lets it.
    /// </summary>
    internal static LandscapeZone? Refusing(Rect rect, string? modId)
    {
        foreach (var zone in Active)
            if (zone.ModId != modId && zone.Reaches(rect)) return zone;
        return null;
    }

    /// <summary>
    /// Refuses a change about to be made when a zone stands in its way (the game's tool, the SDK's own writes): the args
    /// are cancelled and the zone raises <see cref="Refused"/>.
    /// </summary>
    /// <returns>Whether a zone refused it.</returns>
    internal static bool Refuse(LandscapeChangingEventArgs change)
    {
        if (Refusing(change.Area, change.ModId) is not { } zone) return false;
        change.Cancel = true;
        if (zone._refused is { Count: > 0 } handlers) handlers.Raise(h => h(zone, change));
        return true;
    }

    /// <summary>
    /// Whether the zone's own shape (where its area is at least half) lies anywhere inside the rectangle: ground the two
    /// share, looked at every half metre or closer; a rectangle that only touches the zone's edge does not reach it (a
    /// field's tile beside it).
    /// </summary>
    internal bool Reaches(Rect rect)
    {
        var area = Area;
        var bounds = area.Bounds;
        float x0 = Math.Max(rect.m_XMin, bounds.m_XMin), z0 = Math.Max(rect.m_YMin, bounds.m_YMin);
        float x1 = Math.Min(rect.m_XMin + rect.m_Width, bounds.m_XMin + bounds.m_Width);
        float z1 = Math.Min(rect.m_YMin + rect.m_Height, bounds.m_YMin + bounds.m_Height);
        if (x1 - x0 <= Touching || z1 - z0 <= Touching) return false;
        int columns = (int)MathF.Ceiling((x1 - x0) / Step), rows = (int)MathF.Ceiling((z1 - z0) / Step);
        float dx = (x1 - x0) / columns, dz = (z1 - z0) / rows;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
                if (area.Weight(x0 + (c + 0.5f) * dx, z0 + (r + 0.5f) * dz) >= 0.5f) return true;
        return false;
    }

    // Ground shared by less than this across, in metres, is an edge the two only touch.
    private const float Touching = 0.01f;

    /// <summary>Forgets every zone, between unit tests.</summary>
    internal static void ResetForTests()
    {
        lock (s_gate) s_all.Clear();
    }
}
