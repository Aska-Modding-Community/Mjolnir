using System;
using System.ComponentModel;

namespace Mjolnir.SDK.Player;

/// <summary>
/// The local player's stamina (<see cref="LocalPlayer.Stamina"/>): a <see cref="PlayerStat"/>, and the cost of each use
/// before it is spent (<see cref="Draining"/>).
/// </summary>
public sealed class StaminaStat : PlayerStat
{
    private readonly ModHandlers<EventHandler<StaminaDrainingEventArgs>> _draining = new("LocalPlayer.Stamina.Draining");

    internal StaminaStat() : base(StatKind.Stamina) { }

    /// <summary>
    /// Raised on the main thread each time the local player is about to spend stamina (sprinting, harvesting, combat…),
    /// with the amount before the game's own discounts for harvesting and combat. A handler may change
    /// <see cref="StaminaDrainingEventArgs.Amount"/> or set <see cref="CancelEventArgs.Cancel"/> to spend nothing; handlers
    /// run in the order they were added, and each sees what the one before it left. While sprinting it is raised several times a
    /// second. Villagers' stamina is not reported.
    /// </summary>
    /// <example><code>LocalPlayer.Stamina.Draining += (s, e) => e.Amount *= 0.5f; // half the cost</code></example>
    public event EventHandler<StaminaDrainingEventArgs>? Draining
    {
        add
        {
            // The game method is patched only once a mod listens: villagers spend stamina through it too.
            GamePlayer.WantStaminaPatch();
            _draining.Add(value);
        }
        remove => _draining.Remove(value);
    }

    /// <summary>
    /// The game is about to spend <paramref name="amount"/> of <paramref name="character"/>'s stamina (the facade's
    /// patch). For the local player the handlers may change the amount (never below 0) or cancel; false means "spend
    /// nothing".
    /// </summary>
    internal bool Drain(nint character, ref float amount, StaminaUse use)
    {
        if (_draining.Count == 0 || character == 0 || character != LocalPlayer.CharacterId) return true;
        var args = new StaminaDrainingEventArgs(amount, use);
        _draining.Raise(h => h(this, args));
        if (args.Cancel) return false;
        if (float.IsFinite(args.Amount)) amount = Math.Max(0f, args.Amount);
        return true;
    }
}

/// <summary>
/// The local player is about to spend stamina (<see cref="StaminaStat.Draining"/>): how much and on what. Change
/// <see cref="Amount"/> to change the cost, or set <see cref="CancelEventArgs.Cancel"/> to spend nothing.
/// </summary>
public sealed class StaminaDrainingEventArgs(float amount, StaminaUse use) : CancelEventArgs
{
    /// <summary>The stamina about to be spent; set it to change the cost (a value below 0 counts as 0).</summary>
    public float Amount { get; set; } = amount;

    /// <summary>What the stamina is spent on.</summary>
    public StaminaUse Use { get; } = use;
}

/// <summary>What stamina is spent on (<see cref="StaminaDrainingEventArgs.Use"/>), as the game tells it.</summary>
public enum StaminaUse
{
    /// <summary>Anything that is neither harvesting nor combat, sprinting among it.</summary>
    Default = 0,

    /// <summary>Harvesting; the game lowers the cost with the character's harvesting discount.</summary>
    Harvest = 1,

    /// <summary>Combat; the game lowers the cost with the character's combat discount.</summary>
    Combat = 2,
}
