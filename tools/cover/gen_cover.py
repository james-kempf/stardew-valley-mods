#!/usr/bin/env python3
"""Generate a mod's Nexus cover (<out-dir>/cover.svg) as pixel art on a 320x180 grid.

Layouts (--layout):
  coop    sky, hills, fence, a wooden title sign (left) with a green ribbon
          under it, and a scene on the right (--scene). The defaults reproduce
          the AutoSplitScreenCoop cover.
  action  a cave action shot drawn by layout_action.py (DodgeRoll).
  storm   a night thunderstorm drawn by layout_storm.py (DeadlyLightning).
  speed   a sprint past a giant hourglass drawn by layout_speed.py (PerformanceBundle).
See tools/cover/README.md for the full pipeline.

Text uses the "Stardew Valley" FontStruct font by RRDome
(https://fontstruct.com/fontstructions/show/1254619), rasterised 1-bit at its
native 16px brick size and scaled by whole pixels. Font lookup: $COVER_FONT_PATH,
then tools/cover/font/stardew-valley.otf (gitignored), then
~/.local/share/fonts/stardew-valley.otf. COVER_FONT=bitmap switches to the
hand-made 5x7 font below (limited glyph set). Everything is drawn into a
palette-indexed bitmap, then emitted as merged axis-aligned rects with
shape-rendering="crispEdges". Original art only.
"""
import argparse
import math
import os
import random
import sys
from xml.sax.saxutils import escape as xml_escape

W, H = 320, 180

HERE = os.path.dirname(os.path.abspath(__file__))
SCENES = ("split-screen", "none")
ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
ap.add_argument("--out-dir", required=True, help="directory to write cover.svg into, e.g. MyMod/nexus")
ap.add_argument("--layout", choices=("coop", "action", "storm", "speed"), default="coop",
                help="coop: title sign + ribbon + right-hand scene (default); action: cave action shot; "
                     "storm: night thunderstorm; speed: sprint past an hourglass")
ap.add_argument("--title", action="append", metavar="LINE",
                help="title line; repeat for each line (default: Auto / Split-Screen / Co-op for coop, "
                     "Dodge Roll for action, Deadly / Lightning for storm, Performance / Bundle for speed)")
ap.add_argument("--title-tops", metavar="Y,Y,...",
                help="cap-top y of each title line (default: tuned values for the default title, else centred)")
ap.add_argument("--title-scale", type=int, default=2, help="integer scale for title text (default 2)")
ap.add_argument("--ribbon", default="Player 2 joins automatically", help="ribbon text (one line)")
ap.add_argument("--name", help="SVG <title>; default: title lines joined with spaces")
ap.add_argument("--scene", choices=SCENES, default="split-screen",
                help="coop right-hand scene: split-screen (monitor with P1/P2 farmers) or none")
ap.add_argument("--tagline", default="",
                help="action, storm and speed layouts: optional line under the title (default: none)")
ARGS = ap.parse_args()

PAL = {
    "ink": "#2b1a10",     # outlines
    "wd": "#4e2f14",      # wood dark
    "wm": "#8b5a2b",      # wood mid
    "wl": "#c48a4e",      # wood light
    "wh": "#e0ac6c",      # wood highlight / sunset
    "cream": "#fff3d6",
    "s1": "#4f9ad6",      # sky top
    "s2": "#7cc4f0",
    "s3": "#b4e2f8",
    "white": "#ffffff",
    "gold": "#ffd84a",    # sun, flowers
    "g3": "#3e7a24",      # grass dark
    "g2": "#5a9b33",
    "g1": "#7cbf47",      # grass light
    "rib": "#2f6b1f",     # ribbon green
    "ribd": "#1d4613",
    "blue": "#3d7cc9",    # P1 badge
    "red": "#c94a3d",     # P2 badge, flowers
    # P1 farmer's shirt: the reference shirt (sh, shd, sho) recoloured blue, same light/dark steps,
    # lighter and more cyan than the overalls. The other farmer colours come from
    # reference/default_farmer.py when the split-screen scene draws.
    "p1sh": "#3a9ad8",    # shirt
    "p1shd": "#2a72b0",   # shirt, shade
    "p1sho": "#123a63",   # shirt outline
}

