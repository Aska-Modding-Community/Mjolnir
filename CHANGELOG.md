# Changelog

The platform has one version for the loader, the SDK and its modules. Mods built against an earlier minor version
must be rebuilt.

## 0.4.7 — 2026-11-22

### Added

- `NaturalObjects.KeepActive(center)` → `ActivationPoint` (`Center`, `IsActive`, `Dispose`): the game keeps the world's
  items up round the point as it does round the character — kinds placed by mods, the trees the villagers plant, what
  lies on the ground. The game brings them up only near the character, so a tool whose camera looks at the world far
  from it saw none of them, and `NaturalObjects.Around` found none of them there. The point is moved from any thread,
  kept through a change of world and let go when disposed or its mod is disabled.

Mods built against 0.4.0 to 0.4.6 work unchanged: nothing was taken out of the API.

## 0.4.6 — 2026-11-21

### Added

- `LandscapeOptions.ObstacleMode` (`LandscapeObstacleMode`): what a change does where its area reaches a building, a
  fixed place of the world or another mod's protected zone. `Refuse` (the default) refuses it whole, as before; `GoAround`
  leaves the ground under them as it is and changes the rest; `Fit` changes the rest and fits the ground to them — under a
  building to the height the game levels its footprint to (filling and cutting, or only filling or only cutting, as the
  building says), with an embankment of 45° round it; under a den's rocks only between where the ground was and their
  underside, so they neither hang nor sink. Either answers `LandscapeResult.Partial`. `Landscape.ObstaclesIn(area,
  options)` gives what is there (`LandscapeObstacles`: `Names`, `Covers(x, z)`, `HeightAfter(x, z, now, changed)`) for a
  preview, and `Landscape.Check(area, operation, options)` checks a change made with options.
- `RoadPlans.Line(points, kind)` → `RoadLine` (`Samples`, `Sections`, `Length`, `Fits`): the line a road plan through
  the points would run along, drawn by the game's own spline without laying a plan, each sample at the height the road
  is levelled to there — for a preview of a road being planned.
- `LandscapeOptions.KeepOffLandmarks`: a change of the ground is refused (`Blocked`) where a fixed place of the world
  stands — a den, or the rocks of a place the world was made with. The game cannot move them, and ground dug under a
  wolves' den left its rocks hanging in the air. True by default, like `KeepOffBuildings`.

- `BuildPalette` (`PaletteCategory`, `PaletteItem`, `PaletteItemEventArgs`): a palette of a mod's own in the look of the
  game's build menu — its category diamonds in two rows, the chosen category's name, a strip of tiles with pictures and
  names, the ✕ — shown in the menu's place while open; the game's own menu is not touched. It takes the mouse itself,
  also while the player's controls are captured (a diamond chooses its category, a tile raises `ItemChosen` and closes
  it, the wheel scrolls the tiles, Q and E step the categories), and `MouseInput.IsOverInterface` is true over it.
  `Scale` (0.5–1.5) makes it smaller or larger than the menu about the bottom of its diamonds.
- `NaturalObjects.ShowMarks()` → `ObjectMarks`: natural objects drawn over in the game's own see-through red, the red it
  lays on what keeps a building from its place — for a tool, what it is about to take. A kind's ghost blocked
  (`KindPreview.IsBlocked`) wears the same red.
- `NaturalObjects.Touching(center, radius)`: the natural objects that reach into a circle — those whose place lies in it
  and those whose footprint on the ground does, a large boulder whose place stands metres from the part of it a player
  points at.
- `GameCamera.TryRaycast(ray, maxDistance, out point, out target)`: what any ray meets first (a cursor's, through
  `ScreenPointToRay`), the player and the game's trigger volumes left out.
- `NaturalObjects.PictureOf(kind, width, height)`: a picture of a kind of the world, its model taken by a camera of its
  own with lights of its own, kept for the game's run; `PlaceableKind.DisplayName`, the kind's name in the player's
  language (the game's, else one made from its name); `NaturalObjects.ShowPreview(kind, position)` → `KindPreview`, the
  kind's model standing on the ground as the ghost of a tool that places objects one at a time (`Kind`, `Position`,
  `Rotation`, `Scale`, `IsBlocked` drawn red, `IsVisible`, `Dispose`).
- `UiQuickSlot`, a slot of the game's quick bar (its frame, a picture, the key under it, the orange frame of the slot in
  hand), and `Hud.AddInPlaceOf(element, HudPart.QuickBar)`, which shows an element in the place of the game's quick bar
  and hides the bar until the element is taken off with `Hud.Remove`: a tool of a mod's own shows its modes on 1–0.
- `UiKeyHint` shows the mouse: `LeftButton`, `RightButton` and `MiddleButton` in the game's pictures, `Wheel` written
  out in the player's language (the game has no picture of it), with modifiers: `KeyGesture.Parse("Shift+Wheel")`.
