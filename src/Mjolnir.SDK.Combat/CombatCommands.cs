using System;
using System.Globalization;
using System.Linq;

namespace Mjolnir.SDK.Combat;

/// <summary>
/// The combat module's console commands (<see cref="DevConsole"/>): <c>combatants</c> lists what takes damage near the
/// player; <c>hurt</c>, <c>heal</c> and <c>kill</c> act on the player or on the nearest other combatant.
/// </summary>
internal static class CombatCommands
{
    private const float Around = 30f; // metres

    public static void Register()
    {
        // The platform's own commands, whichever mod's code happens to be running.
        var platform = typeof(CombatCommands).Assembly;
        DevConsole.AddFor(platform, "combatants", "Lists the players, villagers, pets, creatures and dens near the player.", List, "[radius]");
        DevConsole.AddFor(platform, "hurt", "Damages the player, or strikes the nearest other combatant as the player.", Hurt, "<amount> [near]");
        DevConsole.AddFor(platform, "heal", "Gives the player, or the nearest other combatant, health back (all of it without an amount).", Heal, "[amount] [near]");
        DevConsole.AddFor(platform, "kill", "Kills the nearest other combatant (within 30 m).", Kill);
    }

    private static Combatant Player()
    {
        var player = Combatants.Game is { } game ? Combatant.Of(game.LocalPlayer) : null;
        return player is { IsAvailable: true } ? player : throw new DevCommandException("No player in the world.");
    }

    private static Combatant Nearest(Combatant player) =>
        Combatants.Near(player.Position, Around).FirstOrDefault(c => c != player && c.IsAlive)
        ?? throw new DevCommandException($"No one else within {Around:0} m.");

    private static void List(DevCommandContext context)
    {
        var player = Player();
        float radius = context.Arguments.Count > 0 ? context.GetFloat(0) : Around;
        var near = Combatants.Near(player.Position, radius);
        context.Print(string.Create(CultureInfo.InvariantCulture, $"{near.Count} within {radius:0} m:"));
        foreach (var c in near)
            context.Print(string.Create(CultureInfo.InvariantCulture,
                $"  {c.Kind,-8} {c.DisplayName} ({c.Name}): {c.Health:0.#}/{c.MaxHealth:0.#}{(c.IsAlive ? "" : ", dead")}, {c.Faction}, team {c.Team}, {Vector(c, player)}"));
    }

    private static void Hurt(DevCommandContext context)
    {
        float amount = context.GetFloat(0);
        if (amount < 0) throw new DevCommandException("The damage is at least 0.");
        var player = Player();
        bool near = context.Arguments.Count > 1 && context.GetString(1).Equals("near", StringComparison.OrdinalIgnoreCase);
        var target = near ? Nearest(player) : player;
        float taken = near ? target.Damage(amount, player) : target.Damage(amount);
        context.Print(string.Create(CultureInfo.InvariantCulture, $"{target.DisplayName}: {taken:0.##} taken, {target.Health:0.#}/{target.MaxHealth:0.#} left{(target.IsAlive ? "" : ", dead")}."));
    }

    private static void Heal(DevCommandContext context)
    {
        var player = Player();
        bool near = context.Arguments.Any(a => a.Equals("near", StringComparison.OrdinalIgnoreCase));
        var target = near ? Nearest(player) : player;
        float amount = context.Arguments.Count > 0 && !context.GetString(0).Equals("near", StringComparison.OrdinalIgnoreCase) ? context.GetFloat(0) : target.MaxHealth;
        float given = target.Heal(amount);
        context.Print(string.Create(CultureInfo.InvariantCulture, $"{target.DisplayName}: {given:0.##} given, {target.Health:0.#}/{target.MaxHealth:0.#}."));
    }

    private static void Kill(DevCommandContext context)
    {
        var player = Player();
        var target = Nearest(player);
        target.Kill(player);
        context.Print($"{target.DisplayName} ({target.Kind}): {(target.IsAlive ? "still alive" : "dead")}.");
    }

    private static string Vector(Combatant c, Combatant player) =>
        string.Create(CultureInfo.InvariantCulture, $"{UnityEngine.Vector3.Distance(c.Position, player.Position):0} m");
}
