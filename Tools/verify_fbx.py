import bpy

# Start from a clean file, then import the exported FBX to verify its contents.
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=r"C:\Dev\projectLEA\Assets\Models\Manuel\FP_Hammer_Rigged.fbx")

print("=" * 60)
print("IMPORTED OBJECTS")
print("=" * 60)
for o in bpy.data.objects:
    print(f"  [{o.type}] {o.name} parent={o.parent.name if o.parent else None}")

print()
print("=" * 60)
print("IMPORTED ACTIONS")
print("=" * 60)
for a in bpy.data.actions:
    print(f"  {a.name}  range={tuple(a.frame_range)}")

print()
print("ARMATURE BONES:")
for arm in bpy.data.armatures:
    print(" ", arm.name, [b.name for b in arm.bones])

print()
print("MESH SKIN CHECK:")
for o in bpy.data.objects:
    if o.type == 'MESH':
        print(" ", o.name, "verts:", len(o.data.vertices),
              "groups:", [g.name for g in o.vertex_groups],
              "modifiers:", [m.type for m in o.modifiers])
