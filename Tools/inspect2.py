import bpy

print("OBJECTS:", [(o.name, o.type) for o in bpy.data.objects])
print("ARMATURES:", [a.name for a in bpy.data.armatures])
print("NUM ACTIONS:", len(bpy.data.actions))

for act in bpy.data.actions:
    print("=" * 60)
    print("ACTION:", repr(act.name))
    # Blender 5.x: actions use slots/layers instead of direct .fcurves
    fr = act.frame_range
    print("  frame_range:", tuple(fr))
    slots = getattr(act, "slots", None)
    if slots:
        for s in slots:
            print("  slot:", repr(s.identifier), "name:", repr(getattr(s, "name_display", None)))
    layers = getattr(act, "layers", None)
    if layers:
        for L in layers:
            for st in L.strips:
                cbs = getattr(st, "channelbags", None) or []
                for cb in cbs:
                    fcs = getattr(cb, "fcurves", []) or []
                    print("  fcurves in bag:", len(fcs))
                    for fc in list(fcs)[:10]:
                        print("     ", fc.data_path, fc.array_index)

# also check scene-level animation & any object animation data
for o in bpy.data.objects:
    ad = o.animation_data
    if ad:
        print("ANIMDATA on", o.name, "action:", ad.action, "nla:", [t.name for t in ad.nla_tracks])

print("SCENE:", bpy.context.scene.name, "fps:", bpy.context.scene.render.fps)
print("FRAME RANGE:", bpy.context.scene.frame_start, "-", bpy.context.scene.frame_end)
