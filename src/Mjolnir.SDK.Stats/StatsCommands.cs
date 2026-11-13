using System;
using System.Globalization;
using System.Linq;

namespace Mjolnir.SDK.Stats;

/// <summary>
/// The stats module's console commands (<see cref="DevConsole"/>): <c>stats</c> shows a character, <c>skill</c> shows or sets
/// a skill of the player, <c>effects</c> lists the game's status effects, <c>effect</c> and <c>uneffect</c> put one on the
/// player and take it off.
/// </summary>
internal static class StatsCommands
{
    private const int ShownAtMost = 40;

    public static void Register()
    {
        // The platform's own commands, whichever mod's code happens to be running.
        var platform = typeof(StatsCommands).Assembly;
        DevConsole.AddFor(platform, "stats", "Shows a character: characteristics, skills, effects. The player without a name.", Show, "[villager]");
        DevConsole.AddFor(platform, "skill", "Shows the player's skills, or sets a level.", SetSkill, "[skill] [level]");
        DevConsole.AddFor(platform, "effects", "Lists the status effects whose names contain the words.", ListEffects, "[words]");
        DevConsole.AddFor(platform, "effect", "Puts a status effect on the player, for seconds or until taken off.", Apply, "<effect> [seconds]");
        DevConsole.AddFor(platform, "uneffect", "Takes a status effect off the player.", Remove, "<effect>");
    }

    private static void Show(DevCommandContext context)
    {
        string name = Words(context);
        var stats = GameStats.FindCharacter(name);
        if (!stats.IsAvailable)
        {
            context.Error(name.Length == 0 ? "No player in the world." : $"No villager called '{name}'.");
            return;
        }
        context.Print(string.Join(", ", new[] { stats.Vitality, stats.Strength, stats.Dexterity, stats.Intelligence, stats.MaxStamina, stats.StaminaRegeneration }.Select(Line)));
        var skills = stats.Skills.Where(s => s.Level.Value > 0).OrderByDescending(s => s.Level.Value).Select(s => Number($"{s.Kind} {s.Level.Value:0.#} ({s.Rank})"));
        context.Print($"Skills: {string.Join(", ", skills)}");
        var effects = stats.Effects.Active.Select(a => a.TimeLeft is { } left ? Number($"{a.Effect.AssetName} {left.TotalSeconds:0} s") : a.Effect.AssetName);
        context.Print($"Effects: {string.Join(", ", effects)}");
    }

    private static string Line(CharacterStat stat) =>
        Number($"{stat.AssetName.Replace("Attribute_", "", StringComparison.Ordinal)} {stat.Value:0.##}");

    private static void SetSkill(DevCommandContext context)
    {
        var stats = GameStats.FindCharacter("");
        if (!stats.IsAvailable)
        {
            context.Error("No player in the world.");
            return;
        }
        if (context.Arguments.Count == 0)
        {
            foreach (var skill in stats.Skills) context.Print(Number($"{skill.Kind} {skill.Level.Value:0.#} ({skill.Rank})"));
            return;
        }
        if (!Enum.TryParse<SkillKind>(context.GetString(0), ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
            throw new DevCommandException($"No skill called '{context.GetString(0)}'. skill lists them.");
        var chosen = stats.Skill(kind);
        if (context.Arguments.Count > 1) chosen.Level.BaseValue = context.GetFloat(1);
        context.Print(Number($"{chosen.Kind} {chosen.Level.Value:0.#} ({chosen.Rank}), experience {chosen.Experience.Value:0.#}, multiplier {chosen.ExperienceMultiplier.Value:0.##}"));
    }

    private static void ListEffects(DevCommandContext context)
    {
        string words = Words(context);
        var found = Effects.All.Where(e => e.AssetName.Contains(words, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var effect in found.Take(ShownAtMost)) context.Print($"{effect.AssetName}  {effect.Kind}");
        if (found.Count > ShownAtMost) context.Print($"  and {found.Count - ShownAtMost} more");
        if (found.Count == 0) context.Print($"No effect contains '{words}'.");
    }

    private static void Apply(DevCommandContext context)
    {
        var stats = GameStats.FindCharacter("");
        var effect = Effect(context);
        float seconds = context.GetFloat(1, 0f);
        if (seconds < 0) throw new DevCommandException("Seconds are not below 0; without them it stays until taken off.");
        bool applied = stats.Effects.Apply(effect, seconds > 0 ? TimeSpan.FromSeconds(seconds) : null);
        if (applied) context.Print(seconds > 0 ? Number($"{effect.AssetName} on the player for {seconds:0} s.") : $"{effect.AssetName} on the player until taken off.");
        else context.Error($"The player did not take {effect.AssetName}.");
    }

    private static void Remove(DevCommandContext context)
    {
        var effect = Effect(context);
        if (GameStats.FindCharacter("").Effects.Remove(effect)) context.Print($"{effect.AssetName} taken off.");
        else context.Error($"The player does not have {effect.AssetName}.");
    }

    private static string Words(DevCommandContext context) => context.Arguments.Count > 0 ? context.GetRest(0) : "";

    private static EffectInfo Effect(DevCommandContext context) =>
        Effects.Find(context.GetString(0)) ?? throw new DevCommandException($"No effect called '{context.GetString(0)}'. effects <words> lists them.");

    private static string Number(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
