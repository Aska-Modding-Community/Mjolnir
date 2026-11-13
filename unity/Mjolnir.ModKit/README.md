# The Mjolnir kit

The Unity project that turns a mod's assets into AssetBundles the game can load: models for items and buildings,
item icons, animations and masks made on the game's own skeletons.

It runs on the game's own Unity version, **6000.3.12f1**, with **HDRP** 17.3.0, the game's render pipeline. A bundle
built with another Unity version, or without HDRP, does not load in the game.

A mod's code is written against the Mjolnir SDK; the kit makes only its assets.

## Setting up

1. Install Unity **6000.3.12f1** through Unity Hub and open this folder with it. The first import takes a while:
   HDRP compiles its shaders.
2. **Mjolnir → Kit Window** opens by itself the first time. It lists what the kit needs, each missing step with the
   button that fixes it: the right Unity version, HDRP in linear colour, the game found, Mjolnir installed in it,
   the game's skeletons imported. It also lists your mods with their bundles, and the kit's tools.

The kit finds the game from `MJOLNIR_GAME_DIR`, from a folder picked before, or from Steam's own records, and asks
for one when it finds none. As it opens it sets HDRP on every quality level and the linear colour space
(**Mjolnir → Set Up the Kit** does it again), so colours and textures look as they will in the game.

## A mod's bundles

**Mjolnir → New Mod…** makes `Assets/Mods/<mod>/<bundle>/`. Name the mod's folder as it is named in the game's
`Mjolnir/Mods`, and each bundle folder becomes one AssetBundle of everything in it.

| Menu | What it does |
|---|---|
| **Build Bundles** | builds every mod's bundles into `Build/Bundles/<mod>/<bundle>` |
| **Build and Copy to Game** | also copies each bundle into `Mjolnir/Mods/<mod>/bundles/` of the installed mod, and the rendered icons into its `items/` |
| **Check Mods** | warns of what will not work in the game: a shader that is not `HDRP/…`, a script on a prefab, a pivot away from its model, a bone no skeleton of the game has, bundles the game would not load together. It also runs before every build, and a click on its line in the Console shows the asset |

Each bundle is built under its mod's name, so two mods' bundles of one folder name, such as `models`, load in the
game together. Rebuild bundles made by an older kit.

In the mod the bundle is loaded by its folder's name: `ModItem.WithModel("bundles/models", "TwineSpool")`,
`Animations.Add("wave", "bundles/clips", "Wave")`, `AnimationMask.Add("arms", "bundles/clips", "Arms")`.

`tools/build-bundles.ps1` in this repository runs the kit in batch mode. Unity locks an open project: close the
editor first, or build from its menu and run the script with `-SkipBuild`.

## The game's skeletons

Animations and masks are made on the game's own skeletons, which the game itself writes out:

1. Start the game with Mjolnir once. It writes the characters' skeletons — bone names and their pose, nothing else
   of the game — to `Mjolnir/ModKit/rigs/` in the game's folder.
2. **Mjolnir → Rigs → Import from the Game** makes `Assets/Mjolnir/Rigs/<rig>.prefab`, one per body: `ragnar` and
   `aska` (the player's male and female), `peasant_male` and `peasant_female` (the villagers).
3. **Mjolnir → Animation → Open Preview Stage** stands them in a lit scene, and **Preview Animation** plays a clip
   on them without changing the scene.

Two settings on Mjolnir's settings page in the game write more of it for the kit, both off by default: the
characters' models, so animations can be previewed on the game's own people, and the game's own animation clips.
Everything the game writes there stays on your computer.

## The menu

| Area | Items |
|---|---|
| | Kit Window, New Mod… |
| | Build Bundles, Build and Copy to Game, Check Mods |
| Items | Render Item Icons |
| Animation | Preview Animation, Open Preview Stage, New Mask…, New Mask from Clip, Bake Humanoid Clips to Rig…, Keep Every Bone |
| Rigs | Import from the Game, Export as FBX |
| Platform | Make Mjolnir's Masks |
| | Set Up the Kit, Examples |

**Export as FBX** writes each skeleton to `Build/Rigs/<rig>.fbx` for Blender, Maya, 3ds Max or Cascadeur; a clip
animated there and dropped into a bundle folder is fitted to the game's skeleton as it is imported. A humanoid
source, such as a Mixamo clip, is made humanoid instead, and **Bake Humanoid Clips to Rig…** records it onto the
game's bones.

**Examples** makes the models of the example mods that ship with Mjolnir, and **Platform → Make Mjolnir's Masks**
builds the platform's own mask; neither is needed for a mod of your own.

## No asset of the game's ships in a mod

What the game writes for the kit — skeletons, the characters' models, the game's own clips — is the game's, and no
mod may ship it. The kit refuses to build a bundle that would carry any of it, **Check Mods** names it, and git
ignores every folder it is imported into. A mod plays the game's own clips by name instead
(`Animations.Find("aska_wave")`).

## The full guide

<https://aska-modding-community.github.io/mjolnir-docs/docs/guides/mod-kit>
