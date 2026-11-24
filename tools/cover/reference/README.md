# Farmer reference

`default-farmer.png` is Stardew Valley's default new player character, supplied by the user as the farmer for mod covers.

**Farmers on covers are built from the sprite data in `default_farmer.py`, not redrawn by hand.** Pose edits (such as a raised arm) and shirt recolours (such as the coop P1 farmer's blue) go on top of that data in the layout, in the reference's own colours and outline style; every other cell stays as it is. A pose that can't be an overlay, such as the action cover's curled ball, is a new grid in the reference's characters, colours and per-part outlines; it is a pose edit and not pixel-identical to the reference. `tools/cover/README.md` shows how a layout loads and draws it.

## Sprite data

`default_farmer.py` holds the native sprite, so a build never reads the PNG:

- `ROWS`: 14x29 characters, one per native pixel, facing the viewer, outlines included. `.` is transparent.
- `PALETTE`: `{char: (palette key, hex)}`, 28 colours. Layouts add the keys to their `PAL`.

How it was made: each 20x20 cell's median colour (inner 16x16, skipping grid lines and edge blur), snapped to the nearest palette colour below. Background (`#dddddd`) becomes `.`. So does the ground-shadow grey in the bottom row, since the farmer stands on each cover's own ground. Regenerate it only if the PNG changes, with the same method.

## Image

- File: 343x585 PNG, light grey background `#dddddd`.
- Upscaled sprite: each native pixel is a 20x20 block (scale **20x**). Grid lines fall at x = 10 + 20k, y = 2 + 20k.
- The character fills **14x29 native pixels** (x 30-310, y 2-582), plus a 3 px strip of ground shadow cut off at the bottom.
- The upscale is lossy: the file has about 2,300 unique colours. The hex values below are the median of each 20x20 cell, grouped by colour.

## Palette

| Role | Hex |
|---|---|
| Hair, light | `#c15a32` |
| Hair, light-mid | `#a44d2a` |
| Hair, mid | `#7d3b1f` |
| Hair, dark | `#542616` |
| Hair, outline | `#2b130b` |
| Brows, eye outline | `#0f0b08` |
| Skin, light | `#faad8a` |
| Skin, shade (chin, cheeks, wrists) | `#e06a65` |
| Skin, hair shadow on forehead | `#a4484c` |
| Skin, outline (dark wine) | `#6b003b` |
| Eye white | `#fffdfc` |
| Eye white, shaded (upper) | `#b4aaad` |
| Iris | `#692b10` |
| Neck shadow (collar) | `#512634` |
| Shirt, red | `#8e1f0b` |
| Shirt, shade | `#701718` |
| Shirt, outline | `#4b0c06` |
| Overalls, light | `#2f55b8` |
| Overalls, mid | `#244398` |
| Overalls, shade | `#162a66` |
| Overalls, outline | `#0a1234` |
| Straps, bright | `#221cae` |
| Straps, dark | `#0f0c48` |
| Buttons, belt buckle | `#b4744a` |
| Belt | `#542616` (hair, dark), `#77291b` (shoes, mid) beside the buckle |
| Pants | overalls blues: `#2f55b8`, `#244398`, `#162a66`, `#0a1234` |
| Shoes, highlight | `#ad471a` |
| Shoes, mid | `#77291b` |
| Shoes, dark | `#5c2024` |
| Shoes, outline | `#3d1223` |
| Ground shadow (over background) | `#b4aaad` |

## Key visual traits

- Short, swept reddish-brown hair with a side part, outlined in very dark brown.
- Dark, heavy brows over small eyes (white plus brown iris).
- Peach skin with a pink-red shade on chin and cheeks.
- Red shirt, visible at the collar and down the arms, under blue overalls.
- Overalls with bright blue straps and tan buttons on the bib.
- A brown belt line with a tan buckle at the waist.
- Blue pants (same blues as the overalls).
- Brown shoes.
- Outlines are a dark wine (`#6b003b` on skin, `#3d1223` on shoes, `#4b0c06` on the shirt), not black.

## Anatomy

`farmer_anatomy.py` splits `ROWS` into 17 named body parts for pose edits. It is reference only: no layout imports it, so covers don't change.

- `PARTS`: a 14x29 map in the same cells as `ROWS`; each character is the code of the part that owns that cell (`.` where the sprite is transparent). Every opaque cell belongs to exactly one part.
- `components(rows)`: `{name: {code, rows, offset, bbox, size, colors}}`, where `rows` is the part cropped to its box (`.` elsewhere) and `offset` is the box's top-left cell in the full sprite.
- `recombine(comps, 14, 29)` pastes the parts back at their offsets.
- `JOINTS`: pivot and attachment cells for posing.

Check and images (from the repo root):

```bash
python3 -I tools/cover/reference/farmer_anatomy.py            # check only
python3 -I tools/cover/reference/farmer_anatomy.py --render   # check, then write anatomy/*.png
python3 -I tools/cover/reference/farmer_anatomy.py --table    # check, then print the part table
```

The check exits 1 if `PARTS` doesn't cover exactly the opaque cells of `ROWS`, uses an unknown code, or doesn't recombine to `ROWS` cell for cell. `--render` needs Pillow and writes:

- `anatomy/parts/<name>.png`: each part alone at 16x on `#dddddd`, with a 1-cell margin.
- `anatomy/overlay.png`: the reference at 20x, the same sprite tinted by part with the joints marked, and a legend.
- `anatomy/exploded.png`: every part at 12x, moved apart by `EXPLODE` and labelled.

"_l" and "_r" are the **viewer's** left and right (the image's sides), so `sleeve_r` and `hand_r` are the farmer's own left arm. Coordinates are `(x, y)` = (column, row) of `ROWS`, origin top-left; boxes are inclusive.

