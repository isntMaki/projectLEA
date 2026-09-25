#!/usr/bin/env python3
"""Simulates ClassSelectUI's layout at real resolutions.

The class-select screen is authored at 1920x1200 and scaled by
min(w/1920, h/1200). The user reported seeing "all those top part and bottom part"
bleeding outside the frame, and Unity cannot be launched from the shell to look at it -
so the layout arithmetic is reproduced here instead and checked against the screen edges
at every resolution the game is likely to run at.

Every element is anchored to the TOP of the screen with a negative Y offset, so a bleed
means some element's bottom edge sits below the screen, or its top sits above it.
"""
import re
from pathlib import Path

REFERENCE = (1920.0, 1200.0)

COLUMN_WIDTH = 430.0
COLUMN_GAP = 14.0
CARD_HEIGHT = 62.0
CARD_GAP = 8.0
GRID_TOP = -196.0          # heading, measured down from the top
DETAIL_TOP = -806.0        # detail plate top
DETAIL_HEIGHT = 254.0
SIDE_MARGIN = 80.0

CHIP_HEIGHT = 78.0
CHIP_TOP = -28.0
HEADING_OFFSET = 46.0      # cards start this far below the heading
HEADING_RULE = 36.0


def classes_per_role(classes_dir):
    counts = {0: 0, 1: 0, 2: 0, 3: 0}
    for f in sorted(Path(classes_dir).glob("*.asset")):
        text = f.read_bytes().decode("utf-8-sig", errors="replace")
        m = re.search(r"^  role: (\d+)", text, re.M)
        if m:
            counts[int(m.group(1))] += 1
    return counts


def fitted_card_height(scale, tallest):
    authored = CARD_HEIGHT * scale
    if tallest <= 0:
        return authored
    column_top = (GRID_TOP - HEADING_OFFSET) * scale
    available = abs(DETAIL_TOP * scale - column_top) - CARD_GAP * scale
    fitted = (available - (tallest - 1) * CARD_GAP * scale) / tallest
    if fitted >= authored:
        return authored
    return max(fitted, 28.0 * scale)


def layout(width, height, per_role):
    scale = max(0.2, min(width / REFERENCE[0], height / REFERENCE[1]))
    tallest = max(per_role.values())

    grid_width = 4 * COLUMN_WIDTH * scale + 3 * COLUMN_GAP * scale
    left_edge = (width - grid_width) / 2
    right_edge = left_edge + grid_width

    # Every element is anchored to the TOP edge with a negative offset and a top pivot, so
    # anchoredPosition.y = -N means "N pixels BELOW the top of the screen". screen_top is
    # therefore -offset, measured down from y = 0 at the top; screen_bottom = top + height.
    # A bottom edge past `height` is a bleed off the bottom of the screen.
    elements = []

    def add(name, ui_offset, ui_height):
        top = -ui_offset * scale
        elements.append((name, top, top + ui_height * scale))

    add("title", -28, 54)
    add("timer", -88, 38)
    add("chips", CHIP_TOP, CHIP_HEIGHT)
    add("grid headings", GRID_TOP, 34)
    add("grid cards", GRID_TOP - HEADING_OFFSET, 0)  # height added per column below

    card_h = fitted_card_height(scale, tallest)
    for role, count in enumerate(per_role.values()):
        top = (HEADING_OFFSET - GRID_TOP) * scale
        bottom = top + count * card_h + max(0, count - 1) * CARD_GAP * scale
        elements.append((f"column {role}", top, bottom))

    # The detail plate, and the LOCK IN button / status line below it. All anchored to the
    # top with negative offsets, so their screen distance down is -offset.
    plate_top = -DETAIL_TOP * scale
    elements.append(("detail plate", plate_top, plate_top + DETAIL_HEIGHT * scale))

    lock_top = -DETAIL_TOP * scale + DETAIL_HEIGHT * scale + 18 * scale
    elements.append(("lock button", lock_top, lock_top + 66 * scale))
    status_top = -DETAIL_TOP * scale + DETAIL_HEIGHT * scale + 20 * scale
    elements.append(("status line", status_top, status_top + 30 * scale))

    return {
        "scale": scale,
        "grid_left": left_edge,
        "grid_right": right_edge,
        "card_height": card_h,
        "elements": elements,
    }


RESOLUTIONS = [
    (1280, 720), (1366, 768), (1440, 900), (1536, 864), (1600, 900),
    (1680, 1050), (1920, 1080), (1920, 1200), (2560, 1080), (2560, 1440),
    (3440, 1440), (3840, 2160),
]


def main():
    classes_dir = "C:/Dev/projectLEA/Assets/Data/Manuel/Classes"
    per_role = classes_per_role(classes_dir)
    names = {0: "Duelist", 1: "Initiator", 2: "Controller", 3: "Sentinel"}

    print("Roster:")
    for r in range(4):
        print(f"  {names[r]:<10} {per_role[r]}")
    tallest = max(per_role.values())
    print(f"  tallest column = {tallest} cards\n")

    print(f"{'resolution':<14}{'scale':>7}{'card h':>8}{'grid bot':>10}"
          f"{'detail bot':>11}{'lock bot':>10}{'lowest':>8}{'left':>8}{'right':>8}  flags")
    print("-" * 100)

    any_problem = False
    for w, h in RESOLUTIONS:
        r = layout(w, h, per_role)

        lowest = max(bottom for _, _, bottom in r["elements"])
        highest = min(top for _, _, top in r["elements"])

        flags = []
        if lowest > h:
            flags.append(f"BLEEDS BOTTOM by {lowest - h:.0f}px")
        if highest < 0:
            flags.append(f"BLEEDS TOP by {-highest:.0f}px")
        if r["grid_left"] < 0:
            flags.append(f"grid off LEFT by {-r['grid_left']:.0f}px")
        if r["grid_right"] > w:
            flags.append(f"grid off RIGHT by {r['grid_right'] - w:.0f}px")
        any_problem = any_problem or bool(flags)

        by_name = {n: (t, b) for n, t, b in r["elements"]}
        grid_bot = max(b for n, _, b in r["elements"] if n.startswith("column"))
        det_bot = by_name["detail plate"][1]
        lock_bot = by_name["lock button"][1]

        print(f"{w}x{h:<10}{r['scale']:7.3f}{r['card_height']:8.1f}"
              f"{grid_bot:10.0f}{det_bot:11.0f}{lock_bot:10.0f}"
              f"{lowest:8.0f}{r['grid_left']:8.0f}{r['grid_right']:8.0f}  "
              f"{'; '.join(flags) if flags else 'ok'}")

    print("-" * 100)
    if any_problem:
        print("PROBLEMS FOUND - see flags above.")
    else:
        print("No element extends past any screen edge at any tested resolution.")
    print()
    print("Note: the backdrop is a full-stretch rect (anchors 0..1, zero offsets), so it")
    print("always covers the screen regardless of resolution. A bleed reported by the user")
    print("therefore means an ELEMENT extending past an edge, which is what this checks.")


if __name__ == "__main__":
    main()
