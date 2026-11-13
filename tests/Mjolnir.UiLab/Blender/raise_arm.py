"""The Blender side of UiLab's "blender" scenario: a skeleton the Mjolnir kit exported, animated as a modder would, written
back as FBX for the kit (its AnimationImport fits it to the game's skeleton).

Run from the repository:
    blender --background --factory-startup --python tests/Mjolnir.UiLab/Blender/raise_arm.py -- \
        unity/Mjolnir.ModKit/Build/Rigs/ragnar.fbx unity/Mjolnir.ModKit/Assets/Mods/Mjolnir.UiLab/blender/raise.fbx [armature] [still]

The action BlenderRaise, 30 fps, one second: the right upper arm turned 70 degrees and the armature object (the skeleton's
master: Blender makes the top node with bones under it the armature) moved 1 m forward, which the kit makes the clip's root
motion. Written with Blender's own FBX defaults, or with "armature" the armature alone; with "still" the armature stays (an
arm's gesture alone, BlenderWave), so every bone but the arm stands at rest in the file.
"""
import math
import sys

import bpy

args = sys.argv[sys.argv.index("--") + 1:]
source, target = args[0], args[1]
armature_only = "armature" in args[2:]
still = "still" in args[2:]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=source)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
print(f"Imported: the armature {arm.name!r} with {len(arm.data.bones)} bones under {arm.parent.name if arm.parent else 'nothing'!r}")

scene = bpy.context.scene
scene.render.fps = 30
scene.frame_start, scene.frame_end = 1, 31
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")
upper = arm.pose.bones["Bip001 R UpperArm"]
upper.rotation_mode = "XYZ"
for frame, angle, forward in ((1, 0.0, 0.0), (31, -70.0, 1.0)):
    upper.rotation_euler = (0.0, 0.0, math.radians(angle))
    upper.keyframe_insert("rotation_euler", frame=frame)
    if not still:
        arm.location = (0.0, 0.0, forward * 100.0)  # the armature's parent is the file's root, in centimetres
        arm.keyframe_insert("location", frame=frame)
bpy.ops.object.mode_set(mode="OBJECT")
arm.animation_data.action.name = "BlenderWave" if still else "BlenderRaise"

if armature_only:
    bpy.ops.export_scene.fbx(filepath=target, object_types={"ARMATURE"}, add_leaf_bones=False)
else:
    bpy.ops.export_scene.fbx(filepath=target)
print(f"Written: {target}")
