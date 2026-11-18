using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Mjolnir.SDK.Terrain;

/// <summary>
/// The terrain module's console commands (<see cref="DevConsole"/>): <c>ground</c> tells the ground under the player (or a
/// point); <c>raise</c>, <c>lower</c>, <c>level</c>, <c>paint</c>, <c>clearveg</c>, <c>grass</c>, <c>smooth</c> and
/// <c>biome</c> change a square in front of the player, through <see cref="Landscape"/> like a mod's change, and <c>undo</c>
/// puts the last of them back; <c>objects</c> tells what the world grew round the player, <c>field</c> works the game's
/// levelling fields, <c>road</c> its road plans, and <c>protect</c> keeps a square from the player's tools and the villagers.
/// </summary>
internal static class TerrainCommands
{
    /// <summary>
    /// How far in front of the player a square's middle is, in metres: its near edge and blend 2.5 m clear of the player's
    /// feet (the game refuses a change around a character standing in it, seen in game).
    /// </summary>
    public static float AheadOf(float size) => size / 2 + 1f + 2.5f;

    /// <summary>The square's side when none is given, in metres.</summary>
    public const float DefaultSize = 4f;

    // The ground of the last square the console changed, as it was before (main thread).
    private static LandscapeSnapshot? s_before;

    // The platform's own commands, whichever mod's code happens to be running.
    public static void Register()
    {
        var owner = typeof(TerrainCommands).Assembly;
        DevConsole.AddFor(owner, "ground", "Shows the ground under the player or at a point: its height, kind, slope and the water.", Ground, "[x z]");
        DevConsole.AddFor(owner, "raise", "Raises a square in front of the player.", c => Step(c, LandscapeOperation.Raise), "[metres=0.5] [size=4]");
        DevConsole.AddFor(owner, "lower", "Digs a square in front of the player.", c => Step(c, LandscapeOperation.Lower), "[metres=0.5] [size=4]");
        DevConsole.AddFor(owner, "level", "Levels a square in front of the player to a height (by default the player's feet).", Level, "[height|~offset] [size=4]");
        DevConsole.AddFor(owner, "paint", "Makes a square in front of the player natural, dirt, path or road.", Paint, "<natural|dirt|bedrock|path|road> [size=4]");
        DevConsole.AddFor(owner, "clearveg", "Clears the grass and bushes of a square in front of the player.", ClearVegetation, "[size=4]");
        DevConsole.AddFor(owner, "grass", "Gives a square in front of the player its natural ground and grass back.", Grass, "[size=4]");
        DevConsole.AddFor(owner, "smooth", "Smooths a square in front of the player.", Smooth, "[strength=0.5] [size=4]");
        DevConsole.AddFor(owner, "biome", "Lists the biomes here, or gives a square in front of the player one of them.", Biome, "[name|number] [size=8]");
        DevConsole.AddFor(owner, "objects", "Lists the trees, stones and plants of the world round the player, clears them, or harvests them with their loot.", Objects, "[radius=8] [name] | clear [radius=4] | harvest [radius=4] [player]");
        DevConsole.AddFor(owner, "field", "Lists the game's levelling fields, or places, levels, finishes or takes away the nearest one.", Field, "[place [size=8]|level|finish|remove]");
        DevConsole.AddFor(owner, "road", "Lists the game's road plans, or lays one ahead of the player, or widens, builds, paves or takes away the nearest one.", Road, "[place [length=16] [dirt]|width <metres>|build|level|remove]");
        DevConsole.AddFor(owner, "protect", "Protects a square in front of the player from the player's tools, the villagers and other mods, or lists or ends the console's zones.", Protect, "[size=8|list|clear]");
        DevConsole.AddFor(owner, "undo", "Puts back the ground the console's last change changed.", Undo);
    }

    private static void Ground(DevCommandContext context)
    {
        float x, z;
        if (context.Arguments.Count >= 2)
        {
            x = context.GetFloat(0);
            z = context.GetFloat(1);
        }
        else
        {
            var (position, _) = Player();
            x = position.x;
            z = position.z;
        }
        if (!Landscape.IsLoaded(x, z)) throw new DevCommandException(Invariant($"({x:0.#}, {z:0.#}) is not in the loaded part of the world."));
        var normal = Landscape.NormalAt(x, z);
        double slope = Math.Acos(Math.Clamp(normal.y, -1f, 1f)) * 180 / Math.PI;
        string water = Landscape.IsUnderwater(x, z) ? Invariant($", under water ({Landscape.WaterLevel:0.##} m)") : "";
        string cleared = Landscape.IsCleared(x, z) ? ", vegetation cleared" : "";
        string biome = Landscape.BiomeAt(x, z) is { } b ? $", biome {b}" : "";
        context.Print(Invariant($"({x:0.#}, {z:0.#}): height {Landscape.HeightAt(x, z):0.##} m, {Landscape.KindAt(x, z).ToString().ToLowerInvariant()}, slope {slope:0}°{cleared}{biome}{water}."));
    }

