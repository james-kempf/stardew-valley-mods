"""Speed layout for gen_cover.py (--layout speed), used by the PerformanceBundle cover.

A bright summer morning in the valley. A giant wooden hourglass stands on the
meadow at the left, its last grains of sand trickling through the neck. The
farmer sprints past it to the right along a dirt path, arms pumping and one
knee up, trailing speed lines and kicked-up dust back toward the hourglass. The
title is stacked top-right in lime green with an ink outline, a deep green drop
shadow and speed streaks off its left edge, with an optional tagline under it
(--tagline).

gen_cover.py calls draw(cv, args) with itself as `cv` (pixel helpers, font,
sprite()) and emits the grid with PAL from this module. Uses its own RNG, so
the other layouts' output is untouched. The farmer is the reference sprite
(tools/cover/reference/default_farmer.py) in a running pose. Original art only.
"""
import math
import random
import sys

PAL = {
    "ink": "#1a1420",     # outlines
    "sk0": "#3f8fd8",     # sky, top
    "sk1": "#62aee8",
    "sk2": "#8fcbf2",
    "sk3": "#c3e6f8",     # sky at the horizon
    "cl0": "#d6e4f2",     # cloud shade
    "white": "#ffffff",
    "hl0": "#5e9f6a",     # far hills
    "hl1": "#7dba7a",
    "gr0": "#3b7a2c",     # meadow, darkest
    "gr1": "#5a9b33",
    "gr2": "#7cbf47",
    "pt0": "#8a5d34",     # dirt path, dark
    "pt1": "#b98552",
    "pt2": "#d9a86c",     # dirt path, light / dust
    "wd": "#4e2f14",      # hourglass wood
    "wm": "#8b5a2b",
    "wl": "#c48a4e",
    "gs0": "#a8d8ee",     # hourglass glass
    "gs1": "#d8f1fb",
    "sd0": "#c9902f",     # sand
    "sd1": "#ecc25a",
    "sd2": "#fbe79a",
    "tl0": "#2a6b1c",     # title shadow
    "tl1": "#5cb83a",     # title fill, lower
    "tl2": "#b6ef5a",     # title fill, upper
    "spd": "#eaf7ff",     # speed lines
    # draw() adds the farmer's 28 colours from reference/default_farmer.py
}

DEFAULT_TITLE = ["Performance", "Bundle"]

# The farmer is the reference sprite (reference/default_farmer.py, via cv.reference_farmer()) drawn
# at k=3, padded PADL cells on the left and PADR on the right, with two pose edits.
# 1. RUN_ARMS overlays rows ARMS_TOP.. (" " keeps the reference cell, "." clears it, anything else
#    is a reference char). The arm on the viewer's right pumps forward: the sleeve (R, shade r,
#    outline Q) runs straight out from the shoulder to the elbow, the cuff shade k and the
#    reference's 2x2 hand in its skin outline m stand up from it. The other arm swings out and down
#    in a 1-cell diagonal: sleeve R, cuff k, the 2x2 hand. Rows 18-21 erase both hanging arms and
#    close the body's sides with the shirt and overalls outlines (Q, Z).
# 2. STRIDE moves the reference's leg cells (rows 22-28): the planted leg on the viewer's right
#    shifts outward by STRIDE[row]; the other knee comes up: its leg keeps its top row and then
#    jumps to the shoe rows (LIFT), shifted outward the other way, so the foot hangs above the path.
PADL, PADR = 3, 4
ARMS_TOP = 11
RUN_ARMS = [
    "                  mm ",    # 11: top of the forward hand
    "                 mKKm",    # 12: forward hand
    "                 mKKm",    # 13
    "               QQmkm ",    # 14: sleeve top outline, cuff shade
    "              RRRRRQ ",    # 15: forward sleeve, shoulder to elbow
    "   QRR       QrrrrrQ ",    # 16: back sleeve starts; forward sleeve shade
    "  QRQ.       QQQQQQ  ",    # 17: back sleeve; forward sleeve bottom outline
    " mkm..        ...    ",    # 18: back cuff; hanging arms cleared
    "mKKm..Z      Z...    ",    # 19: back hand; belt ends
    "mKKm..Z      Z...    ",    # 20
    " mm...        ..     ",    # 21: bottom of the back hand
]
LEGS_TOP = 22
LEFT_LEG = (2, 7)       # reference cols [2, 7): the lifted leg and shoe
RIGHT_LEG = (7, 12)     # reference cols [7, 12): the planted leg and shoe
LIFT = [22, 23, 26, 27, 28]               # lifted leg: two leg rows, then the shoe
STRIDE = {22: (0, 0), 23: (-1, 1), 24: (-1, 1), 25: (-1, 1), 26: (-1, 2), 27: (-1, 2), 28: (0, 2)}

