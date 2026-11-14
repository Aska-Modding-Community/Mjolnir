# Changelog

The platform has one version for the loader, the SDK and its modules. Mods built against an earlier minor version
must be rebuilt.

## Unreleased

### Added

- **Mjolnir.SDK.Net**, a new module.
  - Every multiplayer session checks that a joining player's mods match the host's: the mods every player needs
    (`NetworkCompatibility.Everyone`) by id and version, and Mjolnir by major and minor version. A player whose mods
    differ does not join and sees which mods to turn on or off and which versions differ. A player without Mjolnir is
    removed when the session needs mods.
  - `Network` and `NetChannel`: the players that passed the check, and message channels between their mods.
- **Mjolnir.SDK.Combat**, a new module, with `Combatant` and `Combatants` in the main library (`LocalPlayer.Combatant`,
  `Resident.Combatant`): everything in the world that takes damage — players, villagers, pets, creatures and dens — with
  its health, faction and team, `Damage(amount, attacker)` as the game's own blow or straight off the health, `Heal` and
  `Kill(killer)`. `Damaging` (cancellable, its amount changeable), `Damaged` and `Died` (the killer: the last to strike
  within 5 s) report every blow and death, the game's and a mod's. The console's `combatants`, `hurt`, `heal` and `kill`.
- **Mjolnir.SDK.Camera**, a new module: `GameCamera` reads the player's camera (position, rotation, field of view, which
  of the game's cameras is in use and when that changes) and the screen (`WorldToScreen`, `ScreenPointToRay`,
  `TryRaycast`). `AddModifier` changes the game's cameras every frame, the modifiers adding up in order of priority over
  the game's own values, and `Take` shows a camera of the mod's own over the game's, blended in and out, standing where
  it is put or following and looking at a transform.
- **Animations**: `CharacterAnimator` (`LocalPlayer.Animator`, `Resident.Animator`) plays the game's clips (`Animations.All`,
  `Animations.Find`) and a mod's own over the character's own animation: fades, loops, speed, moments of a clip
  (`AnimationPlayback.At`), an upper-body mask (`AnimationMask.UpperBody`) under which the legs keep walking, root motion
  through the game's movement, the player held while a whole-body animation plays; the game's emotes (`PlayEmote`); a
  character's bones (`Bone`). Played animations are shown to the other players that run the mod.
  - **Mjolnir.SDK.Animation**, a new module: `Animations.Add` and `AnimationMask.Add` from a mod's AssetBundle; the console's
    `anims`, `anim`, `animstop`, `emote`; the characters' skeletons written to `Mjolnir/ModKit/rigs` for the Mjolnir kit.
  - **Mjolnir.SDK.AI**: `ResidentJob.Play`, a step that plays an animation.
- **Mjolnir.SDK.Terrain**, a new module: `Landscape` reads the ground (height, normal, kind of ground, cleared vegetation,
  the sea) and changes it with the game's own tool (`Raise`, `Lower`, `Level`, `Paint`, `ClearVegetation`, `Check`) over a
  turned rectangle with a blended edge or one of the game's brushes, refusing areas with buildings; the changes are saved
  and sent to the other players as the game's own. `GroundChanging` (cancellable) and `GroundChanged` report every change,
  the player's, villagers', buildings', mods' and those from the network. The console's `ground`, `raise`, `lower`, `level`,
  `paint`, `clearveg`, `smooth` and `undo`.
  - **Tools**: areas of any shape (`LandscapeArea.Circle`, `Polygon`, `Mask`, with a smooth or linear `Falloff`), written by
    the SDK sample by sample and still saved, sent to the other players and reported as the game's own changes;
    `Landscape.Smooth`, `Slope` (a ramp), `Shape` (a mod's own brush), `Measure` (lowest, highest, average) and `Capture`
    giving a `LandscapeSnapshot` (`Restore` for an undo, `PasteAt` to lay the same ground elsewhere, turned and raised).
  - **The ground's cover**: the biomes of the part of the world the player is in (`Landscape.Biomes`, `BiomeAt`,
    `FindBiome`) and `PaintBiome`, which changes what grows on the ground; `RestoreVegetation` gives it its own kind and
    its grass back; `LandscapeOptions.OnlyOn` limits a change to one kind of ground and `KeepKind` leaves the kind as it
    was; `ShowOutline` draws an area on the ground as a preview of a change. A snapshot keeps the biomes too. The console's
    `biome` and `grass`.
  - **The world's objects**: `NaturalObjects.In`, `Around` and `Nearest` give the trees, stones, bushes and nests the world
    grew and the items lying on it, each with its kind, the game's name for it, its health, what gathering it gives and
    whether a change of the ground takes it; `Gather` takes from it as a player picking by hand, and `Remove`, `Settle` and
    `MoveTo` take it away or move it; `LandscapeOptions.Objects` leaves the objects of an area standing or settles them on
    the new ground. The console's `objects`.
  - **Levelling fields**: `LevellingFields.Place` sets down the game's own levelling plan for the villagers to work, `All`
    and `Nearest` find the fields of the loaded world, and a `LevellingField` tells its tiles, the height they go to and
    what is left to level, and can be levelled at once (`LevelNow`), finished (`Finish`) or taken away (`Remove`). The
    console's `field`.
