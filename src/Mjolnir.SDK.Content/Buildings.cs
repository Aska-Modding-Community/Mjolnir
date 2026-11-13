using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Mjolnir.SDK.Content;

/// <summary>
/// Buildings of a mod's own, in the build menu, built as the game's own are.
/// <para>A building is the mod's own: its name, id, texts, picture, model and settings, and a network prefab of its own,
/// which every player's game makes and registers alike (a guid from the mod id and the name), so the structure in the world
/// is the mod's object, not a game structure's. The prefab is a copy of a game structure's, whose function and stages the
/// building takes and whose look is taken away entirely: without <c>basedOn</c> a structure of one stage and no function of
/// its own (<see cref="ModBuilding.DefaultBase"/>); with it a structure whose function and stages the building takes (a
/// storage's room, a light's fire, a house's three stages: the asset name of a structure such as <c>Item_StoneLantern</c>,
/// or the <see cref="ModBuilding.AssetName"/> of a mod's building added before). It is placed, built and sent to the other
/// players as that structure is, and saved by its own id. Add buildings in <see cref="Mod.OnLoad"/>: the players of a
/// session register their prefabs in the same order, which a building added later may not keep. The mod gives it everything else: its name and id, texts, picture, model (<see
/// cref="ModBuilding.WithModel(string, string)"/>, or one per stage, <see cref="ModBuilding.WithModel(string, string, int)"/>), the supplies of each stage (<see cref="ModBuilding.Needs(string, int, int)"/>), the work
/// each stage takes, its build menu category and whether the ground is levelled first.</para>
/// <para>How the game builds one: the player places it; a building that levels the
/// ground first shows a plan the player digs with a hoe; then each stage in turn waits for its supplies, which the player or
/// the settlement's builders bring, and for its work, which they do with a hammer; after the last stage it stands
/// finished. While a stage is under way the building shows its construction look in the game's construction material.</para>
/// <para>Its texts come from the mod's language files, <c>[building.&lt;name&gt;] name</c>, <c>description</c> (the line
/// under its name in the build menu) and <c>lore</c> (the text below), in the player's language, else in English. The
/// player knows it from the start: a new game's player and a player of an older save alike. A building put up stays in the
/// save by its id, the same for every player and start (a hash of the mod id and the name). Buildings are added in
/// <see cref="Mod.OnLoad"/>.</para>
/// </summary>
/// <remarks>
/// Items, recipes and buildings are entries of the game's item database, found there by id and by asset name. One whose id or
/// asset name falls on content added before it (of this mod or another: the asset names of mod <c>a_b</c>'s item <c>c</c>
/// and of mod <c>a</c>'s item <c>b_c</c> are one) is left out of the game: an error in the log names both, and the mod goes
/// on. Another name gives another id and asset name.
/// </remarks>
/// <example><code>
/// var altar = Buildings.Add("altar", icon: "items/altar.png")
///                      .WithModel("bundles/models", "Altar")
///                      .WithCategory("Categ_Blueprints_Totems")
///                      .WithGroundLevelling()
///                      .Needs("Item_Stone_Raw", 4);
/// </code></example>
public static class Buildings
{
    /// <summary>
    /// Adds a building of the calling mod. <paramref name="name"/> is letters, digits and underscores, unique among the
    /// mod's items and buildings: the mod's own id for it, from which its id in the game comes. <paramref name="basedOn"/>
    /// is the structure that carries it (above; <see cref="ModBuilding.DefaultBase"/> without one). <paramref name="icon"/> is a PNG or JPG file relative to the mod's folder (the
    /// base building's picture without one). Adding the same name again gives the building already added.
    /// </summary>
    /// <exception cref="ArgumentException">The name is not letters, digits and underscores, or it is taken by an item
    /// of the mod or by a building of the mod made from another building, or the building would stand on itself.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ModBuilding Add(string name, string? basedOn = null, string? icon = null) =>
        ContentRegistry.Shared.AddBuilding(CallingMod.Of(Assembly.GetCallingAssembly()), name, basedOn ?? ModBuilding.DefaultBase, icon);
}

