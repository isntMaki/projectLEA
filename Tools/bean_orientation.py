"""Determines what Blender's FBX export actually does to the axes, and collects the
bean's real dimensions / facing direction.

Part 1: export a unit marker mesh (vertices on +X, +Y, +Z) with the same settings used
for the beans, then locate the raw Vertices double array in the binary FBX. That gives
the exact Blender -> FBX axis mapping without guessing from tutorial folklore.

Part 2: open bean.blend, print every mesh's world-space bounds, the armature object's
world transform, and the eye positions so the bean's forward direction is known.
"""

import bpy
import struct
import sys
import json
import os

AXIS_SETTINGS = dict(axis_forward='-Z', axis_up='Y')
EXPORT_ARGS = dict(
    use_selection=False,
    object_types={'ARMATURE', 'MESH'},
    apply_unit_scale=True,
    use_mesh_modifiers=True,
    bake_anim=True,
    add_leaf_bones=False,
    apply_scale_options='FBX_SCALE_ALL',
    mesh_smooth_type='OFF',
)

MARKER = r"C:\Dev\projectLEA\Tools\__marker.fbx"


def export_marker():
    # A clean file with one mesh whose vertices are the three unit axes plus the origin.
    bpy.ops.wm.read_factory_settings(use_empty=True)
    me = bpy.data.meshes.new("Marker")
    me.from_pydata([(0, 0, 0), (1, 0, 0), (0, 1, 0), (0, 0, 1)], [], [])
    me.update()
    obj = bpy.data.objects.new("Marker", me)
    bpy.context.scene.collection.objects.link(obj)

    # Export only the marker: use_selection would need a selection override, so export
    # the whole (one-object) scene instead.
    bpy.ops.export_scene.fbx(filepath=MARKER, **AXIS_SETTINGS, **EXPORT_ARGS)


def read_marker():
    raw = open(MARKER, 'rb').read()

    # The Vertices array is the only double[12] in a one-triangle-quad-point mesh.
    needle = struct.pack('<12d', 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1)
    at = raw.find(needle)
    if at < 0:
        # Blender may zlib-compress the array. Try every zlib stream in the file.
        import zlib
        for i in range(len(raw) - 2):
            if raw[i:i + 2] == b'x\x9c':
                try:
                    out = zlib.decompress(raw[i:])
                except zlib.error:
                    continue
                if needle in out:
                    print(json.dumps({
                        "note": "vertices array was zlib-compressed",
                        "found_at_stream": i,
                    }))
                    at = -1
                    vals = struct.unpack('<12d', out[out.find(needle):out.find(needle) + 96])
                    break
        else:
            print(json.dumps({"error": "could not locate the marker vertices in the FBX"}))
            return
    else:
        vals = struct.unpack('<12d', raw[at:at + 96])

    # vals = origin, +X, +Y, +Z expressed in FBX coordinates.
    axis = {
        "blender_x_in_fbx": list(vals[3:6]),
        "blender_y_in_fbx": list(vals[6:9]),
        "blender_z_in_fbx": list(vals[9:12]),
    }
    print(json.dumps({"marker_vertices_fbx": axis}, indent=1))


def inspect_bean(path, label):
    bpy.ops.wm.open_mainfile(filepath=path)
    out = {"file": os.path.basename(path), "objects": [], "armatures": []}

    for obj in bpy.data.objects:
        if obj.type == 'MESH':
            # World-space corners of the mesh's own bounding box (evaluated, so modifiers
            # count - the eyes each carry an ARMATURE modifier).
            deps = bpy.context.evaluated_depsgraph_get()
            ev = obj.evaluated_get(deps)
            if ev.data is None:
                continue
            corners = [obj.matrix_world @ mathutils.Vector(c) for c in ev.data.bound_box] \
                if False else None
            import mathutils
            corners = [obj.matrix_world @ mathutils.Vector(c) for c in ev.data.bound_box]
            xs = [c.x for c in corners]
            ys = [c.y for c in corners]
            zs = [c.z for c in corners]
            out["objects"].append({
                "name": obj.name,
                "world_pos": [obj.location.x, obj.location.y, obj.location.z],
                "world_min": [min(xs), min(ys), min(zs)],
                "world_max": [max(xs), max(ys), max(zs)],
                "modifiers": [m.type for m in obj.modifiers],
            })
        elif obj.type == 'ARMATURE':
            out["armatures"].append({
                "name": obj.name,
                "world_pos": [obj.location.x, obj.location.y, obj.location.z],
                "world_rot_euler": [obj.rotation_euler.x, obj.rotation_euler.y, obj.rotation_euler.z],
                "world_scale": [obj.scale.x, obj.scale.y, obj.scale.z],
                "bone_heads": {
                    b.name: [b.head_local.x, b.head_local.y, b.head_local.z]
                    for b in obj.data.bones
                },
            })

    # Combined world bounds of every mesh.
    import mathutils
    allmin = [1e9, 1e9, 1e9]
    allmax = [-1e9, -1e9, -1e9]
    deps = bpy.context.evaluated_depsgraph_get()
    for obj in bpy.data.objects:
        if obj.type != 'MESH':
            continue
        ev = obj.evaluated_get(deps)
        if ev.data is None:
            continue
        for c in ev.data.bound_box:
            p = obj.matrix_world @ mathutils.Vector(c)
            for i in range(3):
                allmin[i] = min(allmin[i], p[i])
                allmax[i] = max(allmax[i], p[i])
    out["combined_world_bounds"] = {"min": allmin, "max": allmax}

    # The bean's facing: eyes sit on the front of the face, so the vector from the body
    # centre to an eye gives the forward direction in Blender space.
    eye_names = [n for n in ("Bean_Eye_L", "Bean_Eye_R", "Bean_Pupil_L", "Bean_Pupil_R")
                 if n in bpy.data.objects]
    if eye_names:
        eyes = []
        for n in eye_names:
            o = bpy.data.objects[n]
            ev = o.evaluated_get(deps)
            centre = sum((o.matrix_world @ mathutils.Vector(c) for c in ev.data.bound_box),
                         mathutils.Vector()) / 8.0
            eyes.append([centre.x, centre.y, centre.z])
        out["eye_centres_world"] = eyes
        ec = [sum(e[i] for e in eyes) / len(eyes) for i in range(3)]
        bc = [(allmin[i] + allmax[i]) / 2 for i in range(3)]
        out["facing_dir_blender"] = [ec[i] - bc[i] for i in range(3)]

    out["actions"] = {a.name: [int(a.frame_range[0]), int(a.frame_range[1])]
                      for a in bpy.data.actions}
    out["unit_scale"] = bpy.context.scene.unit_settings.scale_length
    out["fps"] = bpy.context.scene.render.fps

    print(json.dumps({label: out}, indent=1))


export_marker()
read_marker()
inspect_bean(r"C:\Users\bib\Documents\gunsguns\bean.blend", "bean")
inspect_bean(r"C:\Users\bib\Documents\gunsguns\gentlemanbean.blend", "gentlemanbean")

os.remove(MARKER)
