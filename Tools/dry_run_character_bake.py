#!/usr/bin/env python3
"""Dry run of ContentSetup.BakeCharacter, against the real geometry.

The character bake runs unattended the moment Unity opens the project, and like the weapon
bake it is pure arithmetic on vertices - so it can be reproduced and checked first. This is
the one part of the automatic pass that was still unverified.

Replicates ContentSetup exactly:
    size   = max - min
    center = (min + max) * 0.5
    scale  = CharacterHeight / size.y
    position = -(scale * center)
    localScale = one * scale

A vertex v in the model's own space therefore lands at
    world = position + scale * v = scale * (v - center)
i.e. the baked body is centred on the prefab's origin and stands CharacterHeight tall. That
is the capsule convention the scene already uses (capsule centre at the origin, 2.0 m tall),
which is what makes it drop in at local position zero.
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
sys.path.insert(0, str(Path.home() / ".workbuddy-ai" / "skills" / "unity-glb-import" / "scripts"))
# Reused from the unity-glb-import skill, not duplicated here.
from inspect_glb import load_glb, all_buffers, world_verts  # noqa: E402

CHARACTER = Path("C:/Dev/projectLEA/Assets/Models/Manuel/Characters/PlayerGuy.glb")
CHARACTER_HEIGHT = 1.8

# The scene's CharacterController and PlayerTwoDummy's CapsuleCollider.
CAPSULE_HEIGHT = 2.0
CAPSULE_RADIUS = 0.5
DUMMY_HEIGHT = 1.8
DUMMY_RADIUS = 0.4


def main():
    js, glb_bin = load_glb(CHARACTER)
    buffers = all_buffers(js, glb_bin, CHARACTER.parent)
    pts = list(world_verts(js, buffers))
    if not pts:
        print("NO MESHES")
        return

    lo = [min(p[i] for p in pts) for i in range(3)]
    hi = [max(p[i] for p in pts) for i in range(3)]
    size = tuple(hi[i] - lo[i] for i in range(3))
    center = tuple((lo[i] + hi[i]) * 0.5 for i in range(3))
    scale = CHARACTER_HEIGHT / size[1]

    # Where the feet are, before and after. The source model's pivot is near its feet, so the
    # pre-bake min.y tells us whether the author put the origin at the feet (like the capsule)
    # or at the floor of the bounding box.
    print(f"source: {len(pts)} vertices")
    print(f"  extents  x {size[0]:7.3f}  y {size[1]:7.3f}  z {size[2]:7.3f}")
    print(f"  bounds   ({lo[0]:7.3f},{lo[1]:7.3f},{lo[2]:7.3f}) .. "
          f"({hi[0]:7.3f},{hi[1]:7.3f},{hi[2]:7.3f})")
    print(f"  centre   ({center[0]:7.3f},{center[1]:7.3f},{center[2]:7.3f})")
    print()
    print(f"scale = {CHARACTER_HEIGHT} / {size[1]:.3f} = {scale:.4f}")
    print()

    # The bake: world = scale * (v - centre)
    out = [tuple(scale * (p[k] - center[k]) for k in range(3)) for p in pts]
    o_lo = [min(p[i] for p in out) for i in range(3)]
    o_hi = [max(p[i] for p in out) for i in range(3)]
    o_size = tuple(o_hi[i] - o_lo[i] for i in range(3))

    print("baked (prefab local space, pivot at the origin):")
    print(f"  extents  x {o_size[0]:7.3f}  y {o_size[1]:7.3f}  z {o_size[2]:7.3f}")
    print(f"  bounds   ({o_lo[0]:7.3f},{o_lo[1]:7.3f},{o_lo[2]:7.3f}) .. "
          f"({o_hi[0]:7.3f},{o_hi[1]:7.3f},{o_hi[2]:7.3f})")
    print()

    flags = []

    # Height must be exactly the target.
    if abs(o_size[1] - CHARACTER_HEIGHT) > 1e-4:
        flags.append(f"HEIGHT: baked {o_size[1]:.4f} != {CHARACTER_HEIGHT}")

    # The pivot is supposed to sit at the body's CENTRE so it drops in where a capsule stood.
    # A capsule of height H centred on the origin spans +/- H/2.
    if abs(o_lo[1] + CHARACTER_HEIGHT / 2) > 0.02:
        flags.append(f"PIVOT: baked bottom at {o_lo[1]:.3f}, expected "
                     f"{-CHARACTER_HEIGHT / 2:.3f}")
    if abs(o_hi[1] - CHARACTER_HEIGHT / 2) > 0.02:
        flags.append(f"PIVOT: baked top at {o_hi[1]:.3f}, expected {CHARACTER_HEIGHT / 2:.3f}")

    # The body has to fit inside the colliders that the scene puts on it, or it will clip
    # through walls. Radius is measured horizontally from the origin.
    horiz = max((p[0] ** 2 + p[2] ** 2) ** 0.5 for p in out)
    if horiz > CAPSULE_RADIUS:
        flags.append(f"LOCAL PLAYER: baked body is {horiz:.3f} wide but the CharacterController "
                     f"radius is {CAPSULE_RADIUS} - the limbs will clip through walls")
    if horiz > DUMMY_RADIUS:
        flags.append(f"PLAYER TWO: baked body is {horiz:.3f} wide but the CapsuleCollider radius "
                     f"is {DUMMY_RADIUS} - hits register but the body visually clips")

    # A humanoid in T-pose is much wider than it is deep; a model with arms down is not. Worth
    # knowing, because a T-pose character in the scene looks wrong until the arms are lowered.
    print(f"horizontal footprint radius: {horiz:.3f} m")
    print(f"width (x) {o_size[0]:.3f} vs depth (z) {o_size[2]:.3f} "
          f"-> ratio {o_size[0] / max(o_size[2], 1e-6):.2f}")
    print()

    print("flags:", "; ".join(flags) if flags else "none")
    print()
    print("Capsule collider check (PlayerTwoDummy: centre 0, height 1.8, radius 0.4):")
    print(f"  body height {o_size[1]:.3f} vs collider height {DUMMY_HEIGHT} "
          f"-> {'fits' if o_size[1] <= DUMMY_HEIGHT else 'TALLER than the collider'}")
    print(f"  body radius {horiz:.3f} vs collider radius {DUMMY_RADIUS} "
          f"-> {'fits' if horiz <= DUMMY_RADIUS else 'wider than the collider'}")


if __name__ == "__main__":
    main()
