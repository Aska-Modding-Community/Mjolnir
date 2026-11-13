# Mjolnir

A mod loader and C# modding SDK for **ASKA** (Unity 6000.3, IL2CPP, HDRP).

Mods are .NET 10 assemblies that run on CoreCLR inside the game. They talk to the game through a high-level SDK of
plain C# events, bindable values and builders, and can drop to raw HarmonyX patches when they need to.

> **Status: 0.x.** The public API can change between minor versions; each change is declared in
> `PublicAPI.*.txt`. Everything is tested in single player and when hosting; the mod-compatibility check is tested
> with a host and a client on two machines.

## What a mod can do

| Module | Namespace | What it gives |
|---|---|---|
| Core API | `Mjolnir.SDK` | `Mod` and its lifecycle, manifests and dependencies, TOML config, localization, bindable values, the main-thread `Dispatcher`, character sheets (stats, skills, effects), combatants (health, damage, deaths), developer console commands |
| UI | `Mjolnir.SDK.UI` | Windows, dialogs, HUD elements and markers, notifications, menu buttons and menu levels, tabs in the game's own settings menu and journal, hotkeys, a control set cloned from the game's interface, the in-game Mods window |
| Player | `Mjolnir.SDK.Player` | The local player: spawn and death events, health, stamina, food, water and warmth, stamina costs before they are spent, position and teleport |
| Content | `Mjolnir.SDK.Content` | Items, recipes (by hand and at crafting stations), buildings, status effects, models from AssetBundles, the item catalog |
| AI | `Mjolnir.SDK.AI` | The settlement's villagers, jobs of a mod's own that villagers take by priority among the game's tasks, and interactions of a mod's own that the player and villagers use |
| World | `Mjolnir.SDK.World` | The world's clock: hour, day and night, stopping time |
| Stats | — | No public types: connects `CharacterSheet` to the game |
| Combat | — | No public types: connects `Combatant` and `Combatants` (in `Mjolnir.SDK`) to the game: the player, villagers, pets, animals, enemies and dens; every blow before (change or cancel it) and after it lands, deaths with their killer; damage, healing and kills dealt by a mod |
| Animation | `Mjolnir.SDK.Animation` | Animations of the game and of a mod's own (from AssetBundles made with the Mjolnir kit on the game's skeleton) played on the player and villagers: masks, root motion, emotes, moments of a clip; shown to the other players |
| Net | `Mjolnir.SDK.Net` | The players of a session, message channels between their mods; every session checks that a joining player's mods match the host's |

A mod references `Mjolnir.SDK` and only the modules it uses.

## Installing

1. Download `Mjolnir-<version>.zip` from the releases and unpack it into the game's folder, the one with `Aska.exe`.
2. Start the game from Steam. At the first start, and after each game update, Mjolnir prepares itself for the game's
   build for about half a minute; a progress window shows how far it is.
3. Put mods into `Mjolnir/Mods/`, each in a folder of its own.

Mods are turned on and off in the game: **Main or pause menu → Mjolnir**. The change applies from the next start.

- Start once without mods: launch the game with `--no-mods`, or create a file `Mjolnir/disable`.
- Uninstall: delete `version.dll` from the game's folder.
- Log: `Mjolnir/LogOutput.log`.

## Architecture

```text
Aska.exe
 └─ UnityPlayer.dll ── imports ──► version.dll            Mjolnir.Native (C++): a proxy of the system DLL
                                    │ detours il2cpp_init
                                    ▼
                                  CoreCLR (Mjolnir/dotnet)
                                    │
                                    ▼
                                  Mjolnir.Core             closed runtime: interop, Harmony, mod loading, fault isolation
                                    │ loads mods through internal contracts
                                    ▼
                                  Mjolnir.SDK + modules    the only API mods see
                                    │
                                    ▼
                                  Mjolnir/Mods/<mod>/*.dll
```

### Components

- **Mjolnir.Native** is the `version.dll` placed next to the game. It forwards every real `version.dll` export and
  detours `il2cpp_init` by name; no native address is hardcoded. After the game's runtime is up, it starts CoreCLR
  from Mjolnir's private .NET and calls Core. It does nothing outside `Aska.exe`.