- The platform's own texts in every language of the game: the Mods window, the platform's settings, the console, the
  mouse wheel's word in key hints and the refusal of a session's join, in German, Polish, Ukrainian, French, Italian,
  Romanian, Spanish, Brazilian Portuguese, Japanese and Simplified and Traditional Chinese besides English and Russian.

### Changed

- While a capture of the player's controls shows the cursor (`PlayerControls.Capture`), the game's aim is off: no
  crosshair, and nothing in the middle of the screen becomes a target, so no card, key prompt or highlight follows the
  view of a mod's tool.
- A setting's description in a row wraps over up to three lines, and the row grows for them; it was cut to one line
  with an ellipsis.

### Fixed

- An object next to one the SDK took away (`Clear`, `Remove`, `Harvest`, a change of the ground) no longer counts as
  taken for a minute: the game moves another object into the place in its data the taken one frees, and the SDK read
  that one's spot. A boulder beside one taken could not be found, marked or taken until then.
- An outline on the ground (`Landscape.ShowOutline`) is drawn over whatever stands in front of it: a ring round a rock
  went into the rock.
- A placed kind of the world (`NaturalObjects.Place`) shows the world's own name, description and icon in the game's
  card (a flax bush showed a missing key and another item's icon), and is outlined as the world's object is, along its
  leaves (a bush was outlined as flat cards).
- An object the SDK took away (`Harvest`, `Clear`, `Remove`, and the changes of the ground that take objects) is no
  longer taken again by the next change a moment later. The game marks it destroyed only in its own copy of the data, so
  a brush writing ten times a second took the same objects again, the game failed to remove what was no longer in its
  data, and the game could hang.
- `LandscapeObjects.Settle` puts on the new ground every object where the ground moves, under the area's soft edge too.
  An object there stayed where it stood and hung over the ground that went down under it.
- `RoadPlans.Place` refuses a line shorter than a section (2 m), and `RoadLine.Fits` says no to one: the game cut it in
  no section, so there was nothing to level and the plan's stakes stayed in the world for good.
- The SDK's own changes of the ground come out as asked. The game blurs every change of heights it applies (a 3 × 3
  kernel over the change's square), so a snapshot put back left the ground off by up to 0.35 m and every change was
  smoothed a little. `LandscapeSnapshot.Restore` now puts the ground back exactly, in strips the blur does not reach;
  every other change gives the game heights that its blur turns into what was asked (to a few millimetres on the
  sharpest edges). A strip puts back its second row only: a row a strip starts on reached only the tiles of the world
  from that row on, and a tile's edge put back so left a crack in the ground.

0.4.0's to 0.4.5's mods work unchanged: nothing was taken out of the API.

## 0.4.5 — 2026-11-18

### Added

- Protected zones of the ground: `Landscape.Protect(area)` gives a `LandscapeZone` that refuses every change of this
  machine reaching it — the player's tools, the villagers', buildings and fields, the game's and other mods' — while
  its own mod still changes the ground there. `Refused` reports what it refused, `Area` moves it and `Dispose` ends it;
  another mod's change is `Blocked`, and `Check` says so. `LevellingField.LevelNow` leaves the tiles another mod's
  zone keeps untried. The console's `protect`.
- Road plans: `RoadPlans.Place(points)` lays the game's own road plan through up to 8 points (or, with
  `GroundKind.Dirt`, its levelling plan along a line), for the villagers to supply and pave; `RoadPlan.Build` skips the
  supplies, `LevelNow` paves every section at once and `Remove` takes the plan away. `RoadPlans.All` and `Nearest` find
  the plans of the loaded world. The console's `road`.
- Trees and boulders: `NaturalObject.Clear` takes any natural object away, trees and boulders too, which a change of
  the ground keeps (`IsClearable`); nothing is dropped and the save keeps it gone. `LandscapeObjects.Clear` clears
  every natural object a change of the ground reaches. The console's `objects clear`.
- Felling with loot: `NaturalObject.Harvest` strikes a tree, a stump or a trunk as the game's woodcutters do until
  nothing is left, with the game's loot (the trunk, firewood, logs, sticks, bark); no tool is needed. By default
  nobody strikes; `HarvestOptions.AsPlayer` strikes as the local player, with the experience and the work injuries
  each optional, and `ChopTrunk` chooses whether a felled tree's trunk is chopped too. The console's `objects harvest`.
- Stones and changes of the ground with their loot: `Harvest` takes the game's stone clumps too. `LandscapeObjects.Harvest`
  takes every natural object a change of the ground reaches with its loot (harvested, gathered by hand, or cleared
  where it gives nothing), the loot falling on the new ground; `LandscapeOptions.Harvesting` says who strikes.