BAYER = ((0.0, 0.5), (0.75, 0.25))


def running_farmer(farmer):
    """Reference rows padded and posed (RUN_ARMS, then the STRIDE leg shifts)."""
    src = ["." * PADL + r + "." * PADR for r in farmer.ROWS]
    rows = list(src)
    for j, over in enumerate(RUN_ARMS, ARMS_TOP):
        assert len(over) == len(rows[j]), j
        rows[j] = "".join(o if o != " " else c for c, o in zip(rows[j], over))
    width = len(rows[0])
    legs = [["."] * width for _ in range(LEGS_TOP, len(src))]
    lift = {row: LIFT[i] for i, row in enumerate(range(LEGS_TOP, LEGS_TOP + len(LIFT)))}
    for row in range(LEGS_TOP, len(src)):
        dl, dr = STRIDE[row]
        out = legs[row - LEGS_TOP]
        for (c0, c1), d, from_row in ((LEFT_LEG, dl, lift.get(row)), (RIGHT_LEG, dr, row)):
            if from_row is None:
                continue
            for c in range(c0 + PADL, c1 + PADL):
                v = src[from_row][c]
                if v != ".":
                    out[c + d] = v
    for row, out in enumerate(legs, LEGS_TOP):
        rows[row] = "".join(out)
    return rows