- **Mjolnir.Core** is the closed runtime. It:
  - keeps the interop assemblies for the game's build;
  - starts Il2CppInterop and HarmonyX;
  - scans and loads mods and resolves their dependency graph;
  - isolates faults.

  It knows nothing about the SDK or the game's types; mods never reference its API.
- **Mjolnir.SDK** is the API for mod authors: one main library plus a module per area of the game. Modules reach Core
  only through the main library, never each other. Their public signatures use only .NET, Unity and TextMeshPro
  types, so a rename in the game is fixed inside the SDK, not in every mod.

### Startup phases

| Phase | When | Allowed |
|---|---|---|
| A | Inside `il2cpp_init`, before the engine is up | Logging, config, Harmony patches, registrations (`OnLoad`). **No Unity objects.** |
| B | From the engine's first scene callback | Everything: Unity objects from `OnGameReady` on, then per-frame callbacks |

### Interop assemblies

The C# view of the game's IL2CPP code is generated, never shipped. Mjolnir records what its interop assemblies were
made for: the game's build, the Il2CppInterop version and the generator. When any of them changes, at the first start,
after a game update or after a Mjolnir update, Core runs the generator as a separate process before anything else
loads. The game waits about 30 s and shows no window of its own meanwhile.

The new set takes the old one's place only when it is complete. If the generation fails, or the player chooses
"Start without mods", the game starts without mods and the generation runs again at the next start.

### Mod loading and isolation

- Each first-level folder in `Mjolnir/Mods` gets its own `AssemblyLoadContext` for dependency resolution. The
  platform and the game's types always resolve to one shared copy, so mods can exchange them.
- A mod is the one class deriving from `Mod` with a `[ModManifest]`; an assembly without one is a library.
- Mods are loaded in dependency order. A mod with a missing dependency, or one outside its version range, is skipped,
  and the Mods window shows why.
- An exception from a mod disables that mod only; the game and the other mods keep running. Its Harmony patches stay.
- Mods are never unloaded while the game runs: Harmony patches and injected IL2CPP types cannot be reliably reverted.
- Content ids (items, recipes, buildings, effects) are derived from the mod's id and the content's name, never from
  load order, so they match on every machine.

## Writing a mod

### The project

A mod is a `net10.0` class library compiled against the files of an installed Mjolnir. They are referenced with
`Private="false"`, so the mod ships only its own .dll:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <Version>1.0.0</Version>
    <AskaDir>C:\Program Files (x86)\Steam\steamapps\common\ASKA</AskaDir>
    <MjolnirDir>$(AskaDir)\Mjolnir</MjolnirDir>
  </PropertyGroup>

  <ItemGroup>
    <!-- Always: the SDK, the runtime its Mod type implements, and the engine types its callbacks use. -->
    <Reference Include="$(MjolnirDir)\core\Mjolnir.Core.dll" Private="false" />
    <Reference Include="$(MjolnirDir)\core\Mjolnir.SDK.dll" Private="false" />
    <Reference Include="$(MjolnirDir)\interop\Il2Cppmscorlib.dll" Private="false" />
    <Reference Include="$(MjolnirDir)\interop\UnityEngine.CoreModule.dll" Private="false" />
    <!-- The modules the mod uses. -->
    <Reference Include="$(MjolnirDir)\core\Mjolnir.SDK.Player.dll" Private="false" />
    <Reference Include="$(MjolnirDir)\core\Mjolnir.SDK.UI.dll" Private="false" />
  </ItemGroup>

</Project>
```

`Mjolnir/interop` exists once the game has started with Mjolnir. A mod that patches the game directly also references
`core\0Harmony.dll` and the interop assemblies of the types it patches (`interop\Assembly-CSharp.dll` and others).

### The mod class

```csharp
using Mjolnir.SDK;
using Mjolnir.SDK.Config;
using Mjolnir.SDK.Player;
using Mjolnir.SDK.UI;

namespace MyFirstMod;

