using System;
using SSSGame;
using UnityEngine;

namespace Mjolnir.SDK.AI;

/// <summary>The model's view of one of the game's villagers (main thread).</summary>
internal sealed class GameVillager(Villager villager) : IVillagerSource
{
    public Villager Villager { get; } = villager;

    public nint Key { get; } = villager.Pointer;

    // Unity's == null: the wrapper outlives a destroyed villager.
    public bool IsAlive => Villager != null;

    public string Name
    {
        get
        {
            try
            {
                return Villager.GetName() ?? "";
            }
            catch (Exception)
            {
                return ""; // a villager the game has not named yet
            }
        }
    }

    public (float X, float Y, float Z) Position
    {
        get
        {
            var p = Villager.transform.position;
            return (p.x, p.y, p.z);
        }
    }

    public void Face((float X, float Y, float Z) place)
    {
        var t = Villager.transform;
        var p = t.position;
        float dx = place.X - p.x, dz = place.Z - p.z;
        if (dx * dx + dz * dz < 0.0001f) return;
        var direction = default(Vector3);
        direction.x = dx;
        direction.z = dz;
        t.rotation = Quaternion.LookRotation(direction);
    }

    // A job's quest by its job's name: between its Start and its first step the run is not there yet.
    public string Activity
    {
        get
        {
            var quest = Villager._questRunner?.ActiveQuest;
            if (quest == null) return "";
            return JobQuests.JobOf(quest.Pointer)?.ToString() ?? quest.GetIl2CppType().Name;
        }
    }
}
