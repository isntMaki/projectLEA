import bpy, os

EXPORTS = [
    (r"C:/Users/bib/Documents/gunsguns/bean.blend",         "Bean"),
    (r"C:/Users/bib/Documents/gunsguns/gentlemanbean.blend", "GentlemanBean"),
]
OUT_DIR = r"C:/Dev/projectLEA/Assets/Models/Manuel/Characters"
os.makedirs(OUT_DIR, exist_ok=True)

for src, name in EXPORTS:
    bpy.ops.wm.open_mainfile(filepath=src)
    # Only the rig and its child meshes: nothing else in the file is wanted.
    keep = {o.name for o in bpy.data.objects if o.type in ('ARMATURE', 'MESH')}
    for o in list(bpy.data.objects):
        if o.name not in keep:
            bpy.data.objects.remove(o, do_unlink=True)

    # Tidy: the source ships an unused orphan material named "Material".
    for m in list(bpy.data.materials):
        if m.name == "Material" and m.users == 0:
            bpy.data.materials.remove(m)

    dest = os.path.join(OUT_DIR, name + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=dest,
        use_selection=False,
        object_types={'ARMATURE', 'MESH'},
        # Unity is Y-up, -Z forward; Blender is Z-up. This is the standard conversion.
        axis_forward='-Z', axis_up='Y',
        apply_unit_scale=True,
        # Apply non-armature modifiers (none here), keep the rig for skinning.
        use_mesh_modifiers=True,
        # Bake every action so Unity gets Idle/Walk/Crouch as clips.
        bake_anim=True,
        add_leaf_bones=False,
        # Blender units are metres and unit_scale is 1.0, so the bean stays ~1.79 m.
        apply_scale_options='FBX_SCALE_ALL',
        mesh_smooth_type='OFF',
    )
    print(f"EXPORTED {dest} ({os.path.getsize(dest)} bytes)")