grid = [["s1"] * W for _ in range(H)]
CLIP = None  # optional (x0, y0, x1, y1) inclusive bounds; px() drops pixels outside


def px(x, y, c):
    if CLIP and not (CLIP[0] <= x <= CLIP[2] and CLIP[1] <= y <= CLIP[3]):
        return
    if 0 <= x < W and 0 <= y < H:
        grid[y][x] = c


def set_clip(b):
    global CLIP
    CLIP = b


def rect(x, y, w, h, c):
    for j in range(y, y + h):
        for i in range(x, x + w):
            px(i, j, c)


def dither(x, y, w, h, a, b, phase=0):
    for j in range(y, y + h):
        for i in range(x, x + w):
            px(i, j, a if (i + j + phase) % 2 == 0 else b)


def disc(cx, cy, r):
    pts = []
    for j in range(cy - r, cy + r + 1):
        for i in range(cx - r, cx + r + 1):
            if (i - cx) ** 2 + (j - cy) ** 2 <= r * r + r * 0.6:
                pts.append((i, j))
    return pts


def outline_of(pts):
    s = set(pts)
    out = set()
    for (x, y) in s:
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                q = (x + dx, y + dy)
                if q not in s:
                    out.add(q)
    return out


def ortho_outline(pts):
    s = set(pts)
    out = set()
    for (x, y) in s:
        for q in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if q not in s:
                out.add(q)
    return out


def panel(x, y, w, h, fill, edge, cut=2):
    """Filled box with 1px outline and stepped (pixel-rounded) corners."""
    for j in range(h):
        for i in range(w):
            # distance into corner
            dx = min(i, w - 1 - i)
            dy = min(j, h - 1 - j)
            if dx + dy < cut:
                continue
            border = dx == 0 or dy == 0 or dx + dy == cut
            px(x + i, y + j, edge if border else fill)