- Wide roads: `RoadPlan.Width` and `RoadPlans.Place(…, width)` give a road plan a strip of its own, 1 to 16 m in steps
  of half a metre (`RoadPlans.WidthStep`; the game's road levels 3.5 m), kept through a save and sent to the other
  players, who need the same Mjolnir. The console's `road width`.
- Placing the world's kinds: `NaturalObjects.Place(kind, position, rotation, scale)` places a tree, a bush, a crop, a
  stone or a boulder of the scenery — any of the 70 kinds the game grows but its grass (`NaturalObjects.Kinds`,
  `PlaceableKind`) — as a world item that looks, is felled, mined or gathered and drops its loot as the world's do, and
  is saved; a tree's scale is its age. Every player needs the same Mjolnir. The console's `place`.
- Placed objects over time: a placed bush comes up full, stays when gathered to the end (a save keeps it) and gives
  again when the world's bushes of its kind do; a placed tree smaller than its grown size grows by the world's clock
  (`NaturalObjects.TreeGrowthDays`, 32 days by default, 0 for none).
- Tools on the ground: `Landscape.ShowGrid(center, columns, rows, cellSize, rotation)` draws a grid of cells on the
  ground in the look of the game's levelling field — framed, see-through, lying on the relief — that a mod colours cell
  by cell (`LandscapeGrid.SetCell` for a shade between `NearColor` and `FarColor`, `MarkCell`, `ClearCell`) to show
  what a change will do before it is made; `Landscape.TryRaycast(ray, maxDistance, out point)` finds where a ray, the
  one under the cursor, meets the ground; `LandscapeOutline.Height` and `LineWidth` set how high over the ground and how
  wide an outline runs.
- The player's controls for a mod's tool: `PlayerControls.Capture()` gives a `ControlsCapture` that stops the
  character, its attacks, the game's camera, quick slots and menu keys and shows the cursor until it is disposed, the
  world is left or its mod is disabled (`Ended`); hotkeys and mod windows still work. `MouseInput` reads the mouse:
  `Position`, `IsPressed`/`WasPressed`/`WasReleased(MouseButton)`, `Wheel` in notches and `IsOverInterface`;
  `KeyboardInput` the keys held, pressed and released by name, the modifiers held and whether the player types.

0.4.0's to 0.4.4's mods work unchanged: nothing was taken out of the API.

## 0.4.4 — 2026-11-17

### Added

- `HeaderedControl.ToolTipOnIcon`: a row's hover card belongs to its picture. The card then shows only while the
  pointer is on the picture and stands beside it, as the game's own card of an item stands beside its slot.
  `Mjolnir.CheatInventory` shows its item card that way.

### Fixed

- A hover card now stands beside the pointer instead of beside the whole element: on a row as wide as the window
  the card landed at its far edge, away from the item it describes. Without a pointer the element is still what
  the card stands beside.
- A tooltip that brings a panel of its own (`UiItemCard`) is now as large as that panel: the card was placed by a
  box of the game's own hover card, 180 wide and as tall as the panel was before it took the item, so the panel
  hung out of it and over what it described. The card is placed again while it shows, because the panel settles
  its height a frame after it takes the item.

0.4.0's, 0.4.1's, 0.4.2's and 0.4.3's mods work unchanged: nothing was taken out of the API.

## 0.4.3 — 2026-11-17

### Added

- `UiItemCard`: the game's own card of an item, as the inventory shows it under the pointer — the picture, the name,
  the tier, the kind, the lore and the item's attributes. The item is named by `AssetName` (`Item_Wood_Sticks`, a
  mod's `ModItem.AssetName`, an `ItemDefinition.AssetName`), and the card fills itself from the game's database, so it
  shows items the player does not carry. A tooltip that is a card is drawn as it is, without the hover card's frame
  around it: `row.ToolTip = new UiItemCard { AssetName = item.AssetName };`.
- `Mjolnir.CheatInventory` shows that card for every row of its catalog.

0.4.0's, 0.4.1's and 0.4.2's mods work unchanged: nothing was taken out of the API.

## 0.4.2 — 2026-11-16

### Fixed

- An open dropdown list no longer sits inside a panel of its own: the box of a `UiComboBox` lays its children out
  and grew to the list's height, so the box's dark background and frame were drawn round the whole list, which kept
  the game's own narrower width. The list now hangs under the field, as wide as it.

### Changed

- `Mjolnir.CheatInventory`: an item's field starts at a full stack instead of 1.

0.4.0's and 0.4.1's mods work unchanged: nothing of the API changed.

## 0.4.1 — 2026-11-14

### Added

- A mod declares which Mjolnir versions it works with: `[ModManifest(… Platform = ">=0.4 <0.5")]`, a range written
  as a dependency's. A mod built for another platform version is not loaded, and the log and the Mods window say
  which version it needs and which is installed, instead of the mod failing later on the first type that changed.
  A mod that declares nothing works with any version, as before.
- Another mod loader installed beside Mjolnir is named, in the log and over the list in the Mods window. Two loaders
  cannot share one game: only one of them starts, and the other says nothing at all, so its mods simply do nothing.
  BepInEx (Unity Doorstop) and MelonLoader are recognised.

0.4.0's mods work unchanged.

## 0.4.0 — 2026-11-14

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
- A mod that adds content of its own and chose no mode is raised to `NetworkCompatibility.Everyone` as it adds it,
  with a line in the log, so forgetting it cannot quietly split a session. A mod that chose a mode keeps it and is
  warned about instead.
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
