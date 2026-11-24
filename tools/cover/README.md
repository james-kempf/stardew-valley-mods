# Cover generator

Makes a mod's Nexus cover, `<Mod>/nexus/cover.svg` and `cover.png` (1280x720). The art is Stardew Valley-style pixel art drawn on a 320x180 grid. Four layouts:

- `coop` (default, `gen_cover.py`): a wooden title sign with a ribbon under it, sky, hills and a fence, plus an optional scene on the right: a monitor split into P1 and P2 panes, each with the reference farmer (`reference/default_farmer.py` at 2x, feet on the pane ground, under a P1/P2 badge). P2 is the unmodified reference, red shirt included; P1 differs only in its shirt, recoloured to a lighter, more cyan blue than the overalls. 49 colours (21 scene colours plus the reference farmer's 28). Used by AutoSplitScreenCoop.
- `action` (`layout_action.py`): a torch-lit mine cavern with its own 48-colour palette (22 scene colours plus 26 of the reference farmer's 28; the ball shows no shaded eye white or neck shadow). Large title top-left with an outline and extruded shadow, plus an optional tagline under it. A slime lunges in from the lower left, and its whiff starburst lands only on the farmer's fading afterimages, while the farmer, curled into a ball at 3x, rolls out along a motion arc in a burst of dust. The ball (`BALL`, 19x17 cells with its outline) is a pose edit of the reference farmer: drawn by hand in the reference's palette characters and per-part outlines, so it is not pixel-identical to the reference sprite. Its hair is the reference's shaggy mop: layered locks swept around the ball in the roll direction, dark at the roots (`D`, `C`) and lighter toward their ends (`B`, `A`), with dark `E`/`D` gaps between locks, a lumpy rounded top that hugs the ball, ragged strands hanging over the forehead and a side lock past the face. Its two afterimages are the same ball turned 180 and 90 degrees with `rot90`, in a dithered blue tint. Used by DodgeRoll.
- `storm` (`layout_storm.py`): a farm at night in a thunderstorm with its own 52-colour palette (24 scene colours plus the reference farmer's 28). Storm clouds across the top, rain streaks, a farmhouse with a lit window and a bare tree on the dark hills. A forked bolt drops onto the raised hand of the reference farmer (drawn from `reference/default_farmer.py` at 2x, with the arm on the viewer's right raised in one straight diagonal); the ring on that hand glints inside a dithered flash halo; the flash lights the clouds, sky and grass around the strike. The title is stacked on the right, right-aligned and staggered, in electric yellow with a blue glow, with an optional tagline under it. Used by DeadlyLightning.
- `speed` (`layout_speed.py`): a bright summer morning with its own 55-colour palette (27 scene colours plus the reference farmer's 28). A giant wooden hourglass stands on the meadow at the left, its last sand trickling through the neck into a mound. The reference farmer (`reference/default_farmer.py` at 3x) sprints right along a dirt path past it, trailing speed lines and dust back toward the hourglass. Two pose edits make the run: `RUN_ARMS` overlays the arms (the viewer's-right arm pumps forward, sleeve straight out to the elbow and the 2x2 hand standing up from it; the other arm swings out and down in a 1-cell diagonal), and `STRIDE`/`LIFT` move the reference's leg cells (the planted leg steps out, the other knee comes up so its shoe hangs above the path). The title is stacked top-right in lime green with an ink outline, a deep green drop shadow and speed streaks off its left edge, with an optional tagline under it. Used by PerformanceBundle.

## Requirements

- `python3` with Pillow (`PIL`), `inkscape` and ImageMagick 7 (`magick`).
- The "Stardew Valley" font by RRDome: https://fontstruct.com/fontstructions/show/1254619. The generator looks for it in this order:
  1. `$COVER_FONT_PATH`
  2. `tools/cover/font/stardew-valley.otf` (gitignored)
  3. `~/.local/share/fonts/stardew-valley.otf`

  `COVER_FONT=bitmap` uses a built-in 5x7 font instead. It only has the glyphs used by the default cover; the `action`, `storm` and `speed` layouts refuse it.

## Font license

FontStruct Non-Commercial license:

- Never commit the `.otf` (`.gitignore` covers `tools/cover/font/`, `*.otf` and `*.ttf`).
- Credit the font on every mod page that uses it: "Cover text uses the 'Stardew Valley' font by RRDome (https://fontstruct.com/fontstructions/show/1254619)."
- Don't enroll a mod with this cover in Nexus Donation Points unless the designer gives permission.

## Render a cover

From the repo root:

```bash
tools/cover/render.sh MyMod/nexus --title "My" --title "Mod" --ribbon "Does one thing well" --scene none
```

The DodgeRoll cover:

```bash
tools/cover/render.sh DodgeRoll/nexus --layout action
```

The DeadlyLightning cover:

```bash
tools/cover/render.sh DeadlyLightning/nexus --layout storm
```

The PerformanceBundle cover:

```bash
tools/cover/render.sh PerformanceBundle/nexus --layout speed
```

`render.sh` runs three steps:

```bash
python3 -I tools/cover/gen_cover.py --out-dir MyMod/nexus [options]   # writes cover.svg
inkscape MyMod/nexus/cover.svg -w 320 -h 180 -o /tmp/cover_320.png    # 1:1 raster
magick /tmp/cover_320.png -filter point -resize 1280x720 -strip MyMod/nexus/cover.png
```

## Options

| Option | Default | Meaning |
|---|---|---|
| `--out-dir DIR` | required | Writes `DIR/cover.svg`. |
| `--layout` | `coop` | `coop`, `action`, `storm` or `speed`. |
| `--title LINE` | coop: `Auto` / `Split-Screen` / `Co-op`; action: `Dodge Roll`; storm: `Deadly` / `Lightning`; speed: `Performance` / `Bundle` | One title line. Repeat the option for each line (`action` fits one line, `storm` and `speed` at most two). A line too wide for the sign or cover is an error. |
| `--title-tops Y,Y,...` | tuned for the default title, otherwise centred | coop: cap-top y (on the 320x180 grid) of each line. |
| `--title-scale N` | `2` | coop: integer scale for the title text. The action, storm and speed titles are fixed at 3x. |
| `--ribbon TEXT` | `Player 2 joins automatically` | coop: ribbon text. Text too wide for the ribbon is an error. |
| `--scene` | `split-screen` | coop: right-hand scene, `split-screen` (a monitor with P1/P2 farmers) or `none`. |
| `--tagline TEXT` | none | action, storm, speed: optional line under the title. Text too wide is an error. |
| `--name TEXT` | title lines joined with spaces | SVG `<title>`. |

With no options except `--out-dir`, the output is the AutoSplitScreenCoop cover, byte-identical to `AutoSplitScreenCoop/nexus/cover.svg`. The DodgeRoll, DeadlyLightning and PerformanceBundle commands above reproduce their committed `cover.svg` byte for byte too. Check with `cmp`.

## Adding a scene or layout

- Coop scene: write a `scene_<name>()` function inside `layout_coop()` in `gen_cover.py`, modeled on `scene_split_screen()`. Use the right-hand area, x 170-319 and y 20-140. Add the name to `SCENES` and dispatch it next to `scene_split_screen()`.
- New layout: add a module `layout_<name>.py` (like `layout_action.py`, `layout_storm.py` or `layout_speed.py`) with its own `PAL` and a `draw(cv, args)` that returns the default SVG `<title>`; `cv` is the generator module with its helpers. Add `<name>` to the `--layout` choices; `load_layout()` then loads the module by path (since `python3 -I` leaves the script directory off `sys.path`). Give it its own `random.Random`, so the other layouts' output doesn't change.

`sprite()` blits a character map at an integer scale with an automatic ink outline and fails on out-of-bounds pixels. `outline=False` draws the cells only, for sprites that carry their own outline, such as the reference farmer.

Farmers are built from the reference sprite data, not redrawn by hand (see `reference/README.md`):

- `cv.reference_farmer()` loads `reference/default_farmer.py`: `ROWS` (14x29 cells, outlines included, `.` transparent) and `PALETTE` (`{char: (palette key, hex)}`).
- In `draw()`, add the colours to the layout's palette (`PAL.update(dict(farmer.PALETTE.values()))`), build the cell map (`{char: key}`), and blit with `cv.sprite(rows, x, y, cmap, k=..., outline=False)` at an integer scale.
- Make pose edits as an overlay on `ROWS` in the layout, using the reference's own characters, and leave every other cell as it is. `layout_storm.py`'s `RAISED_ARM` is the example: it pads the rows 2 cells to the right, draws the arm as one straight diagonal from the shoulder, stepping 1 cell across per 2 up (about 63 degrees) and as long as the reference's hanging arm (6 rows: shoulder, sleeve, cuff, wrist, 2x2 hand), and clears the hanging arm.
- A pose can also move reference cells instead of overlaying new ones. `layout_speed.py`'s `STRIDE` shifts each leg's cells sideways per row and `LIFT` skips leg rows of the raised leg so its shoe hangs higher; the cells and colours stay the reference's.
- Draw props on top afterwards, such as the storm's ring band.
- To recolour a part, map its palette keys to other keys in the cell map and leave the cells alone. The coop P1 farmer maps the shirt keys `sh`, `shd` and `sho` to `p1sh`, `p1shd` and `p1sho`.
- A pose too far from the reference for an overlay, such as the action cover's curled ball, can be a new character grid in the reference's characters: its palette keys only (from `PALETTE`, no hand-typed farmer colours) and its per-part outlines (`E` hair, `m` skin, `Q` shirt, `Z` overalls, `X` shoes) in the grid itself, blitted with `outline=False`. It is a pose edit, not the reference sprite, so it is not pixel-identical to it.
- To turn a sprite, rotate its rows in exact 90-degree steps, which keeps every pixel crisp. `layout_action.py`'s `rot90(rows, turns)` turns a char grid clockwise (negative `turns`: counter-clockwise); its afterimages draw the turned ball in a blue tint (`GHOST_TINT`, by the brightness of each reference colour), dithered.

Apart from the reference farmer, use only palette keys and the `px`/`rect`/`panel`/`dither` helpers, so the result stays pixel-exact, and no game assets.