    private static void Step(DevCommandContext context, LandscapeOperation operation)
    {
        float metres = context.GetFloat(0, 0.5f);
        if (!(metres > 0 && metres <= Landscape.MaximumStep)) throw new DevCommandException(Invariant($"A step is over 0 and at most {Landscape.MaximumStep} m."));
        var area = Square(context, 1);
        Keep(area);
        var result = operation == LandscapeOperation.Raise ? Landscape.Raise(area, metres) : Landscape.Lower(area, metres);
        Answer(context, result, Invariant($"{(operation == LandscapeOperation.Raise ? "Raised" : "Lowered")} {area} by {metres:0.##} m"));
    }

    private static void Level(DevCommandContext context)
    {
        var (position, _) = Player();
        float height = context.Arguments.Count > 0 ? ParseHeight(context.GetString(0), position.y) : position.y;
        if (!(height >= 0 && height <= Landscape.MaximumHeight)) throw new DevCommandException(Invariant($"A height is from 0 to {Landscape.MaximumHeight} m."));
        var area = Square(context, 1);
        Keep(area);
        Answer(context, Landscape.Level(area, height), Invariant($"Levelled {area} to {height:0.##} m"));
    }

    private static void Paint(DevCommandContext context)
    {
        string word = context.GetString(0);
        if (!Enum.TryParse(word, true, out GroundKind kind) || !Enum.IsDefined(kind)) throw new DevCommandException($"'{word}' is no kind of ground: natural, dirt, bedrock, path or road.");
        var area = Square(context, 1);
        Keep(area);
        Answer(context, Landscape.Paint(area, kind), $"Made {area} {kind.ToString().ToLowerInvariant()}");
    }

    private static void ClearVegetation(DevCommandContext context)
    {
        var area = Square(context, 0);
        Keep(area);
        Answer(context, Landscape.ClearVegetation(area), $"Cleared {area}");
    }

    private static void Grass(DevCommandContext context)
    {
        var area = Square(context, 0);
        Keep(area);
        Answer(context, Landscape.RestoreVegetation(area), $"Gave {area} its grass back");
    }

    private static void Biome(DevCommandContext context)
    {
        if (context.Arguments.Count == 0)
        {
            var (position, _) = Player();
            string here = Landscape.BiomeAt(position.x, position.z) is { } at ? at.ToString() : "unknown";
            var all = Landscape.Biomes;
            context.Print($"Biome here: {here}. This part of the world grows: {(all.Count == 0 ? "nothing known" : string.Join(", ", all))}.");
            return;
        }
        var biome = ParseBiome(context.GetString(0), Landscape.Biomes);
        var area = Square(context, 1, BiomeSize);
        Keep(area);
        Answer(context, Landscape.PaintBiome(area, biome), $"Made {area} {biome}");
    }

    /// <summary>The square's side for <c>biome</c> when none is given: two of the biome map's cells.</summary>
    public const float BiomeSize = 2 * Landscape.BiomeCellSize;

