using System;

namespace Mjolnir.SDK.AI;

/// <summary>Someone used an interaction (<see cref="ModInteraction.Used"/>).</summary>
public sealed class InteractionUsedEventArgs(ModInteraction interaction, Resident? resident) : EventArgs
{
    /// <summary>The interaction.</summary>
    public ModInteraction Interaction { get; } = interaction;

    /// <summary>The villager that used it; null for the player.</summary>
    public Resident? Resident { get; } = resident;

    /// <summary>Whether the player of this machine used it.</summary>
    public bool IsPlayer => Resident == null;
}
