using System;
using System.Reflection;
using HarmonyLib;
using Mjolnir.Core.Lifecycle;
using Mjolnir.Core.Mods;
using Mjolnir.SDK.Config;
using UnityEngine.SceneManagement;

namespace Mjolnir.SDK;

/// <summary>
/// Base class of a mod: one non-abstract subclass per mod assembly, marked with <see cref="ModManifestAttribute"/>
/// and having a public parameterless constructor. Mjolnir creates it and calls the virtual methods below.
/// An exception from any of them disables the whole mod (its Harmony patches stay), never the game.
/// </summary>
public abstract class Mod : IModEntry
{
    private readonly string _configPath;
    private Harmony? _harmony;
    private ModConfig? _config;

    protected Mod()
    {
        // Injected by the loader (ModContext.Create) for the duration of this constructor, so Manifest, Log and
        // Config already work in a mod's own constructor.
        var context = ModContext.Creating ?? throw new InvalidOperationException(
            $"{GetType().FullName}: mods are created by Mjolnir's mod loader");
        Log = new ModLogger(context.Mod.Name); // first: the manifest reports an icon it cannot find there
        Manifest = new ModManifest(context.Mod, GetType().GetCustomAttribute<ModManifestAttribute>(inherit: false), Log.Warn);
        ModNetwork.Track(Manifest);   // before OnLoad: content added there raises the mod to Everyone
        _configPath = context.ConfigPath;
    }

    /// <summary>What the loader knows about this mod: id, name, version, dependencies.</summary>
    public ModManifest Manifest { get; }

    /// <summary>The mod's log, prefixed with its name.</summary>
    public ModLogger Log { get; }

    /// <summary>
    /// Raw HarmonyX for the mod's own patches ([HarmonyPatch], Prefix/Postfix, <c>Harmony.PatchAll()</c> in
    /// <see cref="OnLoad"/>). Created on first use; its id is the mod id.
    /// </summary>
    public Harmony Harmony => _harmony ??= new Harmony(Manifest.Id);

    /// <summary>
    /// The mod's config, Mjolnir/Config/&lt;id&gt;.toml, read on first use. Its settings also appear in the game's Mods
    /// window (main menu and pause menu), where the player can change them.
    /// </summary>
    public ModConfig Config => _config ??= ModConfig.Load(_configPath, Manifest.ToString(), Log.Warn);

    /// <summary>The config if the mod ever used it; reading <see cref="Config"/> would create an empty file.</summary>
    internal ModConfig? ConfigIfUsed => _config;

    /// <summary>
    /// Phase A, inside il2cpp_init: the Unity engine is not up yet. Apply Harmony patches, read config, register
    /// things; do not create Unity objects. Mods are loaded in dependency order.
    /// </summary>
    public virtual void OnLoad() { }

    /// <summary>Once, when phase B starts (the engine's first scene callback): Unity objects may be created.</summary>
    public virtual void OnGameReady() { }

    /// <summary>Every frame (Unity Update).</summary>
    public virtual void OnUpdate() { }

    /// <summary>Every frame (Unity LateUpdate).</summary>
    public virtual void OnLateUpdate() { }

    /// <summary>Every physics step (Unity FixedUpdate).</summary>
    public virtual void OnFixedUpdate() { }

    /// <summary>Every scene load (Unity SceneManager.sceneLoaded), the first scene included.</summary>
    public virtual void OnSceneLoaded(Scene scene, LoadSceneMode mode) { }

    void IModEntry.Load()
    {
        OnLoad();
        _config?.SaveIfChanged();
        ModRegistry.Add(this);
    }

    void IModEntry.Attach()
    {
        // Only overridden callbacks are subscribed, so a mod without OnUpdate costs nothing per frame. The
        // delegates point at the mod's own overrides: a throwing callback disables this mod, not the SDK.
        var type = GetType();
        if (Overrides(type, nameof(OnGameReady))) GameEvents.GameReady += OnGameReady;
        if (Overrides(type, nameof(OnUpdate))) GameEvents.Update += OnUpdate;
        if (Overrides(type, nameof(OnLateUpdate))) GameEvents.LateUpdate += OnLateUpdate;
        if (Overrides(type, nameof(OnFixedUpdate))) GameEvents.FixedUpdate += OnFixedUpdate;
        if (Overrides(type, nameof(OnSceneLoaded), typeof(Scene), typeof(LoadSceneMode))) GameEvents.SceneLoaded += OnSceneLoaded;
        GameEvents.ApplicationQuit += SaveConfigOnQuit;
        SdkModules.EnsureStarted(); // the SDK's modules: the UI module adds the Mods window
    }

    private void SaveConfigOnQuit() => _config?.SaveIfChanged();

    private static bool Overrides(Type type, string name, params Type[] parameters) =>
        type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, parameters)?.DeclaringType != typeof(Mod);
}
