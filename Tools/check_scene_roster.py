#!/usr/bin/env python3
"""Resolves the class roster GUIDs serialised in Manuel.unity to their roles.

ClassSelectUI splits the roster into four columns by role, and the layout only fits if the
tallest column is within what FittedCardHeight can compress. The scene serialises the
roster as GUID references, so this maps each one back to its .asset's role field.
"""
import collections
import re
from pathlib import Path

SCENE = Path("C:/Dev/projectLEA/Assets/Scenes/Manuel.unity")
CLASSES = Path("C:/Dev/projectLEA/Assets/Data/Manuel/Classes")
ROLE_NAMES = {0: "Duelist", 1: "Initiator", 2: "Controller", 3: "Sentinel"}


def main():
    text = SCENE.read_bytes().decode("utf-8-sig", errors="replace")
    lines = text.replace("\r\n", "\n").split("\n")

    # Find the 'classes:' key inside the ClassManager component, then collect the GUID
    # references that follow it until the list ends.
    start = next(i for i, ln in enumerate(lines) if ln.strip() == "classes:")
    guids = []
    for ln in lines[start + 1:]:
        stripped = ln.strip()
        if not stripped.startswith("- {fileID:"):
            break
        m = re.search(r"guid: ([0-9a-f]{32})", stripped)
        if m:
            guids.append(m.group(1))

    if not guids:
        print("No roster entries found under 'classes:'.")
        return

    # GUID -> role, from each asset's .meta (for the GUID) and the asset (for the role).
    role_of = {}
    for meta in CLASSES.glob("*.meta"):
        guid = re.search(r"guid: ([0-9a-f]{32})", meta.read_text(encoding="utf-8-sig"))
        if not guid:
            continue
        asset = meta.with_suffix("")
        role = re.search(r"role: (\d+)", asset.read_text(encoding="utf-8-sig"))
        role_of[guid.group(1)] = int(role.group(1)) if role else -1

    counts = collections.Counter(role_of.get(g, "MISSING") for g in guids)
    print(f"roster entries in scene: {len(guids)}")
    for r in range(4):
        print(f"  {ROLE_NAMES[r]:<10} {counts.get(r, 0)}")
    if counts.get("MISSING"):
        print(f"  MISSING/unresolved: {counts['MISSING']}")

    tallest = max(counts.get(r, 0) for r in range(4))
    print(f"  tallest column = {tallest}")


if __name__ == "__main__":
    main()
