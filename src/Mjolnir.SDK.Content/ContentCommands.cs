using System.Linq;

namespace Mjolnir.SDK.Content;

/// <summary>
/// The content module's console commands (<see cref="DevConsole"/>): <c>give</c> puts items into the player's inventory
/// (the game's items and the mods', a recipe teaches the player), <c>items</c> finds an item's asset name.
/// </summary>
internal static class ContentCommands
{
    private const int MaxCount = 9999;
    private const int MaxListed = 40;
    private const int MaxCandidates = 10;

    public static void Register()
    {
        // The platform's own commands, whichever mod's code happens to be running.
        var platform = typeof(ContentCommands).Assembly;
        DevConsole.AddFor(platform, "give", "Gives the player an item: its asset name (Item_Wood_Sticks, or Wood_Sticks), its English name, or a mod's item (<mod id>.<name>).", Give, "<item> [count]");
        DevConsole.AddFor(platform, "items", "Lists the items whose asset name or English name contains the words.", List, "[words]");
    }

    private static void Give(DevCommandContext context)
    {
        if (context.Arguments.Count == 0) throw new DevCommandException("Which item?");
        var (query, count) = ItemSearch.GiveArguments(context.Arguments);
        if (count is < 1 or > MaxCount) throw new DevCommandException($"The count is 1 to {MaxCount}.");
        if (!GameItems.HasPlayer)
        {
            context.Error("No player in the world.");
            return;
        }
        // A mod's item by its full name, as the mod's ModItem names itself.
        var modItem = ContentRegistry.Shared.Items.FirstOrDefault(i => string.Equals(i.ToString(), query, System.StringComparison.OrdinalIgnoreCase));
        var found = ItemSearch.Find(GameItems.Catalog(), modItem?.AssetName ?? query);
        if (found.Count == 0)
        {
            context.Error($"No item matches '{query}'. items <words> lists them.");
            return;
        }
        if (found.Count > 1)
        {
            context.Error($"{found.Count} items match '{query}':");
            foreach (var entry in found.Take(MaxCandidates)) context.Print($"  {Describe(entry)}");
            if (found.Count > MaxCandidates) context.Print($"  and {found.Count - MaxCandidates} more");
            return;
        }
        var item = found[0];
        int given = GameItems.Give(item.Asset, count);
        if (given == count) context.Print($"Gave {given} x {item.Name} ({item.Asset})");
        else context.Error($"Gave {given} x {item.Name} ({item.Asset}): no room for {count - given}");
    }

    private static void List(DevCommandContext context)
    {
        var catalog = GameItems.Catalog();
        if (context.Arguments.Count == 0)
        {
            context.Print($"{catalog.Count} items. items <words> lists those whose asset name or name contains them.");
            return;
        }
        var found = ItemSearch.Containing(catalog, context.GetRest(0));
        if (found.Count == 0) context.Error($"No item matches '{context.GetRest(0)}'.");
        foreach (var entry in found.Take(MaxListed)) context.Print(Describe(entry));
        if (found.Count > MaxListed) context.Print($"and {found.Count - MaxListed} more: add a word");
    }

    private static string Describe(ItemEntry entry) => $"{entry.Asset}  {entry.Name}{(entry.IsRecipe ? "  (recipe)" : "")}";
}