[ModManifest("author.myfirstmod", "My First Mod", "1.0.0", Description = "Cheaper sprinting.", Author = "Author")]
public sealed class MyFirstMod : Mod
{
    public override void OnLoad()
    {
        ConfigEntry<float> cost = Config.Bind("Stamina", "CostMultiplier", 0.5f, "Part of the game's stamina cost.")
                                        .WithRange(0f, 1f)
                                        .WithDisplayName("Stamina cost");

        LocalPlayer.Stamina.Draining += (_, e) => e.Amount *= cost.Value;
        LocalPlayer.Spawned += (_, _) => Notify.Message($"Stamina costs {cost.Value:P0}");
        Log.Info("Loaded");
    }
}
```

The setting appears in the Mods window by itself, as a slider, and is saved to
`Mjolnir/Config/author.myfirstmod.toml`.

### Lifecycle

| Callback | When |
|---|---|
| constructor, `OnLoad()` | Phase A, in dependency order: config, patches, event subscriptions, content registration |
| `OnGameReady()` | Once, when Unity objects become available |
| `OnSceneLoaded(scene, mode)` | Each scene load: the main menu, each world |
| `OnUpdate()`, `OnLateUpdate()`, `OnFixedUpdate()` | Every frame / physics step |

Every mod has `Manifest`, `Log` (lines prefixed with the mod's name in `LogOutput.log`), `Config` and a lazy `Harmony`
whose id is the mod's id.

### Manifest and dependencies

```csharp
[ModManifest("author.mymod", "My Mod", "1.2.0", Description = "…", Author = "…",
             Network = NetworkCompatibility.Everyone, Icon = "art/icon.png")]
