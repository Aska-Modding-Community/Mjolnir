using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Mjolnir.SDK.Content;

/// <summary>
/// The content mods added (no Unity: unit-tested): items, recipes and buildings in the order they were added, by mod.
/// The game side reads it when it builds the item database, and hears of later additions.
/// </summary>
/// <remarks>
/// Items, recipes and buildings are all entries of the game's item database, which keys them by id and finds them by asset
/// name: two of them with one id or one asset name would stand for each other. Ids come from the mod id and the name (a
/// save keeps them), so they never change: content whose id or asset name another mod's content (or the mod's own) took first
/// is left out of the game, with an error in the log naming both, and its mod goes on (an exception would disable the whole
/// mod over one name). Effects have ids and names of their own, kept apart the same way.
/// </remarks>
internal sealed class ContentRegistry
{
    private readonly object _lock = new();
    private readonly List<ModItem> _items = [];
    private readonly List<ModRecipe> _recipes = [];
    private readonly List<ModBuilding> _buildings = [];
    private readonly List<ModEffect> _effects = [];
    private readonly List<object> _refused = []; // content left out of the game for an id or a name taken
    private readonly Dictionary<int, ModItem> _itemsById = [];
    private readonly Dictionary<int, ModBuilding> _buildingsById = [];
    // The item database's ids and asset names taken, by what took them; the effects' apart.
    private readonly Dictionary<int, object> _ids = [];
    private readonly Dictionary<string, object> _assetNames = new(StringComparer.Ordinal);
    private readonly Dictionary<int, ModEffect> _effectIds = [];
    private readonly Dictionary<string, ModEffect> _effectNames = new(StringComparer.Ordinal);
    private readonly Func<Assembly, (string Id, string? Folder)?> _owner;
    private int _version;

    public ContentRegistry(Func<Assembly, (string Id, string? Folder)?> owner) => _owner = owner;

    /// <summary>The registry the public API fills: a mod is found by its assembly as its strings are.</summary>
    public static ContentRegistry Shared { get; } = new(OwnerOf);

    /// <summary>Content was added or a recipe changed: the game side applies it (if the database exists already).</summary>
    public event Action? Updated;

    /// <summary>Grows with every change, so the game side does nothing while it stays the same.</summary>
    public int Version => Volatile.Read(ref _version);

    public IReadOnlyList<ModItem> Items
    {
        get
        {
            lock (_lock) return [.. _items];
        }
    }

    public IReadOnlyList<ModEffect> Effects
    {
        get
        {
            lock (_lock) return [.. _effects];
        }
    }

    public IReadOnlyList<ModRecipe> Recipes
    {
        get
        {
            lock (_lock) return [.. _recipes];
        }
    }

    public IReadOnlyList<ModBuilding> Buildings
    {
        get
        {
            lock (_lock) return [.. _buildings];
        }
    }

    /// <summary>A mod's item in the game by its id; null for the game's own and for content left out.</summary>
    public ModItem? ItemById(int id)
    {
        lock (_lock) return _itemsById.GetValueOrDefault(id);
    }

    /// <summary>A mod's building in the game by its id; null for the game's own and for content left out.</summary>
    public ModBuilding? BuildingById(int id)
    {
        lock (_lock) return _buildingsById.GetValueOrDefault(id);
    }

    /// <summary>Whether this content was left out of the game for an id or an asset name taken.</summary>
    public bool IsLeftOut(object content)
    {
        lock (_lock) return _refused.Contains(content);
    }

