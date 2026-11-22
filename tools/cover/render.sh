#!/usr/bin/env bash
# Render <out-dir>/cover.svg and <out-dir>/cover.png (1280x720, nearest-neighbour).
# Usage: tools/cover/render.sh <out-dir> [gen_cover.py options...]
#   e.g. tools/cover/render.sh MyMod/nexus --title "My" --title "Mod" --ribbon "Does one thing" --scene none
set -euo pipefail

if [[ $# -lt 1 || "$1" == -h || "$1" == --help ]]; then
  sed -n '2,4p' "$0" | sed 's/^# \{0,1\}//'
  exit $([[ $# -lt 1 ]] && echo 2 || echo 0)
fi

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
out="$1"; shift
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

python3 -I "$here/gen_cover.py" --out-dir "$out" "$@"
# 1:1 raster of the 320x180 grid, then integer 4x upscale with no smoothing
inkscape "$out/cover.svg" -w 320 -h 180 -o "$tmp/cover_320.png" 2>/dev/null
magick "$tmp/cover_320.png" -filter point -resize 1280x720 -strip "$out/cover.png"
echo "wrote $out/cover.svg and $out/cover.png"
