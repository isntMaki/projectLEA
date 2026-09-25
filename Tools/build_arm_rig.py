"""
Builds a first-person arm rig for the hammer weapon, binds the hammer mesh to it,
and authors two skeletal attack animations: Attack_Normal and Attack_Heavy.

Run:  blender -b testweapon.blend --python build_arm_rig.py
"""
import bpy
import math
from mathutils import Vector, Euler

# ----------------------------------------------------------------------------
# 0. Clean slate on the weapon object: drop the old object-level animation.
# ----------------------------------------------------------------------------
hammer = bpy.data.objects.get("Hammer_Weapon")
assert hammer is not None, "Hammer_Weapon mesh not found"

# Remove old object animation (we replace it with skeletal animation).
if hammer.animation_data:
    hammer.animation_data_clear()

# Remove any leftover actions so the export is unambiguous.
for act in list(bpy.data.actions):
    bpy.data.actions.remove(act)

# Capture the hammer's current world-space bounds so we can place the rig sensibly.
bpy.context.view_layer.update()
hammer_dims = hammer.dimensions.copy()
print("Hammer dimensions:", tuple(round(v, 4) for v in hammer_dims))

# ----------------------------------------------------------------------------
# 1. Create the armature.
# ----------------------------------------------------------------------------
arm_data = bpy.data.armatures.new("FP_Armature")
arm_obj = bpy.data.objects.new("FP_Armature", arm_data)
bpy.context.collection.objects.link(arm_obj)

bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.mode_set(mode='EDIT')

eb = arm_data.edit_bones

# A simple first-person arm chain running from the camera/right shoulder outward.
# X = right, Y = forward, Z = up (Blender convention; FBX export converts to Unity).
# We keep the hammer in front-right of the camera.
def mk(name, head, tail, parent=None, roll=0.0):
    b = eb.new(name)
    b.head = Vector(head)
    b.tail = Vector(tail)
    b.roll = roll
    if parent is not None:
        b.parent = parent
        b.use_connect = False
    return b

# Chain: Shoulder -> UpperArm -> ForeArm -> Hand -> WeaponSocket
shoulder = mk("Shoulder", (0.10, 0.00, -0.10), (0.18, 0.05, -0.16))
upper    = mk("UpperArm", (0.18, 0.05, -0.16), (0.26, 0.28, -0.20), shoulder)
fore     = mk("ForeArm",  (0.26, 0.28, -0.20), (0.30, 0.50, -0.16), upper)
hand     = mk("Hand",     (0.30, 0.50, -0.16), (0.32, 0.62, -0.14), fore)
socket   = mk("WeaponSocket", (0.32, 0.62, -0.14), (0.34, 0.74, -0.12), hand)

bpy.ops.object.mode_set(mode='OBJECT')

# ----------------------------------------------------------------------------
# 2. Bind the hammer to the rig.
#    The hammer is a separate mesh with no vertex groups, so we assign the whole
#    mesh weight 1.0 to WeaponSocket. That makes it a rigid attachment that
#    follows the hand -- exactly what a held prop needs.
# ----------------------------------------------------------------------------
# Clear any existing parenting so we control the transform ourselves.
hammer.parent = None

# Remove any pre-existing modifiers / groups.
hammer.vertex_groups.clear()
for m in list(hammer.modifiers):
    hammer.modifiers.remove(m)

vg = hammer.vertex_groups.new(name="WeaponSocket")
vg.add(range(len(hammer.data.vertices)), 1.0, 'REPLACE')

arm_mod = hammer.modifiers.new(name="Armature", type='ARMATURE')
arm_mod.object = arm_obj

# Parent the mesh to the armature object (keep transform).
hammer.parent = arm_obj
hammer.matrix_parent_inverse = arm_obj.matrix_world.inverted()

# ----------------------------------------------------------------------------
# 3. Pose bones and author the two action clips.
# ----------------------------------------------------------------------------
def set_pose_rotation(pbone, euler_xyz):
    pbone.rotation_mode = 'XYZ'
    pbone.rotation_euler = Euler(euler_xyz, 'XYZ')

def set_pose_location(pbone, loc):
    pbone.location = Vector(loc)

def key_all(action_frame, bones, params):
    """params: dict bone_name -> dict(rot=(x,y,z), loc=(x,y,z))"""
    for bname, data in params.items():
        pb = bones[bname]
        if 'rot' in data:
            set_pose_rotation(pb, data['rot'])
            pb.keyframe_insert(data_path="rotation_euler", frame=action_frame)
        if 'loc' in data:
            set_pose_location(pb, data['loc'])
            pb.keyframe_insert(data_path="location", frame=action_frame)

def new_action(name, frame_end):
    act = bpy.data.actions.new(name)
    arm_obj.animation_data_create()
    arm_obj.animation_data.action = act
    # Blender 5.x requires a slot assignment for the action to evaluate.
    try:
        slot = act.slots.new(id_type='OBJECT', name=arm_obj.name)
        arm_obj.animation_data.action_slot = slot
    except Exception as e:
        print("slot setup note:", e)
    bpy.context.scene.frame_start = 0
    bpy.context.scene.frame_end = frame_end
    return act

bones = arm_obj.pose.bones
for pb in bones:
    pb.rotation_mode = 'XYZ'

FPS = 24

# ---------------------------------------------------------------- normal swing
# A compact, snappy overhead swing. ~0.6s total at 24fps -> 15 frames.
norm = new_action("Attack_Normal", 15)

