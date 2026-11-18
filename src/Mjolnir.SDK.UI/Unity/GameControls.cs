using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SandSailorStudio;
using SandSailorStudio.UI;
using SSSGame.UI;
using UnityEngine;
using Context = SSSGame.InputContext.Context;
using GameInput = SSSGame.InputManager;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The game's controls for <see cref="PlayerControls"/>. An input context of the SDK's with no input maps, above the
/// player's own (priority 1) and below mod windows (16) and typing (100): the game then switches off every map below it —
/// moving, attacking, the camera and its zoom, the quick slots, the menu keys and Esc's pause. The cursor is shown as a
/// menu with a cursor shows it, and handed back to the menu manager, which decides it by the menus open.
/// </summary>
internal sealed class GameControls : IControlsSource
{
    private static Context? s_context;
    private GameInput? _input;

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
    }

    public void ReleaseCursor() => MenuManager.Instance?.RefreshInputMode(-1, false);

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
