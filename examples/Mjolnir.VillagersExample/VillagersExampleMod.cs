using System;
using System.Collections.Generic;
using Mjolnir.SDK;
using Mjolnir.SDK.AI;
using Mjolnir.SDK.Config;
using Mjolnir.SDK.Player;
using Mjolnir.SDK.UI;
using UnityEngine;

namespace Mjolnir.VillagersExample;

/// <summary>
/// Example mod: a job of a mod's own for the game's villagers, through Mjolnir.SDK.AI.
/// <list type="bullet">
/// <item>a villager with nothing to do who notices the player near it (within <c>Greeting.Range</c> metres) walks up to the
/// player, waits a moment and greets them (a notification in the player's language, from <c>lang/</c>). Its priority 12 is
/// above idling at a workplace (11) and below work (15), so nobody leaves their work for it, and sleep (21) goes first;</item>
/// <item>it greets the player once in a while (<c>Greeting.Minutes</c>), so the job lowers its priority after a greeting
/// and the villager goes back to its own needs and work;</item>
/// <item>the settings are in Mjolnir/Config/mjolnir.villagersexample.toml and the Mods window; the console's
/// <c>villager</c> spawns a villager to try it with, <c>villagers</c> lists what each does.</item>
/// </list>
/// </summary>
[ModManifest(
    "mjolnir.villagersexample",
    "Villagers Example",
    "1.0.0",
    Description = "Villagers walk up to the player and greet them.",
    Author = "Mjolnir")]
public sealed class VillagersExampleMod : Mod
{
    private const float GreetPriority = 12f;

    // When each villager last greeted the player, on the game's clock.
    private readonly Dictionary<Resident, float> _greeted = [];
    private ConfigEntry<float> _range = null!;
    private ConfigEntry<float> _minutes = null!;

    public override void OnLoad()
    {
        _range = Config.Bind("Greeting", "Range", 25f, "How near the player a villager notices them, in metres.")
                       .WithRange(5f, 60f).WithDisplayName("Range");
        _minutes = Config.Bind("Greeting", "Minutes", 3f, "How long a villager waits before it greets the player again, in minutes.")
                         .WithRange(0.5f, 30f).WithDisplayName("Minutes between greetings");

        Residents.AddJob("greet", priority: Wants)
                 .MoveTo(_ => LocalPlayer.Position, distance: 3f)
                 .Wait(TimeSpan.FromSeconds(2))
                 .Do(Greet);
        Residents.Removed += (_, e) => _greeted.Remove(e.Resident);
    }

    // Asked often, for every villager: quick.
    private float Wants(Resident villager)
    {
        if (!LocalPlayer.IsSpawned) return -1f;
        if (_greeted.TryGetValue(villager, out float at) && Time.time - at < _minutes.Value * 60f) return -1f;
        return Vector3.Distance(villager.Position, LocalPlayer.Position) <= _range.Value ? GreetPriority : -1f;
    }

    private void Greet(Resident villager)
    {
        _greeted[villager] = Time.time;
        Notify.Message(Localization.Get("greeting", villager.Name));
        Log.Info($"{villager.Name} greeted the player");
    }
}
