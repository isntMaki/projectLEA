import bpy, json, sys

def dump(path):
    bpy.ops.wm.open_mainfile(filepath=path)
    out = {"file": path, "objects": [], "actions": [], "meshes": [], "materials": [], "textures": []}
    for o in bpy.data.objects:
        entry = {"name": o.name, "type": o.type, "location": list(o.location), "rotation": list(o.rotation_euler), "scale": list(o.scale)}
        if o.type == 'MESH':
            entry["verts"] = len(o.data.vertices)
            entry["polys"] = len(o.data.polygons)
            entry["materials"] = [m.name for m in o.data.materials]
            entry["uv_layers"] = [l.name for l in o.data.uv_layers]
            entry["vertex_colors"] = [l.name for l in o.data.color_attributes]
            mods = []
            for m in o.modifiers:
                mods.append({"name": m.name, "type": m.type})
            entry["modifiers"] = mods
        if o.type == 'ARMATURE':
            entry["bones"] = [b.name for b in o.data.bones]
            entry["bone_count"] = len(o.data.bones)
        if o.parent:
            entry["parent"] = o.parent.name
        out["objects"].append(entry)
    for a in bpy.data.actions:
        out["actions"].append({"name": a.name, "frames": (int(a.frame_range[0]), int(a.frame_range[1]))})
    for m in bpy.data.materials:
        out["materials"].append({"name": m.name, "users": m.users})
    for t in bpy.data.textures:
        out["textures"].append({"name": t.name, "type": t.type})
    # scene settings
    sc = bpy.context.scene
    out["scene"] = {
        "fps": sc.render.fps,
        "frame_start": sc.frame_start,
        "frame_end": sc.frame_end,
        "unit_scale": sc.unit_settings.scale_length,
    }
    return out

results = [dump(p) for p in [
    r"C:/Users/bib/Documents/gunsguns/bean.blend",
    r"C:/Users/bib/Documents/gunsguns/gentlemanbean.blend",
]]
print(json.dumps(results, indent=1))
