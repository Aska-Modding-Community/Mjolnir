extern alias unity;
using System;
using unity::UnityEngine;

namespace Mjolnir.Core.Lifecycle;

/// <summary>
/// Mjolnir's MonoBehaviour, injected into IL2CPP in phase B (ClassInjector). Unity finds the messages below
/// by name on the injected class and calls them; they relay to <see cref="GameEvents"/>.
/// </summary>
internal sealed class MjolnirBehaviour : MonoBehaviour
{
    private static bool s_quitting;

    public MjolnirBehaviour(IntPtr ptr) : base(ptr) { }

    public void Update() => GameEvents.RaiseUpdate();

    public void LateUpdate() => GameEvents.RaiseLateUpdate();

    public void FixedUpdate() => GameEvents.RaiseFixedUpdate();

    // Unity destroys every object, DontDestroyOnLoad ones included, when the game exits.
    public void OnApplicationQuit()
    {
        s_quitting = true;
        Log.Info("PhaseB", "Game is quitting");
        GameEvents.RaiseApplicationQuit();
    }

    public void OnDestroy()
    {
        if (!s_quitting)
            Log.Warn("PhaseB", "MjolnirBehaviour was destroyed; GameEvents.Update/LateUpdate are no longer raised");
    }
}
