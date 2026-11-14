using System;
using Mjolnir.SDK;
using Mjolnir.SDK.Content;

namespace Mjolnir.EquipmentExample;

/// <summary>
/// Example mod: a weapon, a piece of armour and a food of a mod's own, through Mjolnir.SDK.Content's item kinds.
/// <list type="bullet">
/// <item>A rune sword: a one-handed weapon (<see cref="ItemKind.OneHandedWeapon"/>: held in the right hand, the game's sword
/// attacks) dealing 14 to 18 a blow, lasting 800, and setting what it hits on fire one blow in four (the game's
/// <c>Effect_Burning</c>, for 4 seconds);</item>
/// <item>a rune hood: a helmet (<see cref="ItemKind.Helmet"/>) giving 60 armour and 50 cold resistance, and the game's
/// energized effect while it is worn;</item>
/// <item>a honey cake: food (<see cref="ItemKind.Food"/>) that energizes whoever eats it for two minutes, beside what the
/// berries it stands on give;</item>
/// <item>their names, descriptions and lore in the player's language from <c>lang/</c>, and a console command,
/// <c>equipment</c>, giving the player one of each (the console opens with <c>`</c>).</item>
/// </list>
/// They have the pictures and looks of the plain game items their kinds stand on; a mod gives its own with <c>icon</c> and
/// <see cref="ModItem.WithModel"/>, as the items example does.
/// </summary>
[ModManifest(
    "mjolnir.equipmentexample",
    "Equipment Example",
    "1.0.0",
    Network = NetworkCompatibility.Everyone,   // its items travel between the players
    Description = "A weapon, armour and food of a mod's own.",
    Author = "Mjolnir")]
public sealed class EquipmentExampleMod : Mod
{
    public override void OnLoad()
    {
        // The game's effects are named: the game knows them only once it runs, after OnLoad.
        var sword = Items.Add("rune_sword", ItemKind.OneHandedWeapon)
                         .WithDamage(14, 18)
                         .WithDurability(800)
                         .AppliesOnHit("Effect_Burning", TimeSpan.FromSeconds(4), chance: 0.25f);

        var hood = Items.Add("rune_hood", ItemKind.Helmet)
                        .WithArmor(60)
                        .WithAttribute("Attribute_ColdResistance", 50)
                        .AppliesWhileEquipped("Effect_Energized");

        var cake = Items.Add("honey_cake", ItemKind.Food, stackSize: 5)
                        .AppliesWhenEaten("Effect_Energized", TimeSpan.FromMinutes(2));

        DevConsole.Add("equipment", "Gives the player the example's sword, hood and cake.", context =>
        {
            int given = Items.Give(sword) + Items.Give(hood) + Items.Give(cake);
            context.Print($"Gave {given} of the example's items.");
        });
    }
}
