#!/usr/bin/env python3
"""Per-gun geometry check used to validate the WeaponModelBake auto-detect.

For every usable GLB this prints:
  * world-space extents (x, y, z) after composing node TRS down the hierarchy
  * the barrel axis = the axis with the largest extent
  * which end of the barrel is the MUZZLE, found by comparing how thin the model
    is at each end (a barrel is narrow, a stock + grip + magazine is wide)
  * the up axis = the non-barrel axis with the larger extent
  * the mean cross-section radius at each end, as evidence
"""
import json, struct, sys
from pathlib import Path
from math import sqrt

ROOT = Path("C:/Dev/projectLEA/Assets/Models/Manuel/Weapons")

AXES = ["x", "y", "z"]

def load_glb(path):
    raw = path.read_bytes()
    assert raw[:4] == b"glTF", "not a glb"
    length = struct.unpack_from("<I", raw, 8)[0]
    pos = 12
    chunks = []
    while pos < length:
        clen, ctype = struct.unpack_from("<II", raw, pos)
        chunks.append((ctype, raw[pos + 8: pos + 8 + clen]))
        pos += 8 + clen
    js = json.loads(next(c for t, c in chunks if t == 0x4E4F534A))
    bins = [c for t, c in chunks if t == 0x004E4942]
    return js, bins[0] if bins else b""

def resolve(uri, glb_bin):
    if uri is None:
        return glb_bin
    if uri.startswith("data:"):
        return b""
    return (ROOT.parent.parent / uri).read_bytes()

def all_buffers(js, glb_bin):
    out = []
    for b in js.get("buffers", []):
        out.append(resolve(b.get("uri"), glb_bin))
    return out

def accessor_view(js, acc_idx, buffers):
    acc = js["accessors"][acc_idx]
    bvs = js["bufferViews"]
    bv = bvs[acc["bufferView"]]
    data = buffers[bv["buffer"]]
    off = acc.get("byteOffset", 0) + bv.get("byteOffset", 0)
    ctype, fmt = acc["componentType"], acc["type"]
    n = acc["count"]
    comp = {5120: 1, 5121: 1, 5122: 2, 5123: 2, 5125: 4, 5126: 4}[ctype]
    ncomp = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[fmt]
    stride = bv.get("byteStride", comp * ncomp)
    out = []
    for i in range(n):
        p = off + i * stride
        vals = []
        for c in range(ncomp):
            if ctype == 5126:
                vals.append(struct.unpack_from("<f", data, p + c * 4)[0])
            elif ctype == 5122:
                vals.append(struct.unpack_from("<h", data, p + c * 2)[0])
            elif ctype == 5123:
                vals.append(struct.unpack_from("<H", data, p + c * 2)[0])
            elif ctype == 5120:
                v = data[p + c]
                vals.append(v - 256 if v > 127 else v)
            else:
                vals.append(data[p + c])
        out.append(vals)
    return out

def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz)

def qrot(q, v):
    qx, qy, qz, qw = q
    vx, vy, vz = v
    # v' = q * v * q^-1  (q assumed normalised)
    tx = 2 * (qy * vz - qz * vy)
    ty = 2 * (qz * vx - qx * vz)
    tz = 2 * (qx * vy - qy * vx)
    return (vx + qw * tx + qy * tz - qz * ty,
            vy + qw * ty + qz * tx - qx * tz,
            vz + qw * tz + qx * ty - qy * tx)

def node_transform(n, parent_xf):
    """Compose parent_xf * local(node). xf = (pos, quat, scale)."""
    t = n.get("translation", [0, 0, 0])
    r = n.get("rotation", [0, 0, 0, 1])
    s = n.get("scale", [1, 1, 1])
    if parent_xf is None:
        return (tuple(t), tuple(r), tuple(s))
    pt, pr, ps = parent_xf
    # local applied first: world = parent * local
    # scale
    ws = (ps[0] * s[0], ps[1] * s[1], ps[2] * s[2])
    # rotation: parent * local
    wr = qmul(pr, r)
    # translation: pt + pr * (ps * t)
    scaled = (ps[0] * t[0], ps[1] * t[1], ps[2] * t[2])
    rotated = qrot(pr, scaled)
    wt = (pt[0] + rotated[0], pt[1] + rotated[1], pt[2] + rotated[2])
    return (wt, wr, ws)

