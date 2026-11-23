"""Storm layout for gen_cover.py (--layout storm), used by the DeadlyLightning cover.

A farm at night in a thunderstorm. Heavy clouds across the top, rain streaks
over everything, a farmhouse with a lit window on the far hill and a bare tree
on the right. A jagged bolt (with forks) drops from the clouds onto a small
farmer's raised hand; the ring on it glints inside a dithered flash halo, and
the flash lights the clouds, sky and grass around the strike. The title is
stacked on the right, right-aligned, in electric yellow with a blue glow, with
an optional tagline under it (--tagline).

gen_cover.py calls draw(cv, args) with itself as `cv` (pixel helpers, font,
sprite()) and emits the grid with PAL from this module. Uses its own RNG, so
the other layouts' output is untouched. The farmer is the reference sprite
(tools/cover/reference/default_farmer.py) with one arm raised.
"""
import math
import random
import sys

PAL = {
    "ink": "#0b0a16",     # outlines
    "sk0": "#10152b",     # night sky, darkest
    "sk1": "#18203f",
    "sk2": "#243159",
    "sk3": "#364a7e",
    "sk4": "#5670a8",     # sky next to the flash
    "cl0": "#1e1b30",     # storm cloud, darkest
    "cl1": "#2f2a48",
    "cl2": "#4a4368",
    "cl3": "#7a72a0",     # cloud lit by the bolt
    "hl0": "#0f1a1e",     # far hill
    "hl1": "#18292a",
    "gr0": "#0e2116",     # foreground grass, darkest
    "gr1": "#193620",
    "gr2": "#2a5530",
    "gr3": "#467d44",     # grass in the flash
    "rain": "#7389bd",
    "el0": "#3a5fc4",     # electric glow, outer
    "el1": "#8fb4ff",     # electric glow, inner
    "yl0": "#e89a2a",     # title shade
    "yl1": "#ffe25a",     # title fill / bolt glow
    "white": "#ffffff",
    "win": "#ffc95a",     # lit window
    "gold": "#ffd23a",    # ring
    # draw() adds the farmer's 28 colours from reference/default_farmer.py (52 in all)
}

DEFAULT_TITLE = ["Deadly", "Lightning"]

# The farmer is the reference sprite (reference/default_farmer.py, via cv.reference_farmer()) drawn
# at k=2, with one pose edit: the arm on the viewer's right (toward the bolt) is raised instead of
# hanging. RAISED_ARM overlays the reference rows, padded PAD cells on the right: " " keeps the
# reference cell, "." clears it, anything else is a reference char.
# The arm is one straight diagonal from the shoulder, up and away from the head, stepping 1 cell
# across per 2 up (about 63 degrees): its rows alternate 2 and 1 cells wide. It is as long as the
# reference's hanging arm, 6 rows: shoulder (row 14, r under the arm), sleeve R (row 13) with the
# shirt outline Q, cuff shade k (row 12), wrist K (row 11), then the reference's 2x2 hand
# (cols 13-14, rows 9-10) in its skin outline m, beside the face and clear of the hair.
# Rows 15-21 close the body's side with the shirt and overalls outlines and erase the hanging arm.
RAISED_ARM = [
    "             mm ",    # 8: top of the hand
    "            mKKm",    # 9: hand
    "            mKKm",    # 10
    "            mKm ",    # 11: wrist
    "           mkkm ",    # 12: cuff shade
    "            RQ  ",    # 13: sleeve
    "           rrQ  ",    # 14: shoulder, shade under the arm
    "           Q    ",    # 15: armpit, shirt outline
    "           ..   ",    # 16: hanging arm cleared
    "           ..   ",    # 17
    "           ..   ",    # 18
    "          Z...  ",    # 19: belt end
    "          Z...  ",    # 20
    "           ..   ",    # 21
]
ARM_TOP = 8
PAD = 2

BAYER = ((0.0, 0.5), (0.75, 0.25))