def draw(cv, args):
    if not cv.USE_TTF:
        sys.exit("gen_cover.py: --layout speed needs the TTF font (COVER_FONT=bitmap lacks its glyphs)")
    W, H = cv.W, cv.H
    px, rect = cv.px, cv.rect
    rng = random.Random(1123)

    def lit(x, y, v, lo, hi):
        """Light level -> ramp index, Bayer-dithered across each step."""
        n = math.floor(v)
        if v - n > BAYER[y % 2][x % 2]:
            n += 1
        return max(lo, min(hi, n))

    HORIZON = 112

    def hill_y(x):
        return int(HORIZON - 6 + 5 * math.sin(x / 41.0 + 0.6) + 2 * math.sin(x / 13.0))

    def path_top(x):
        return int(150 + 2 * math.sin(x / 47.0 + 1.3))

    # ------------------------------------------------------------ sky and clouds
    for y in range(HORIZON + 10):
        for x in range(W):
            v = 3.4 * y / 112.0
            px(x, y, ("sk0", "sk1", "sk2", "sk3")[lit(x, y, v, 0, 3)])

    def cloud(cx, cy, parts):
        pts = set()
        for (dx, dy, r) in parts:
            pts |= set(cv.disc(cx + dx, cy + dy, r))
        pts = {(x, y) for (x, y) in pts if y <= cy + 3}
        for (x, y) in cv.ortho_outline(pts):
            if (x, y - 1) in pts or (x + y) % 2 == 0:
                px(x, y, "sk2")
        for (x, y) in pts:
            px(x, y, "cl0" if (x, y + 2) not in pts else "white")

    cloud(108, 80, [(-8, 2, 4), (0, -1, 6), (9, 2, 4)])
    cloud(262, 92, [(-8, 1, 5), (0, -2, 7), (9, 1, 5)])
    cloud(30, 14, [(-6, 2, 4), (2, -1, 6), (10, 2, 4)])

    # ------------------------------------------------------------ far hills and meadow
    for x in range(W):
        h = hill_y(x)
        for y in range(h, H):
            px(x, y, ("hl0", "hl1")[lit(x, y, 1.4 - (y - h) / 14.0, 0, 1)])
        px(x, h, "ink" if x % 2 else "hl0")
        g = HORIZON + int(3 * math.sin(x / 29.0))
        for y in range(g, H):
            px(x, y, ("gr0", "gr1", "gr2")[lit(x, y, 2.4 - (y - g) / 22.0, 0, 2)])
        px(x, g, "ink")
    for _ in range(80):                    # grass tufts
        x = rng.randrange(2, W - 3)
        y = rng.randrange(HORIZON + 6, 148)
        c = "gr2" if y < 128 else "gr1"
        px(x, y, c)
        px(x + 2, y, c)
        px(x + 1, y - 1, c)
    for _ in range(14):                    # flowers
        x = rng.randrange(4, W - 4)
        y = rng.randrange(HORIZON + 8, 146)
        px(x, y, "white")
        px(x, y + 1, "gr0")

    # ------------------------------------------------------------ dirt path
    for x in range(W):
        t = path_top(x)
        for y in range(t, H):
            px(x, y, ("pt0", "pt1", "pt2")[lit(x, y, 1.6 + (y - t) / 30.0, 0, 2)])
        px(x, t, "ink")
        px(x, t + 1, "pt0")
    for _ in range(40):                    # pebbles
        x = rng.randrange(2, W - 3)
        y = rng.randrange(path_top(x) + 4, H - 1)
        rect(x, y, rng.randrange(1, 3), 1, "pt2")
        px(x, y + 1, "pt0")

    # ------------------------------------------------------------ the hourglass
    HX0, HX1 = 14, 78                      # outer edges of the caps
    HCX = (HX0 + HX1) // 2
    TOPCAP, BOTCAP = 34, 140               # top edge of the top cap, top edge of the bottom cap
    NECK = (TOPCAP + 9 + BOTCAP) // 2      # glass neck row

    def bulb_half(y):
        """Half-width of the glass at row y: two round bulbs pinched at NECK."""
        top, bot = TOPCAP + 9, BOTCAP - 1
        if y <= NECK:
            t = (y - top) / (NECK - top)
        else:
            t = (bot - y) / (bot - NECK)
        return int(3 + 20 * math.sin(math.pi * min(1.0, t * 0.9 + 0.1) / 1.6) ** 1.2 * (1 - t ** 6))

    floor_shadow_y = BOTCAP + 9
    for y in range(floor_shadow_y - 2, floor_shadow_y + 3):     # shadow on the meadow
        for x in range(HX0 - 6, HX1 + 8):
            if ((x - HCX - 2) / 40) ** 2 + ((y - floor_shadow_y) / 2.6) ** 2 <= 1:
                px(x, y, "gr0")
    glass = set()
    for y in range(TOPCAP + 9, BOTCAP):
        hw = bulb_half(y)
        glass |= {(x, y) for x in range(HCX - hw, HCX + hw + 1)}
    # sand: a funnel left in the top bulb, a stream through the neck, a mound below
    sand = set()
    for (x, y) in glass:
        if NECK - 12 <= y < NECK and abs(x - HCX) <= (NECK - y) * 1.3 - 1:
            sand.add((x, y))
        if y > NECK and y >= BOTCAP - 1 - max(0, 22 - abs(x - HCX)) * 0.9:
            sand.add((x, y))
    stream = {(HCX, y) for y in range(NECK - 2, BOTCAP - 18)}
    for (x, y) in glass:
        p = abs(x - HCX) / max(1, bulb_half(y))
        px(x, y, "gs1" if p > 0.55 and x < HCX else ("gs0" if (x + y) % 2 == 0 or p > 0.7 else "gs1"))
    for (x, y) in sand:
        top_row = (x, y - 1) not in sand
        px(x, y, "sd2" if top_row else ("sd1" if (x, y + 2) in sand or y > NECK else "sd0"))
    for (x, y) in stream:
        px(x, y, "sd1" if y % 3 else "sd2")
    for y in range(TOPCAP + 12, NECK - 14):    # glint on the left of each bulb
        px(HCX - bulb_half(y) + 3, y, "white")
    for y in range(NECK + 10, BOTCAP - 26):
        px(HCX - bulb_half(y) + 3, y, "white")
    for (x, y) in cv.ortho_outline(glass):
        if TOPCAP + 9 <= y < BOTCAP:
            px(x, y, "ink")
    # posts: turned wood at both sides, lit from the left
    for (pxl, light) in ((HX0 + 3, True), (HX1 - 8, False)):
        for y in range(TOPCAP + 9, BOTCAP):
            bump = 1 if (y - TOPCAP) % 18 in (0, 1, 2) else 0
            rect(pxl - bump - 1, y, 7 + 2 * bump, 1, "ink")
            rect(pxl - bump, y, 5 + 2 * bump, 1, "wm")
            px(pxl - bump, y, "wl" if light else "wm")
            px(pxl + 4 + bump, y, "wd")
    # caps: thick planks with a lit top edge and an ink outline
    for top in (TOPCAP, BOTCAP):
        rect(HX0 - 1, top - 1, HX1 - HX0 + 2, 11, "ink")
        rect(HX0, top, HX1 - HX0, 9, "wm")
        rect(HX0, top, HX1 - HX0, 2, "wl")
        rect(HX0, top + 7, HX1 - HX0, 2, "wd")
        rect(HX0 + 2, top + 4, HX1 - HX0 - 4, 1, "wd")
        for x in range(HX0 + 6, HX1 - 4, 14):
            rect(x, top + 2, 1, 5, "wd")

    # ------------------------------------------------------------ title
    def speed_text(s, x0, top, tk):
        pts, w = cv.ttf_pts(s, tk)
        if x0 < 2 or x0 + w + 4 > W:
            sys.exit(f"gen_cover.py: title line {s!r} is too wide for the cover; shorten it")
        body = cv.solidify({(x + x0, y + top) for (x, y) in pts})
        ring = cv.outline_of(body)
        solid = body | ring
        for (x, y) in cv.outline_of(solid | {(x + 2, y + 2) for (x, y) in solid}) - solid:
            px(x, y, "ink")
        for (x, y) in {(x + d, y + d) for (x, y) in solid for d in (1, 2)} - solid:
            px(x, y, "tl0")
        for (x, y) in ring:
            px(x, y, "ink")
        mid = top + 5 * tk
        for (x, y) in body:
            if (x, y - 1) not in body:
                c = "white"
            elif y < mid:
                c = "tl2"
            elif y < mid + 2:
                c = "tl2" if (x + y) % 2 else "tl1"
            else:
                c = "tl1"
            px(x, y, c)
        # speed streaks off the line's left edge, on rows where the first letter has ink
        left = {}
        for (x, y) in ring:
            if y not in left or x < left[y]:
                left[y] = x
        for n, y in enumerate(sorted(left)[2:-2:4]):
            length = (18, 10, 26, 14, 8)[n % 5]
            for i in range(2, length):
                if i < length // 2 or (left[y] - i) % 2 == 0:
                    px(left[y] - i, y, "white")
        return w

    title = args.title or DEFAULT_TITLE
    TK = 3
    if len(title) > 2:
        sys.exit("gen_cover.py: --layout speed fits at most two title lines")
    # with a tagline the lines sit higher and closer, leaving room for it above the farmer's head
    top = 2 if args.tagline else (10 if len(title) == 2 else 8)
    lefts = (100, 172) if len(title) == 2 else (100,)
    for line, left in zip(title, lefts):
        w = cv.ttf_pts(line, TK)[1]
        speed_text(line, min(left, W - 8 - w), top, TK)
        top += 13 * TK - (5 if args.tagline else 2)
    tag_top = top + 1

    # ------------------------------------------------------------ the farmer
    farmer = cv.reference_farmer()
    PAL.update(dict(farmer.PALETTE.values()))
    cmap = {c: key for c, (key, _) in farmer.PALETTE.items()}
    rows = running_farmer(farmer)
    k = 3
    FX = 150                               # farmer cell (0, 0), top-left pixel
    FY = 172 - k * len(rows)               # planted shoe on the path
    FEET = FY + k * len(rows) - 1

    for y in range(FEET - 3, FEET + 3):    # shadow on the path
        for x in range(FX, FX + k * len(rows[0])):
            if ((x - FX - 34) / 26) ** 2 + ((y - FEET) / 2.6) ** 2 <= 1:
                px(x, y, "pt0")

    # speed lines: streaks trailing back toward the hourglass, solid near the farmer, dotted at the tail
    for (y, x1, n) in ((FY + 20, FX + 14, 52), (FY + 34, FX + 6, 66), (FY + 46, FX + 4, 40),
                       (FY + 58, FX + 10, 58), (FY + 70, FX + 14, 34), (FY + 8, FX + 22, 30)):
        for i in range(n):
            x = x1 - i
            if i < n * 0.55 or (i < n * 0.8 and x % 2 == 0) or x % 4 == 0:
                px(x, y, "spd")
                if i < n * 0.3:
                    px(x, y + 1, "sk3" if y + 1 < HORIZON else "gr2")

    # kicked-up dust behind the feet
    def puff(cx, cy, parts):
        pts = set()
        for (dx, dy, r) in parts:
            pts |= set(cv.disc(cx + dx, cy + dy, r))
        for (x, y) in cv.ortho_outline(pts):
            if (x + y) % 2 == 0:
                px(x, y, "pt0")
        for (x, y) in pts:
            px(x, y, "pt1" if (x, y + 1) not in pts else ("white" if (x, y - 1) not in pts else "pt2"))

    puff(FX + 6, FEET - 4, [(-5, 0, 4), (2, -2, 5), (8, 1, 3)])
    puff(FX - 14, FEET - 2, [(-3, 0, 3), (2, -1, 3)])
    puff(FX - 30, FEET, [(0, 0, 2)])
    for (x, y) in ((FX - 6, FEET - 14), (FX - 20, FEET - 10), (FX - 40, FEET - 6)):
        rect(x, y, 2, 2, "pt2")
        px(x + 1, y + 1, "pt0")

    cv.sprite(rows, FX, FY, cmap, k=k, outline=False)

    # tagline last, so it stays legible where it meets the farmer's hair
    if args.tagline:
        pts, w = cv.ttf_pts(args.tagline, 1)
        right = W - 9
        if right - w < 96:
            sys.exit("gen_cover.py: tagline is too wide; shorten it")
        body = cv.solidify({(x + right - w, y + tag_top) for (x, y) in pts})
        ring = cv.outline_of(body)
        for (x, y) in body | ring:
            px(x + 1, y + 1, "ink")
        for (x, y) in ring:
            px(x, y, "ink")
        for (x, y) in body:
            px(x, y, "white")

    return " ".join(title)
