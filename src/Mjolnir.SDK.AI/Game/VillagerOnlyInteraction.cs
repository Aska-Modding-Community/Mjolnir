using UnityEngine;

namespace Mjolnir.SDK.AI;

/// <summary>On an object that is no building: villagers walk to it and use it; the game shows the player nothing.</summary>
internal sealed class VillagerOnlyInteraction(GameObject target) : IGameInteraction
{
    private bool _removed;
    private (float X, float Y, float Z) _last = Place(target);

    public bool Exists => !_removed && target != null;

    public bool PlayerCanUse => false;

    public GameObject? Object => Exists ? target : null;

    public (float X, float Y, float Z) Position => Exists ? _last = Place(target) : _last;

    public void Apply()
    {
    }

    public void Remove()
    {
        if (_removed) return;
        _removed = true;
        GameInteractions.Forget(this);
    }

    private static (float, float, float) Place(GameObject target)
    {
        var p = target.transform.position;
        return (p.x, p.y, p.z);
    }
}
