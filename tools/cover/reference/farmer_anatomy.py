"""Anatomy of the reference farmer (default_farmer.py), split into named body parts.

PARTS is a 14x29 map in the same cells as default_farmer.ROWS: each character names
the part that owns that sprite cell ("." where the sprite is transparent). Sides are
the viewer's: "_l" is the image's left (the farmer's own right). Cell coordinates are
(x, y) = (column, row) of default_farmer.ROWS, origin top-left.

    python3 -I tools/cover/reference/farmer_anatomy.py            # check only
    python3 -I tools/cover/reference/farmer_anatomy.py --render   # check, then write anatomy/*.png

The check exits non-zero if PARTS does not cover exactly the sprite's opaque cells
or if recombining the parts does not reproduce default_farmer.ROWS cell for cell.
Nothing in gen_cover.py or the layouts imports this module; it is a reference for
writing pose edits. See README.md in this folder.
"""

import importlib.util
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))

# code -> (part name, what it covers). Order is drawing order (back to front is irrelevant:
# parts never overlap) and the order used in the images.
PART_NAMES = {
    "h": ("hair", "hair and its dark outline, incl. the side lock beside the viewer-right eye"),
    "f": ("head", "face skin, forehead shadow, cheeks, chin, skin outline"),
    "b": ("brows", "the two brows"),
    "e": ("eyes", "eye whites, irises, the eye-outline cell left of the left eye"),
    "n": ("neck", "neck skin and its collar shadow"),
    "s": ("shirt", "red shirt at the collar and shoulders, with its outline"),
    "o": ("bib", "overall bib: straps, buttons, bib fill"),
    "L": ("sleeve_l", "viewer-left sleeve and its outline"),
    "l": ("hand_l", "viewer-left wrist and hand, with skin outline"),
    "R": ("sleeve_r", "viewer-right sleeve and its outline"),
    "r": ("hand_r", "viewer-right wrist and hand, with skin outline"),
    "w": ("belt", "belt and buckle"),
    "p": ("hips", "overalls seat across both legs, above the split"),
    "[": ("leg_l", "viewer-left pant leg"),
    "]": ("leg_r", "viewer-right pant leg"),
    "(": ("shoe_l", "viewer-left shoe"),
    ")": ("shoe_r", "viewer-right shoe"),
}

PARTS = [
    "...hhhhhh.....",
    "....hhhhhhh...",
    ".hhhhhhhhhhh..",
    "..hhhhhhhhhhh.",
    ".hhhhhhhhhhhh.",
    "hhhhhfhhhhhhh.",
    "hhhhfffffhhhhh",
    "h.hbbbffbbhhhh",
    "..feeeffeehh..",
    "..ffeeffeeff..",
    "...ffffffff...",
    "....ffffff....",
    "...sonnnnos...",
    "..ssossssoss..",
    "..ssossssoss..",
    ".LLLooooooRRR.",
    ".LLLooooooRRR.",
    ".LlLooooooRrR.",
    ".llLooooooRrr.",
    "llllwwwwwwrrrr",
    "llllpppppprrrr",
    ".llpppppppprr.",
    "...[[[[]]]]...",
    "...[[[[]]]]...",
    "...[[[[]]]]...",
    "...(((())))...",
    "..((((()))))..",
    "..((((()))))..",
    "...(((..)))...",
]

# Joint points for posing, as (x, y) cell coordinates in default_farmer.ROWS. A joint
# names the cell a part pivots on or attaches at; move or rotate the part around it.
JOINTS = {
    "neck": (6.5, 12),        # head sits on the neck; head centre line is x = 6.5..7
    "shoulder_l": (3, 15),    # top-inner sleeve cell, under the shirt shoulder
    "shoulder_r": (10, 15),
    "wrist_l": (2, 17),       # first skin cell below the sleeve
    "wrist_r": (11, 17),
    "hand_l": (1, 19),        # inner middle of the hand (rows 19-21)
    "hand_r": (12, 19),
    "waist": (6.5, 19),       # belt row, buckle at x = 6..7
    "hip_l": (5, 21),         # seat row above each leg
    "hip_r": (8, 21),
    "ankle_l": (4, 25),       # top shoe row, under each leg
    "ankle_r": (9, 25),
}