- **Mjolnir.SDK.Content**
  - Weapons, armour and food of a mod's own: `Items.Add(name, ItemKind kind, icon, stackSize)` stands an item on a plain
    game item of its kind — material, food, one- and two-handed weapons, shields, bows, arrows and every piece of armour —
    so a mod needs no game item to copy. `WithDamage`, `WithArmor`, `WithDurability` and `WithAttribute` set its numbers,
    and `AppliesOnHit`, `AppliesWhileEquipped` and `AppliesWhenEaten` put a status effect on whoever is struck, holds it
    or eats it.
  - Buildings in full: a model of the mod's own for the building or for each stage, and the look while a stage is under way
    (`WithModel`, `WithConstructionModel`); supplies per stage (`Needs(item, count, stage)`, `PartsAt`); the build menu's
    tab (`WithCategory`); the ground levelled before the first stage (`WithGroundLevelling`); the hammer work a stage takes
    (`WithBuildEffort`); and another building of the mod as the base.
- **Mjolnir.SDK.AI**: interactions of a mod's own. `Interactions.AddToBuildings(building, name, setUp)` gives every building
  of a kind its own prompt beside the game's, and `Interactions.Add(gameObject, name)` gives one object its own; the text,
  icon, hold time, distance and how many villagers use it at once belong to each. `Using` (cancellable) and `Used` report
  who used it, and `Interactions.Nearest` with the job step `Use` sends villagers to one. The console's `interactions`.
- **Mjolnir kit**: New Mod, Build and Copy to Game, Render Item Icons (a model drawn into an item's picture), Check Mods
  (shaders, pivots, scripts, bone paths), Preview Animation (a clip on every skeleton), New Mask, Export Rigs as FBX (for
  Blender, Maya, 3ds Max), Bake Humanoid Clips to Rig (Mixamo or motion capture onto the game's skeleton); the kit sets up
  the linear color space HDRP needs.
  - The Kit Window lists what the kit still needs, each missing step with the button that fixes it, and the mods it holds
    with their bundles and whether the game has them.
  - A clip animated in another program is fitted to the game's skeleton as it is imported, and a bone the clip leaves at
    rest keeps the game's own animation under it; New Mask from Clip makes a mask of exactly the bones a clip moves, and
    Keep Every Bone keeps every bone for a pose that must hold the whole body.
  - The game writes what the kit needs into `Mjolnir/ModKit/`: the characters' skeletons always, and, with the two settings
    on Mjolnir's page, their models and the game's own clips, to animate and preview on them.

### Changed

- A mod is `NetworkCompatibility.ClientOnly` by default, not `Everyone`: a mod of one player's own (interface,
  camera, hotkeys) no longer keeps anyone out of a multiplayer session. A mod that adds content (items, recipes,
  buildings, effects) or changes the game's rules declares `Network = NetworkCompatibility.Everyone` in its
  `[ModManifest]`, as the content examples now do, and only such mods are compared when a player joins.
- A building's `basedOn` is optional: without it a building is the mod's own on the game's plain base, instead of a game
  building wearing the mod's name.
- The game writes the kit's files into `Mjolnir/ModKit/` instead of `Mjolnir/rigs`, and takes the old folder away.

### Not yet

- A client joining another player's world: every module's own work there is unchecked (villagers' jobs, stat modifiers,
  terrain changes). The mod check and mod messages are checked between a host and a client.
- Felling a tree with its loot, and plants or stones of a mod's own growing in the world.
- Worn models for armour, and buildings with a function of their own.

## 0.3.0 — 2026-11-07

### Added

- **Loader**
  - Interop assemblies are generated on the player's machine at the first start, after a game update and after a
    Mjolnir update. The generator runs as a separate process with a progress window; the player can start the game
    without mods instead.
  - Mods can be turned on and off in the Mods window, from the next start. The window shows why a mod did not load.
  - The SDK starts even when no mod is loaded, so the Mods window is always there.
- **Mjolnir.SDK**
  - Localization: `Localization`, `TranslatedText`, `lang/<language>.toml` next to a mod, the game's own strings
    overridable from a mod.
  - Character sheets for the player and villagers: `CharacterSheet`, `CharacterStat` with modifiers, `Skill`,
    `CharacterEffects`, `Effects`.
  - Developer console registry: `DevConsole`, `DevCommand`, `DevCommandContext`.
  - `KeyGesture` as a config type, `IReadOnlyBindable<T>`, the main thread's `SynchronizationContext`.
  - Manifest: `Description`, `Author`, `Icon`; config metadata `WithDisplayName`, `WithOrder`, `WithRestartRequired`.
- **Mjolnir.SDK.UI**
  - Controls: `UiExpander`, `UiScrollViewer`, `UiGrid`, `UiProgressRing`, `UiHotKeyBox`, `UiMenuItem`, `UiTabControl`,
    `UiListBox`, `UiKeyHint`, `UiSlot`, `UiCard`, `UiWrapPanel`, `UiLayoutTransformControl`.
  - Menu levels in the main and pause menus, tabs in the game's settings menu and journal (`Menus.Settings`,
    `Menus.Journal`), tooltips, HUD markers (`Hud.AddAt`), counting notifications (`Notify.Counter`).
  - Awaited dialogs (`ConfirmAsync`, `MessageAsync`, `PromptAsync`), hotkeys (`HotKeys`), the fluent layer
    (`WithBinding`, `OnClick`…).
  - Window styles (`WindowStyle.Menu`, `WindowStyle.Dialog`), `UiWindow.Footer`, sizes (`MinWidth`…`MaxHeight`,
    `SizeToContent`), inherited `FontSize`, `Padding`, `Opacity`, and the player's interface scale (`InterfaceScale`).
  - The developer console's window.