    /// <summary>A biome typed by its name in this part of the world (any case) or by its number.</summary>
    public static LandscapeBiome ParseBiome(string text, IReadOnlyList<LandscapeBiome> biomes)
    {
        foreach (var biome in biomes)
            if (string.Equals(biome.Name, text, StringComparison.OrdinalIgnoreCase)) return biome;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && id >= 0)
        {
            foreach (var biome in biomes)
                if (biome.Id == id) return biome;
            return new LandscapeBiome(id, Invariant($"biome {id}"));
        }
        throw new DevCommandException($"'{text}' is no biome of this part of the world: {(biomes.Count == 0 ? "none known" : string.Join(", ", biomes.Select(b => b.Name)))}, or a number.");
    }

    private static void Smooth(DevCommandContext context)
    {
        float strength = context.GetFloat(0, 0.5f);
        if (!(strength > 0 && strength <= 1)) throw new DevCommandException("A strength is over 0 and at most 1.");
        var area = Square(context, 1);
        Keep(area);
        Answer(context, Landscape.Smooth(area, strength), Invariant($"Smoothed {area} by {strength:0.##}"));
    }

    private static void Objects(DevCommandContext context)
    {
        if (context.Arguments.Count > 0 && context.GetString(0).Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            float reach = context.GetFloat(1, ClearRadius);
            if (!(reach > 0 && reach <= NaturalObjects.MaximumRadius)) throw new DevCommandException(Invariant($"A radius is over 0 and at most {NaturalObjects.MaximumRadius} m."));
            var (at, _) = Player();
            var standing = NaturalObjects.Around(at, reach).Where(o => o.Kind != NaturalObjectKind.Item).ToList();
            int cleared = standing.Count(o => o.Clear());
            context.Print(Invariant($"Cleared {cleared} of {standing.Count} within {reach:0.#} m."));
            return;
        }
        if (context.Arguments.Count > 0 && context.GetString(0).Equals("harvest", StringComparison.OrdinalIgnoreCase))
        {
            float reach = context.Arguments.Count > 1 && !context.GetString(1).Equals("player", StringComparison.OrdinalIgnoreCase) ? context.GetFloat(1, ClearRadius) : ClearRadius;
            if (!(reach > 0 && reach <= NaturalObjects.MaximumRadius)) throw new DevCommandException(Invariant($"A radius is over 0 and at most {NaturalObjects.MaximumRadius} m."));
            bool player = context.Arguments.Skip(1).Any(a => a.Equals("player", StringComparison.OrdinalIgnoreCase));
            var (at, _) = Player();
            var options = new HarvestOptions { AsPlayer = player };
            var harvestable = NaturalObjects.Around(at, reach).Where(o => o.IsHarvestable && o.Kind != NaturalObjectKind.Item).ToList();
            int harvested = harvestable.Count(o => o.Harvest(options));
            context.Print(Invariant($"Harvested {harvested} of {harvestable.Count} within {reach:0.#} m{(player ? " as the player" : "")}."));
            return;
        }
        float radius = context.GetFloat(0, 8f);
        if (!(radius > 0 && radius <= NaturalObjects.MaximumRadius)) throw new DevCommandException(Invariant($"A radius is over 0 and at most {NaturalObjects.MaximumRadius} m."));
        string? name = context.Arguments.Count > 1 ? context.GetString(1) : null;
        var (position, _) = Player();
        var found = NaturalObjects.Around(position, radius);
        if (name != null) found = found.Where(o => o.AssetName.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (found.Count == 0)
        {
            context.Print(Invariant($"Nothing{(name != null ? $" like '{name}'" : "")} within {radius:0.#} m."));
            return;
        }
        context.Print(Invariant($"{found.Count} within {radius:0.#} m: ") + string.Join(", ", found.GroupBy(o => o.Kind).Select(g => Invariant($"{g.Count()} {g.Key.ToString().ToLowerInvariant()}"))) + ".");
        foreach (var o in found.Take(ListedObjects))
        {
            var at = o.Position;
            context.Print(Invariant($"  {o.AssetName} ({o.Kind.ToString().ToLowerInvariant()}) at ({at.x:0.#}, {at.z:0.#}), {Math.Sqrt((at.x - position.x) * (at.x - position.x) + (at.z - position.z) * (at.z - position.z)):0.#} m away, {at.y - (Landscape.TryGetHeight(at.x, at.z, out float ground) ? ground : at.y):+0.00;-0.00;0.00} m over the ground{(o.Health > 0 ? Invariant($", health {o.Health:0.#}") : "")}{(o.Quantity > 0 ? Invariant($", {o.Quantity} left") : "")}{(o.ResourceName.Length > 0 ? $", gives {o.ResourceName}" : "")}{(o.IsRemovable ? ", a change of the ground takes it" : "")}"));
        }
        if (found.Count > ListedObjects) context.Print(Invariant($"  … and {found.Count - ListedObjects} more."));
    }

    /// <summary>How far round the player the console's <c>objects clear</c> clears when no radius is given, in metres.</summary>
    public const float ClearRadius = 4f;

    /// <summary>How many of the objects found the console names one by one.</summary>
    public const int ListedObjects = 10;

    private static void Field(DevCommandContext context)
    {
        var (position, _) = Player();
        string what = context.Arguments.Count > 0 ? context.GetString(0).ToLowerInvariant() : "list";
        if (what == "place")
        {
            float size = context.GetFloat(1, FieldSize);
            if (!(size > 0 && size <= LandscapeArea.MaximumSide)) throw new DevCommandException(Invariant($"A size is over 0 and at most {LandscapeArea.MaximumSide} m."));
            var area = Square(context, -1, size);
            var placed = LevellingFields.Place(area);
            if (placed == null) context.Error("No field placed (see the log).");
            else context.Print($"Placed a {placed}.");
            return;
        }
        if (what == "list")
        {
            var all = LevellingFields.All;
            if (all.Count == 0)
            {
                context.Print("No levelling fields in the loaded world.");
                return;
            }
            context.Print(Invariant($"{all.Count} levelling field(s):"));
            foreach (var one in all.Take(ListedObjects)) context.Print(Invariant($"  {one}, {Math.Sqrt((one.Center.x - position.x) * (one.Center.x - position.x) + (one.Center.z - position.z) * (one.Center.z - position.z)):0.#} m away"));
            return;
        }
        if (what is not ("level" or "finish" or "remove")) throw new DevCommandException($"'{what}' is nothing a field does: place, level, finish or remove.");
        var field = LevellingFields.Nearest(position, 32f) ?? throw new DevCommandException("No levelling field within 32 m.");
        switch (what)
        {
            case "level":
                int levelled = field.LevelNow();
                context.Print(Invariant($"Levelled {levelled} of {field.Tiles} tiles of the field at ({field.Center.x:0.#}, {field.Center.z:0.#})."));
                break;
            case "finish":
                context.Print(field.Finish() ? "The field is finished." : "The field could not be finished (see the log).");
                break;
            case "remove":
                context.Print(field.Remove() ? "The field is taken away." : "The field could not be taken away (see the log).");
                break;
        }
    }

    /// <summary>The side of the field the console places when none is given, in metres.</summary>
    public const float FieldSize = 8f;

    private static void Road(DevCommandContext context)
    {
        var (position, forward) = Player();
        string what = context.Arguments.Count > 0 ? context.GetString(0).ToLowerInvariant() : "list";
        if (what == "place")
        {
            float length = context.GetFloat(1, RoadLength);
            if (!(length >= RoadPlans.SectionLength && length <= RoadPlans.MaximumSections * RoadPlans.SectionLength))
                throw new DevCommandException(Invariant($"A length is from {RoadPlans.SectionLength} to {RoadPlans.MaximumSections * RoadPlans.SectionLength} m."));
            var kind = context.Arguments.Count > 2 && context.GetString(2).Equals("dirt", StringComparison.OrdinalIgnoreCase) ? GroundKind.Dirt : GroundKind.Road;
            float fl = MathF.Sqrt(forward.x * forward.x + forward.z * forward.z);
            float fx = fl > 0 ? forward.x / fl : 0, fz = fl > 0 ? forward.z / fl : 1;
            Vector3 from = default, to = default;
            from.x = position.x + fx * RoadStart;
            from.z = position.z + fz * RoadStart;
            to.x = from.x + fx * length;
            to.z = from.z + fz * length;
            var placed = RoadPlans.Place([from, to], kind);
            if (placed == null) context.Error("No road plan laid (see the log).");
            else context.Print($"Laid a {placed}.");
            return;
        }
        if (what == "list")
        {
            var all = RoadPlans.All;
            if (all.Count == 0)
            {
                context.Print("No road plans in the loaded world.");
                return;
            }
            context.Print(Invariant($"{all.Count} road plan(s):"));
            foreach (var one in all.Take(ListedObjects)) context.Print($"  {one}");
            return;
        }
        if (what is not ("width" or "build" or "level" or "remove")) throw new DevCommandException($"'{what}' is nothing a road plan does: place, width, build, level or remove.");
        var plan = RoadPlans.Nearest(position, 32f) ?? throw new DevCommandException("No road plan within 32 m.");
        switch (what)
        {
            case "width":
                if (context.Arguments.Count < 2) throw new DevCommandException(Invariant($"Give the width, from {RoadPlans.MinimumWidth} to {RoadPlans.MaximumWidth} m."));
                float width = context.GetFloat(1, 0f);
                if (!(width >= RoadPlans.MinimumWidth && width <= RoadPlans.MaximumWidth)) throw new DevCommandException(Invariant($"A width is from {RoadPlans.MinimumWidth} to {RoadPlans.MaximumWidth} m."));
                plan.Width = width;
                context.Print(Invariant($"The {plan} levels {plan.Width:0.#} m now."));
                break;
            case "build":
                context.Print(plan.Build() ? $"Built the {plan}." : "The road plan could not be built (see the log).");
                break;
            case "level":
                int sections = plan.Sections;
                context.Print(Invariant($"Paved {plan.LevelNow()} of {sections} sections."));
                break;
            case "remove":
                context.Print(plan.Remove() ? "The road plan is taken away." : "The road plan could not be taken away (see the log).");
                break;
        }
    }

    /// <summary>The length of the road plan the console lays when none is given, in metres.</summary>
    public const float RoadLength = 16f;

    /// <summary>How far in front of the player the console's road plan starts, in metres (clear of their feet).</summary>
    public const float RoadStart = 3f;

    // The zones the console made, which it lists and ends (main thread).
    private static readonly List<LandscapeZone> s_zones = [];

    private static void Protect(DevCommandContext context)
    {
        string what = context.Arguments.Count > 0 ? context.GetString(0).ToLowerInvariant() : "";
        if (what == "list")
        {
            s_zones.RemoveAll(z => !z.IsActive);
            if (s_zones.Count == 0) context.Print("The console protects no ground.");
            foreach (var zone in s_zones) context.Print($"  {zone.Area}");
            return;
        }
        if (what == "clear")
        {
            int count = s_zones.Count(z => z.IsActive);
            foreach (var zone in s_zones) zone.Dispose();
            s_zones.Clear();
            context.Print(Invariant($"Ended {count} zone(s)."));
            return;
        }
        var area = Square(context, 0, ZoneSize);
        var made = Landscape.ProtectFor(typeof(TerrainCommands).Assembly, area);
        s_zones.Add(made);
        context.Print($"Protected {area}.");
    }

    /// <summary>The side of the square the console protects when none is given, in metres.</summary>
    public const float ZoneSize = 8f;

    private static void Undo(DevCommandContext context)
    {
        if (s_before is not { } before) throw new DevCommandException("Nothing to undo.");
        var result = before.Restore();
        if (result == LandscapeResult.Done) s_before = null;
        Answer(context, result, $"Put back {before.Area}");
    }

    // The square's ground before the console changes it, for undo (a snapshot of the blend too).
    private static void Keep(LandscapeArea area) => s_before = Landscape.Capture(area);

    /// <summary>Forgets the last change, between unit tests.</summary>
    internal static void ResetForTests()
    {
        s_before = null;
        s_zones.Clear();
    }

    /// <summary>A height typed as a number, or ~ and an offset from <paramref name="feet"/> (~ alone: the feet).</summary>
    public static float ParseHeight(string text, float feet)
    {
        if (text.StartsWith('~'))
        {
            if (text.Length == 1) return feet;
            if (float.TryParse(text.AsSpan(1), NumberStyles.Float, CultureInfo.InvariantCulture, out float offset)) return feet + offset;
        }
        else if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float height)) return height;
        throw new DevCommandException($"'{text}' is no height: 40, ~ or ~-1.5.");
    }

    // The square in front of the player, turned as the player faces, its side the argument at sizeIndex.
    private static LandscapeArea Square(DevCommandContext context, int sizeIndex, float defaultSize = DefaultSize)
    {
        float size = sizeIndex < 0 ? defaultSize : context.GetFloat(sizeIndex, defaultSize);
        if (!(size > 0 && size <= LandscapeArea.MaximumSide)) throw new DevCommandException(Invariant($"A size is over 0 and at most {LandscapeArea.MaximumSide} m."));
        var (position, forward) = Player();
        float length = MathF.Sqrt(forward.x * forward.x + forward.z * forward.z);
        float fx = length > 0 ? forward.x / length : 0, fz = length > 0 ? forward.z / length : 1;
        Vector3 center = default;
        float ahead = AheadOf(size);
        center.x = position.x + fx * ahead;
        center.y = position.y;
        center.z = position.z + fz * ahead;
        float rotation = (float)(Math.Atan2(fx, fz) * 180 / Math.PI);
        return LandscapeArea.Rectangle(center, size, size, rotation, 1f);
    }

    private static (Vector3 Position, Vector3 Forward) Player()
    {
        if (!Landscape.IsAvailable || Landscape.Source?.TryGetPlayer(out var position, out var forward) != true) throw new DevCommandException("No world loaded.");
        return (position, forward);
    }

    private static void Answer(DevCommandContext context, LandscapeResult result, string done)
    {
        if (result == LandscapeResult.Done) context.Print(done + ".");
        else context.Error($"Not changed: {Describe(result)}.");
    }

    /// <summary>Why a change was not made, in the console's words.</summary>
    public static string Describe(LandscapeResult result) => result switch
    {
        LandscapeResult.Queued => "it waits for the next frame",
        LandscapeResult.NotAvailable => "no world loaded",
        LandscapeResult.NotLoaded => "the square is not all in the loaded part of the world",
        LandscapeResult.Blocked => "a building stands there, or the game refuses the place",
        LandscapeResult.TooSteep => "too steep",
        LandscapeResult.TooHigh => "higher than the game allows",
        LandscapeResult.Underwater => "under water",
        LandscapeResult.Cancelled => "a mod cancelled it",
        _ => "the game failed (see the log)",
    };

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
