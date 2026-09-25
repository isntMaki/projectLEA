import bpy, os

out = r"C:\Dev\projectLEA\Assets\Models\Manuel\Hammer_Weapon.fbx"

# Select only the weapon mesh so the export is clean.
bpy.ops.object.select_all(action='DESELECT')
for o in bpy.data.objects:
    if o.type == 'MESH':
        o.select_set(True)
        bpy.context.view_layer.objects.active = o

bpy.ops.export_scene.fbx(
    filepath=out,
    use_selection=True,
    apply_scale_options='FBX_SCALE_ALL',
    axis_forward='-Z',
    axis_up='Y',
    mesh_smooth_type='FACE',
    use_mesh_modifiers=True,
    bake_anim=True,
    bake_anim_use_all_actions=True,
    bake_anim_force_startend_keying=False,
    path_mode='COPY',
    embed_textures=False,
    object_types={'MESH', 'ARMATURE', 'EMPTY'},
)

print("EXPORTED TO:", out)
print("EXISTS:", os.path.exists(out), os.path.getsize(out) if os.path.exists(out) else 0)