/// <summary>A building of a mod, added with <see cref="Buildings.Add"/>.</summary>
public sealed class ModBuilding
{
    /// <summary>The work a construction stage of the game takes, in hammer work (each hit adds some).</summary>
    public const float DefaultBuildEffort = 10f;

    /// <summary>
    /// The structure that carries a building added without <c>basedOn</c>: the game's decorative birch, one construction
    /// stage, no function of its own, placed without levelling the ground; its look is never shown.
    /// </summary>
    public const string DefaultBase = "Item_Decoration_DecorativeBirch";

    private readonly object _lock = new();
    private readonly List<(int Stage, RecipePart Part)> _parts = [];
    private readonly SortedDictionary<int, (string Path, string Asset)> _stageModels = [], _stageConstruction = [];
    private readonly Action _changed;
    private readonly string? _folder;
    private string? _modelPath, _modelAsset, _constructionPath, _constructionAsset, _category;
    private float? _buildEffort;
    private bool? _levelsGround;
    private int _version;

    internal ModBuilding(string modId, string name, string basedOn, string? iconPath, string? folder, ModBuilding? baseBuilding, Action changed)
    {
        _changed = changed;
        _folder = folder;
        ModId = modId;
        Name = name;
        BasedOn = basedOn;
        BaseBuilding = baseBuilding;
        IconPath = iconPath;
        Id = ContentIds.BuildingId(modId, name);
        AssetName = ContentIds.BuildingAssetName(modId, name);
    }

    /// <summary>The mod that added it.</summary>
    public string ModId { get; }

    /// <summary>Its name in the mod (its texts are <c>[building.&lt;name&gt;]</c> in the mod's language files).</summary>
    public string Name { get; }

    /// <summary>The building it stands on, by asset name: a game structure or a mod's building.</summary>
    public string BasedOn { get; }

    /// <summary>The mod's building it stands on; null when it stands on a game structure.</summary>
    internal ModBuilding? BaseBuilding { get; }

    /// <summary>The game structure under it all: its own base, or its base building's.</summary>
    internal string GameBase => BaseBuilding?.GameBase ?? BasedOn;

    /// <summary>Its picture's file (full path); null for the base building's picture.</summary>
    public string? IconPath { get; }

    /// <summary>Its id in the game: negative, from the mod id and the name, the same for every player.</summary>
    public int Id { get; }

    /// <summary>Its asset name in the game: <c>Item_&lt;mod id&gt;_&lt;name&gt;</c>.</summary>
    public string AssetName { get; }

    /// <summary>The AssetBundle of its model for every stage (full path); null while it has its base's look.</summary>
    public string? ModelPath
    {
        get
        {
            lock (_lock) return _modelPath ?? BaseBuilding?.ModelPath;
        }
    }

    /// <summary>The name of its model, a prefab in <see cref="ModelPath"/>; null while it has its base's look.</summary>
    public string? ModelAsset
    {
        get
        {
            lock (_lock) return _modelPath != null ? _modelAsset : BaseBuilding?.ModelAsset;
        }
    }

    /// <summary>The AssetBundle of its construction look (full path); null when the model stands for it.</summary>
    public string? ConstructionModelPath
    {
        get
        {
            lock (_lock) return _constructionPath ?? BaseBuilding?.ConstructionModelPath;
        }
    }

    /// <summary>The name of its construction look, a prefab in <see cref="ConstructionModelPath"/>.</summary>
    public string? ConstructionModelAsset
    {
        get
        {
            lock (_lock) return _constructionPath != null ? _constructionAsset : BaseBuilding?.ConstructionModelAsset;
        }
    }

    /// <summary>Its build menu category, the game's asset name; null for its base's.</summary>
    public string? Category
    {
        get
        {
            lock (_lock) return _category ?? BaseBuilding?.Category;
        }
    }

