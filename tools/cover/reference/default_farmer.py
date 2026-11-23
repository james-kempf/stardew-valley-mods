"""Native sprite of the default farmer (default-farmer.png), for cover layouts.

Generated from default-farmer.png: median of each 20x20 cell, snapped to PALETTE;
background and ground-shadow cells are "." (transparent). 14x29 cells, facing
the viewer, outlines included. See README.md in this folder.
"""

# char -> (palette key, hex); layouts add these keys to their PAL
PALETTE = {
    "A": ("hair", "#c15a32"),    # hair, light
    "B": ("hair1", "#a44d2a"),   # hair, light-mid
    "C": ("hair2", "#7d3b1f"),   # hair, mid
    "D": ("hair3", "#542616"),   # hair, dark; belt
    "E": ("hairo", "#2b130b"),   # hair outline
    "b": ("brow", "#0f0b08"),    # brows, eye outline
    "K": ("skin", "#faad8a"),    # skin, light
    "k": ("skind", "#e06a65"),   # skin, shade
    "f": ("skinf", "#a4484c"),   # skin, hair shadow on forehead
    "m": ("skino", "#6b003b"),   # skin outline
    "W": ("eyew", "#fffdfc"),    # eye white
    "w": ("eyews", "#b4aaad"),   # eye white, shaded
    "e": ("iris", "#692b10"),    # iris
    "n": ("neck", "#512634"),    # neck shadow
    "R": ("sh", "#8e1f0b"),      # shirt
    "r": ("shd", "#701718"),     # shirt, shade
    "Q": ("sho", "#4b0c06"),     # shirt outline
    "O": ("ov", "#2f55b8"),      # overalls, light
    "o": ("ovm", "#244398"),     # overalls, mid
    "q": ("ovd", "#162a66"),     # overalls, shade
    "Z": ("ovo", "#0a1234"),     # overalls outline
    "T": ("strap", "#221cae"),   # straps, bright
    "t": ("strapd", "#0f0c48"),  # straps, dark
    "U": ("btn", "#b4744a"),     # buttons, buckle
    "S": ("shoeh", "#ad471a"),   # shoes, highlight
    "s": ("shoem", "#77291b"),   # shoes, mid; belt beside the buckle
    "z": ("shoed", "#5c2024"),   # shoes, dark
    "X": ("shoeo", "#3d1223"),   # shoes, outline
}

ROWS = [
    "...EEEEEE.....",
    "....EDDCCDD...",
    ".EEEDCCDCCCD..",
    "..EDEDCCBCBCD.",
    ".EDCDEDCBBCBE.",
    "EABCEfEEEABBD.",
    "EEEEfkkkfEAAAD",
    "E.EbbbKKbbECEE",
    "..mbweKKewDE..",
    "..mkWeKKeWkm..",
    "...mKKKKKKm...",
    "....mmkkmm....",
    "...QtnKKntQ...",
    "..QRtRRRRtRQ..",
    "..QRTrRRrTRQ..",
    ".QrrUttttUrrQ.",
    ".QRQTTOOTTQRQ.",
    ".QkQtOOOOtQkQ.",
    ".mKQtTOOTtQKm.",
    "mKKkDsUUsDkKKm",
    "mKKmoooooomKKm",
    ".mmZoOooOoZmm.",
    "...ZOoZqoOZ...",
    "...ZOoZZoOZ...",
    "...qOoqqoOq...",
    "...XszXXzsX...",
    "..XsssXXsssX..",
    "..XSSzXXzSSX..",
    "...XXX..XXX...",
]
