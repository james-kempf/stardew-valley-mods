"""Action layout for gen_cover.py (--layout action), used by the DodgeRoll cover.

A torch-lit mine cavern. Big title top-left with a chunky outline and an
extruded shadow, and an optional tagline under it (--tagline). A green slime
lunges from the lower left and hits only the farmer's fading afterimages
(i-frames); the farmer, curled into a ball in the reference farmer's palette and
outlines, rolls out along a curved motion arc to the lower right in a burst of dust.

gen_cover.py calls draw(cv, args) with itself as `cv` (pixel helpers, font,
sprite()) and emits the grid with PAL from this module. Uses its own RNG, so
the coop layout's random stream is untouched. Original art only.
"""
import math
import random
import sys

PAL = {
    "ink": "#160f1c",     # outlines
    "cv0": "#1f1828",     # cave wall, darkest
    "cv1": "#2e2438",
    "cv2": "#45364c",
    "cv3": "#634c5e",     # cave wall, torch-lit
    "fl0": "#2a1f22",     # floor, darkest
    "fl1": "#45322c",
    "fl2": "#654a3a",
    "fl3": "#8a6a4e",     # floor, lit
    "gl1": "#c8682e",     # torch orange / title shade
    "gl2": "#f2b445",     # torch gold / title fill
    "glc": "#fff2c4",     # white-hot / highlights
    "sl0": "#2f6a2a",     # slime
    "sl1": "#5cb83a",
    "sl2": "#b6ef6a",
    "gh0": "#34508f",     # afterimage
    "gh1": "#6c9ee0",
    "gh2": "#c2e6ff",
    "wm": "#8b5a2b",
    "wd": "#4e2f14",
    "dust": "#d2bc94",
    "white": "#ffffff",
    # draw() adds the farmer's 28 colours from reference/default_farmer.py
}

DEFAULT_TITLE = ["Dodge Roll"]
HORIZON = 112          # first floor row

# The farmer curled into a ball rolling right. A pose edit of the reference farmer
# (reference/default_farmer.py): its characters and colours, and its per-part outlines (E hair,
# m skin, Q shirt, Z overalls, X shoes), so the ball carries its own outline and is drawn with
# outline=False. Brick-red shirt back with the bright strap (T, shade t) running down to its button
# (U) on the overalls seat (left); swept hair on top with its light-to-dark ramp (A B C D); the face
# tucked under the fringe (right) with the forehead shadow, brow and one eye; the hand hugging the
# blue knees; two-tone brown shoes with their orange highlight under the knees (bottom right).
# The hair is the reference's shaggy mop: layered locks swept around the ball in the roll direction,
# darker at the roots on top (D, C) and lighter toward their ends (B, A), with dark E/D gaps between
# locks; a lumpy rounded top, ragged strands hanging over the forehead, and a side lock past the face.
BALL = [
    ".....EEEEE.EEE.....",
    "....QEECEDEEDCEE...",
    "...QEDDCCEDDDDCCE..",
    "..QREDCCBBDDDCCBBE.",
    ".QRRtEDCCBBDDDCCBBE",
    ".QRtTECCBBAADCCBBAE",
    "QRrtTRDCCBfAfDCCBBE",
    "QRrtTRRDfbbbKKKEADE",
    "QRrtTRRRmKKWeKKKkm.",
    "QRrtTRRRRmKKKKkkmZ.",
    "QRrtTrRRRRmmkkmOOOZ",
    ".QrtTQrRRRRQmKKmOOZ",
    ".ZotUZQrrrrQmKKmOqZ",
    "..ZqOOOZQQQQmmmOqZ.",
    "..ZqqoOOoXsSSSXqZ..",
    "....ZqqqXzsSSssX...",
    "......ZZZXXXXXX....",
]

# Afterimage tint by brightness of the reference colour: its outlines and darks, mids and lights.
GHOST_TINT = {"brow": "gh0", "strapd": "gh0", "ovo": "gh0", "hairo": "gh0", "sho": "gh0", "skino": "gh0",
              "shoeo": "gh0", "strap": "gh0", "ovd": "gh0", "shd": "gh0", "shoed": "gh0", "hair3": "gh0",
              "neck": "gh0", "iris": "gh0",
              "sh": "gh1", "shoem": "gh1", "ovm": "gh1", "hair2": "gh1", "ov": "gh1", "shoeh": "gh1",
              "skinf": "gh1", "hair1": "gh1",
              "hair": "gh2", "btn": "gh2", "skind": "gh2", "eyews": "gh2", "skin": "gh2", "eyew": "gh2"}

