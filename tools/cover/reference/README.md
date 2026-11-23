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

## Covers

- Storm (`layout_storm.py`, DeadlyLightning): drawn from `default_farmer.py` at 2x. The pose edit (`RAISED_ARM`) raises the arm on the viewer's right in one straight diagonal from the shoulder, away from the head; the hand wears the gold ring.
- Coop (`gen_cover.py`, AutoSplitScreenCoop): drawn from `default_farmer.py` at 2x, one farmer per monitor pane, in the reference pose. P2 is the unmodified reference. P1 changes only its shirt cells, to blue: `#3a9ad8` fill, `#2a72b0` shade, `#123a63` outline.
- Action (`layout_action.py`, DodgeRoll): the farmer curled into a rolling ball at 3x (`BALL`, 19x17 cells with its outline), a pose edit and not pixel-identical to the reference. It uses only `default_farmer.py`'s characters and colours (26 of the 28; no shaded eye white or neck shadow) and its per-part outlines: wine skin (`m`), dark-brown hair (`E`), navy overalls (`Z`), dark shirt (`Q`), shoes (`X`). It shows the reference's shaggy hair (layered locks swept around the ball in the roll direction, dark at the roots and lighter toward the ends with `A`/`B`/`C`/`D` streaks, dark `E`/`D` gaps between locks, a lumpy rounded top, ragged strands over the forehead and a side lock past the face), the face tucked under the fringe with forehead shadow, brow and one eye, the red shirt back with the bright strap and its button, the hand hugging the blue knees, and the two-tone brown shoes with their orange highlight. The two afterimages are the ball turned 180 and 90 degrees (`rot90`) in the dithered blue afterimage tint.
