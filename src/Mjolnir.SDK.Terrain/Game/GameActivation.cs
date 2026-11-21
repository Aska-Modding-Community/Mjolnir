using SandSailorStudio;
using SSSGame;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// A point the game tracks as it tracks the character: a hidden object of the SDK's own given to the world's
/// <see cref="InstanceColliderManager"/> (<c>RegisterTrackedTransform</c>), which brings up the world's items in the cells
/// round every tracked object. The manager is the world's: a new world's is given the point anew.
/// </summary>
internal sealed class GameActivation : IActivationView
{
    private GameObject? _holder;
    private InstanceColliderManager? _manager;

    public bool IsActive => _holder != null && _manager != null;

    public void MoveTo(Vector3 center)
    {
        if (_holder == null)
        {
            _holder = new GameObject("Mjolnir_ActivationPoint");
            Object.DontDestroyOnLoad(_holder);
            _holder.hideFlags = HideFlags.HideAndDontSave;
            _manager = null;
        }
        _holder.transform.position = center;
        if (_manager != null) return;
        var manager = GameManagers.Get<InstanceColliderManager>();
        if (manager == null) return;
        manager.RegisterTrackedTransform(_holder.transform);
        _manager = manager;
    }

    public void Destroy()
    {
        if (_holder != null && _manager != null) _manager.UnregisterTrackedTransform(_holder.transform);
        if (_holder != null) Object.Destroy(_holder);
        _holder = null;
        _manager = null;
    }
}
