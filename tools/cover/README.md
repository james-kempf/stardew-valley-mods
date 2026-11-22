# Cover generator

Makes a mod's Nexus cover, `<Mod>/nexus/cover.svg` and `cover.png` (1280x720). The art is Stardew Valley-style pixel art drawn on a 320x180 grid. Two layouts:

- `coop` (default, `gen_cover.py`): a wooden title sign with a ribbon under it, sky, hills and a fence, plus an optional scene on the right. Used by AutoSplitScreenCoop.
- `action` (`layout_action.py`): a torch-lit mine cavern with its own 28-colour palette. Large title top-left with an outline and extruded shadow, plus an optional tagline under it. A slime lunges in from the lower left, and its whiff starburst lands only on the farmer's fading afterimages while the farmer rolls out along a motion arc in a burst of dust. Used by DodgeRoll.

## Requirements

- `python3` with Pillow (`PIL`), `inkscape` and ImageMagick 7 (`magick`).
- The "Stardew Valley" font by RRDome: https://fontstruct.com/fontstructions/show/1254619. The generator looks for it in this order:
  1. `$COVER_FONT_PATH`
  2. `tools/cover/font/stardew-valley.otf` (gitignored)
  3. `~/.local/share/fonts/stardew-valley.otf`

  `COVER_FONT=bitmap` uses a built-in 5x7 font instead. It only has the glyphs used by the default cover; the `action` layout refuses it.

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
| `--layout` | `coop` | `coop` or `action`. |
| `--title LINE` | coop: `Auto` / `Split-Screen` / `Co-op`; action: `Dodge Roll` | One title line. Repeat the option for each line (coop only; `action` fits one line). A line too wide for the sign or cover is an error. |
| `--title-tops Y,Y,...` | tuned for the default title, otherwise centred | coop: cap-top y (on the 320x180 grid) of each line. |
| `--title-scale N` | `2` | coop: integer scale for the title text. The action title is fixed at 3x. |
| `--ribbon TEXT` | `Player 2 joins automatically` | coop: ribbon text. Text too wide for the ribbon is an error. |
| `--scene` | `split-screen` | coop: right-hand scene, `split-screen` (a monitor with P1/P2 farmers) or `none`. |
| `--tagline TEXT` | none | action: optional line under the title. Text too wide is an error. |
| `--name TEXT` | title lines joined with spaces | SVG `<title>`. |

With no options except `--out-dir`, the output is the AutoSplitScreenCoop cover, byte-identical to `AutoSplitScreenCoop/nexus/cover.svg`. Check with `cmp`.

## Adding a scene or layout

- Coop scene: write a `scene_<name>()` function inside `layout_coop()` in `gen_cover.py`, modeled on `scene_split_screen()`. Use the right-hand area, x 170-319 and y 20-140. Add the name to `SCENES` and dispatch it next to `scene_split_screen()`.
- New layout: add a module like `layout_action.py` with its own `PAL` and a `draw(cv, args)` that returns the default SVG `<title>`; `cv` is the generator module with its helpers. Add the name to `--layout` and load it the way `layout_action()` does (by path, since `python3 -I` leaves the script directory off `sys.path`). Give it its own `random.Random`, so the coop output doesn't change.

`sprite()` blits a character map at an integer scale with an automatic ink outline and fails on out-of-bounds pixels. Use only palette keys and the `px`/`rect`/`panel`/`dither` helpers so the result stays pixel-exact. The art must be original: no game assets.
