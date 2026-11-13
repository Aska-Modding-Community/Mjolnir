using System;
using System.Collections.Generic;
using Cinemachine;
using HarmonyLib;
using Mjolnir.SDK.Threading;
using SSSGame;
using SSSGame.Controllers;
using SandSailorStudio;
using UnityEngine;
using UnityCamera = UnityEngine.Camera;

namespace Mjolnir.SDK.Camera;

/// <summary>
/// The game's camera for the model: the output camera of the Cinemachine brain, the ten state cameras
/// of <c>InputManager</c> (their lens and their third-person follow), and a virtual camera per mod's camera, above the game's
/// by priority. Its hidden patch wraps <c>PlayerDrive.LateUpdate</c>, where the game moves its camera each frame (zoom, field
/// of view, which state).
/// </summary>
internal sealed partial class GameCameraSource : ICameraSource
{
    internal const string Source = "SDK.Camera";

    // Above every priority the game gives its own cameras (its highest base is 16, its rooms' overrides add some).
    private const int ModPriority = 1_000_000;

    // A mod's camera that is no longer shown stays below the game's until the blend back is over, so the brain blends from it.
    private const int LeavingPriority = -1000;

    private static readonly InputManager.CameraStates[] States = Enum.GetValues<InputManager.CameraStates>();
    private static Harmony? s_harmony;

    private readonly List<ICameraRig> _rigs = [];
    private readonly GameRig?[] _byState = new GameRig?[States.Length];
    private readonly Dictionary<ModCamera, OwnCamera> _own = [];
    private readonly List<(OwnCamera Camera, float Until)> _leaving = [];
    private readonly List<ModCamera> _gone = [];
    private IntPtr _manager;
    private InputManager? _input;
    private UnityCamera? _output;
    private CinemachineBrain? _brain;
    private CinemachineBlendDefinition? _gameBlend;
    private int _blendFrames;

    public static void Start()
    {
        try
        {
            Dispatcher.Hook();
            var source = new GameCameraSource();
            CameraDirector.Camera = source;
            // Each part on its own, so a fault in one (logged once) leaves the others working.
            Action refresh = source.Refresh, frame = CameraDirector.Frame, keepBlend = source.KeepBlend;
            GameLoop.Update += () =>
            {
                Guard("looking up the game's camera", refresh);
                Guard("following the camera", frame);
                Guard("putting the game's blend back", keepBlend);
            };
            s_harmony = new Harmony("mjolnir.sdk.camera");
            int patched = s_harmony.CreateClassProcessor(typeof(CameraPatches)).Patch()?.Count ?? 0;
            SdkLog.Info(Source, $"Camera module started: {patched} game methods patched");
        }
        catch (Exception e)
        {
            SdkLog.Error(Source, $"Camera module not started: {e}");
        }
    }

    /// <summary>Runs SDK code from a game callback: a fault is logged once for each <paramref name="what"/>, never thrown into the game.</summary>
    internal static void Guard(string what, Action action) => GameGuard.RunLogOnce(Source, what, action);

    public bool IsAvailable => _input != null && _output != null && _rigs.Count > 0;

    public Vector3 Position => _output!.transform.position;

    public Quaternion Rotation => _output!.transform.rotation;

    public Vector3 Forward => _output!.transform.forward;

    public float FieldOfView => _output!.fieldOfView;

    public CameraMode Mode
    {
        get
        {
            GameRig? best = null;
            foreach (var rig in _byState)
                if (rig != null && (best == null || rig.Camera.Priority > best.Camera.Priority)) best = rig;
            return best?.Mode ?? CameraMode.Normal;
        }
    }

    public IReadOnlyList<ICameraRig> Rigs => _rigs;

    public Ray MiddleRay => _output!.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

    public Vector3 WorldToScreen(Vector3 world) => _output!.WorldToScreenPoint(world);