# green slime stretched into a lunge toward the upper right
SLIME = [
    "..........LLLL..",
    "........LLLLLLL.",
    "......LLhhLLLLLL",
    ".....LLhhLLeLLeL",
    "....LLLhLLLeLLeL",
    "...LLLLLLLLLLLLL",
    "..LLLLLLLLLLLLLd",
    ".LLLLLLLLLLLLLd.",
    "LLLLLLLLLLLLdd..",
    "dLLLLLLLLddd....",
    ".ddddddd........",
]
SLIME_MAP = {"L": "sl1", "d": "sl0", "h": "sl2", "e": "ink"}

FLAME = [
    "...F...",
    "..FF...",
    "..FYF..",
    ".FYYF..",
    ".FYWYF.",
    "FYWWYF.",
    "FYWWYF.",
    ".FYYF..",
]
FLAME_MAP = {"F": "gl1", "Y": "gl2", "W": "glc"}

BAYER = ((0.0, 0.5), (0.75, 0.25))


def rot90(rows, turns=1):
    """Char grid turned clockwise by 90 degrees `turns` times (negative: counter-clockwise)."""
    for _ in range(turns % 4):
        rows = ["".join(r) for r in zip(*rows[::-1])]
    return list(rows)


def draw(cv, args):
    if not cv.USE_TTF:
        sys.exit("gen_cover.py: --layout action needs the TTF font (COVER_FONT=bitmap lacks its glyphs)")
    W, H = cv.W, cv.H
    px, rect = cv.px, cv.rect
    rng = random.Random(2611)

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

    TORCH = (252, 30)

    def wall_light(x, y):
        d = math.hypot(x - TORCH[0], (y - TORCH[1]) * 1.2)
        torch = max(0.0, 2.6 - d / 34.0)
        amb = max(0.0, 1.2 - math.hypot((x - 180) / 150.0, (y - 120) / 70.0))
        return max(torch, amb)

    # ------------------------------------------------------------ wall: irregular stone courses
    cls = [["gap"] * W for _ in range(HORIZON)]
    y = -6
    row = 0
    while y < HORIZON:
        h = rng.randrange(11, 16)
        x = -rng.randrange(0, 20) - (row % 2) * 9
        while x < W:
            w = rng.randrange(16, 32)
            jy = rng.randrange(-2, 3)
            for j in range(h - 1 + rng.randrange(0, 3)):
                for i in range(w - 1):
                    dx, dy = min(i, w - 2 - i), min(j, h - 2 - j)
                    if dx + dy < 3:
                        continue
                    xx, yy = x + i, y + j + jy
                    if 0 <= xx < W and 0 <= yy < HORIZON:
                        c = "stone"
                        if j <= 2 or (i <= 1 and j < h - 4):
                            c = "hi"
                        elif j >= h - 3:
                            c = "lo"
                        cls[yy][xx] = c
            x += w
        y += h
        row += 1
    RAMP = {"gap": ["ink", "cv0", "cv1", "cv1"], "lo": ["cv0", "cv0", "cv1", "cv2"],
            "stone": ["cv0", "cv1", "cv2", "cv3"], "hi": ["cv1", "cv2", "cv3", "cv3"]}
    for y in range(HORIZON):
        for x in range(W):
            px(x, y, RAMP[cls[y][x]][lit(x, y, wall_light(x, y), 0, 3)])
    # stalactites hanging from the ceiling: stepped spikes, lit from the left
    for (sx, sw, sh) in ((192, 14, 28), (213, 8, 15), (284, 16, 34), (306, 8, 18), (162, 8, 13)):
        pts = set()
        for j in range(sh):
            half = max(0, round(sw * (sh - j) / (2 * sh)))
            pts |= {(sx + i, j) for i in range(-half, half + 1)}
        for (x, y) in cv.ortho_outline(pts):
            px(x, y, "ink")
        for (x, y) in pts:
            px(x, y, "cv3" if x < sx - 1 else ("cv2" if x <= sx + 1 else "cv1"))

    # ------------------------------------------------------------ floor
    def floor_light(x, y):
        return max(0.0, 2.7 - 2.4 * math.hypot((x - 175) / 150.0, (y - 150) / 34.0))

    for y in range(HORIZON, H):
        for x in range(W):
            px(x, y, ["fl0", "fl1", "fl2", "fl3"][lit(x, y, floor_light(x, y), 0, 2)])
    # wall foot: ink seam and a dark rubble band
    for x in range(W):
        bump = int(2 * math.sin(x / 9.0) + math.sin(x / 3.7))
        for y in range(HORIZON - 2 + bump, HORIZON + 2):
            px(x, y, "ink" if y < HORIZON + bump + 1 else "fl0")
        if x % 2 == 0:
            px(x, HORIZON + 2, "fl0")
    # pebbles and cracks
    for _ in range(70):
        x = rng.randrange(2, W - 4)
        y = rng.randrange(HORIZON + 5, H - 2)
        base = lit(x, y, floor_light(x, y), 0, 2)
        n = rng.randrange(1, 4)
        rect(x, y, n, 1, ["fl1", "fl2", "fl3", "fl3"][base + 1])
        rect(x, y + 1, n, 1, ["fl0", "fl0", "fl1"][base])
    for _ in range(8):
        x = rng.randrange(6, W - 30)
        y = rng.randrange(HORIZON + 8, H - 4)
        for i in range(rng.randrange(8, 18)):
            px(x + i, y + (i // 4) % 2 - (i // 9), "fl0")

    # ------------------------------------------------------------ torch
    tx, ty = TORCH
    for j in range(12):
        rect(tx - 1, ty + 6 + j, 4, 1, "ink")
        rect(tx, ty + 6 + j, 2, 1, "wm" if j < 10 else "wd")
    rect(tx - 3, ty + 5, 8, 3, "ink")
    rect(tx - 2, ty + 6, 6, 1, "wd")
    cv.sprite(FLAME, tx - 7, ty - 14, FLAME_MAP, k=2)

    # ------------------------------------------------------------ title
    def chunky_text(s, x0, top, k, depth):
        pts, w = cv.ttf_pts(s, k)
        if x0 + w + depth + 3 > W - 4:
            sys.exit(f"gen_cover.py: title line {s!r} is too wide for the cover; shorten it")
        body = cv.solidify({(x + x0, y + top) for (x, y) in pts})
        ring = cv.outline_of(body)
        solid = body | ring
        ext = set()
        for d in range(1, depth + 1):
            ext |= {(x + d, y + d) for (x, y) in solid}
        ext -= solid
        for (x, y) in cv.outline_of(solid | ext):
            px(x, y, "ink")
        for (x, y) in ext:
            px(x, y, "wd")
        for (x, y) in ring:
            px(x, y, "ink")
        mid = top + 5 * k
        for (x, y) in body:
            if (x, y - 1) not in body:
                c = "glc"
            elif y < mid:
                c = "gl2"
            elif y < mid + 2:
                c = "gl2" if (x + y) % 2 else "gl1"
            else:
                c = "gl1"
            px(x, y, c)
        return w

    def plain_text(s, x0, top, k, fill, shade):
        pts, w = cv.ttf_pts(s, k)
        body = cv.solidify({(x + x0, y + top) for (x, y) in pts})
        ring = cv.outline_of(body)
        for (x, y) in body | ring:
            px(x + 1, y + 1, "ink")
        for (x, y) in ring:
            px(x, y, "ink")
        for (x, y) in body:
            px(x, y, shade if (x, y + 1) not in body and k > 1 else fill)
        return w

    title = args.title or DEFAULT_TITLE
    TK = 3
    top = 7
    if top + len(title) * (13 * TK + 4) + (14 if args.tagline else 0) > 70:
        sys.exit("gen_cover.py: --layout action fits one title line (plus the tagline) above the action")
    for line in title:
        chunky_text(line, 8, top, TK, 3)
        top += 13 * TK + 4
    if args.tagline:
        if 11 + cv.ttf_pts(args.tagline)[1] > W - 40:
            sys.exit("gen_cover.py: tagline is too wide; shorten it")
        plain_text(args.tagline, 11, top + 1, 1, "glc", "glc")

    # ------------------------------------------------------------ shadows on the floor
    def floor_shadow(cx, cy, rx, ry, dense):
        for y in range(cy - ry, cy + ry + 1):
            for x in range(cx - rx, cx + rx + 1):
                if ((x - cx) / rx) ** 2 + ((y - cy) / max(ry, 1)) ** 2 <= 1:
                    if dense or (x + y) % 2 == 0:
                        px(x, y, "fl0")

    farmer = cv.reference_farmer()
    PAL.update(dict(farmer.PALETTE.values()))
    cmap = {c: key for c, (key, _) in farmer.PALETTE.items()}
    k = 3
    DX = 18                                # action shift right, balancing the right edge
    FX, FY = 210 + DX, 113                 # farmer outline box x228-284, y113-163
    floor_shadow(FX + 30, FY + 52, 26, 3, True)
    floor_shadow(56 + DX, 166, 18, 2, True)    # slime is airborne: faint shadow

    # ------------------------------------------------------------ motion arc (i-frame trail)
    def bez(p0, p1, p2, t):
        a = (1 - t) ** 2
        b = 2 * (1 - t) * t
        c = t * t
        return (a * p0[0] + b * p1[0] + c * p2[0], a * p0[1] + b * p1[1] + c * p2[1])

    def arc(p0, p1, p2, width):
        """Swoosh that thickens and brightens toward p2 (the farmer)."""
        for s in range(400):
            t = s / 399
            x, y = bez(p0, p1, p2, t)
            for dy in range(1 + int(width * t)):
                px(int(x), int(y) + dy, "white" if t > 0.7 else ("gh2" if t > 0.35 else "gh1"))

    arc((92 + DX, 100), (150 + DX, 58), (222 + DX, 108), 3)

    # ------------------------------------------------------------ afterimages
    def ghost(rows, x0, y0, keep):
        """The ball in the blue afterimage tint, keeping only the pixels where keep(x, y)."""
        for j, line in enumerate(rows):
            for i, v in enumerate(line):
                if v != ".":
                    for dy in range(k):
                        for dx in range(k):
                            x, y = x0 + i * k + dx, y0 + j * k + dy
                            if keep(x, y):
                                px(x, y, GHOST_TINT[cmap[v]])

    ghost(rot90(BALL, 2), 90 + DX, 92, lambda x, y: (x + y) % 2 == 0)
    ghost(rot90(BALL, -1), 152 + DX, 96, lambda x, y: not (x % 2 == 0 and y % 2 == 0))

    # ------------------------------------------------------------ slime lunge + whiff
    # lunge streaks behind the slime, pointing back down-left
    for (x0, y0, n) in ((30, 138, 26), (46, 146, 18), (26, 124, 14)):
        for c in range(n):
            col = "sl2" if c < n // 3 else ("sl1" if c < 2 * n // 3 else "sl0")
            px(x0 + DX - c, y0 + c // 2, col)
            px(x0 + DX - c, y0 + c // 2 + 1, col)
    cv.sprite(SLIME, 28 + DX, 102, SLIME_MAP, k=3)

    def ray(cx, cy, ang, r0, r1, c):
        for r in range(r0, r1):
            x = cx + round(r * math.cos(ang))
            y = cy + round(r * math.sin(ang))
            px(x, y, c)
            px(x + 1, y, c)

    BX, BY = 88 + DX, 108
    for n in range(8):
        ang = n * math.pi / 4 + math.pi / 8
        long = n % 2 == 0
        ray(BX, BY, ang, 4, 12 if long else 9, "glc" if long else "gl2")

    # ------------------------------------------------------------ dust burst and debris
    def puff(cx, cy, parts):
        pts = set()
        for (dx, dy, r) in parts:
            pts |= set(cv.disc(cx + dx, cy + dy, r))
        for (x, y) in pts:
            below = (x, y + 1) not in pts or (x, y + 2) not in pts
            above = (x, y - 1) not in pts
            px(x, y, "fl3" if below else ("glc" if above and x < cx else "dust"))

    puff(204 + DX, 156, [(-6, 0, 5), (2, -3, 6), (8, 2, 4)])
    puff(184 + DX, 160, [(-3, 0, 3), (2, -1, 4)])
    puff(170 + DX, 163, [(0, 0, 2)])
    puff(272 + DX, 158, [(0, 0, 3), (4, 1, 2)])
    for (x, y) in ((188, 136), (178, 142), (200, 132), (270, 144)):
        x += DX
        rect(x, y, 2, 2, "fl3")
        px(x + 1, y + 1, "fl1")

    # ------------------------------------------------------------ the farmer
    cv.sprite(BALL, FX, FY, cmap, k=k, outline=False)

    return " ".join(title)
