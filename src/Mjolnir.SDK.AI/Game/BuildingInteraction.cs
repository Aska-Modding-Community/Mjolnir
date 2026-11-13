using System;
using SSSGame;
using UnityEngine;
using UnityEngine.Events;

namespace Mjolnir.SDK.AI;

/// <summary>
/// One interaction in a building's own area: a SimpleInteraction on an object of its own beside the building's (as a well's
/// «Drink» lies beside its area), shown, hidden and named through the game's own prompt.
/// </summary>
internal sealed class BuildingInteraction : IGameInteraction
{
    private readonly GameObject _target;
    private readonly ModInteraction _interaction;
    private readonly StructureInteractionArea _area;
    private readonly GameObject _holder;
    private readonly SimpleInteraction _simple;
    private readonly Interaction? _sibling;
    private int _applied = -1;
    private bool _inTable, _tableLogged;
    private (float X, float Y, float Z) _last;
    private bool _removed, _shown;
    private float _playerSince = -1;

    public BuildingInteraction(GameObject target, StructureInteractionArea area, ModInteraction interaction)
    {
        _target = target;
        _area = area;
        _interaction = interaction;
        var p = target.transform.position;
        _last = (p.x, p.y, p.z);
        _holder = new GameObject($"Mjolnir {interaction}");
        _holder.SetActive(false);
        _holder.layer = area.gameObject.layer;
        _holder.transform.SetParent(area.transform, false);
        _simple = _holder.AddComponent<SimpleInteraction>();
        var prompt = GameInteractions.Prompt;
        _simple.host = area.gameObject;
        _simple.order = InteractionOrder.PRIMARY;
        _simple.mode = prompt.Mode;
        _simple.accessDistance = 3f;
        _simple.interactSlot = prompt.Slot;
        _simple.widgetIcon = prompt.Icon;
        _simple.events ??= new Interaction.InteractionEvents();
        _simple.events.OnBegin ??= new UnityEvent();
        _simple.events.OnEnd ??= new UnityEvent();
        _simple.events.OnFinish ??= new UnityEvent();
        // Raised by the game at every touch of the area (the prompt's look): null, it threw there and no prompt came.
        _simple.events.OnTouch ??= new Interaction.InteractionEvents.InteractionTouchEvent();
        _holder.SetActive(true);
        area.interactions ??= new Il2CppSystem.Collections.Generic.List<Interaction>();
        // The building's own first interaction (its menu): the game switches its interactions off while it is not standing
        // whole (under construction), and ours follows it.
        foreach (var other in area.interactions)
        {
            if (other == null) continue;
            _sibling = other;
            break;
        }
        area.interactions.Add(_simple);
        area.SetAreaDirty();
        SimplePointer = _simple.Pointer;
        AreaObjectPointer = area.gameObject.Pointer;
        SdkLog.Info(GameVillagers.Source, $"Interactions: {interaction} on the building '{target.name}'");
    }

    public nint SimplePointer { get; }

    public nint AreaObjectPointer { get; }

    // Unity's == null: the wrapper outlives a destroyed object.
    public bool Exists => !_removed && _target != null && _holder != null && _area != null;

    public bool PlayerCanUse => true;

    public GameObject? Object => Exists ? _target : null;

    public (float X, float Y, float Z) Position
    {
        get
        {
            if (Exists)
            {
                var p = _target.transform.position;
                _last = (p.x, p.y, p.z);
            }
            return _last;
        }
    }

    /// <summary>
    /// Every frame: the interaction in the building's network table until it is there (a building fills it as it spawns,
    /// before the SDK puts ours in; without it the game never shows ours and its session failed), its settings, and whether
    /// the player may use it now (free, for the player, and the building's own interactions on), shown on the game's objects
    /// when they changed.
    /// </summary>
    public void Apply()
    {
        Register();
        bool changed = false;
        int version = _interaction.Version;
        if (version != _applied)
        {
            _applied = version;
            _simple.widgetMessage = _interaction.ShownText;
            _simple.holdTargetTime = (float)_interaction.HoldTime.TotalSeconds;
            _simple.widgetIcon = _interaction.Icon != null ? _interaction.Icon : GameInteractions.Prompt.Icon;
            changed = true;
        }
        bool shown = _interaction.IsFree && _interaction.PlayerCanUse && (_sibling == null || _sibling.enabled);
        if (shown != _simple.enabled || shown != _shown)
        {
            _shown = shown;
            if (!shown) _simple.FinishAllSessions(InteractionSessionState.ABORTED);
            _simple.enabled = shown;
            changed = true;
        }
        if (changed) _area.SetAreaDirty();
    }

    private void Register()
    {
        if (_inTable) return;
        var table = _simple.interactable;
        if (table == null) return;
        if (table.SessionID(_simple) >= 0)
        {
            _inTable = true;
            return;
        }
        _inTable = table.TryRegisterInteraction(_simple);
        if (!_tableLogged) SdkLog.Info(GameVillagers.Source, $"Interactions: {_interaction} in the building's network table: {_inTable}");
        _tableLogged = true;
    }

    /// <summary>
    /// A player's session left running at this interaction is ended (aborted): a used one ends at once, and while one runs the
    /// game locks the player's interactions (<c>LockActiveInteraction</c>), so a session the game stopped updating would leave
    /// the player unable to use anything (seen 2026-11-11). It is given its hold time and 2 s more while the player looks at it.
    /// </summary>
    public void WatchPlayer(IInteractionAgent player, bool lookedAt)
    {
        var session = _simple.FindActiveSession(player);
        if (session == null || session.SessionState != InteractionSessionState.RUNNING)
        {
            _playerSince = -1;
            return;
        }
        float now = Time.realtimeSinceStartup;
        if (_playerSince < 0) _playerSince = now;
        if (lookedAt && now - _playerSince < (float)_interaction.HoldTime.TotalSeconds + 2f) return;
        _playerSince = -1;
        SdkLog.Info(GameVillagers.Source, $"Interactions: the player's session at {_interaction} left running: ended");
        _simple.Finish(player, InteractionSessionState.ABORTED);
    }

    /// <summary>Takes the SDK's objects out of the building (it stays), once.</summary>
    public void Remove()
    {
        if (_removed) return;
        _removed = true;
        GameInteractions.Forget(this);
        try
        {
            if (_holder != null)
            {
                _simple.FinishAllSessions(InteractionSessionState.ABORTED);
                _simple.interactable?.UnregisterInteraction(_simple);
                if (_area != null)
                {
                    _area.interactions?.Remove(_simple);
                    _area.SetAreaDirty();
                }
                UnityEngine.Object.Destroy(_holder);
            }
        }
        catch (Exception e)
        {
            SdkLog.Warn(GameVillagers.Source, $"Interactions: removing {_interaction} failed: {e.Message}");
        }
    }
}