[ModDependency("author.library", ">=1.0 <2.0")]
[ModDependency("author.extras", "*", Optional = true)]
```

- The id is letters, digits, `.`, `_` and `-`, unique among mods (`author.mod` by convention; `mjolnir` is taken):
  it names the config file, seeds the mod's content ids and prefixes its strings for other mods.
- A version range is a list of bounds that must all hold (`>=`, `>`, `<=`, `<`, `=`); a bare version is a minimum,
  `*` accepts any.
- `Network` declares who needs the mod in multiplayer: `Everyone`, `ClientOnly` or `ServerOnly`.

### The mod's folder

```text
Mjolnir/Mods/MyMod/
├─ MyMod.dll        the mod (and any libraries it brings)
├─ icon.png         its picture in the Mods window; a letter tile without one
├─ lang/            english.toml, russian.toml…: texts by Steam language name, English as the fallback
├─ items/           pictures of its items and effects (PNG)
└─ bundles/         its AssetBundles (models)
```

`lang/*.toml` also translates the Mods window's entries for the mod:

```toml
greeting = "Hello, {0}!"            # Localization.Get("greeting", name)

[mod]
name = "My Mod"
description = "What it does."

[config.Stamina.CostMultiplier]
name = "Stamina cost"

[item.twine]
name = "Twine"
description = "Thin cord."
```

### Rules of the platform

- **No Unity objects in the constructor or `OnLoad`:** the engine is not up yet. Build windows, sprites and textures
  on first use or from `OnGameReady`.
- **The game's objects belong to the main thread.** From another thread, go through
  `Dispatcher.MainThread.Post(...)`. An `await` on the main thread comes back to it.
- **Test an IL2CPP object's type with `TryCast<T>()`, never with `is` or `as`.** Il2CppInterop wraps a returned
  object as the method's declared type.
- **Prefer the SDK's events to patches.** They survive game updates; a direct patch on a game method is the mod's
  responsibility after each update.
- **What a mod adds, it can take away** on the same host: `Hud.Remove`, `MenuButtons.Remove`, `Dispose` of what `Add`
  or `Bind` returned.
- **Models and other assets** are AssetBundles built with Unity 6000.3.12f1 and HDRP, the game's version and
  pipeline. The Unity project in `unity/Mjolnir.ModKit` is set up for it.

### Animations of a mod's own

The Mjolnir kit (`unity/Mjolnir.ModKit`, Unity 6000.3.12f1) has the game's skeletons: start the game with Mjolnir once
(it writes them to `Mjolnir/ModKit`), then **Mjolnir → Rigs → Import from the Game**. They are `ragnar` and `aska` (the player's male and
female characters), `peasant_male` and `peasant_female` (villagers); a clip made on one plays on all of them, since the
bones' names are the same. **Mjolnir → New Mod…** makes the mod's bundle folder, `Assets/Mods/<mod>/<bundle>`.
**Mjolnir → Kit Window** shows whether the kit is ready (each missing step with its button), your mods and the tools.
To see a clip on the game's people rather than on stick figures, turn on **Characters' models for the kit** on the Mjolnir
settings page in the game (the Mjolnir window's Settings): the game then writes their meshes and textures (colour, normal and mask maps, about 160 MB) beside the
skeletons, and Import Rigs dresses the skeletons in them (the default hair and beard on, the others there, switched off).
They are the game's: they stay on your computer, out of git and out of every bundle.
**The game's own animations** come the same way: turn on **The game's animations for the kit** there too, and Import Rigs
brings every clip of the game's people (some 730) into the kit, to choose in Preview Animation and play on its characters
(Save as Asset keeps one for the Animation window). A mod plays them by name (`Animations.Find("aska_wave")`), never ships them: a bundle that would take one in (or a copy
of one) is not built.

A clip gets there one of three ways:

- **Made in Unity** on an imported skeleton, in the Animation window.
- **From Mixamo or another humanoid source:** download the animation as FBX, Without Skin, 30 fps, Keyframe Reduction
  none (a walk with In Place off, to keep its movement), and put it into the mod's bundle folder. The kit sees a
  skeleton that is not the game's and makes the file a humanoid source: it stays out of the bundle, its clip is named
  after the file (Mixamo calls every clip mixamo.com), and the body's turn and height stay in the pose (Bake Into Pose),
  since the game moves a character by a clip only when asked. Then **Mjolnir → Animation → Bake Humanoid Clips to Rig…** records
  it bone by bone on a skeleton of the game, the feet kept on the ground by the clip's own foot targets (In Place
  leaves out the movement forward). Checked with three of Mixamo's spells and attacks: the pose, the turn and the
  crouch as in Mixamo on all four skeletons, and in the game the player crouching into them with the feet on the
  ground. Keep Mixamo's files out of a public repository: Adobe lets you use them in a game, not share them as files.
- **From Blender** (also the way for Maya, 3ds Max and Cascadeur, which only Blender was checked for):
  1. **Mjolnir → Rigs → Export as FBX** writes `Build/Rigs/<skeleton>.fbx`: the bones with the game's names and a stick
     figure skinned to them.
  2. In Blender, File → Import → FBX with its defaults. The skeleton's top bone `master` becomes the armature object
     itself: animate the bones in Pose mode, and move the armature object for root motion (the clip moving the
     character, played with `RootMotion = true`). One action is one clip; its name is the clip's name.
  3. File → Export → FBX, with its defaults (or Object Types: Armature only, for a smaller file), into the bundle
     folder.
  
  The kit fits the file to the game's skeleton as it imports it: the nodes Blender puts above `master` go, the
  armature's movement becomes the clip's root motion, and the clip is named after its action. Blender writes every
  bone, the ones you did not touch at rest; the kit leaves a bone's position, rotation or scale that stays at rest all
  through the clip to the game's own animation, so a clip that only raises an arm moves only the arm, and the
  character walks and breathes under it. **Mjolnir → Animation → Keep Every Bone** (on the selected file) keeps them all, for a
  pose that must hold the rest pose. Checked with Blender 5.2: every bone at the game's rest pose on the first frame,
  a bone turned as it was in Blender, the movement forward kept; in the game the arm raised as in Blender, the player
  carried forward by root motion, and an arm's gesture played while walking with the legs still walking.

**Mjolnir → Animation → New Mask from Clip** makes a mask of exactly the bones a clip moves; played through it, a clip leaves
everything else to the game. A whole-body animation stands the character by default, a masked one lets it walk:
a gesture to play on the move goes through its mask (or `MoveSpeed = 1`).

Then **Mjolnir → Animation → Preview Animation** plays a clip on the game's characters, who stand on a stage of their own (ground, sun and sky) at rest until then, **Mjolnir → Check Mods** finds
bones no skeleton has, and **Mjolnir → Build and Copy to Game** builds the bundle (a model file goes in as its clips,
each under its action's name) and copies it into the mod's folder in the game. In the mod:

```csharp
var wave = Animations.Add("wave", "bundles/anims", "Wave");               // in OnLoad: the bundle, the clip's name
var waveMask = AnimationMask.Add("wave", "bundles/anims", "WaveMask");     // New Mask from Clip
LocalPlayer.Animator.Play(wave, new AnimationOptions { Mask = waveMask }); // the arm waves, the player walks on
```

### Weapons, armour and food of a mod's own

An item is made from one of the game's: a kind's plain item (`ItemKind`: a material, food, a one- or two-handed weapon, a
shield, a bow, arrows, a helmet, a shirt, trousers, boots, gloves, a cape), or one named with `basedOn` for an item like
that one in particular. What makes it the mod's goes on top:

```csharp
// in OnLoad (Mjolnir.SDK.Content)
Items.Add("rune_sword", ItemKind.OneHandedWeapon)
     .WithDamage(14, 18)                                                    // a blow's damage, before the game's own sums
     .WithDurability(800)
     .AppliesOnHit("Effect_Burning", TimeSpan.FromSeconds(4), chance: 0.25f); // on what it hits
Items.Add("rune_hood", ItemKind.Helmet)
     .WithArmor(60)                                                          // counts for whoever wears it
     .WithAttribute("Attribute_ColdResistance", 50)
     .AppliesWhileEquipped("Effect_Energized");                              // while worn or held
Items.Add("honey_cake", ItemKind.Food)
     .AppliesWhenEaten("Effect_Energized", TimeSpan.FromMinutes(2));
```

A game effect is named (the game knows its effects only once it runs, after `OnLoad`); a mod's own effect is passed as
the object `Effects.Add` gave. The item keeps its base's picture and look until it is given an `icon` and a model
(`WithModel`).

### Buildings of a mod's own

A building is the mod's own: its name, texts, picture, model, cost and settings, and a network prefab of its own, which
every player's game makes and registers alike. That prefab is a copy of one of the game's structures, whose look is never
shown: without `basedOn` one of a single stage and no function of its own (`ModBuilding.DefaultBase`); with `basedOn` one
whose function and stages it takes (a storage's room, a light's fire, a cart's harness, a house's three stages).

```csharp
// in OnLoad (Mjolnir.SDK.Content)
var altar = Buildings.Add("altar", icon: "items/altar.png")
                     .WithModel("bundles/models", "Altar")          // its look, from the mod's AssetBundle
                     .WithCategory("Categ_Blueprints_Totems")        // its tab in the build menu
                     .WithGroundLevelling()                          // a plan dug with a hoe first
                     .WithBuildEffort(15)                            // hammer work per stage (the game's: 10)
                     .Needs("Item_Stone_Raw", 4);                    // the first stage's supplies
```

The game builds it as its own: after the plan (if the ground is levelled), each stage waits for its supplies, which the
player or the settlement's builders bring, then for its hammer work; while a stage is under way the model stands in the
game's construction material (or a look of its own, `WithConstructionModel`), and once the last stage is done it shows as
it is. `Needs(item, count, stage)` gives a later stage its supplies (a stage without any is only built); dismantling gives
back what the game gives back of them.

A building of several stages can grow as the game's houses do, each stage a model of its own (all with their origin on
the building's place): the stage under way stands in the construction material, the stages built show as they are, the
stages to come show nothing, and the finished building and the placement preview show them all.

```csharp
Buildings.Add("lodge", basedOn: "Item_Structures_BuilderHutL1")               // three stages: frame, walls, roof
         .WithModel("bundles/lodge", "LodgeFrame", stage: 0)
         .WithModel("bundles/lodge", "LodgeWalls", stage: 1)
         .WithModel("bundles/lodge", "LodgeRoof", stage: 2)
         .WithConstructionModel("bundles/lodge", "LodgeRoofScaffolding", stage: 2) // shown while the roof is built
         .Needs("Item_Wood_RawLog", 4, stage: 0)
         .Needs("Item_Wood_Sticks", 6, stage: 1)
         .Needs("Item_Wood_Bark", 8, stage: 2);
``` A building may stand on another mod building (`basedOn: other.AssetName`), taking
its look, cost and settings unless it gives its own. Its name in the mod is its id: the game's id comes from it and the
mod's id, the same for every player. Its texts are `[building.<name>] name/description/lore` in the mod's lang files.

### Interactions of a mod's own

A building can carry things the player and villagers use, as the game's well does: a mod's own building or one of the
game's. The player sees the game's prompt beside the building's own menu, in the building's card, and holds the interact
key. Villagers use it in a job: they walk up, wait for their turn, and stay at it for a while, playing an animation:

```csharp
// in OnLoad, with the altar above
Interactions.AddToBuildings(altar.AssetName, "altar", a =>                     // Mjolnir.SDK.AI, every altar standing
{
    a.WithText(Localization.Get("altar.pray")).WithHoldTime(TimeSpan.FromSeconds(1.5));
    a.Used += (_, e) => Notify.Message(e.IsPlayer ? "You prayed" : $"{e.Resident!.Name} prayed");
});
Residents.AddJob("pray", r => Interactions.Nearest(r.Position, "altar", 30) != null ? 12 : -1)
         .Use(r => Interactions.Nearest(r.Position, "altar", 30), TimeSpan.FromSeconds(5), "peasant_male_conjuring");
```

`Using` (cancellable) comes before each use, `IsEnabled`, `MaxUsers` and `PlayerCanUse` say who may use it, and
`Interactions.Remove` takes one away; it also goes with its building. `Interactions.Add(gameObject, name)` puts one on a
single object: on a building the player uses it too, on any other object only villagers do. An interaction exists only on
the machine whose mod made it: villagers use it on the host.

### Developer console

The `` ` `` key opens a console with commands from the platform and mods: `give`, `items`, `pos`, `tp`, `time`,
`stats`, `effect`, `villager`, `help`… A mod adds its own:

```csharp
DevConsole.Add("heal", "Restores the player's health", ctx => LocalPlayer.Health.Value = LocalPlayer.Health.Maximum);
```

### Examples

`examples/` holds complete mods, each showing one area:

| Mod | Shows |
|---|---|
| `Mjolnir.InfiniteStamina` | Config and raw Harmony patches |
| `Mjolnir.UiExample` | Every UI control, a window in MVVM style, menu levels, tabs in the game's windows, localization |
| `Mjolnir.PlayerExample` | HUD bars bound to the player's stats, cheaper stamina |
| `Mjolnir.ItemsExample` | An item with its own model, recipes by hand and at a workshop, a building |
| `Mjolnir.StatsExample` | Experience, an effect of the mod's own |
| `Mjolnir.VillagersExample` | A job villagers take: they walk up to the player and greet them |
| `Mjolnir.AltarExample` | A building of its own: its model, category, levelled ground, supplies and construction; the player prays at it |
| `Mjolnir.CheatInventory` | The item catalog in a searchable window |
| `Mjolnir.EquipmentExample` | Items of a kind: a sword that sets on fire, a hood with armour and an effect while worn, a cake |

## Building from source

Requirements:
- Windows with ASKA installed;
- .NET 10 SDK;
- Visual Studio or Build Tools with the C++ x64 compiler (MSVC) and CMake.

```bash
powershell -File tools/gen-interop.ps1        # once: the interop assemblies the projects compile against
powershell -File tools/deploy.ps1 -Examples   # build and install into the game, with the examples
dotnet test tests/Mjolnir.Core.Tests          # unit tests: one project per assembly under tests/
powershell -File tools/package.ps1            # the release archive: artifacts/package/Mjolnir-<version>.zip
```

The scripts find the game in your Steam libraries and the Unity 6000.3.12f1 editor in Unity Hub's install
folders by themselves. To point them elsewhere, pass `-GameDir` (`deploy.ps1`, `gen-interop.ps1`) or `-Unity`
(`build-bundles.ps1`), or set `MJOLNIR_GAME_DIR` / `MJOLNIR_UNITY` once for all of them.

| Path | Contents |
|---|---|
| `src/Mjolnir.Native` | The proxy DLL (C++, CMake) |
| `src/Mjolnir.Core` | The runtime |
| `src/Mjolnir.SDK`, `src/Mjolnir.SDK.*` | The SDK's main library and its modules |
| `tests/` | Unit tests per assembly, and in-game test mods |
| `examples/` | Example mods |
| `tools/` | Build, deploy and packaging scripts, the interop generator, a code map of the game's binary |
| `unity/Mjolnir.ModKit` | The Unity project for mods' AssetBundles |

## License

MIT, see `LICENSE`. The third-party components a release ships, with their licenses, are listed in
`Mjolnir/licenses/THIRD-PARTY-NOTICES.txt` inside the archive. Mjolnir is not affiliated with Sand Sailor Studio.