def draw(cv, args):
    if not cv.USE_TTF:
        sys.exit("gen_cover.py: --layout storm needs the TTF font (COVER_FONT=bitmap lacks its glyphs)")
    W, H = cv.W, cv.H
    px, rect = cv.px, cv.rect
    rng = random.Random(1122)

    def step(v):
        """Continuous light level -> integer, dithered only near each step."""
        n = math.floor(v)
        f = v - n
        return n + max(0.0, min(1.0, (f - 0.35) / 0.3))

    def lit(x, y, v, lo, hi):
        lv = step(v)
        n = math.floor(lv)
        if lv - n > BAYER[y % 2][x % 2]:
            n += 1
        return max(lo, min(hi, n))

    farmer = cv.reference_farmer()
    PAL.update(dict(farmer.PALETTE.values()))
    rows = [r + "." * PAD for r in farmer.ROWS]
    for j, over in enumerate(RAISED_ARM, ARM_TOP):
        rows[j] = "".join(o if o != " " else c for c, o in zip(rows[j], over))
    cmap = {c: key for c, (key, _) in farmer.PALETTE.items()}

    k = 2
    FX, FY = 70, 98                        # farmer cell (0, 0), top-left pixel
    HIT = (FX + 28, FY + 2 * ARM_TOP)      # top of the raised hand (middle)
    RING = (FX + 26, FY + 19)              # ring band's left pixel (4x1 px across the hand)
    FEET = FY + k * len(rows) - 1          # bottom outline row of the shoes
    TOP = (152, 18)                        # where the bolt leaves the cloud

    def flash(x, y, cx, cy, r, amp, sy=1.0):
        return amp * max(0.0, 1.0 - math.hypot(x - cx, (y - cy) * sy) / r)

    # ------------------------------------------------------------ sky
    for y in range(H):
        for x in range(W):
            v = 0.2 + 1.3 * y / 130.0
            v += flash(x, y, HIT[0], HIT[1] - 20, 120, 2.9, 1.15)
            v = max(v, flash(x, y, TOP[0], TOP[1], 70, 3.2))
            px(x, y, ("sk0", "sk1", "sk2", "sk3", "sk4")[lit(x, y, v, 0, 4)])

    # ------------------------------------------------------------ storm clouds
    cloud = set()
    puffs = []
    x = -10
    while x < W + 10:
        r = rng.randrange(11, 20)
        puffs.append((x, rng.randrange(-6, 8), r))
        if rng.random() < 0.7:
            puffs.append((x + rng.randrange(-6, 7), rng.randrange(14, 24), r - rng.randrange(4, 8)))
        x += rng.randrange(13, 20)
    puffs += [(TOP[0] - 14, TOP[1] - 2, 14), (TOP[0] + 10, TOP[1] - 4, 15), (TOP[0] - 2, TOP[1] + 4, 10)]
    for (cx, cy, r) in puffs:
        cloud |= {(i, j) for (i, j) in cv.disc(cx, cy, r) if 0 <= i < W and 0 <= j < H}
    for (x, y) in cloud:
        under = (x, y + 2) not in cloud
        rim = (x, y + 1) not in cloud
        v = 0.6 + (1.0 if under else 0.0) + (0.6 if rim else 0.0) - 0.4 * (y >= 3 and (x, y - 3) not in cloud)
        v += flash(x, y, TOP[0], TOP[1], 95, 2.2, 1.4)
        px(x, y, ("cl0", "cl1", "cl2", "cl3")[lit(x, y, v, 0, 3)])
    for (cx, cy, r) in puffs:              # lumps: the upper arc of each lower puff, one shade lighter
        if cy < 12:
            continue
        own = set(cv.disc(cx, cy, r))
        for (x, y) in own:
            if (x, y - 1) not in own and y < cy - r // 3 and (x, y) in cloud and 0 <= x < W:
                v = 1.4 + flash(x, y, TOP[0], TOP[1], 95, 2.2, 1.4)
                px(x, y, ("cl0", "cl1", "cl2", "cl3")[min(3, int(v))])

    # ------------------------------------------------------------ far hill, farmhouse, tree
    def hill_y(x):
        return int(120 + 5 * math.sin(x / 37.0 + 1.0) + 2 * math.sin(x / 11.0))

    def ground_y(x):
        return int(136 + 4 * math.sin(x / 61.0 + 2.2))

    for x in range(W):
        for y in range(hill_y(x), H):
            v = flash(x, y, HIT[0], FEET, 110, 1.6, 1.6)
            px(x, y, ("hl0", "hl1")[lit(x, y, v, 0, 1)])
        px(x, hill_y(x), "ink")

    hx, base = 136, hill_y(150) + 2       # farmhouse silhouette with one lit window
    rect(hx, base - 14, 30, 14, "ink")
    for j in range(11):
        rect(hx - 3 + j, base - 15 - j, 36 - 2 * j, 1, "ink")
    rect(hx + 24, base - 26, 4, 8, "ink")
    rect(hx + 6, base - 10, 5, 5, "win")
    px(hx + 8, base - 10, "ink")
    px(hx + 8, base - 9, "ink")
    rect(hx + 6, base - 8, 5, 1, "ink")
    rect(hx + 8, base - 8, 1, 1, "ink")
    rect(hx + 8, base - 7, 1, 2, "ink")
    rect(hx + 18, base - 9, 5, 9, "hl0")

    def branch(x, y, ang, length, width):
        for s in range(length):
            xx = x + round(s * math.cos(ang))
            yy = y - round(s * math.sin(ang))
            rect(xx, yy, width, 1, "ink")
        return x + round(length * math.cos(ang)), y - round(length * math.sin(ang))

    tx = 22                                # bare tree bent by the wind
    ty = hill_y(tx) + 2
    top = branch(tx, ty, math.radians(97), 38, 4)
    branch(top[0], top[1], math.radians(150), 14, 2)
    branch(top[0], top[1], math.radians(70), 16, 2)
    branch(top[0] + 1, top[1] + 1, math.radians(20), 12, 1)
    branch(tx, ty - 20, math.radians(25), 16, 2)
    branch(tx, ty - 26, math.radians(165), 13, 1)
    branch(tx + 10, ty - 27, math.radians(75), 7, 1)

    # ------------------------------------------------------------ foreground meadow
    for x in range(W):
        g = ground_y(x)
        for y in range(g, H):
            v = 0.6 + 0.5 * (y - g) / 40.0
            v += flash(x, y, HIT[0] - 6, FEET, 120, 2.6, 2.2)
            px(x, y, ("gr0", "gr1", "gr2", "gr3")[lit(x, y, v, 0, 3)])
        px(x, g, "ink")
        if x % 3 == 0:
            px(x, g - 1, "ink")
    for _ in range(90):                    # grass tufts
        x = rng.randrange(2, W - 3)
        y = rng.randrange(ground_y(x) + 3, H - 1)
        v = 1.1 + flash(x, y, HIT[0] - 6, FEET, 120, 2.6, 2.2)
        c = ("gr1", "gr2", "gr3", "gr3")[min(3, int(v))]
        px(x, y, c)
        px(x + 2, y, c)
        px(x + 1, y - 1, c)
    for (cx, cy, rx) in ((190, 162, 20), (272, 171, 14), (24, 168, 10)):   # puddles
        for y in range(cy - 2, cy + 3):
            for x in range(cx - rx, cx + rx + 1):
                if ((x - cx) / rx) ** 2 + ((y - cy) / 2.6) ** 2 <= 1:
                    px(x, y, "sk1" if y < cy + 1 else "sk0")
        rect(cx - rx // 2, cy - 1, rx // 2, 1, "sk3")
        rect(cx + 3, cy + 1, rx // 3, 1, "sk2")

    # ------------------------------------------------------------ rain
    def rain_streak(x, y, n):
        for s in range(n):
            px(x - s // 2, y + s, "rain")

    for _ in range(260):
        x = rng.randrange(0, W + 20)
        y = rng.randrange(-4, H - 4)
        rain_streak(x, y, 3 if y > 140 else 5)
    for _ in range(30):                    # splashes
        x = rng.randrange(4, W - 4)
        y = rng.randrange(ground_y(x) + 4, H - 2)
        px(x - 1, y, "rain")
        px(x + 1, y, "rain")
        px(x, y - 1, "rain")

    # ------------------------------------------------------------ flash halo around the ring
    cx, cy = RING[0] + 2, RING[1] + 1
    for y in range(cy - 21, cy + 22):
        for x in range(cx - 21, cx + 22):
            d = math.hypot(x - cx, y - cy)
            if d <= 7:
                px(x, y, "white" if d <= 4 else "yl1")
            elif d <= 11:
                px(x, y, "yl1" if (x + y) % 2 else "el1")
            elif d <= 15:
                px(x, y, "el1" if (x + y) % 2 == 0 else "el0")
            elif d <= 20 and (x + y) % 2 == 0 and x % 2 == 0:
                px(x, y, "el1")

    # ------------------------------------------------------------ the bolt
    def line(p0, p1):
        n = max(abs(p1[0] - p0[0]), abs(p1[1] - p0[1]), 1)
        return [(round(p0[0] + (p1[0] - p0[0]) * t / n), round(p0[1] + (p1[1] - p0[1]) * t / n))
                for t in range(n + 1)]

    def bolt(pts, thick):
        core = set()
        for a, b in zip(pts, pts[1:]):
            for (x, y) in line(a, b):
                core |= {(x + i, y) for i in range(thick)}
        inner = cv.outline_of(core)
        outer = cv.outline_of(core | inner)
        for (x, y) in outer:
            if (x + y) % 2 == 0:
                px(x, y, "el0")
        for (x, y) in inner:
            px(x, y, "el1" if thick == 1 else "yl1")
        for (x, y) in core:
            px(x, y, "white")

    main = [TOP, (137, 35), (146, 40), (125, 60), (134, 64), (110, 87), (118, 91), (100, 101), (107, 104),
            (HIT[0] - 1, HIT[1])]
    bolt([(137, 35), (122, 45), (112, 43), (98, 58), (90, 61)], 1)
    bolt([(125, 60), (139, 71), (135, 76), (146, 86)], 1)
    bolt(main, 2)

    # ------------------------------------------------------------ the farmer
    for y in range(FEET - 3, FEET + 3):    # shadow on the wet grass
        for x in range(FX - 4, FX + 34):
            if ((x - FX - 14) / 18) ** 2 + ((y - FEET) / 2.6) ** 2 <= 1:
                px(x, y, "gr0")
    cv.sprite(rows, FX, FY, cmap, k=k, outline=False)
    rect(RING[0], RING[1], 4, 1, "gold")   # ring band on the raised hand
    # crackles around the body
    for (x0, y0, dirx) in ((FX - 6, FY + 40, -1), (FX + 26, FY + 44, 1), (FX - 6, FY + 20, -1)):
        for s, dy in enumerate((0, -1, 0, 1, 1, 0)):
            px(x0 + dirx * s, y0 + dy + s // 2, "el1" if s % 3 else "white")
    # ring glint: a four-point star off the hand's upper right
    px(RING[0], RING[1], "white")
    gx, gy = RING[0] + 7, RING[1] - 2       # clear of the hand's outline
    for d in range(1, 6):
        c = "white" if d < 4 else "yl1"
        px(gx + d, gy, c)
        px(gx, gy - d, c)
        if d < 2:
            px(gx - d, gy, c)
            px(gx, gy + d, c)
    px(gx, gy, "white")
    for (dx, dy) in ((1, 1), (-1, -1), (1, -1), (-1, 1)):
        px(gx + dx, gy + dy, "yl1")

    # ------------------------------------------------------------ title
    def storm_text(s, right, top, tk):
        pts, w = cv.ttf_pts(s, tk)
        x0 = right - w
        if x0 - 3 < 0 or right + 3 > W:
            sys.exit(f"gen_cover.py: title line {s!r} is too wide for the cover; shorten it")
        body = cv.solidify({(x + x0, y + top) for (x, y) in pts})
        ring = cv.outline_of(body)
        glow = cv.outline_of(body | ring)
        halo = cv.outline_of(body | ring | glow)
        for (x, y) in halo:
            if (x + y) % 2 == 0:
                px(x, y, "el0")
        for (x, y) in glow:
            px(x, y, "el1" if (x, y + 1) in ring or (x, y + 2) in ring else "el0")
        for (x, y) in ring:
            px(x, y, "ink")
        mid = top + 5 * tk
        for (x, y) in body:
            if (x, y - 1) not in body:
                c = "white"
            elif y < mid:
                c = "yl1"
            elif y < mid + 2:
                c = "yl1" if (x + y) % 2 else "yl0"
            else:
                c = "yl0"
            px(x, y, c)

    title = args.title or DEFAULT_TITLE
    TK = 3
    if len(title) > 2:
        sys.exit("gen_cover.py: --layout storm fits at most two title lines")
    rights = (W - 26, W - 8) if len(title) == 2 else (W - 8,)
    top = (9 if args.tagline else 16) if len(title) == 2 else 24   # sit lower when there is no tagline
    for line_s, right in zip(title, rights):
        storm_text(line_s, right, top, TK)
        top += 13 * TK + 1
    if args.tagline:
        pts, w = cv.ttf_pts(args.tagline, 1)
        right = W - 9
        if right - w < 120:
            sys.exit("gen_cover.py: tagline is too wide; shorten it")
        body = cv.solidify({(x + right - w, y + top + 5) for (x, y) in pts})
        ring = cv.outline_of(body)
        for (x, y) in body | ring:
            px(x + 1, y + 1, "ink")
        for (x, y) in ring:
            px(x, y, "ink")
        for (x, y) in body:
            px(x, y, "white")

    return " ".join(title)
