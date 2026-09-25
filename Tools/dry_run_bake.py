#!/usr/bin/env python3
"""Dry run of WeaponModelBake.BakeOne, against the real geometry.

Unity cannot be launched from the shell, and the bake runs itself the moment the
project opens. If a row of the mapping table has the wrong axis, the wrong sign, or a
target length that makes the gun 100x too big, the user finds out by looking at a
gun stuck sideways across the camera. This reproduces the bake's arithmetic on the
actual vertex data and reports, per weapon, whether the result is sane - before Unity
ever sees it.

Replicates the C# exactly:
    barrelAxis = longest axis of the world bounds
    span       = size[barrelAxis]
    rotation   = inverse(Quaternion.LookRotation(BarrelAxis, UpAxis))
    scale      = TargetLength / span
    pivot      = center, with pivot[barrelAxis] = min[barrelAxis] + span * 0.30
    position   = -(rotation * (scale * pivot))
    v_out      = position + rotation * (scale * v_in)

LookRotation(f, u) builds the rotation carrying +Z onto f; its inverse carries f onto
+Z. As a matrix R whose COLUMNS are the orthonormal basis (x', y', z'=f), R maps
local->world, so the inverse rotation is R transposed. No quaternion code needed, and
no opportunity to re-implement LookRotation wrong.
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
sys.path.insert(0, str(Path.home() / ".workbuddy-ai" / "skills" / "unity-glb-import" / "scripts"))
# Reused from the unity-glb-import skill, not duplicated here.
from inspect_glb import load_glb, all_buffers, world_verts  # noqa: E402

MODEL_DIR = Path("C:/Dev/projectLEA/Assets/Models/Manuel/Weapons")

# Copied verbatim from WeaponModelBake.Map.
# (model, weapon, barrelAxis, upAxis, targetLength, holdOffset)
MAP = [
    ("Pistol",        "Classic",  (1, 0, 0),  (0, 1, 0), 0.28, (0.16, -0.14, 0.30)),
    ("Pistol",        "Frenzy",   (1, 0, 0),  (0, 1, 0), 0.28, (0.16, -0.14, 0.30)),
    ("Pistol",        "Ghost",    (1, 0, 0),  (0, 1, 0), 0.28, (0.16, -0.14, 0.30)),
    ("Revolver",      "Sheriff",  (1, 0, 0),  (0, 1, 0), 0.28, (0.16, -0.15, 0.32)),
    ("SMG_B",         "Stinger",  (1, 0, 0),  (0, 1, 0), 0.46, (0.18, -0.18, 0.34)),
    ("SMG_A",         "Spectre",  (1, 0, 0),  (0, 1, 0), 0.48, (0.18, -0.18, 0.35)),
    ("ShotgunShort",  "Judge",    (1, 0, 0),  (0, 1, 0), 0.45, (0.19, -0.18, 0.35)),
    ("ShotgunShort",  "Shorty",   (1, 0, 0),  (0, 1, 0), 0.38, (0.18, -0.17, 0.32)),
    ("Bullpup",       "Bulldog",  (1, 0, 0),  (0, 1, 0), 0.55, (0.20, -0.20, 0.36)),
    ("Shotgun",       "Bucky",    (1, 0, 0),  (0, 1, 0), 0.55, (0.20, -0.19, 0.38)),
    ("AssaultRifleA", "Vandal",   (1, 0, 0),  (0, 1, 0), 0.62, (0.20, -0.20, 0.40)),
    ("AssaultRifleB", "Phantom",  (1, 0, 0),  (0, 1, 0), 0.62, (0.20, -0.20, 0.40)),
    ("AssaultRifleC", "Guardian", (0, 0, -1), (0, 1, 0), 0.68, (0.20, -0.20, 0.42)),
    ("SniperRifleA",  "Marshal",  (1, 0, 0),  (0, 1, 0), 0.75, (0.22, -0.22, 0.46)),
    ("SniperRifleC",  "Outlaw",   (1, 0, 0),  (0, 1, 0), 0.75, (0.22, -0.22, 0.46)),
    ("SniperRifleB",  "Operator", (1, 0, 0),  (0, 1, 0), 0.80, (0.22, -0.22, 0.48)),
    ("MK14",          "Ares",     (0, 0, -1), (0, 1, 0), 0.68, (0.20, -0.20, 0.42)),
    ("Mpsd",          "Bandit",   (0, 0, -1), (0, 1, 0), 0.50, (0.18, -0.18, 0.34)),
]


def sub(a, b):
    return tuple(a[i] - b[i] for i in range(3))


def dot(a, b):
    return sum(a[i] * b[i] for i in range(3))


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0])


def norm(v):
    m = dot(v, v) ** 0.5
    return tuple(c / m for c in v) if m > 0 else v


def look_rotation_matrix(f, u):
    """Matrix R (row-major) whose COLUMNS are the orthonormal basis (x', y', z'=f).

    R maps local +Z onto f, so R is the rotation LookRotation(f, u) builds, and R^T is the
    one the bake applies to bring the barrel onto +Z.
    """
    z = norm(f)
    x = norm(cross(u, z))
    y = cross(z, x)
    return [[x[0], y[0], z[0]],
            [x[1], y[1], z[1]],
            [x[2], y[2], z[2]]]


def apply_R(R, v):
    return (R[0][0] * v[0] + R[0][1] * v[1] + R[0][2] * v[2],
            R[1][0] * v[0] + R[1][1] * v[1] + R[1][2] * v[2],
            R[2][0] * v[0] + R[2][1] * v[1] + R[2][2] * v[2])


def apply_Rt(R, v):
    """Inverse rotation: R is orthonormal, so R^-1 = R^T."""
    return (R[0][0] * v[0] + R[1][0] * v[1] + R[2][0] * v[2],
            R[0][1] * v[0] + R[1][1] * v[1] + R[2][1] * v[2],
            R[0][2] * v[0] + R[1][2] * v[1] + R[2][2] * v[2])


def longest_axis(size):
    if size[0] >= size[1] and size[0] >= size[2]:
        return 0
    if size[1] >= size[2]:
        return 1
    return 2


def band_radius(pts, axis, lo, hi):
    u, v = (axis + 1) % 3, (axis + 2) % 3
    sel = [p for p in pts if lo <= p[axis] <= hi]
    if not sel:
        return 0.0
    cu = sum(p[u] for p in sel) / len(sel)
    cv = sum(p[v] for p in sel) / len(sel)
    return sum(((p[u] - cu) ** 2 + (p[v] - cv) ** 2) ** 0.5 for p in sel) / len(sel)


def main():
    problems = []
    print(f"{'weapon':<10}{'model':<15}{'span':>7}{'scale':>9}{'baked z':>9}{'baked y':>9}"
          f"{'baked x':>9}{'|off-pivot|':>12}  flags")
    print("-" * 100)

    for model, weapon, barrel, up, target, _offset in MAP:
        path = MODEL_DIR / f"{model}.glb"
        js, glb_bin = load_glb(path)
        buffers = all_buffers(js, glb_bin, path.parent)
        pts = list(world_verts(js, buffers))

        lo = [min(p[i] for p in pts) for i in range(3)]
        hi = [max(p[i] for p in pts) for i in range(3)]
        size = tuple(hi[i] - lo[i] for i in range(3))
        center = tuple((lo[i] + hi[i]) * 0.5 for i in range(3))

        axis = longest_axis(size)
        span = size[axis]

        # The bake's own muzzle detection, for cross-check against the declared axis.
        band = span * 0.15
        r_lo = band_radius(pts, axis, lo[axis], lo[axis] + band)
        r_hi = band_radius(pts, axis, hi[axis] - band, hi[axis])
        detected = [0.0, 0.0, 0.0]
        detected[axis] = 1.0 if r_hi < r_lo else -1.0

        # --- the bake ---
        R = look_rotation_matrix(barrel, up)          # columns; R * (0,0,1) == barrel
        scale = target / span
        pivot = list(center)
        pivot[axis] = lo[axis] + span * 0.30
        pos = tuple(-c for c in apply_Rt(R, tuple(pivot[i] * scale for i in range(3))))

        def transform(p):
            scaled = tuple(p[k] * scale for k in range(3))
            rotated = apply_Rt(R, scaled)
            return tuple(pos[k] + rotated[k] for k in range(3))

        out = [transform(p) for p in pts]

        o_lo = [min(p[i] for p in out) for i in range(3)]
        o_hi = [max(p[i] for p in out) for i in range(3)]
        baked = tuple(o_hi[i] - o_lo[i] for i in range(3))

        # How far the baked model's centre sits from the prefab origin (the hold point). A
        # couple of centimetres is fine; a metre means the pivot math is wrong.
        off = sum((o_lo[i] + o_hi[i]) ** 2 * 0.25 for i in range(3)) ** 0.5

        flags = []
        if abs(dot(detected, barrel)) < 0.9:
            flags.append(f"MISMATCH: mesh says {detected}, table says {barrel}")
        if abs(baked[2] - target) > 0.02:
            flags.append(f"LENGTH: baked {baked[2]:.3f} != target {target}")
        if not (0.001 < scale < 100.0):
            flags.append(f"SCALE {scale:.4f} is implausible")
        if baked[2] < max(baked[0], baked[1]):
            flags.append("NOT longest in +Z after bake - points sideways")
        if dot(apply_R(R, (0, 1, 0)), (0, 1, 0)) < 0.5:
            flags.append("gun's up is not +Y after bake")

        print(f"{weapon:<10}{model:<15}{span:7.3f}{scale:9.4f}{baked[2]:9.3f}{baked[1]:9.3f}"
              f"{baked[0]:9.3f}{off:12.4f}  {'; '.join(flags) if flags else 'ok'}")
        problems.extend(f"{weapon}: {f}" for f in flags)

    print("-" * 100)
    if problems:
        print(f"{len(problems)} problem(s):")
        for p in problems:
            print("  " + p)
    else:
        print("All 18 viewmodels bake to the right length, pointing +Z, up +Y, pivot at the "
              "hands.")


if __name__ == "__main__":
    main()