# ---------------------------------------------------------------- font
# 5x7 cap height, rows 7-8 are descenders. Variable width, 1px tracking.
FONT_SRC = {
    "A": [".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
    "C": [".####", "#....", "#....", "#....", "#....", "#....", ".####"],
    "P": ["####.", "#...#", "#...#", "####.", "#....", "#....", "#...."],
    "S": [".####", "#....", "#....", ".###.", "....#", "....#", "####."],
    "a": [".....", ".....", ".###.", "....#", ".####", "#...#", ".####"],
    "c": [".....", ".....", ".####", "#....", "#....", "#....", ".####"],
    "e": [".....", ".....", ".###.", "#...#", "#####", "#....", ".####"],
    "i": [".#.", "...", "##.", ".#.", ".#.", ".#.", "###"],
    "j": ["..#", "...", ".##", "..#", "..#", "..#", "..#", "..#", "##."],
    "l": ["##.", ".#.", ".#.", ".#.", ".#.", ".#.", "###"],
    "m": [".....", ".....", "##.#.", "#.#.#", "#.#.#", "#.#.#", "#.#.#"],
    "n": [".....", ".....", "####.", "#...#", "#...#", "#...#", "#...#"],
    "o": [".....", ".....", ".###.", "#...#", "#...#", "#...#", ".###."],
    "p": [".....", ".....", "####.", "#...#", "#...#", "#...#", "####.", "#....", "#...."],
    "r": [".....", ".....", "#.##.", "##..#", "#....", "#....", "#...."],
    "s": [".....", ".....", ".####", "#....", ".###.", "....#", "####."],
    "t": [".#..", ".#..", "####", ".#..", ".#..", ".#..", "..##"],
    "u": [".....", ".....", "#...#", "#...#", "#...#", "#..##", ".##.#"],
    "y": [".....", ".....", "#...#", "#...#", "#...#", "#...#", ".####", "....#", "####."],
    "1": [".#.", "##.", ".#.", ".#.", ".#.", ".#.", "###"],
    "2": [".###.", "#...#", "....#", "..##.", ".#...", "#....", "#####"],
    "-": ["....", "....", "....", "####", "....", "....", "...."],
    " ": ["...", "...", "...", "...", "...", "...", "..."],
}


USE_TTF = os.environ.get("COVER_FONT", "ttf") != "bitmap"


def find_font():
    env = os.environ.get("COVER_FONT_PATH")
    if env:
        return env
    for p in (os.path.join(HERE, "font", "stardew-valley.otf"),
              os.path.expanduser("~/.local/share/fonts/stardew-valley.otf")):
        if os.path.isfile(p):
            return p
    sys.exit("gen_cover.py: font not found. Set COVER_FONT_PATH, put stardew-valley.otf in "
             "tools/cover/font/, or set COVER_FONT=bitmap.")


FONT_PATH = find_font() if USE_TTF else None
TTF_NATIVE = 16   # FontStruct brick = 125/2000 em -> exactly 1px at 16px
TTF_CAP_TOP = 2   # rows above cap height in PIL's layout box at 16px


def ttf_pts(s, scale=1):
    """1-bit (no anti-aliasing) raster of s at native size, scaled by an
    integer. Origin (0,0) = left edge, cap-height top."""
    from PIL import Image, ImageDraw, ImageFont
    ft = ImageFont.truetype(FONT_PATH, TTF_NATIVE)
    l, t, r, b = ft.getbbox(s)
    im = Image.new("1", (r + 2, b + 2), 0)
    d = ImageDraw.Draw(im)
    d.fontmode = "1"
    d.text((0, 0), s, font=ft, fill=1)
    pts = []
    for y in range(im.size[1]):
        for x in range(im.size[0]):
            if im.getpixel((x, y)):
                for sy in range(scale):
                    for sx in range(scale):
                        pts.append((x * scale + sx, (y - TTF_CAP_TOP) * scale + sy))
    return pts, r * scale


def text_pts(s, scale=1, track=1):
    if USE_TTF:
        return ttf_pts(s, scale)
    pts = []
    x = 0
    for ch in s:
        g = FONT_SRC[ch]
        gw = len(g[0])
        for row, line in enumerate(g):
            assert len(line) == gw, ch
            for col, v in enumerate(line):
                if v == "#":
                    for sy in range(scale):
                        for sx in range(scale):
                            pts.append((x + col * scale + sx, row * scale + sy))
        x += (gw + track) * scale
    width = x - track * scale
    return pts, width


def solidify(body, bold=0):
    """Thicken strokes `bold` px to the right, then bridge every corner-only
    diagonal contact so strokes join edge to edge. Counters stay open: a pixel
    is only added where its two filled orthogonal neighbours share an EMPTY
    diagonal (a stair step), never at a counter's inner corner."""
    body = set(body)
    for _ in range(bold):
        body |= {(x + 1, y) for (x, y) in body}
    add = set()
    for (x, y) in body:
        for dx in (-1, 1):
            for dy in (-1, 1):
                if (x + dx, y + dy) in body and (x + dx, y) not in body and (x, y + dy) not in body:
                    add.add((x + dx, y))
                    add.add((x, y + dy))
    return body | add


def draw_text(s, cx, top, scale, fill, outline=None, shadow=None, track=1, bold=None):
    pts, w = text_pts(s, scale, track)
    if bold is not None:
        w += bold
    x0 = cx - w // 2
    pts = [(x + x0, y + top) for (x, y) in pts]
    body = set(pts) if bold is None else solidify(pts, bold)
    ring = outline_of(body) if outline else set()
    if shadow:
        for (x, y) in body | ring:
            px(x + 1, y + 1, shadow)
    for (x, y) in ring:
        px(x, y, outline)
    for (x, y) in body:
        px(x, y, fill)
    return x0, w


# ---------------------------------------------------------------- sprites
def sprite(rows, x0, y0, cmap, k=3, outline=True):
    """Blit a cell sprite at integer scale k with a 1-cell ink outline (orthogonal,
    so corners stay rounded). (x0, y0) is the outline's top-left pixel.
    outline=False draws the cells only (for sprites that carry their own
    outline, like the reference farmer); (x0, y0) is then cell (0, 0)."""
    cells = {}
    for j, line in enumerate(rows):
        assert len(line) == len(rows[0]), line
        for i, v in enumerate(line):
            if v != ".":
                cells[(i + 1, j + 1)] = cmap[v]
    if not outline:
        cells = {(i - 1, j - 1): c for (i, j), c in cells.items()}
    ring = ortho_outline(cells) if outline else set()
    for (i, j) in ring | set(cells):
        assert 0 <= x0 + i * k and x0 + (i + 1) * k <= W and 0 <= y0 + j * k and y0 + (j + 1) * k <= H, \
            "sprite pixel out of bounds"
    for (i, j) in ring:
        rect(x0 + i * k, y0 + j * k, k, k, "ink")
    for (i, j), c in cells.items():
        rect(x0 + i * k, y0 + j * k, k, k, c)


# ---------------------------------------------------------------- layout: coop (default)
def layout_coop():
    """Sky, hills, fence, wooden title sign with a ribbon, and an optional
    right-hand scene (--scene). Returns the default SVG <title>."""
    # ---------------------------------------------------------------- sky
    rng = random.Random(7)
    bands = [(0, 34, "s1"), (34, 70, "s2"), (70, 112, "s3")]
    for y0, y1, c in bands:
        rect(0, y0, W, y1 - y0, c)
    # stepped dither transitions (2 rows checker, then sparse)
    for y0, a, b in [(34, "s1", "s2"), (70, "s2", "s3")]:
        dither(0, y0 - 2, W, 2, a, b)
        for i in range(W):
            if i % 4 == 0:
                px(i, y0 - 3, b)
            if i % 4 == 2:
                px(i, y0, a)
    rect(0, 112, W, H - 112, "s3")
    dither(0, 112, W, 4, "s3", "white")
    for i in range(W):
        if i % 4 == 0:
            px(i, 111, "white")

    # sun with halo
    sun = disc(298, 16, 11)
    for (x, y) in outline_of(disc(298, 16, 13)) | set(disc(298, 16, 13)):
        if (x + y) % 2 == 0:
            px(x, y, "cream")
    for (x, y) in sun:
        px(x, y, "gold")
    for (x, y) in disc(294, 12, 3):
        px(x, y, "cream")


    def cloud(cx, cy, parts):
        pts = set()
        for (dx, dy, r) in parts:
            pts |= set(disc(cx + dx, cy + dy, r))
        # flatten bottom
        maxy = cy + 4
        pts = {(x, y) for (x, y) in pts if y <= maxy}
        for (x, y) in pts:
            px(x, y, "white")
        # shaded underside
        for (x, y) in pts:
            if (x, y + 1) not in pts or (x, y + 2) not in pts:
                px(x, y, "s3")
        for (x, y) in pts:
            if (x, y + 1) not in pts:
                px(x, y, "s2" if (x % 2) else "s3")


    cloud(48, 14, [(-14, 2, 5), (-5, -2, 7), (6, 0, 6), (15, 2, 4)])
    cloud(222, 16, [(-11, 2, 4), (-3, -1, 6), (7, 1, 5)])
    cloud(148, 9, [(-6, 1, 3), (0, -1, 4), (6, 1, 3)])

    # ---------------------------------------------------------------- hills / grass
    def ground_y(x):
        return int(round(139 - 4 * math.sin(x / 38.0 + 0.6) - 2 * math.sin(x / 13.0)))


    # far hill silhouette
    for x in range(W):
        hy = int(round(128 - 6 * math.sin(x / 55.0 + 2.0)))
        px(x, hy, "g1" if (x + hy) % 2 else "s3")
        rect(x, hy + 1, 1, H - hy - 1, "g1")
    for x in range(W):
        gy = ground_y(x)
        px(x, gy - 1, "g3")
        for y in range(gy, H):
            d = y - gy
            if d < 4:
                c = "g1"
            elif d < 6:
                c = "g1" if (x + y) % 2 else "g2"
            elif d < 18:
                c = "g2"
            elif d < 20:
                c = "g2" if (x + y) % 2 else "g3"
            else:
                c = "g3"
            px(x, y, c)
    # grass tufts
    for _ in range(90):
        x = rng.randrange(2, W - 2)
        y = rng.randrange(ground_y(x) + 4, H - 1)
        c = "g3" if y < 160 else "g2"
        px(x, y, c)
        px(x - 1, y + 1, c)
        px(x + 1, y + 1, c)
    # flowers
    for _ in range(18):
        x = rng.randrange(4, W - 4)
        y = rng.randrange(ground_y(x) + 6, H - 3)
        px(x, y, rng.choice(["cream", "gold", "red"]))

    # ---------------------------------------------------------------- fence
    def wood_bar(x, y, w, h):
        rect(x, y, w, h, "wl")
        rect(x, y, w, 1, "wh")
        rect(x, y + h - 1, w, 1, "wm")
        for i in range(x, x + w):
            px(i, y - 1, "ink")
            px(i, y + h, "ink")


    wood_bar(0, 150, W, 3)
    wood_bar(0, 158, W, 3)
    # posts every 32 px; offset keeps them clear of the sign legs (x 33-41, 133-141)
    for x in range(19, 312, 32):
        rect(x - 1, 144, 7, 23, "ink")
        rect(x, 145, 5, 21, "wl")
        rect(x, 145, 1, 21, "wh")
        rect(x + 4, 145, 1, 21, "wm")
        px(x, 145, "ink")
        px(x + 4, 145, "ink")
        px(x + 2, 152, "wm")
        px(x + 2, 160, "wm")
        rect(x - 1, 166, 7, 1, "g3")

    # ---------------------------------------------------------------- title sign
    SX, SY, SW, SH = 10, 24, 154, 94
    # legs
    for lx in (34, 134):
        rect(lx - 1, SY + SH - 2, 8, 30, "ink")
        rect(lx, SY + SH - 2, 6, 29, "wm")
        rect(lx, SY + SH - 2, 1, 29, "wl")
    panel(SX, SY, SW, SH, "wm", "ink", cut=3)
    # frame bevel
    for i in range(SX + 3, SX + SW - 3):
        px(i, SY + 1, "wl")
        px(i, SY + SH - 2, "wd")
    for j in range(SY + 3, SY + SH - 3):
        px(SX + 1, j, "wl")
        px(SX + SW - 2, j, "wd")
    # nails
    for (nx, ny) in ((SX + 3, SY + 3), (SX + SW - 5, SY + 3), (SX + 3, SY + SH - 5), (SX + SW - 5, SY + SH - 5)):
        rect(nx, ny, 2, 2, "wd")
        px(nx, ny, "cream")
    IX, IY, IW, IH = SX + 6, SY + 5, SW - 12, SH - 10
    rect(IX - 1, IY - 1, IW + 2, IH + 2, "wd")
    rect(IX, IY, IW, IH, "wl")
    # planks: seams + grain
    plank_h = IH // 4
    for p in range(4):
        y0 = IY + p * plank_h
        if p:
            rect(IX, y0, IW, 1, "wm")
        rect(IX, y0 + 1, IW, 1, "wh")
        for _ in range(9):
            gx = rng.randrange(IX + 2, IX + IW - 14)
            gy = rng.randrange(y0 + 3, y0 + plank_h - 1)
            ln = rng.randrange(5, 14)
            rect(gx, gy, ln, 1, "wm" if rng.random() < 0.6 else "wh")
        # knot
        kx = rng.randrange(IX + 6, IX + IW - 6)
        ky = y0 + plank_h // 2
        rect(kx - 1, ky, 3, 1, "wm")
        px(kx, ky, "wd")

    cx = SX + SW // 2
    # title: one dark-brown fill, 1px down-right ink drop shadow, no outline
    # title: dark-brown fill, 1px down-right ink shadow; glyphs solidified
    # (diagonal stair steps bridged edge to edge, no extra weight)
    TSH = "ink"
    TB = 0
    DEFAULT_TITLE = ["Auto", "Split-Screen", "Co-op"]
    TITLE = ARGS.title or DEFAULT_TITLE
    if ARGS.title_tops:
        tops = [int(v) for v in ARGS.title_tops.split(",")]
        if len(tops) != len(TITLE):
            sys.exit("gen_cover.py: --title-tops needs one value per --title line")
        lines = [(t, y, ARGS.title_scale) for (t, y) in zip(TITLE, tops)]
    elif TITLE == DEFAULT_TITLE and USE_TTF:
        # (text, cap-top y, integer scale): all 2x (20px caps), planks span y29-112;
        # glyph corner-only joins bridged by solidify(bold=0)
        lines = (("Auto", 32, 2), ("Split-Screen", 56, 2), ("Co-op", 83, 2))
    elif TITLE == DEFAULT_TITLE:
        lines = (("Auto", 31, 3), ("Split-Screen", 57, 2), ("Co-op", 79, 3))
    else:
        # centre the block on the planks; pitch = cap height (10px) + 3px gap, x scale
        pitch = 13 * ARGS.title_scale
        first = IY + (IH - (pitch * (len(TITLE) - 1) + 10 * ARGS.title_scale)) // 2
        lines = [(t, first + i * pitch, ARGS.title_scale) for i, t in enumerate(TITLE)]
    for (t, top, k) in lines:
        if text_pts(t, k)[1] > IW - 4:
            sys.exit(f"gen_cover.py: title line {t!r} is wider than the sign ({IW - 4}px); "
                     "split it or lower --title-scale")
        draw_text(t, cx, top, k, "wd", shadow=TSH, bold=TB)

    # ---------------------------------------------------------------- ribbon
    RX, RY, RW, RH = 9, 120, 156, 18  # y120-137 (was 121-136)
    # swallowtails (behind the ribbon ends, notched)
    TH = RH - 4
    for side in (0, 1):
        tail = set()
        for j in range(TH):
            for i in range(8):
                x = (RX - 4 + i) if side == 0 else (RX + RW - 4 + i)
                tail.add((x, RY + 4 + j))
        mid = RY + 4 + TH // 2
        for j in range(-3, 4):
            d = 3 - abs(j)
            for i in range(d + 1):
                x = (RX - 4 + i) if side == 0 else (RX + RW + 3 - i)
                tail.discard((x, mid + j))
        for (x, y) in ortho_outline(tail):
            px(x, y, "ink")
        for (x, y) in tail:
            px(x, y, "ribd")
    panel(RX, RY, RW, RH, "rib", "ink", cut=2)
    rect(RX + 2, RY + 1, RW - 4, 1, "g2")
    rect(RX + 2, RY + RH - 2, RW - 4, 1, "ribd")
    # text top fixed at y122 (ttf) / y124 (bitmap); descenders now clear the stripes
    if text_pts(ARGS.ribbon)[1] > RW - 8:
        sys.exit(f"gen_cover.py: ribbon text is wider than the ribbon ({RW - 8}px); shorten it")
    draw_text(ARGS.ribbon, RX + RW // 2, 122 if USE_TTF else 124, 1, "cream", shadow="ribd")


    # ---------------------------------------------------------------- scene: split-screen monitor
    def scene_split_screen():
        """Monitor with two farmer panes labelled P1 / P2 (AutoSplitScreenCoop)."""
        MX, MY, MW, MH = 176, 30, 136, 92
        # stand
        rect(233, MY + MH, 24, 12, "ink")
        rect(234, MY + MH, 22, 11, "wd")
        rect(234, MY + MH, 2, 11, "wm")
        panel(218, MY + MH + 10, 54, 8, "wd", "ink", cut=2)
        rect(220, MY + MH + 11, 50, 1, "wm")
        # frame
        panel(MX, MY, MW, MH, "wd", "ink", cut=3)
        for i in range(MX + 3, MX + MW - 3):
            px(i, MY + 1, "wm")
        for j in range(MY + 3, MY + MH - 3):
            px(MX + 1, j, "wm")
        # frame grain
        for _ in range(14):
            side = rng.choice(["t", "b"])
            gx = rng.randrange(MX + 6, MX + MW - 16)
            gy = MY + 3 if side == "t" else MY + MH - 4
            rect(gx, gy, rng.randrange(4, 10), 1, "wm")
        # power LED
        rect(MX + MW - 10, MY + MH - 4, 2, 1, "g1")

        PX0, PY0 = MX + 6, MY + 6
        PW, PH = (MW - 12 - 2) // 2, MH - 12
        LX, RXp = PX0, PX0 + PW + 2
        HZ = PY0 + int(PH * 0.62)


        def pane_clip(x0):
            """Pane interior, inside the inner bezel shadow and clear of the divider."""
            return (x0 + 1 if x0 == LX else x0, PY0 + 1,
                    x0 + PW - 1 if x0 == LX else x0 + PW - 2, PY0 + PH - 2)


        def pane(x0, skyA, skyB, grassA, grassB):
            set_clip(pane_clip(x0))
            rect(x0, PY0, PW, PH, skyA)
            rect(x0, PY0 + PH // 3, PW, HZ - PY0 - PH // 3, skyB)
            dither(x0, PY0 + PH // 3 - 2, PW, 3, skyA, skyB)
            rect(x0, HZ, PW, PY0 + PH - HZ, grassA)
            dither(x0, HZ + 10, PW, 2, grassA, grassB)
            rect(x0, HZ + 12, PW, PY0 + PH - HZ - 12, grassB)
            rect(x0, HZ, PW, 1, "g3")
            set_clip(None)


        pane(LX, "s2", "s3", "g1", "g2")
        pane(RXp, "wh", "cream", "g1", "g2")
        # inner bezel shadow
        rect(PX0, PY0, PW * 2 + 2, 1, "ink")
        rect(PX0, PY0, 1, PH, "ink")
        rect(RXp + PW - 1, PY0, 1, PH, "ink")
        rect(PX0, PY0 + PH - 1, PW * 2 + 2, 1, "ink")

        # tiny scenery in panes
        for (x0, c) in ((LX, "white"), (RXp, "cream")):
            set_clip(pane_clip(x0))
            for (dx, dy) in ((8, 12), (9, 12), (10, 12), (7, 13), (8, 13), (9, 13), (10, 13), (11, 13)):
                px(x0 + dx, PY0 + dy, c)
        # crops rows in the panes
        for x0 in (LX, RXp):
            set_clip(pane_clip(x0))
            for k in range(5):
                cxp = x0 + 6 + k * 12
                for (dx, dy) in ((0, 0), (-1, 1), (1, 1), (0, 1)):
                    px(cxp + dx, HZ + 4 + dy, "g3")
                for (dx, dy) in ((0, 0), (-1, 1), (1, 1), (0, 1)):
                    px(cxp + 6 + dx, HZ + 15 + dy, "g3")
        set_clip(None)

        # farmers: the reference sprite (reference/default_farmer.py), unmodified for P2; P1 only swaps
        # the shirt keys for blue ones. Feet on the pane ground, centred, under the badge.
        farmer = reference_farmer()
        PAL.update(dict(farmer.PALETTE.values()))
        ref = {c: key for c, (key, _) in farmer.PALETTE.items()}
        p1 = dict(ref, **{c: {"sh": "p1sh", "shd": "p1shd", "sho": "p1sho"}.get(key, key) for c, key in ref.items()})
        k = 2
        fw, fh = k * len(farmer.ROWS[0]), k * len(farmer.ROWS)
        fy = PY0 + PH - 3 - fh                     # shoes 2 px above the pane bottom; shadow fills them
        for (x0, cmap) in ((LX, p1), (RXp, ref)):
            set_clip(pane_clip(x0))
            fx = x0 + (PW - fw) // 2
            for i in range(fw - 4):                # ground shadow just under the shoes
                px(fx + 2 + i, fy + fh, "g3")
                px(fx + 2 + i, fy + fh + 1, "g3" if i % 2 else "g2")
            sprite(farmer.ROWS, fx, fy, cmap, k=k, outline=False)
        set_clip(None)


        def badge(cxp, top, label, fill):
            pts, w = text_pts(label)
            th = max(y for (_, y) in pts) + 1
            bw, bh = w + 8, th + 4
            panel(cxp - bw // 2 - 1, top - 1, bw + 2, bh + 2, "white", "ink", cut=2)
            panel(cxp - bw // 2, top, bw, bh, fill, "white", cut=1)
            draw_text(label, cxp, top + 2, 1, "white", shadow=None)


        for (x0, lab, f) in ((LX, "P1", "blue"), (RXp, "P2", "red")):
            set_clip(pane_clip(x0))
            badge(x0 + PW // 2, PY0 + 3, lab, f)   # rows PY0+2..PY0+17, clear of the farmer below
        set_clip(None)

        # split divider: drawn last so no pane content overlaps it; spans exactly the
        # inner screen rows PY0..PY0+PH-1 (never into the wood frame)
        rect(LX + PW, PY0, 2, PH, "cream")


    if ARGS.scene == "split-screen":
        scene_split_screen()
    return " ".join(TITLE)


# ---------------------------------------------------------------- emit SVG
def emit(path, pal, name):
    # horizontal runs, then merge vertically identical runs
    runs = {}
    for y in range(H):
        x = 0
        while x < W:
            c = grid[y][x]
            x1 = x
            while x1 < W and grid[y][x1] == c:
                x1 += 1
            runs.setdefault((x, x1, c), []).append(y)
            x = x1
    by_color = {}
    for (x0, x1, c), ys in runs.items():
        ys.sort()
        start = prev = ys[0]
        for y in ys[1:] + [None]:
            if y is not None and y == prev + 1:
                prev = y
                continue
            by_color.setdefault(c, []).append((x0, start, x1 - x0, prev - start + 1))
            if y is not None:
                start = prev = y
    bg = max(by_color, key=lambda c: sum(w * h for (_, _, w, h) in by_color[c]))
    out = ['<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="720" '
           'viewBox="0 0 320 180" shape-rendering="crispEdges">',
           f'  <title>{xml_escape(name)}</title>',
           '  <!-- Pixel art on a 320x180 grid; generated, original art. -->',
           f'  <rect width="320" height="180" fill="{pal[bg]}"/>']
    for c in sorted(by_color, key=lambda k: list(pal).index(k)):
        if c == bg:
            continue
        out.append(f'  <g fill="{pal[c]}">')
        rs = sorted(by_color[c], key=lambda r: (r[1], r[0]))
        out.append("".join(f'<rect x="{x}" y="{y}" width="{w}" height="{h}"/>' for (x, y, w, h) in rs))
        out.append("  </g>")
    out.append("</svg>")
    with open(path, "w") as f:
        f.write("\n".join(out) + "\n")
    used = {c for row in grid for c in row}
    print(f"colors used: {len(used)}; rects: {sum(len(v) for v in by_color.values())}")


def load_module(name, path):
    """Load a module by path (python -I drops the script dir from sys.path)."""
    import importlib.util
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def load_layout(name):
    return load_module(f"layout_{name}", os.path.join(HERE, f"layout_{name}.py"))


def reference_farmer():
    """The default farmer's sprite data, reference/default_farmer.py: ROWS (14x29
    cells, outlines included, "." transparent) and PALETTE {char: (key, hex)}."""
    return load_module("default_farmer", os.path.join(HERE, "reference", "default_farmer.py"))


if ARGS.layout == "coop":
    pal, name = PAL, layout_coop()
else:
    lay = load_layout(ARGS.layout)
    pal, name = lay.PAL, lay.draw(sys.modules[__name__], ARGS)
os.makedirs(ARGS.out_dir, exist_ok=True)
emit(os.path.join(ARGS.out_dir, "cover.svg"), pal, ARGS.name or name)