    public ModItem AddItem(Assembly caller, string name, string basedOn, string? icon, int? stackSize, ItemKind? kind = null)
    {
        var (modId, folder) = Check(caller, name, basedOn, () =>
        {
            if (stackSize is <= 0) throw new ArgumentOutOfRangeException(nameof(stackSize), stackSize, "a stack holds at least one");
        });
        string? iconPath = icon == null ? null : Resolve(folder, icon);
        ModItem item;
        string? clash;
        lock (_lock)
        {
            if (Known<ModItem>(i => i.ModId == modId && i.Name == name) is { } known)
            {
                if (known.BasedOn != basedOn) throw new ArgumentException($"{known} is made from {known.BasedOn} already", nameof(name));
                return known;
            }
            if (Known<ModBuilding>(b => b.ModId == modId && b.Name == name) != null) throw new ArgumentException($"{modId}.{name} is a building of the mod already", nameof(name));
            item = new ModItem(modId, name, basedOn, iconPath, stackSize, folder, Changed, kind);
            clash = Take(item, item.Id, item.AssetName);
            if (clash == null)
            {
                _items.Add(item);
                _itemsById[item.Id] = item;
            }
        }
        return Added(item, clash);
    }

    public ModRecipe AddRecipe(Assembly caller, ModItem result, int quantity, string basedOn)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        ArgumentException.ThrowIfNullOrWhiteSpace(basedOn);
        var (modId, _) = Owner(caller);
        ModRecipe recipe;
        string? clash;
        lock (_lock)
        {
            // One recipe per result and mod: the result itself, not its name (another mod's item may share it).
            if (Known<ModRecipe>(r => r.ModId == modId && r.Result.Id == result.Id) is { } known) return known;
            recipe = new ModRecipe(modId, result, quantity, basedOn, Changed);
            if (_refused.Contains(result))
            {
                clash = $"{recipe} makes {result}, which is left out of the game";
                _refused.Add(recipe);
            }
            else clash = Take(recipe, recipe.Id, recipe.AssetName);
            if (clash == null) _recipes.Add(recipe);
        }
        return Added(recipe, clash);
    }

    public ModBuilding AddBuilding(Assembly caller, string name, string basedOn, string? icon)
    {
        var (modId, folder) = Check(caller, name, basedOn);
        string? iconPath = icon == null ? null : Resolve(folder, icon);
        ModBuilding building;
        string? clash;
        lock (_lock)
        {
            if (Known<ModBuilding>(b => b.ModId == modId && b.Name == name) is { } known)
            {
                if (known.BasedOn != basedOn) throw new ArgumentException($"{known} is made from {known.BasedOn} already", nameof(name));
                return known;
            }
            if (Known<ModItem>(i => i.ModId == modId && i.Name == name) != null) throw new ArgumentException($"{modId}.{name} is an item of the mod already", nameof(name));
            // A mod's building may stand on another one added before it (its look, cost and settings unless it gives its own).
            if (basedOn == ContentIds.BuildingAssetName(modId, name)) throw new ArgumentException($"{modId}.{name} cannot stand on itself", nameof(basedOn));
            var baseBuilding = _buildings.FirstOrDefault(b => b.AssetName == basedOn);
            building = new ModBuilding(modId, name, basedOn, iconPath, folder, baseBuilding, Changed);
            clash = Take(building, building.Id, building.AssetName);
            if (clash == null)
            {
                _buildings.Add(building);
                _buildingsById[building.Id] = building;
            }
        }
        return Added(building, clash);
    }

    public ModEffect AddEffect(Assembly caller, string name, string basedOn, string? icon)
    {
        var (modId, folder) = Check(caller, name, basedOn);
        string? iconPath = icon == null ? null : Resolve(folder, icon);
        ModEffect effect;
        string? clash = null;
        lock (_lock)
        {
            if (Known<ModEffect>(e => e.ModId == modId && e.Name == name) is { } known)
            {
                if (known.BasedOn != basedOn) throw new ArgumentException($"{known} is made from {known.BasedOn} already", nameof(name));
                return known;
            }
            effect = new ModEffect(modId, name, basedOn, iconPath, Changed);
            // Ids are short (the network's 16 bits), so two effects meet on one more often than items do.
            if (_effectIds.TryGetValue(effect.Id, out var byId)) clash = $"{effect} has the id of {byId}";
            else if (_effectNames.TryGetValue(effect.AssetName, out var byName)) clash = $"{effect} has the asset name {effect.AssetName} of {byName}";
            if (clash == null)
            {
                _effects.Add(effect);
                _effectIds[effect.Id] = effect;
                _effectNames[effect.AssetName] = effect;
            }
            else _refused.Add(effect);
        }
        if (clash != null)
        {
            // The effect added first keeps its id; this one stays out of the game, and its mod goes on.
            SdkLog.Error(ContentLog.Source, $"Effects: {clash}, so it is left out of the game: take another name");
            return effect;
        }
        Changed();
        return effect;
    }

    public void Changed()
    {
        Interlocked.Increment(ref _version);
        Updated?.Invoke();
    }

    /// <summary>
    /// A file of the calling mod: relative to its folder (as given without one: the unit tests); null (logged as
    /// <paramref name="what"/> ignored) when the caller is no mod.
    /// </summary>
    public string? TryPathOf(Assembly caller, string file, string what)
    {
        if (_owner(caller) is { } owner) return Resolve(owner.Folder, file);
        SdkLog.Warn(ContentLog.Source, $"{what} ignored: {caller.GetName().Name} is not a mod, and only a mod's files are found");
        return null;
    }

    /// <summary>A file relative to a mod's folder, or as given without one.</summary>
    public static string Resolve(string? folder, string file) => folder == null ? file : Path.GetFullPath(Path.Combine(folder, file));

    // What every kind of content checks first: its name, its base, then (after the kind's own checks) its mod.
    private (string Id, string? Folder) Check(Assembly caller, string name, string basedOn, Action? own = null)
    {
        if (!ContentIds.IsValidName(name)) throw new ArgumentException($"'{name}' is not a name of letters, digits and underscores", nameof(name));
        ArgumentException.ThrowIfNullOrWhiteSpace(basedOn);
        own?.Invoke();
        return Owner(caller);
    }

    // Content of a kind added already, in the game or left out (under the lock).
    private T? Known<T>(Func<T, bool> match) where T : class
    {
        IEnumerable<T> added = typeof(T) == typeof(ModItem) ? (IEnumerable<T>)_items
            : typeof(T) == typeof(ModRecipe) ? (IEnumerable<T>)_recipes
            : typeof(T) == typeof(ModBuilding) ? (IEnumerable<T>)_buildings
            : (IEnumerable<T>)_effects;
        return added.Concat(_refused.OfType<T>()).FirstOrDefault(match);
    }

    // The id and asset name of an entry of the item database, taken for it (under the lock); why not when another has them.
    private string? Take(object content, int id, string assetName)
    {
        string? clash = _ids.TryGetValue(id, out var byId) ? $"{content} has the id {id} of {byId}"
            : _assetNames.TryGetValue(assetName, out var byName) ? $"{content} has the asset name {assetName} of {byName}"
            : null;
        if (clash != null)
        {
            _refused.Add(content);
            return clash;
        }
        _ids[id] = content;
        _assetNames[assetName] = content;
        return null;
    }

    private T Added<T>(T content, string? clash)
    {
        if (clash != null)
        {
            SdkLog.Error(ContentLog.Source, $"Items: {clash}, so it is left out of the game: take another name");
            return content;
        }
        Changed();
        return content;
    }

    private (string Id, string? Folder) Owner(Assembly caller) =>
        _owner(caller) ?? throw new InvalidOperationException($"{caller.GetName().Name} is not a mod: only a mod adds content");

    private static (string Id, string? Folder)? OwnerOf(Assembly assembly)
    {
        var strings = ModStringsRegistry.ForAssembly(assembly);
        if (strings == null) return null;
        string location = assembly.Location;
        return (strings.ModId, location.Length > 0 ? Path.GetDirectoryName(location) : null);
    }
}
