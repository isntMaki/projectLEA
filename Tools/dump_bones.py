import bpy, json
out = {}
for path in [r"C:/Users/bib/Documents/gunsguns/bean.blend",
             r"C:/Users/bib/Documents/gunsguns/gentlemanbean.blend"]:
    bpy.ops.wm.open_mainfile(filepath=path)
    arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    info = []
    for a in arms:
        bones = []
        for b in a.data.bones:
            bones.append({"name": b.name, "head": [round(v,4) for v in b.head_local],
                          "tail": [round(v,4) for v in b.tail_local],
                          "parent": b.parent.name if b.parent else None})
        info.append({"armature": a.name, "bones": bones})
    # world bounds of all visible meshes
    import mathutils
    mins = [1e9]*3; maxs = [-1e9]*3
    for o in bpy.data.objects:
        if o.type != 'MESH': continue
        for v in o.bound_box:
            w = o.matrix_world @ mathutils.Vector(v)
            for i in range(3):
                mins[i] = min(mins[i], w[i]); maxs[i] = max(maxs[i], w[i])
    out[path.split("\\")[-1]] = {"armatures": info,
        "bounds_min": [round(v,4) for v in mins], "bounds_max": [round(v,4) for v in maxs],
        "height": round(maxs[2]-mins[2],4)}
    # which action is assigned / NLA strips
    ad = {}
    for o in bpy.data.objects:
        if o.type == 'ARMATURE':
            ad[o.name] = {"active_action": o.animation_data.action.name if (o.animation_data and o.animation_data.action) else None,
                          "nla_tracks": [t.name for t in (o.animation_data.nla_tracks if o.animation_data else [])]}
    out[path.split("\\")[-1]]["anim_data"] = ad
print("BLOBSTART")
print(json.dumps(out, indent=1))
print("BLOBEND")
