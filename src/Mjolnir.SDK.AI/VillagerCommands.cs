using System.Globalization;

namespace Mjolnir.SDK.AI;

/// <summary>
/// The villager module's console commands (<see cref="DevConsole"/>): <c>villager</c> spawns villagers in front of the
/// player, <c>villagers</c> lists the world's villagers with what they do and where, <c>interactions</c> the mods'
/// interactions.
/// </summary>
internal static class VillagerCommands
{
    public static void Register()
    {
        // The platform's own commands, whichever mod's code happens to be running.
        var platform = typeof(VillagerCommands).Assembly;
        DevConsole.AddFor(platform, "villager", "Spawns villagers in front of the player (1 without a count).", Spawn, "[count]");
        DevConsole.AddFor(platform, "villagers", "Lists the world's villagers: what they do and where.", List);
        DevConsole.AddFor(platform, "interactions", "Lists the mods' interactions: where they are and who uses them.", ListInteractions);
    }

    private static void Spawn(DevCommandContext context)
    {
        int count = context.GetInt(0, 1);
        if (count < 1 || count > 20) throw new DevCommandException("A count from 1 to 20.");
        if (GameVillagers.InFrontOfPlayer(3f) is not { } place)
        {
            context.Error("No player in the world.");
            return;
        }
        int asked = 0;
        for (int i = 0; i < count; i++)
        {
            if (Residents.Spawn(place)) asked++;
        }
        context.Print(asked == count ? $"Spawning {count} villager(s); they come in a moment." : "Villagers cannot be spawned here.");
    }

    private static void List(DevCommandContext context)
    {
        var all = Residents.All;
        if (all.Count == 0)
        {
            context.Print("No villagers in the world.");
            return;
        }
        foreach (var resident in all)
        {
            var (x, y, z) = resident.PositionXyz;
            string activity = resident.Activity.Length > 0 ? resident.Activity : "nothing";
            context.Print(string.Create(CultureInfo.InvariantCulture, $"{resident.Name}: {activity} at {x:0.0} {y:0.0} {z:0.0}"));
        }
    }

    private static void ListInteractions(DevCommandContext context)
    {
        var all = Interactions.All;
        if (all.Count == 0)
        {
            context.Print("No interactions of mods in the world.");
            return;
        }
        foreach (var interaction in all)
        {
            var (x, y, z) = interaction.PositionXyz;
            string state = !interaction.IsEnabled ? "disabled" : $"{interaction.UserCount} of {interaction.MaxUsers} in use";
            context.Print(string.Create(CultureInfo.InvariantCulture, $"{interaction}: '{interaction.ShownText}' at {x:0.0} {y:0.0} {z:0.0}, {state}{(interaction.PlayerCanUse ? "" : ", villagers only")}"));
        }
    }
}