- **Mjolnir.SDK.Player**: `LocalPlayer` (spawn, death, position, teleport), its stats, `Stamina.Draining`.
- **Mjolnir.SDK.Content**: items, recipes by hand and at crafting stations, buildings, status effects of a mod's own,
  item models from AssetBundles, the item catalog (`Items.All`, `Items.Categories`).
- **Mjolnir.SDK.AI**: `Residents` and jobs of a mod's own that villagers take by priority (`Residents.AddJob`).
- **Mjolnir.SDK.World**: the world's clock (`WorldTime`).
- **Mjolnir.SDK.Stats**: connects character sheets to the game.
- **Tools**: `unity/Mjolnir.ModKit` for mods' AssetBundles, `tools/package.ps1` for the release archive; the scripts
  find the game and the Unity editor by themselves.

### Changed (breaking)

| 0.2.0 | 0.3.0 |
|---|---|
| `MenuHook`, `Menus.Main` / `Menus.Pause` returning it | `MenuButtons` |
| `UiCustom` | `UiUnityHost` |
| `UiSelector` | `UiStepper` |
| `SettingControl.For` | `SettingControls.For` |
| `UiWindow.IsVisible` | `UiWindow.IsOpen` |
| `UiTextBlock.FontSize`, `UiBorder.Padding` | `Control.FontSize`, `Control.Padding` (on every control) |
| `IBindable<T>.Changed` | declared on `IReadOnlyBindable<T>` |
| `UiToggleSwitch.IsCheckedChanged` (`EventHandler`) | carries `ValueChangedEventArgs<bool>` |
| `RangeValueChangedEventArgs.OldValue` / `NewValue` | inherited from `ValueChangedEventArgs<double>` |
| `Dialog.Confirm` / `Dialog.Message` with `"OK"` defaults | button texts default to the player's language (`null`) |

### Not yet

- A client joining a host: the mod-compatibility handshake and mod messages between players.
- Models of a mod's own for buildings.

## 0.2.0 — 2026-10-22

- The SDK split into a main library and modules; the UI module became its own assembly.
- Events follow one convention; what a mod adds can be removed; the public API is declared and documented.

## 0.1.0

- The loader: the native proxy, CoreCLR, Il2CppInterop and HarmonyX, mod loading with dependencies and fault
  isolation; the SDK's `Mod`, config and the first UI.
