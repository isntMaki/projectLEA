#!/usr/bin/env python3
"""Scale analysis for the two large supplied assets: the map and the character.

Both GLBs come from authors who used wildly different units than the game (the Quaternius
guns are ~5 units long for a 0.6 m rifle, so 1 of their units is roughly 12 cm). Before
either can be placed we need to know the author's unit, and the only honest way is to
measure architectural features of the mesh itself:

  * the map  -> floor thickness, storey height (floor top to next floor top), doorway height
  * the guy  -> total height, and where the vertical centre sits

Prints the raw numbers plus the scale factor implied by assuming the obvious real-world
size (a 2.7 m storey, a 1.8 m person).
"""
import struct, sys
from pathlib import Path
from math import sqrt

sys.path.insert(0, str(Path(__file__).parent))
from inspect_guns import load_glb, all_buffers, world_verts


def stats(path):
    js, glb_bin = load_glb(path)
    buffers = all_buffers(js, glb_bin)
    pts = list(world_verts(js, buffers))
    if not pts:
        return None
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    zs = [p[2] for p in pts]
    lo = (min(xs), min(ys), min(zs))
    hi = (max(xs), max(ys), max(zs))
    return pts, lo, hi


def histogram(pts, axis, bin_size):
    """Vertex-count histogram along one axis, to find floors and ceilings."""
    lo = min(p[axis] for p in pts)
    hi = max(p[axis] for p in pts)
    n = int((hi - lo) / bin_size) + 1
    bins = [0] * n
    for p in pts:
        bins[min(int((p[axis] - lo) / bin_size), n - 1)] += 1
    return lo, hi, bins


def analyse_map(path):
    pts, lo, hi = stats(path)
    print(f"  raw extents: x {hi[0] - lo[0]:.2f}  y {hi[1] - lo[1]:.2f}  z {hi[2] - lo[2]:.2f}")
    print(f"  raw bounds:  min ({lo[0]:.2f}, {lo[1]:.2f}, {lo[2]:.2f})")
    print(f"               max ({hi[0]:.2f}, {hi[1]:.2f}, {hi[2]:.2f})")
    print(f"  vertices:    {len(pts)}")

    # A floor is a dense horizontal slab. Histogram the height at a fine resolution and
    # look for spikes - those are the floor/ceiling plates.
    print("\n  height histogram (0.25 unit bins, bars scaled to 120):")
    ylo, yhi, bins = histogram(pts, 1, 0.25)
    peak = max(bins)
    for i, c in enumerate(bins):
        if c < peak * 0.02:
            continue
        bar = "#" * max(1, int(c / peak * 120))
        print(f"    y {ylo + i * 0.25:7.2f}  {c:8d}  {bar}")

    # The thickest single solid run of vertices near the bottom is the ground floor slab.
    # Report the vertical gaps between the big spikes: those are the storey heights.
    spikes = [ylo + i * 0.25 for i, c in enumerate(bins) if c > peak * 0.10]
    if len(spikes) >= 2:
        gaps = [round(spikes[i + 1] - spikes[i], 2) for i in range(len(spikes) - 1)]
        print(f"\n  major horizontal layers at y = {spikes}")
        print(f"  gaps between them (candidate storey heights): {gaps}")
        median_gap = sorted(gaps)[len(gaps) // 2]
        print(f"  median gap {median_gap:.2f} units")
        if median_gap > 0:
            print(f"  -> if a storey is 2.70 m, 1 unit = {2.70 / median_gap:.4f} m, "
                  f"scale to apply = {2.70 / median_gap:.4f}")
            print(f"  -> if a storey is 3.00 m, 1 unit = {3.00 / median_gap:.4f} m, "
                  f"scale to apply = {3.00 / median_gap:.4f}")
    return pts, lo, hi


def analyse_character(path):
    pts, lo, hi = stats(path)
    ext = (hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2])
    print(f"  raw extents: x {ext[0]:.2f}  y {ext[1]:.2f}  z {ext[2]:.2f}")
    print(f"  vertices:    {len(pts)}")
    print(f"  y bounds:    {lo[1]:.2f} .. {hi[1]:.2f}  "
          f"({'feet at 0' if abs(lo[1]) < 0.01 else 'pivot is NOT at the feet'})")
    centre = sum(p[1] for p in pts) / len(pts)
    print(f"  mean vertex y (approx vertical centre): {centre:.2f} "
          f"= {centre / ext[1] * 100:.0f}% of the height")
    target = 1.8
    scale = target / ext[1]
    print(f"\n  -> to stand {target} m tall, scale = {scale:.4f}")
    print(f"  -> after that scale the bounds centre sits at local y "
          f"{(lo[1] + hi[1]) * 0.5 * scale:+.3f}")
    return ext


def main():
    paths = [Path(p) for p in sys.argv[1:]] or [
        Path("C:/Dev/projectLEA/Assets/Models/Manuel/Maps/FpsTpsMap.glb"),
        Path("C:/Dev/projectLEA/Assets/Models/Manuel/Characters/PlayerGuy.glb"),
    ]
    for path in paths:
        print(f"\n=== {path.name} ===")
        if not path.exists():
            print(f"  MISSING: {path}")
            continue
        if not stats(path):
            print("  NO MESHES")
            continue
        if "Map" in path.stem or "map" in path.stem:
            analyse_map(path)
        else:
            analyse_character(path)


if __name__ == "__main__":
    main()
