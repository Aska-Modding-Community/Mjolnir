using Mjolnir.SDK;
using Mjolnir.SDK.Content;

namespace Mjolnir.ItemsExample;

/// <summary>
/// Example mod: items, recipes and a building of a mod's own, through Mjolnir.SDK.Content.
/// <list type="bullet">
/// <item>Twine: made from the game's rope (what it is in the world, its kind and sounds), with its own picture
/// (<c>items/twine.png</c>), stack and model (<c>WithModel</c>: a coil of twine as the picture shows it, the prefab Twine of
/// <c>bundles/models</c>, an AssetBundle the Mjolnir kit builds), its name, description and lore in the player's language from <c>lang/</c>
/// (<c>[item.twine]</c>);</item>
/// <item>its recipe: two sticks and a rope make two twines, made where the game makes rope: by hand, in the build menu's
/// crafting next to the game's rope (a new game's player knows it from the start, a player of an older save as the
/// world loads), and at the workshop;</item>
/// <item>a twine spool (<c>items/twine_spool.png</c>): five twines wound round a stick, made only at the workshop, of
/// every level (<c>MadeAt</c>), by the player at its table or by its villagers when asked for; in the world it is a model
/// of its own (<c>WithModel</c>: the prefab TwineSpool of <c>bundles/models</c>, an AssetBundle the Mjolnir kit builds);</item>
/// <item>a twine arch (<c>items/twine_arch.png</c>): the game's flimsy arch with a name, picture and cost of its own, five
/// sticks and four twine, in the build menu beside the game's arch;</item>
/// <item>a console command of its own: <c>twine [count]</c> gives the player twine (the console opens with <c>`</c>).</item>
/// </list>
/// </summary>
[ModManifest(
    "mjolnir.itemsexample",
    "Items Example",
    "1.0.0",
    Network = NetworkCompatibility.Everyone,   // its items and its building travel between the players
    Description = "Items, recipes and a building of a mod's own.",
    Author = "Mjolnir")]
public sealed class ItemsExampleMod : Mod
{
    public override void OnLoad()
    {
        // Models of their own from the mod's bundle (the Mjolnir kit builds it), as their pictures show them: a coil of twine, a
        // spool wound with twine.
        var twine = Items.Add("twine", basedOn: "Item_Materials_BasicRope", icon: "items/twine.png", stackSize: 20)
                         .WithModel("bundles/models", "Twine");
        Recipes.Add(twine, quantity: 2)
               .Needs("Item_Wood_Sticks", 2)
               .Needs("Item_Materials_BasicRope", 1);

        var spool = Items.Add("twine_spool", basedOn: "Item_Materials_BasicRope", icon: "items/twine_spool.png", stackSize: 10)
                         .WithModel("bundles/models", "TwineSpool");
        Recipes.Add(spool)
               .Needs(twine, 5)
               .Needs("Item_Wood_Sticks", 1)
               .MadeAt("Item_Structures_WorkshopL0");

        Buildings.Add("twine_arch", basedOn: "Item_Decoration_ArchFlimsy", icon: "items/twine_arch.png")
                 .Needs("Item_Wood_Sticks", 5)
                 .Needs(twine, 4);

        DevConsole.Add("twine", "Gives the player twine.", context =>
        {
            int given = Items.Give(twine, context.GetInt(0, 10));
            context.Print($"Gave {given} twine.");
        }, "[count]");
    }
}