    /// <summary>The work each of its stages takes; null for its base's (the game's is <see cref="DefaultBuildEffort"/>).</summary>
    public float? BuildEffort
    {
        get
        {
            lock (_lock) return _buildEffort ?? BaseBuilding?.BuildEffort;
        }
    }

    /// <summary>Whether the ground is levelled with a hoe before it is built; null as its base does.</summary>
    public bool? LevelsGround
    {
        get
        {
            lock (_lock) return _levelsGround ?? BaseBuilding?.LevelsGround;
        }
    }

    /// <summary>
    /// The looks of its stages (<see cref="WithModel(string, string, int)"/>), by stage; its base building's while it has
    /// none of its own, and empty for a building with one model for every stage.
    /// </summary>
    internal IReadOnlyList<StageLook> StageLooks
    {
        get
        {
            lock (_lock)
            {
                if (_stageModels.Count == 0 && _stageConstruction.Count == 0) return BaseBuilding?.StageLooks ?? [];
                return [.. _stageModels.Keys.Union(_stageConstruction.Keys).Order().Select(stage =>
                {
                    _stageModels.TryGetValue(stage, out var model);
                    _stageConstruction.TryGetValue(stage, out var construction);
                    return new StageLook(stage, model.Path, model.Asset, construction.Path, construction.Asset);
                })];
            }
        }
    }

    /// <summary>Whether it has a look of the mod's own: one model, or models of its stages.</summary>
    internal bool HasOwnLook => ModelPath != null || StageLooks.Count > 0;

    /// <summary>Grows with each change, so the game side and buildings standing already take it.</summary>
    internal int Version
    {
        get
        {
            lock (_lock) return _version + (BaseBuilding?.Version ?? 0);
        }
    }

    /// <summary>
    /// Gives the building a look of the mod's own: the prefab <paramref name="asset"/> (its name) in the AssetBundle
    /// <paramref name="bundle"/>, a file relative to the mod's folder, built for the game's Unity (6000.3.12f1, Windows, HDRP)
    /// with the Mjolnir kit; its materials use the game's shaders, such as <c>HDRP/Lit</c>. Its origin stands on the
    /// building's place, on the ground. The model is put on the building's own prefab, so the placement preview and every
    /// structure in the world carry it, and the base's meshes are hidden. While a stage is under way the model stands
    /// in the game's construction material (or the construction look, <see cref="WithConstructionModel(string, string)"/>), and it shows as
    /// it is once the building is finished; the player's look outlines it. Its solid colliders are the model's own (the
    /// base's are switched off), and the footprint and the boxes the player's look finds are fitted round the model. For a
    /// building that grows stage by stage, give each stage its model instead (<see cref="WithModel(string, string, int)"/>).
    /// Returns the building.
    /// </summary>
    public ModBuilding WithModel(string bundle, string asset)
    {
        string path = PathOf(bundle, asset);
        lock (_lock)
        {
            if (_modelPath == path && _modelAsset == asset) return this;
            _modelPath = path;
            _modelAsset = asset;
            _version++;
        }
        _changed();
        return this;
    }