def world_verts(js, buffers):
    """Yield every position vertex in glb-root space, walking the node tree."""
    nodes = js.get("nodes", [])
    meshes = js.get("meshes", [])
    for ni, node in enumerate(nodes):
        pass
    def walk(ni, parent_xf, seen):
        if ni in seen:
            return
        seen.add(ni)
        node = nodes[ni]
        xf = node_transform(node, parent_xf)
        mi = node.get("mesh")
        if mi is not None:
            mesh = meshes[mi]
            for prim in mesh.get("primitives", []):
                pos_acc = prim["attributes"].get("POSITION")
                if pos_acc is None:
                    continue
                for v in accessor_view(js, pos_acc, buffers):
                    # node scale, then node rotation, then node translation
                    sv = (v[0] * xf[2][0], v[1] * xf[2][1], v[2] * xf[2][2])
                    rv = qrot(xf[1], sv)
                    yield (xf[0][0] + rv[0], xf[0][1] + rv[1], xf[0][2] + rv[2])
        for c in node.get("children", []):
            yield from walk(c, xf, seen)
    seen = set()
    scenes = js.get("scenes", [])
    if scenes:
        for ni in scenes[0].get("nodes", []):
            for v in walk(ni, None, seen):
                yield v
    else:
        for ni in range(len(nodes)):
            for v in walk(ni, None, seen):
                yield v

def analyse(path):
    js, glb_bin = load_glb(path)
    buffers = all_buffers(js, glb_bin)
    pts = list(world_verts(js, buffers))
    if not pts:
        return None
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]; zs = [p[2] for p in pts]
    lo = (min(xs), min(ys), min(zs))
    hi = (max(xs), max(ys), max(zs))
    ext = (hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2])

    barrel = max(range(3), key=lambda i: ext[i])
    order = sorted(range(3), key=lambda i: -ext[i])
    up = order[1]  # second-longest axis is "up" for a gun lying on its side

    # Thinness at each end of the barrel axis, in the model's own frame.
    lo_b, hi_b = lo[barrel], hi[barrel]
    span = hi_b - lo_b
    band = span * 0.12
    def band_stats(which):
        if which == "lo":
            sel = [p for p in pts if lo_b <= p[barrel] <= lo_b + band]
        else:
            sel = [p for p in pts if hi_b - band <= p[barrel] <= hi_b]
        if not sel:
            return 0.0, 0
        # mean distance from the barrel axis line (axis passes through the band centroid,
        # direction = barrel axis)
        cx = sum(p[(barrel + 1) % 3] for p in sel) / len(sel)
        cy = sum(p[(barrel + 2) % 3] for p in sel) / len(sel)
        r = 0.0
        for p in sel:
            dx = p[(barrel + 1) % 3] - cx
            dy = p[(barrel + 2) % 3] - cy
            r += sqrt(dx * dx + dy * dy)
        return r / len(sel), len(sel)
    r_lo, n_lo = band_stats("lo")
    r_hi, n_hi = band_stats("hi")

    # The narrow end is the muzzle.
    muzzle = "hi" if r_hi < r_lo else "lo"

    # Vertical centre of mass, expressed as a fraction of the up-axis height.
    up_lo, up_hi = lo[up], hi[up]
    com_up = sum(p[up] for p in pts) / len(pts)
    com_frac = (com_up - up_lo) / (up_hi - up_lo) if up_hi > up_lo else 0.5

    return dict(ext=ext, barrel=AXES[barrel], up=AXES[up],
                muzzle_sign="+" if muzzle == "hi" else "-",
                r_lo=r_lo, r_hi=r_hi, n_lo=n_lo, n_hi=n_hi, com=com_frac, verts=len(pts))

def main():
    files = sorted(ROOT.glob("*.glb"))
    print(f"{ 'model':<16}{'extents (x,y,z)':<30}{'barrel':<7}{'up':<5}{'muzzle':<8}"
          f"{'r@lo':>7}{'r@hi':>7}{'com_up':>8}{'verts':>8}")
    for f in files:
        r = analyse(f)
        if r is None:
            print(f"{f.stem:<16}NO MESHES")
            continue
        e = r["ext"]
        print(f"{f.stem:<16}({e[0]:6.2f},{e[1]:6.2f},{e[2]:6.2f})     "
              f"{r['barrel']:<7}{r['up']:<5}{r['muzzle_sign']:<8}"
              f"{r['r_lo']:7.3f}{r['r_hi']:7.3f}{r['com']:8.2f}{r['verts']:8d}")

if __name__ == "__main__":
    main()