| Code | Part | Offset (x, y) | Size (w x h) | Box x0-x1, y0-y1 | Colours (`PALETTE` chars) | Covers |
|---|---|---|---|---|---|---|
| `h` | hair | (0, 0) | 14x9 | 0-13, 0-8 | `A B C D E` | hair and its outline, including the side lock beside the right eye (row 8) |
| `f` | head | (2, 5) | 10x7 | 2-11, 5-11 | `K k f m` | face skin, forehead shadow, cheeks, chin, wine outline |
| `b` | brows | (3, 7) | 7x1 | 3-9, 7 | `b` | the two brows (cells 3-5 and 8-9) |
| `e` | eyes | (3, 8) | 7x2 | 3-9, 8-9 | `W w e b` | 2x2 eyes (white, shaded white, iris) and the outline cell at (3, 8) |
| `n` | neck | (5, 12) | 4x1 | 5-8, 12 | `K n` | neck skin with the collar shadow on each side |
| `s` | shirt | (2, 12) | 10x3 | 2-11, 12-14 | `R r Q` | red shirt at the collar and shoulders |
| `o` | bib | (4, 12) | 6x7 | 4-9, 12-18 | `t T O U` | straps, buttons and bib of the overalls |
| `L` | sleeve_l | (1, 15) | 3x4 | 1-3, 15-18 | `R r Q` | left sleeve; (3, 16-18) is its inner outline |
| `l` | hand_l | (0, 17) | 4x5 | 0-3, 17-21 | `K k m` | left wrist (2, 17-18) and hand (rows 19-21) with outline |
| `R` | sleeve_r | (10, 15) | 3x4 | 10-12, 15-18 | `R r Q` | right sleeve (mirror of sleeve_l) |
| `r` | hand_r | (10, 17) | 4x5 | 10-13, 17-21 | `K k m` | right wrist and hand (mirror of hand_l) |
| `w` | belt | (4, 19) | 6x1 | 4-9, 19 | `D s U` | belt with the 2-cell buckle at x 6-7 |
| `p` | hips | (3, 20) | 8x2 | 3-10, 20-21 | `O o Z` | overalls seat above the leg split |
| `[` | leg_l | (3, 22) | 4x3 | 3-6, 22-24 | `O o q Z` | left pant leg |
| `]` | leg_r | (7, 22) | 4x3 | 7-10, 22-24 | `O o q Z` | right pant leg |
| `(` | shoe_l | (2, 25) | 5x4 | 2-6, 25-28 | `S s z X` | left shoe |
| `)` | shoe_r | (7, 25) | 5x4 | 7-11, 25-28 | `S s z X` | right shoe |