    /// <summary>
    /// Gives a construction stage (0 the first) a model of its own: what that stage builds, the prefab
    /// <paramref name="asset"/> in the AssetBundle <paramref name="bundle"/>, its origin on the building's place as the
    /// building's model's. The building then grows as the game's houses do: the stage under way shows its model in the game's
    /// construction material (or its construction look, <see cref="WithConstructionModel(string, string, int)"/>), the
    /// stages built show theirs as they are, and the stages to come show nothing; once finished it shows every stage's
    /// model, as the placement preview does. A stage without a model shows nothing of its own. The stages are its base's (the
    /// game's houses have three or four, its decorations one): a stage the base lacks is left out,
    /// with a warning in the log. A building with stage models has no other model: one given with
    /// <see cref="WithModel(string, string)"/> is left out, with a warning. Returns the building.
    /// </summary>
    /// <example><code>
    /// Buildings.Add("lodge", basedOn: "Item_Structures_BuilderHutL1") // three stages: frame, walls, roof
    ///          .WithModel("bundles/lodge", "LodgeFrame", stage: 0)
    ///          .WithModel("bundles/lodge", "LodgeWalls", stage: 1)
    ///          .WithModel("bundles/lodge", "LodgeRoof", stage: 2)
    ///          .WithConstructionModel("bundles/lodge", "LodgeRoofScaffolding", stage: 2)
    ///          .Needs("Item_Wood_RawLog", 4, stage: 0)
    ///          .Needs("Item_Wood_Sticks", 6, stage: 1)
    ///          .Needs("Item_Wood_Bark", 8, stage: 2);
    /// </code></example>
    /// <exception cref="ArgumentOutOfRangeException">The stage is negative.</exception>
    public ModBuilding WithModel(string bundle, string asset, int stage) => WithStageLook(_stageModels, bundle, asset, stage);

    /// <summary>
    /// Gives a construction stage (0 the first) a construction look of its own (scaffolding, a frame round what is built):
    /// the prefab <paramref name="asset"/> in the AssetBundle <paramref name="bundle"/>, shown in the game's construction
    /// material while that stage is under way, instead of its model (<see cref="WithModel(string, string, int)"/>); with no
    /// model for the stage, the stage shows it while under way and nothing once built. Returns the building.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The stage is negative.</exception>
    public ModBuilding WithConstructionModel(string bundle, string asset, int stage) => WithStageLook(_stageConstruction, bundle, asset, stage);

    /// <summary>
    /// Gives the building a construction look of its own (scaffolding, a frame): the prefab <paramref name="asset"/> in the
    /// AssetBundle <paramref name="bundle"/>, shown in the game's construction material while a stage is under way, instead
    /// of the model (<see cref="WithModel(string, string)"/>, which it needs). A building with stage models takes each
    /// stage's construction look instead (<see cref="WithConstructionModel(string, string, int)"/>). Returns the building.
    /// </summary>
    public ModBuilding WithConstructionModel(string bundle, string asset)
    {
        string path = PathOf(bundle, asset);
        lock (_lock)
        {
            if (_constructionPath == path && _constructionAsset == asset) return this;
            _constructionPath = path;
            _constructionAsset = asset;
            _version++;
        }
        _changed();
        return this;
    }

    /// <summary>
    /// Puts the building in a category of the build menu: the game's asset name of one, such as
    /// <c>Categ_Blueprints_Decoration</c>, <c>Categ_Blueprints_Structures</c>, <c>Categ_Blueprints_Storage</c>,
    /// <c>Categ_Blueprints_Furniture</c>, <c>Categ_Blueprints_Totems</c> or <c>Categ_Blueprints_ExteriorLights</c>; its
    /// base's without one, or when the game has no such category (a warning in the log). Returns the building.
    /// </summary>
    public ModBuilding WithCategory(string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        lock (_lock)
        {
            if (_category == category) return this;
            _category = category;
            _version++;
        }
        _changed();
        return this;
    }

    /// <summary>
    /// The hammer work each construction stage takes: the game's stages take <see cref="DefaultBuildEffort"/>, more is
    /// longer, less shorter. Returns the building.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Not more than 0.</exception>
    public ModBuilding WithBuildEffort(float effort)
    {
        if (!(effort > 0) || float.IsInfinity(effort)) throw new ArgumentOutOfRangeException(nameof(effort), effort, "a work of more than 0");
        lock (_lock)
        {
            if (_buildEffort == effort) return this;
            _buildEffort = effort;
            _version++;
        }
        _changed();
        return this;
    }

