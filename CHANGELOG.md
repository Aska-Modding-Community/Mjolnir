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
- **Animations**: `CharacterAnimator` (`LocalPlayer.Animator`, `Resident.Animator`) plays the game's clips (`Animations.All`,
  `Animations.Find`) and a mod's own over the character's own animation: fades, loops, speed, moments of a clip
  (`AnimationPlayback.At`), an upper-body mask (`AnimationMask.UpperBody`) under which the legs keep walking, root motion
  through the game's movement, the player held while a whole-body animation plays; the game's emotes (`PlayEmote`); a
  character's bones (`Bone`). Played animations are shown to the other players that run the mod.
  - **Mjolnir.SDK.Animation**, a new module: `Animations.Add` and `AnimationMask.Add` from a mod's AssetBundle; the console's
    `anims`, `anim`, `animstop`, `emote`; the characters' skeletons written to `Mjolnir/rigs` for the Mjolnir kit.
  - **Mjolnir.SDK.AI**: `ResidentJob.Play`, a step that plays an animation.
  - **Mjolnir kit**: Mjolnir → Import Rigs (the game's skeletons from the player's own game), Mjolnir's masks.
- **Mjolnir kit**: New Mod, Build and Copy to Game, Render Item Icons (a model drawn into an item's picture), Check Mods
  (shaders, pivots, scripts, bone paths), Preview Animation (a clip on every skeleton), New Mask, Export Rigs as FBX (for
  Blender, Maya, 3ds Max), Bake Humanoid Clips to Rig (Mixamo or motion capture onto the game's skeleton); the kit sets up the
  linear color space HDRP needs.
- **Mjolnir.SDK.Terrain**, a new module: `Landscape` reads the ground (height, normal, kind of ground, cleared vegetation,
  the sea) and changes it with the game's own tool (`Raise`, `Lower`, `Level`, `Paint`, `ClearVegetation`, `Check`) over a
  turned rectangle with a blended edge or one of the game's brushes, refusing areas with buildings; the changes are saved
  and sent to the other players as the game's own. `GroundChanging` (cancellable) and `GroundChanged` report every change,
  the player's, villagers', buildings', mods' and those from the network. The console's `ground`, `raise`, `lower`, `level`,
  `paint`, `clearveg`, `smooth` and `undo`.
- **Terrain tools**: areas of any shape (`LandscapeArea.Circle`, `Polygon`, `Mask`, with a smooth or linear `Falloff`),
  written by the SDK sample by sample and still saved, sent to the other players and reported as the game's own changes;
  `Landscape.Smooth`, `Slope` (a ramp), `Shape` (a mod's own brush), `Measure` (lowest, highest, average) and
  `Capture` → `LandscapeSnapshot` (`Restore` for an undo, `PasteAt` to lay the same ground elsewhere, turned and raised).

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
