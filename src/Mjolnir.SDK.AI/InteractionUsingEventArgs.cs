using System.ComponentModel;

namespace Mjolnir.SDK.AI;

/// <summary>Someone is about to use an interaction (<see cref="ModInteraction.Using"/>).</summary>
public sealed class InteractionUsingEventArgs(ModInteraction interaction, Resident? resident) : CancelEventArgs
{
    /// <summary>The interaction.</summary>
    public ModInteraction Interaction { get; } = interaction;

    /// <summary>The villager using it; null for the player.</summary>
    public Resident? Resident { get; } = resident;

    /// <summary>Whether the player of this machine uses it.</summary>
    public bool IsPlayer => Resident == null;
}