Joints (`JOINTS`, cell coordinates; `.5` is the line between two cells):

| Joint | Cell | Use |
|---|---|---|
| neck | (6.5, 12) | the head (hair, head, brows, eyes) sits on the neck; the face's centre line is between columns 6 and 7 |
| shoulder_l / shoulder_r | (3, 15) / (10, 15) | top inner cell of each sleeve, under the shirt shoulder: pivot for raising an arm |
| wrist_l / wrist_r | (2, 17) / (11, 17) | first skin cell under each sleeve |
| hand_l / hand_r | (1, 19) / (12, 19) | inner middle of each hand (rows 19-21) |
| waist | (6.5, 19) | belt row, buckle at x 6-7 |
| hip_l / hip_r | (5, 21) / (8, 21) | seat row above each leg: pivot for stepping a leg |
| ankle_l / ankle_r | (4, 25) / (9, 25) | top shoe row under each leg |

Notes for pose edits:

- The arms are short: a 4-row sleeve, then a 5-row hand that starts at the wrist (row 17) and ends at row 21, beside the hips. There is no separate elbow cell; the existing poses bend at the sleeve cuff.
- The shoulders (rows 13-14, columns 2-3 and 10-11) belong to `shirt`, not to the sleeves, so moving a sleeve leaves the shoulder in place. Fill the gap with the shirt's `Q` outline, as `RAISED_ARM` and `RUN_ARMS` do.
- The shirt's red shows only at the collar, shoulders and sleeves; the bib covers the chest. Recolouring the shirt's characters (`R`, `r`, `Q`), as coop P1 does, changes exactly `shirt`, `sleeve_l` and `sleeve_r`.
- The legs share the hip row; they split at row 22 between columns 6 and 7. `layout_speed.py`'s `LEFT_LEG`/`RIGHT_LEG` columns ([2, 7) and [7, 12)) match `leg_l`+`shoe_l` and `leg_r`+`shoe_r`.

## Covers

- Storm (`layout_storm.py`, DeadlyLightning): drawn from `default_farmer.py` at 2x. The pose edit (`RAISED_ARM`) raises the arm on the viewer's right in one straight diagonal from the shoulder, away from the head; the hand wears the gold ring.
- Coop (`gen_cover.py`, AutoSplitScreenCoop): drawn from `default_farmer.py` at 2x, one farmer per monitor pane, in the reference pose. P2 is the unmodified reference. P1 changes only its shirt cells, to blue: `#3a9ad8` fill, `#2a72b0` shade, `#123a63` outline.
- Speed (`layout_speed.py`, PerformanceBundle): drawn from `default_farmer.py` at 3x in a running pose. `RUN_ARMS` overlays the arms in the reference's characters (forward arm: sleeve straight out to the elbow, cuff and 2x2 hand standing up from it; back arm: a 1-cell diagonal out and down), and `STRIDE`/`LIFT` move the reference's own leg cells (planted leg stepped out, the other knee up). Every other cell is the reference's.
- Action (`layout_action.py`, DodgeRoll): the farmer curled into a rolling ball at 3x (`BALL`, 19x17 cells with its outline), a pose edit and not pixel-identical to the reference. It uses only `default_farmer.py`'s characters and colours (26 of the 28; no shaded eye white or neck shadow) and its per-part outlines: wine skin (`m`), dark-brown hair (`E`), navy overalls (`Z`), dark shirt (`Q`), shoes (`X`). It shows the reference's shaggy hair (layered locks swept around the ball in the roll direction, dark at the roots and lighter toward the ends with `A`/`B`/`C`/`D` streaks, dark `E`/`D` gaps between locks, a lumpy rounded top, ragged strands over the forehead and a side lock past the face), the face tucked under the fringe with forehead shadow, brow and one eye, the red shirt back with the bright strap and its button, the hand hugging the blue knees, and the two-tone brown shoes with their orange highlight. The two afterimages are the ball turned 180 and 90 degrees (`rot90`) in the dithered blue afterimage tint.