    /// <summary>
    /// Whether the ground is levelled before the building is built: true places a plan the player digs with a hoe first, as
    /// the game's lanterns, workshops and houses; false starts its first stage as it is placed, as the game's decorations.
    /// Without it the building does as its base does; called without an argument, it levels the ground. Returns the
    /// building.
    /// </summary>
    public ModBuilding WithGroundLevelling(bool levels = true)
    {
        lock (_lock)
        {
            if (_levelsGround == levels) return this;
            _levelsGround = levels;
            _version++;
        }
        _changed();
        return this;
    }

    /// <summary>What it costs, every stage's in the order given; empty while it costs what its base costs.</summary>
    public IReadOnlyList<RecipePart> Parts
    {
        get
        {
            lock (_lock)
            {
                if (_parts.Count == 0 && BaseBuilding != null) return BaseBuilding.Parts;
                return [.. _parts.Select(p => p.Part)];
            }
        }
    }

    /// <summary>What one construction stage costs (0 the first), in the order given.</summary>
    public IReadOnlyList<RecipePart> PartsAt(int stage)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stage);
        lock (_lock)
        {
            if (_parts.Count == 0 && BaseBuilding != null) return BaseBuilding.PartsAt(stage);
            return [.. _parts.Where(p => p.Stage == stage).Select(p => p.Part)];
        }
    }

    /// <summary>The number of stages it has supplies for (the last with a part, plus one); 0 while it costs its base's.</summary>
    public int StagesWithParts
    {
        get
        {
            lock (_lock)
            {
                if (_parts.Count == 0 && BaseBuilding != null) return BaseBuilding.StagesWithParts;
                return _parts.Count == 0 ? 0 : _parts.Max(p => p.Stage) + 1;
            }
        }
    }

    /// <summary>
    /// Adds to its first stage's supplies an item by asset name (a game item such as <c>Item_Stone_Raw</c>); returns the
    /// building. Once it has a part, the building costs only what its parts say, whatever its base costs: a stage without
    /// parts is only built. Dismantling it gives back what the game gives back of its supplies.
    /// </summary>
    public ModBuilding Needs(string item, int count) => Needs(item, count, 0);

    /// <summary>Adds a mod's item to its first stage's supplies; returns the building.</summary>
    public ModBuilding Needs(ModItem item, int count)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Needs(item.AssetName, count, 0);
    }

    /// <summary>
    /// Adds to the supplies of a construction stage (0 the first) an item by asset name; returns the building. The stages
    /// are its base's (the game's houses have three: foundation, walls, roof; its decorations one): a stage the base lacks
    /// is left out, with a warning in the log.
    /// </summary>
    public ModBuilding Needs(string item, int count, int stage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentOutOfRangeException.ThrowIfNegative(stage);
        lock (_lock)
        {
            _parts.Add((stage, new RecipePart(item, count)));
            _version++;
        }
        _changed(); // the game side gives a building made already its new cost
        return this;
    }

    /// <summary>Adds a mod's item to the supplies of a construction stage (0 the first); returns the building.</summary>
    public ModBuilding Needs(ModItem item, int count, int stage)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Needs(item.AssetName, count, stage);
    }

    public override string ToString() => $"{ModId}.{Name}";

    private ModBuilding WithStageLook(SortedDictionary<int, (string Path, string Asset)> looks, string bundle, string asset, int stage)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stage);
        string path = PathOf(bundle, asset);
        lock (_lock)
        {
            if (looks.TryGetValue(stage, out var known) && known == (path, asset)) return this;
            looks[stage] = (path, asset);
            _version++;
        }
        _changed();
        return this;
    }

    private string PathOf(string bundle, string asset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundle);
        ArgumentException.ThrowIfNullOrWhiteSpace(asset);
        return ContentRegistry.Resolve(_folder, bundle);
    }
}

/// <summary>A construction stage's look: its model and its construction look, either of them null.</summary>
internal readonly record struct StageLook(int Stage, string? ModelPath, string? ModelAsset, string? ConstructionPath, string? ConstructionAsset);