    public Ray ScreenPointToRay(Vector2 screen) => _output!.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));

    public bool Raycast(Ray ray, float maxDistance, out Vector3 point, out GameObject? target)
    {
        point = default;
        target = null;
        Transform? player = LocalPlayerRoot();
        var hits = Physics.RaycastAll(ray, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        for (int i = 0; i < hits.Length; i++)
        {
            var hit = hits[i];
            var collider = hit.collider;
            if (collider == null || hit.distance >= nearest) continue;
            if (player != null && collider.transform.IsChildOf(player)) continue;
            nearest = hit.distance;
            point = hit.point;
            target = collider.gameObject;
        }
        return target != null;
    }

    public void ShowCameras(IReadOnlyList<ModCamera> ranked, TimeSpan? blend)
    {
        if (blend is { } time) SetBlend(time);
        for (int i = 0; i < ranked.Count; i++)
        {
            var camera = ranked[i];
            if (!_own.TryGetValue(camera, out var own) || !own.IsAlive)
            {
                own = OwnCamera.Create(camera, _byState[(int)InputManager.CameraStates.NORMAL]?.Camera);
                _own[camera] = own;
            }
            own.Apply(camera, ModPriority + i);
        }
        if (_own.Count == ranked.Count) return;
        foreach (var (camera, own) in _own)
            if (!Contains(ranked, camera)) _gone.Add(camera);
        foreach (var camera in _gone)
        {
            var own = _own[camera];
            _own.Remove(camera);
            if (!own.IsAlive) continue;
            own.Leave(LeavingPriority);
            _leaving.Add((own, Time.unscaledTime + (float)camera.Blend.TotalSeconds + 0.5f));
        }
        _gone.Clear();
    }

    /// <summary>Each frame: the game's camera objects looked up again when they changed (a world loaded or left, the player spawned).</summary>
    private void Refresh()
    {
        InputManager? input = null;
        try
        {
            input = GameManagers.Get<InputManager>();
        }
        catch (Exception)
        {
            // no managers yet
        }
        if (input == null || input.Pointer == IntPtr.Zero)
        {
            Forget();
            return;
        }
        if (input.Pointer != _manager)
        {
            Forget();
            _manager = input.Pointer;
        }
        _input = input;
        var output = input._playerCamera;
        if (output == null) output = UnityCamera.main;
        _output = output != null ? output : null;
        var brain = input.brain;
        _brain = brain != null ? brain : null;
        bool changed = false;
        for (int i = 0; i < States.Length; i++)
        {
            var o = input.GetCameraObject(States[i]);
            var cam = o?.cam;
            if (cam == null)
            {
                changed |= _byState[i] != null;
                _byState[i] = null;
                continue;
            }
            if (_byState[i] is { } known && known.Camera.Pointer == cam.Pointer) continue;
            var follow = o!.baseComp;
            _byState[i] = new GameRig(cam, follow != null ? follow : null, (CameraMode)i);
            changed = true;
        }
        if (changed)
        {
            _rigs.Clear();
            foreach (var rig in _byState)
                if (rig != null) _rigs.Add(rig);
        }
        for (int i = _leaving.Count - 1; i >= 0; i--)
        {
            if (Time.unscaledTime < _leaving[i].Until && _leaving[i].Camera.IsAlive) continue;
            _leaving[i].Camera.Destroy();
            _leaving.RemoveAt(i);
        }
    }

    private void Forget()
    {
        _manager = IntPtr.Zero;
        _input = null;
        _output = null;
        _brain = null;
        _gameBlend = null;
        _blendFrames = 0;
        Array.Clear(_byState);
        _rigs.Clear();
        foreach (var own in _own.Values) own.Destroy();
        _own.Clear();
        foreach (var (own, _) in _leaving) own.Destroy();
        _leaving.Clear();
    }

    /// <summary>The brain's default blend set for a change of the camera shown, the game's put back a few frames later.</summary>
    private void SetBlend(TimeSpan time)
    {
        if (_brain == null) return;
        _gameBlend ??= _brain.m_DefaultBlend;
        float seconds = (float)time.TotalSeconds;
        _brain.m_DefaultBlend = new CinemachineBlendDefinition(
            seconds > 0 ? CinemachineBlendDefinition.Style.EaseInOut : CinemachineBlendDefinition.Style.Cut, seconds);
        // The brain may update before or after the game's LateUpdate: hold it long enough for either order.
        _blendFrames = 3;
    }

    private void KeepBlend()
    {
        if (_blendFrames == 0 || --_blendFrames > 0) return;
        if (_brain != null && _gameBlend is { } game) _brain.m_DefaultBlend = game;
        _gameBlend = null;
    }

    private static Transform? LocalPlayerRoot()
    {
        try
        {
            var character = GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter;
            return character != null ? character.transform.root : null;
        }
        catch (Exception)
        {
            return null; // no session
        }
    }

    private static bool Contains(IReadOnlyList<ModCamera> ranked, ModCamera camera)
    {
        for (int i = 0; i < ranked.Count; i++)
            if (ReferenceEquals(ranked[i], camera)) return true;
        return false;
    }
}
