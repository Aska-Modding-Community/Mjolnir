using System;

namespace Mjolnir.SDK.AI;

/// <summary>
/// What the model sees of one villager of the game (Game/GameVillager.cs; a fake in the unit tests). Read on the main
/// thread.
/// </summary>
internal interface IVillagerSource
{
    /// <summary>The villager's identity: the same while it lives, whatever wrapper the game hands out.</summary>
    nint Key { get; }

    /// <summary>Whether the game object still exists (a villager dies, leaves, or its world unloads).</summary>
    bool IsAlive { get; }

    string Name { get; }

    /// <summary>Its place (unit tests cannot make a Vector3).</summary>
    (float X, float Y, float Z) Position { get; }

    /// <summary>The type name of the game quest it runs (<c>ComplainQuest</c>…), "" without one.</summary>
    string Activity { get; }

    /// <summary>Turns it to face a place across the ground (it stands to use an interaction).</summary>
    void Face((float X, float Y, float Z) place);
}

/// <summary>
/// What the model sees of an interaction's object in the game (Game/GameInteractions.cs; a fake in the unit tests). Read on
/// the main thread.
/// </summary>
internal interface IInteractionTarget
{
    /// <summary>Whether its object is still in the world.</summary>
    bool Exists { get; }

    /// <summary>Its object; null once gone (and in the unit tests).</summary>
    UnityEngine.GameObject? Object { get; }

    /// <summary>Its object's place, the last one known once it is gone.</summary>
    (float X, float Y, float Z) Position { get; }

    /// <summary>Whether the player can use it: on a building, where the game shows its prompt; else villagers only.</summary>
    bool PlayerCanUse { get; }

    /// <summary>Undoes what the SDK put on the object (the interaction was removed).</summary>
    void Remove();
}
