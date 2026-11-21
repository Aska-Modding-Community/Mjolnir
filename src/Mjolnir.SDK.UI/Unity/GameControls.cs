using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SandSailorStudio;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.UI;
using UnityEngine;
using Context = SSSGame.InputContext.Context;
using GameInput = SSSGame.InputManager;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The game's controls for <see cref="PlayerControls"/>. An input context of the SDK's with no input maps, above the
/// player's own (priority 1) and below mod windows (16) and typing (100): the game then switches off every map below it —
/// moving, attacking, the camera and its zoom, the quick slots, the menu keys and Esc's pause. The cursor is shown as a
/// menu with a cursor shows it, and handed back to the menu manager, which decides it by the menus open. While the
/// cursor shows, the game's aim is off: the HUD's interaction widget (the crosshair) hidden, and the local player's
/// interaction agent kept from finding targets (`LockAreaDetection`), which takes the target's card, its key prompts and
/// its highlight away; both are given back with the cursor.
/// </summary>
internal sealed class GameControls : IControlsSource
{
    private static Context? s_context;
    private GameInput? _input;
    private InteractWidget? _aim;
    private PlayerInteractionAgent? _agent;

    private GameControls() { }

    /// <summary>The game's controls, or null in a game without a screen.</summary>
    public static GameControls? Create() => Application.isBatchMode ? null : new GameControls();

    public bool InWorld => Menu.GetInstance<PlayerHUDMenu>() is { } hud && hud != null;

    public void Block()
    {
        _input = GameManagers.Get<GameInput>();
        _input?.AddContext(Context());
    }

    public void Unblock()
    {
        // The input manager it went on: a scene loaded since may have another one, which never had it.
        if (_input != null) _input.RemoveContext(Context());
        _input = null;
    }

    public void HoldCursor()
    {
        var mode = Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen ? CursorLockMode.Confined : CursorLockMode.None;
        if (!Cursor.visible) Cursor.visible = true;
        if (Cursor.lockState != mode) Cursor.lockState = mode;
        QuietAim();
    }

    public void ReleaseCursor()
    {
        GiveAimBack();
        MenuManager.Instance?.RefreshInputMode(-1, false);
    }

    // The aim off while the cursor is the pointer: the crosshair hidden alone left the target's card and its highlight
    // (seen in game, 2026-11-21: a boulder in the middle of a view from above showed its card and «hold R»); the agent's
    // area detection locked takes them away. Each frame: a new HUD has a new widget, and the game shows it again when a
    // menu over it closes.
    private void QuietAim()
    {
        if (_aim == null)
        {
            if (Menu.GetInstance<PlayerHUDMenu>() is not { } hud || hud == null) return;
            _aim = hud.GetComponentInChildren<InteractWidget>(true);
            if (_aim == null) return;
        }
        if (_aim.IsVisible) _aim.IsVisible = false;
        if (_agent == null) _agent = _aim.InteractionAgent?.TryCast<PlayerInteractionAgent>();
        if (_agent != null && !_agent.LockAreaDetection)
        {
            _agent.LockAreaDetection = true;
            _agent.ClearInteractionAreas();
        }
    }

    private void GiveAimBack()
    {
        if (_agent != null) _agent.LockAreaDetection = false;
        if (_aim != null) _aim.IsVisible = true;
        _agent = null;
        _aim = null;
    }

    private static Context Context()
    {
        if (s_context != null) return s_context;
        s_context = ScriptableObject.CreateInstance<Context>();
        s_context.name = "Mjolnir.SDK.UI controls captured";
        s_context.hideFlags = HideFlags.DontUnloadUnusedAsset;
        s_context.inputMaps = new Il2CppStructArray<GameInput.InputMaps>(System.Array.Empty<GameInput.InputMaps>());
        s_context.priority = 15;
        s_context.dontChangeCamera = true;
        s_context.contextCamera = GameInput.CameraStates.LOCK;
        s_context.additive = false;
        return s_context;
    }
}
