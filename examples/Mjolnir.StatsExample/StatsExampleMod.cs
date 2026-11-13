using System;
using System.Collections.Generic;
using Mjolnir.SDK;
using Mjolnir.SDK.Config;
using Mjolnir.SDK.Content;
using Mjolnir.SDK.Player;
using Mjolnir.SDK.Threading;

namespace Mjolnir.StatsExample;

/// <summary>
/// Example mod: what a character is made of.
/// <list type="bullet">
/// <item>the player learns faster: <see cref="Skill.ExperienceGaining"/> multiplies their experience by
/// <c>Stats.Experience</c>, and villagers learn as ever;</item>
/// <item>a status effect of its own, the Blessing (<c>Effects.Add</c>): three more strength and health coming back while
/// it lasts, with its picture and its name in the player's language, from <c>lang/</c>;</item>
/// <item>each level the player gains in a skill blesses them for <c>Stats.BlessingSeconds</c> (a skill's level is an attribute
/// like any other, and tells when it changes);</item>
/// <item>the console's <c>blessing</c> blesses the player at once.</item>
/// </list>
/// </summary>
[ModManifest(
    "mjolnir.statsexample",
    "Stats Example",
    "1.0.0",
    Description = "The player learns faster and is blessed at each level.",
    Author = "Mjolnir")]
public sealed class StatsExampleMod : Mod
{
    private ConfigEntry<float> _experience = null!;
    private ConfigEntry<float> _seconds = null!;
    private ModEffect _blessing = null!;
    private IReadOnlyList<Skill> _watched = [];

    public override void OnLoad()
    {
        _experience = Config.Bind("Stats", "Experience", 1.5f, "What the player's experience is multiplied by.")
                            .WithRange(1f, 10f).WithDisplayName("Experience");
        _seconds = Config.Bind("Stats", "BlessingSeconds", 60f, "How long a blessing lasts, in seconds.")
                         .WithRange(5f, 600f).WithDisplayName("Blessing");

        _blessing = Effects.Add("blessing", icon: "items/blessing.png")
                           .Modifies("Attribute_Strength", 3)
                           .PerSecond("VariableAttribute_Health", 1);

        Skill.ExperienceGaining += (_, e) =>
        {
            if (e.Sheet == LocalPlayer.Sheet) e.Amount *= _experience.Value;
        };

        // A character's stats belong to it: take them again whenever the player appears, and let them go with the character. The
        // save's values reach the character in the frame it spawns, so from the next frame on a change of level is a level gained.
        LocalPlayer.Spawned += (_, _) => Dispatcher.MainThread.Post(WatchLevels);
        LocalPlayer.Despawned += (_, _) => StopWatching();

        DevConsole.Add("blessing", "Blesses the player.", _ => Bless());
    }

    private void WatchLevels()
    {
        StopWatching();
        _watched = LocalPlayer.Sheet.Skills;
        foreach (var skill in _watched) skill.Level.Changed += OnLevelChanged;
    }

    private void StopWatching()
    {
        foreach (var skill in _watched) skill.Level.Changed -= OnLevelChanged;
        _watched = [];
    }

    private void OnLevelChanged(object? sender, ValueChangedEventArgs<float> e)
    {
        if (e.NewValue > e.OldValue) Bless();
    }

    // An effect applied again adds a stack of its own (this one's base stacks freely), so a blessing runs out first.
    private void Bless()
    {
        var effects = LocalPlayer.Sheet.Effects;
        if (!effects.Has(_blessing)) effects.Apply(_blessing, TimeSpan.FromSeconds(_seconds.Value));
    }
}
