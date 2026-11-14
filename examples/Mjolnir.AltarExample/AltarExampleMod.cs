using System;
using Mjolnir.SDK;
using Mjolnir.SDK.AI;
using Mjolnir.SDK.Content;
using Mjolnir.SDK.Player;
using Mjolnir.SDK.UI;

namespace Mjolnir.AltarExample;

/// <summary>
/// Example mod: a building of a mod's own that the player uses, as the game's well, through Mjolnir.SDK.Content and
/// Mjolnir.SDK.AI.
/// <list type="bullet">
/// <item>the altar, a building in the build menu's totems: a runestone on a slab (the model of the mod's bundle
/// <c>bundles/models</c>, made by the Mjolnir kit), built as the game's own buildings are: its ground is levelled with a hoe
/// first, then it waits for four large stones and is built with a hammer, standing in the game's construction material
/// until it is finished; outlined, named and saved as the game's own (its texts are <c>[building.altar]</c> in
/// <c>lang/</c>);</item>
/// <item>once it stands, the player holds the interact key at it to pray («Pray», beside the building's own menu) and gets their strength
/// back;</item>
/// <item>the console's <c>interactions</c> lists the altars standing.</item>
/// </list>
/// </summary>
[ModManifest(
    "mjolnir.altarexample",
    "Altar Example",
    "1.0.0",
    Network = NetworkCompatibility.Everyone,   // its building and its effect travel between the players
    Description = "An altar to build, where the player prays.",
    Author = "Mjolnir")]
public sealed class AltarExampleMod : Mod
{
    private const string Altar = "altar";
    private const string AltarStone = "Item_Stone_Raw";

    public override void OnLoad()
    {
        // The altar: a building of the mod's own, with its model, among the totems, its ground levelled first, for four large
        // stones; and the prayer on every altar standing in the world.
        var altar = Buildings.Add(Altar, icon: "items/altar.png")
                             .WithModel("bundles/models", "Altar")
                             .WithCategory("Categ_Blueprints_Totems")
                             .WithGroundLevelling()
                             .Needs(AltarStone, 4);
        Interactions.AddToBuildings(altar.AssetName, Altar, a =>
        {
            a.WithText(Localization.Get("altar.pray")).WithHoldTime(TimeSpan.FromSeconds(1.5));
            a.Used += Prayed;
        });
    }

    private void Prayed(object? sender, InteractionUsedEventArgs e)
    {
        if (!e.IsPlayer) return;
        LocalPlayer.Stamina.Value = LocalPlayer.Stamina.Maximum;
        Notify.Message(Localization.Get("altar.prayed"));
    }
}
