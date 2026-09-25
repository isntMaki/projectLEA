"""Print the world-space bounds of every mesh object in a .blend, in Blender axes.

Used to settle the bean-scale question: Unity's SkinnedMeshRenderer.localBounds on an imported
Blender FBX is in the *mesh's* local space (Blender axes, Z up), while the root node carries the
-90 X rotation. A bake that measures localBounds.y is measuring Blender's *depth*, not its height.
"""

import bpy
import sys


def bounds_world(obj):
    from mathutils import Vector
    coords = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    xs = [c.x for c in coords]
    ys = [c.y for c in coords]
    zs = [c.z for c in coords]
    return (min(xs), min(ys), min(zs)), (max(xs), max(ys), max(zs))


for path in sys.argv[1:]:
    bpy.ops.wm.open_mainfile(filepath=path)
    print(f"=== {path} ===")
    all_min = [1e9, 1e9, 1e9]
    all_max = [-1e9, -1e9, -1e9]
    for obj in bpy.data.objects:
        if obj.type not in ("MESH", "CURVE", "SURFACE", "META"):
            continue
        if obj.hide_render:
            continue
        lo, hi = bounds_world(obj)
        print(f"  {obj.name:24s} blender min({lo[0]:8.3f},{lo[1]:8.3f},{lo[2]:8.3f}) "
              f"max({hi[0]:8.3f},{hi[1]:8.3f},{hi[2]:8.3f})")
        for a in range(3):
            all_min[a] = min(all_min[a], lo[a])
            all_max[a] = max(all_max[a], hi[a])
    print(f"  TOTAL blender min({all_min[0]:.3f},{all_min[1]:.3f},{all_min[2]:.3f}) "
          f"max({all_max[0]:.3f},{all_max[1]:.3f},{all_max[2]:.3f})")
    print(f"  Blender: height(Z)={all_max[2] - all_min[2]:.4f} "
          f"depth(Y)={all_max[1] - all_min[1]:.4f} "
          f"width(X)={all_max[0] - all_min[0]:.4f}")
    # Blender (x,y,z) -> Unity (x, z, -y)
    print(f"  Unity:   height(Y)={all_max[2] - all_min[2]:.4f} "
          f"depth(Z)={all_max[1] - all_min[1]:.4f} "
          f"width(X)={all_max[0] - all_min[0]:.4f}")