# rest pose
REST = {
    "Shoulder":     dict(rot=(0.0, 0.0, 0.0), loc=(0, 0, 0)),
    "UpperArm":     dict(rot=(0.0, 0.0, 0.0), loc=(0, 0, 0)),
    "ForeArm":      dict(rot=(0.0, 0.0, 0.0), loc=(0, 0, 0)),
    "Hand":         dict(rot=(0.0, 0.0, 0.0), loc=(0, 0, 0)),
    "WeaponSocket": dict(rot=(0.0, 0.0, 0.0), loc=(0, 0, 0)),
}

# Wind-up (raised back), then strike (swung forward/down), then recover.
key_all(0, bones, REST)

WINDUP = {
    "Shoulder":     dict(rot=(-0.12, 0.0, 0.0)),
    "UpperArm":     dict(rot=(-0.95, 0.15, 0.0)),
    "ForeArm":      dict(rot=(-0.55, 0.0, 0.0)),
    "Hand":         dict(rot=(-0.25, 0.0, 0.25)),
    "WeaponSocket": dict(rot=(-0.30, 0.0, 0.0)),
}
key_all(4, bones, WINDUP)

STRIKE = {
    "Shoulder":     dict(rot=(0.22, 0.0, 0.0)),
    "UpperArm":     dict(rot=(0.85, -0.10, 0.0)),
    "ForeArm":      dict(rot=(0.70, 0.0, 0.0)),
    "Hand":         dict(rot=(0.35, 0.0, -0.30)),
    "WeaponSocket": dict(rot=(0.45, 0.0, 0.0)),
}
key_all(8, bones, STRIKE)

# settle
SETTLE = {
    "Shoulder":     dict(rot=(0.05, 0.0, 0.0)),
    "UpperArm":     dict(rot=(0.18, 0.0, 0.0)),
    "ForeArm":      dict(rot=(0.15, 0.0, 0.0)),
    "Hand":         dict(rot=(0.08, 0.0, -0.08)),
    "WeaponSocket": dict(rot=(0.10, 0.0, 0.0)),
}
key_all(12, bones, SETTLE)
key_all(15, bones, REST)

# ---------------------------------------------------------------- heavy swing
# A wider, slower, more committed overhead smash. ~1.0s -> 24 frames.
heavy = new_action("Attack_Heavy", 24)
key_all(0, bones, REST)

HEAVY_WINDUP = {
    "Shoulder":     dict(rot=(-0.20, 0.0, 0.0)),
    "UpperArm":     dict(rot=(-1.45, 0.25, 0.0)),
    "ForeArm":      dict(rot=(-0.95, 0.0, 0.0)),
    "Hand":         dict(rot=(-0.40, 0.0, 0.45)),
    "WeaponSocket": dict(rot=(-0.55, 0.0, 0.0)),
}
key_all(7, bones, HEAVY_WINDUP)

HEAVY_STRIKE = {
    "Shoulder":     dict(rot=(0.35, 0.0, 0.0)),
    "UpperArm":     dict(rot=(1.15, -0.18, 0.0)),
    "ForeArm":      dict(rot=(1.05, 0.0, 0.0)),
    "Hand":         dict(rot=(0.55, 0.0, -0.50)),
    "WeaponSocket": dict(rot=(0.70, 0.0, 0.0)),
}
key_all(14, bones, HEAVY_STRIKE)

# longer recovery to sell the weight
HEAVY_SETTLE = {
    "Shoulder":     dict(rot=(0.14, 0.0, 0.0)),
    "UpperArm":     dict(rot=(0.45, 0.0, 0.0)),
    "ForeArm":      dict(rot=(0.40, 0.0, 0.0)),
    "Hand":         dict(rot=(0.20, 0.0, -0.18)),
    "WeaponSocket": dict(rot=(0.28, 0.0, 0.0)),
}
key_all(20, bones, HEAVY_SETTLE)
key_all(24, bones, REST)

# Set interpolation to Bezier-ish easing for a snappier strike.
for act in (norm, heavy):
    for fc in act.fcurves if hasattr(act, "fcurves") else []:
        for kp in fc.keyframe_points:
            kp.interpolation = 'BEZIER'

# ----------------------------------------------------------------------------
# 4. Verify the actions survived.
# ----------------------------------------------------------------------------
print("ACTIONS:", [a.name for a in bpy.data.actions])
print("BONES:", [b.name for b in arm_data.bones])

# ----------------------------------------------------------------------------
# 5. Export to FBX with both clips.
# ----------------------------------------------------------------------------
out = r"C:\Dev\projectLEA\Assets\Models\Manuel\FP_Hammer_Rigged.fbx"

bpy.ops.object.select_all(action='DESELECT')
arm_obj.select_set(True)
hammer.select_set(True)
bpy.context.view_layer.objects.active = arm_obj

bpy.ops.export_scene.fbx(
    filepath=out,
    use_selection=True,
    apply_scale_options='FBX_SCALE_ALL',
    axis_forward='-Z',
    axis_up='Y',
    mesh_smooth_type='FACE',
    use_mesh_modifiers=False,
    bake_anim=True,
    bake_anim_use_all_actions=True,
    bake_anim_force_startend_keying=True,
    path_mode='COPY',
    embed_textures=False,
    object_types={'MESH', 'ARMATURE'},
)

import os
print("EXPORTED:", out, os.path.exists(out), os.path.getsize(out) if os.path.exists(out) else 0)

# Save the working blend so the rig is preserved for the user.
bpy.ops.wm.save_as_mainfile(filepath=r"C:\Blender\with animattion\testweapon_rigged.blend")
print("SAVED BLEND")