def load_farmer():
    """default_farmer.py from this folder, loaded by path (works under python3 -I)."""
    spec = importlib.util.spec_from_file_location("default_farmer", os.path.join(HERE, "default_farmer.py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def components(rows, parts=PARTS):
    """Split sprite rows by the part map.

    Returns {name: dict(code, rows, offset, bbox, size, colors)}: rows is the cropped
    grid ("." outside the part), offset the (x, y) of its top-left cell in the full
    sprite, bbox (x0, y0, x1, y1) inclusive, colors the sprite characters it uses.
    """
    out = {}
    for code, (name, _) in PART_NAMES.items():
        cells = [(x, y) for y, row in enumerate(parts) for x, p in enumerate(row) if p == code]
        if not cells:
            continue
        x0, x1 = min(x for x, _ in cells), max(x for x, _ in cells)
        y0, y1 = min(y for _, y in cells), max(y for _, y in cells)
        crop = [["."] * (x1 - x0 + 1) for _ in range(y1 - y0 + 1)]
        for x, y in cells:
            crop[y - y0][x - x0] = rows[y][x]
        out[name] = {
            "code": code,
            "rows": ["".join(r) for r in crop],
            "offset": (x0, y0),
            "bbox": (x0, y0, x1, y1),
            "size": (x1 - x0 + 1, y1 - y0 + 1),
            "colors": "".join(sorted({rows[y][x] for x, y in cells})),
        }
    return out


def recombine(comps, width, height):
    """Paste every component back at its offset; a cell written twice is an error."""
    grid = [["."] * width for _ in range(height)]
    for name, c in comps.items():
        ox, oy = c["offset"]
        for j, row in enumerate(c["rows"]):
            for i, ch in enumerate(row):
                if ch == ".":
                    continue
                if grid[oy + j][ox + i] != ".":
                    raise ValueError(f"{name} overlaps another part at {(ox + i, oy + j)}")
                grid[oy + j][ox + i] = ch
    return ["".join(r) for r in grid]


def check(rows, parts=PARTS):
    """List of problems (empty when the part map is valid and recombination is exact)."""
    errs = []
    if len(parts) != len(rows) or any(len(p) != len(r) for p, r in zip(parts, rows)):
        return ["PARTS shape differs from ROWS"]
    for y, (row, prow) in enumerate(zip(rows, parts)):
        for x, (c, p) in enumerate(zip(row, prow)):
            if (c == ".") != (p == "."):
                errs.append(f"cell {(x, y)}: sprite {c!r}, part {p!r}")
            elif p != "." and p not in PART_NAMES:
                errs.append(f"cell {(x, y)}: unknown part code {p!r}")
    if errs:
        return errs
    try:
        back = recombine(components(rows, parts), len(rows[0]), len(rows))
    except ValueError as e:
        return [str(e)]
    errs += [f"row {y}: {a!r} != {b!r}" for y, (a, b) in enumerate(zip(back, rows)) if a != b]
    for name, (jx, jy) in JOINTS.items():
        if not (0 <= jx < len(rows[0]) and 0 <= jy < len(rows)):
            errs.append(f"joint {name} outside the sprite")
    return errs


# ------------------------------------------------------------------ images

# One distinct tint per part for the overlay and exploded view (not game colours).
PART_TINT = {
    "hair": "#e6194b", "head": "#f58231", "brows": "#000000", "eyes": "#ffe119",
    "neck": "#bcf60c", "shirt": "#3cb44b", "bib": "#46f0f0", "sleeve_l": "#4363d8",
    "hand_l": "#911eb4", "sleeve_r": "#008080", "hand_r": "#f032e6", "belt": "#9a6324",
    "hips": "#fabebe", "leg_l": "#800000", "leg_r": "#000075", "shoe_l": "#808000",
    "shoe_r": "#aaffc3",
}
BG = "#dddddd"

# Exploded view: (dx, dy) cells added to each part's offset. Bands move down, paired
# parts move apart, and brows/eyes move out beside the head they sit on.
EXPLODE = {
    "hair": (0, 0), "head": (0, 6), "brows": (13, 6), "eyes": (13, 9), "neck": (0, 9),
    "shirt": (0, 12), "bib": (0, 17), "sleeve_l": (-5, 12), "sleeve_r": (5, 12),
    "hand_l": (-5, 16), "hand_r": (5, 16), "belt": (0, 19), "hips": (0, 21),
    "leg_l": (-1, 24), "leg_r": (1, 24), "shoe_l": (-1, 27), "shoe_r": (1, 27),
}


def _rgb(hexs):
    return tuple(int(hexs[i:i + 2], 16) for i in (1, 3, 5))


def _font(size):
    from PIL import ImageFont
    try:
        return ImageFont.load_default(size=size)
    except TypeError:        # Pillow without FreeType sizing
        return ImageFont.load_default()


def _paint(draw, rows, ox, oy, k, colors, tint=None, alpha=0.0):
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if ch == ".":
                continue
            c = _rgb(colors[ch])
            if tint:
                t = _rgb(tint)
                c = tuple(round(a * (1 - alpha) + b * alpha) for a, b in zip(c, t))
            draw.rectangle([ox + x * k, oy + y * k, ox + (x + 1) * k - 1, oy + (y + 1) * k - 1], fill=c)


def render(farmer, out_dir):
    from PIL import Image, ImageDraw

    rows = farmer.ROWS
    colors = {ch: hexs for ch, (_, hexs) in farmer.PALETTE.items()}
    comps = components(rows)
    W, H = len(rows[0]), len(rows)
    os.makedirs(os.path.join(out_dir, "parts"), exist_ok=True)
    written = []

    # each part alone, at 16x, inside a 1-cell margin
    k = 16
    for name, c in comps.items():
        w, h = c["size"]
        img = Image.new("RGB", ((w + 2) * k, (h + 2) * k), BG)
        _paint(ImageDraw.Draw(img), c["rows"], k, k, k, colors)
        path = os.path.join(out_dir, "parts", f"{name}.png")
        img.save(path)
        written.append(path)

    # overlay: original | part tints | legend
    k = 20
    font = _font(16)
    legend_w = 300
    img = Image.new("RGB", (2 * (W + 2) * k + legend_w, (H + 2) * k), BG)
    d = ImageDraw.Draw(img)
    _paint(d, rows, k, k, k, colors)
    for name, c in comps.items():
        ox, oy = c["offset"]
        _paint(d, c["rows"], (W + 3) * k + ox * k, k + oy * k, k, colors, PART_TINT[name], 0.7)
    for name, (jx, jy) in JOINTS.items():
        cx, cy = (W + 3) * k + (jx + 0.5) * k, k + (jy + 0.5) * k
        d.ellipse([cx - 4, cy - 4, cx + 4, cy + 4], fill="white", outline="black")
    lx = 2 * (W + 2) * k + 10
    for i, (name, c) in enumerate(comps.items()):
        y = k + i * 26
        d.rectangle([lx, y, lx + 18, y + 18], fill=PART_TINT[name], outline="black")
        d.text((lx + 26, y), f"{c['code']} {name}", fill="black", font=font)
    y = k + len(comps) * 26 + 8
    d.ellipse([lx + 5, y + 5, lx + 13, y + 13], fill="white", outline="black")
    d.text((lx + 26, y), "joint (JOINTS)", fill="black", font=font)
    path = os.path.join(out_dir, "overlay.png")
    img.save(path)
    written.append(path)

    # exploded view: each part at its offset + EXPLODE shift, labelled above its box
    k, pad = 12, 3
    placed = {n: (c["offset"][0] + EXPLODE[n][0], c["offset"][1] + EXPLODE[n][1]) for n, c in comps.items()}
    # inclusive cell boxes: the part, a 2-row label strip above it, 1 column of margin left
    boxes = {n: (x - 1, y - 2, x + comps[n]["size"][0] - 1, y + comps[n]["size"][1] - 1) for n, (x, y) in placed.items()}
    for a in boxes:
        for b in boxes:
            A, B = boxes[a], boxes[b]
            if a < b and A[0] <= B[2] and B[0] <= A[2] and A[1] <= B[3] and B[1] <= A[3]:
                raise ValueError(f"exploded view: {a} and {b} overlap; adjust EXPLODE")
    minx = min(x for x, _ in placed.values()) - pad
    miny = min(y for _, y in placed.values()) - pad
    maxx = max(placed[n][0] + comps[n]["size"][0] for n in comps) + pad
    maxy = max(placed[n][1] + comps[n]["size"][1] for n in comps) + pad
    img = Image.new("RGB", ((maxx - minx) * k + 120, (maxy - miny) * k), BG)
    d = ImageDraw.Draw(img)
    font = _font(13)
    for name, c in comps.items():
        px, py = (placed[name][0] - minx) * k + 60, (placed[name][1] - miny) * k
        w, h = c["size"]
        d.rectangle([px - 2, py - 2, px + w * k + 1, py + h * k + 1], outline=PART_TINT[name], width=2)
        _paint(d, c["rows"], px, py, k, colors)
        d.text((px, py - 16), name, fill="black", font=font)
    path = os.path.join(out_dir, "exploded.png")
    img.save(path)
    written.append(path)
    return written


def main(argv):
    farmer = load_farmer()
    errs = check(farmer.ROWS)
    if errs:
        print("farmer anatomy check FAILED:", *errs, sep="\n  ", file=sys.stderr)
        return 1
    comps = components(farmer.ROWS)
    print(f"farmer anatomy OK: {len(comps)} parts recombine to default_farmer.ROWS exactly")
    if "--render" in argv:
        for path in render(farmer, os.path.join(HERE, "anatomy")):
            print("wrote", os.path.relpath(path, os.getcwd()))
    if "--table" in argv:
        for name, c in comps.items():
            print(f"{c['code']} {name:9} offset={c['offset']} size={c['size']} bbox={c['bbox']} colors={c['colors']}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
