#!/usr/bin/env python3
"""ART-02 icon generator: painted PNG masters in the art-bible style.

Draws every icon in ``docs/art/briefs/icons.md`` with Pillow only, deterministic
from ``SEED``. Each master is 256 x 256 on a transparent ground, laid out on the
64-unit design grid (4 px per unit), with an 8 px ink stroke (``ink.strong``) and
flat fills sampled from ``docs/art/palette.md`` tokens only. No gradients, no
filters, no blur, no text.

Usage (from a venv with ``tools/art/requirements.txt`` installed)::

    python generate.py --output game/assets/icons --review <shots dir>

``--review`` additionally writes a contact sheet with a 16 px downscale row for
the silhouette check (brief section 1); those files are not committed.
"""

from __future__ import annotations

import argparse
import math
import os
import random
import sys

from PIL import Image, ImageDraw

SEED = 402
SIZE = 256

# --- Palette tokens (docs/art/palette.md) ------------------------------------

INK = "#2E2620"

UI_LINK = "#1F4E5F"
UI_ACCENT = "#8A2F1B"

WOOD_BARK = "#59371D"
WOOD_BEAM = "#4A3A26"
WOOD_PLANK = "#8A6A45"
WOOD_TIMBER = "#D0A05C"
WALL_WATTLE = "#CDBF9B"
WALL_PLASTER = "#EFE6D2"
ROOF_THATCH_L = "#D3A944"
ROOF_THATCH_S = "#A8812F"
ROOF_SHINGLE = "#8D7B5F"
ROOF_CANVAS = "#E4DCC8"
ROOF_STONE = "#7A7A74"
STONE_L = "#9B9B95"
STONE_M = "#7A7A74"
STONE_D = "#55554F"
CLAY_RAW = "#8F5636"
BRICK = "#B25A38"
POTTERY = "#C46A4A"
PROP_TARP = "#4A6B74"
PROP_ROPE = "#8A7355"
PROP_NET = "#5E6E5A"
PROP_SAIL = "#EFE6D2"
LEAF_DEEP = "#2F5B2A"
LEAF_MID = "#3D7342"
FIELD_SOIL = "#6B4E2E"
FIELD_GRAIN = "#D3A944"
FIELD_PASTURE = "#7FA35A"
PAPER_HI = "#FFF8E8"
PAPER_SH = "#E2D3B0"
ROAD_DIRT = "#9A7B52"
ROAD_COBBLE = "#9B9B95"
ROAD_BRIDGE = "#7A5C3D"

GOOD = {
    "wood": "#59371D",
    "clay": "#8F5636",
    "apples": "#BE3B28",
    "bread": "#A9602F",
    "pottery": "#B25A38",
    "fish": "#3E7E96",
    "homespun": "#A8895A",
    "cider": "#C9851F",
    "stone": "#9B9B95",
    "timber": "#D0A05C",
    "grain": "#D3A944",
    "wool": "#D5CCB2",
    "flour": "#F2EAD8",
}

W = 8  # ink stroke at master size (2 units on the 64 grid)
WD = 5  # interior detail stroke (1.25 units; never below 4 px)


def rgb(token: str) -> tuple[int, int, int, int]:
    """Converts an sRGB hex token to an (R, G, B, A) tuple."""
    token = token.lstrip("#")
    return (int(token[0:2], 16), int(token[2:4], 16), int(token[4:6], 16), 255)


INK_RGBA = rgb(INK)


def canvas() -> Image.Image:
    """A transparent 256 x 256 master."""
    return Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))


# --- Ink-first primitives -----------------------------------------------------
# Small dark-timber parts (posts, handles, stems, rails) are filled with ink
# directly: they read as silhouette, and it keeps every icon to a base material
# pair plus one accent (brief section 2: at most three tones).


def _loop(d: ImageDraw.ImageDraw, pts: list[tuple[float, float]], w: int) -> None:
    d.line(pts + pts[:1], fill=INK_RGBA, width=w, joint="curve")


def shape(
    d: ImageDraw.ImageDraw,
    pts: list[tuple[float, float]],
    fill: str,
    w: int = W,
) -> None:
    """A filled polygon with a closed ink outline."""
    d.polygon(pts, fill=rgb(fill))
    _loop(d, pts, w)


def box(
    d: ImageDraw.ImageDraw,
    x0: float,
    y0: float,
    x1: float,
    y1: float,
    fill: str,
    w: int = W,
) -> None:
    """A filled rect with an ink outline."""
    d.rectangle([x0, y0, x1, y1], fill=rgb(fill), outline=INK_RGBA, width=w)


def rbox(
    d: ImageDraw.ImageDraw,
    x0: float,
    y0: float,
    x1: float,
    y1: float,
    r: float,
    fill: str,
    w: int = W,
) -> None:
    """A filled rounded rect with an ink outline."""
    d.rounded_rectangle([x0, y0, x1, y1], radius=r, fill=rgb(fill),
                        outline=INK_RGBA, width=w)


def ell(
    d: ImageDraw.ImageDraw,
    x0: float,
    y0: float,
    x1: float,
    y1: float,
    fill: str,
    w: int = W,
) -> None:
    """A filled ellipse with an ink outline."""
    d.ellipse([x0, y0, x1, y1], fill=rgb(fill), outline=INK_RGBA, width=w)


def ink_ell(
    d: ImageDraw.ImageDraw,
    x0: float,
    y0: float,
    x1: float,
    y1: float,
) -> None:
    """A solid ink ellipse (eyes, dots, knots that read as silhouette)."""
    d.ellipse([x0, y0, x1, y1], fill=INK_RGBA)


def stroke(
    d: ImageDraw.ImageDraw,
    pts: list[tuple[float, float]],
    w: int = W,
    ink: tuple[int, int, int, int] = INK_RGBA,
) -> None:
    """A polyline with round caps (endpoint dots)."""
    d.line(pts, fill=ink, width=w, joint="curve")
    for x, y in (pts[0], pts[-1]):
        d.ellipse([x - w / 2, y - w / 2, x + w / 2, y + w / 2], fill=ink)


def band(
    d: ImageDraw.ImageDraw,
    pts: list[tuple[float, float]],
    w_fill: float,
    fill: str,
    w_ink: int = W,
) -> None:
    """A filled band with ink edges: ink underlay, then the fill on top."""
    d.line(pts, fill=INK_RGBA, width=int(w_fill + 2 * w_ink), joint="curve")
    for x, y in (pts[0], pts[-1]):
        r = (w_fill + 2 * w_ink) / 2
        d.ellipse([x - r, y - r, x + r, y + r], fill=INK_RGBA)
    d.line(pts, fill=rgb(fill), width=int(w_fill), joint="curve")
    for x, y in (pts[0], pts[-1]):
        r = w_fill / 2
        d.ellipse([x - r, y - r, x + r, y + r], fill=rgb(fill))


def detail(
    d: ImageDraw.ImageDraw,
    pts: list[tuple[float, float]],
) -> None:
    """An interior detail line (fold, crack, gill): thinner ink, round caps."""
    stroke(d, pts, WD)


# --- Shared building parts ----------------------------------------------------
# One light direction everywhere: lit faces carry the light tone, shaded faces
# the shadow tone. Roofs split light (left) / shade (right).


def wall(
    d: ImageDraw.ImageDraw,
    x0: float,
    x1: float,
    ytop: float,
    ybot: float,
    fill: str,
) -> None:
    """A front wall rect with footing."""
    box(d, x0, ytop, x1, ybot, fill)
    box(d, x0 - 4, ybot - 14, x1 + 4, ybot, STONE_M, WD)


def gable(
    d: ImageDraw.ImageDraw,
    x0: float,
    x1: float,
    eave: float,
    apex: float,
    over: float = 12,
    light: str = ROOF_THATCH_L,
    shade: str = ROOF_THATCH_S,
) -> None:
    """A front gable roof: light left slope, shaded right slope, overhang."""
    ax = (x0 + x1) / 2
    shape(d, [(x0 - over, eave), (ax, eave), (ax, apex)], light)
    shape(d, [(ax, eave), (x1 + over, eave), (ax, apex)], shade)


def door(d: ImageDraw.ImageDraw, cx: float, ybot: float, w: float = 30,
         h: float = 54) -> None:
    """A plank door on the long face, off-centre (style guide section 4)."""
    box(d, cx - w / 2, ybot - h, cx + w / 2, ybot, WOOD_PLANK)
    detail(d, [(cx, ybot - h + 4), (cx, ybot - 4)])


def window_sq(d: ImageDraw.ImageDraw, cx: float, cy: float, s: float = 26) -> None:
    """A shuttered window: ink frame, plank cross."""
    box(d, cx - s / 2, cy - s / 2, cx + s / 2, cy + s / 2, WOOD_TIMBER)
    detail(d, [(cx, cy - s / 2), (cx, cy + s / 2)])
    detail(d, [(cx - s / 2, cy), (cx + s / 2, cy)])


def chimney(d: ImageDraw.ImageDraw, x: float, ytop: float, h: float = 40) -> None:
    """A chimney rising above the ridge."""
    box(d, x - 12, ytop - h, x + 12, ytop + 6, STONE_M)


def flame(d: ImageDraw.ImageDraw, cx: float, base: float, h: float = 44) -> None:
    """A teardrop flame: glaze body, ink outline."""
    shape(d, [(cx - 15, base), (cx - 8, base - h * 0.55), (cx, base - h),
              (cx + 8, base - h * 0.55), (cx + 15, base)], POTTERY)


def tree(d: ImageDraw.ImageDraw, cx: float, base: float, r: float,
         leaf: str = LEAF_MID) -> None:
    """A round orchard/forest tree: ink trunk, leaf canopy."""
    d.rectangle([cx - 6, base - 44, cx + 6, base], fill=INK_RGBA)
    ell(d, cx - r, base - 44 - 2 * r, cx + r, base - 44, leaf)


def stones_row(d: ImageDraw.ImageDraw, cx: float, base: float, rx: float,
               n: int = 7, s: float = 12) -> None:
    """A ring of capstones seen edge-on (shrine, storytellers)."""
    for i in range(n):
        a = math.pi * i / (n - 1)
        x = cx - rx * math.cos(a)
        y = base - 10 * math.sin(a)
        box(d, x - s / 2, y - s, x + s / 2, y, STONE_L, WD)


# --- Goods (13) ---------------------------------------------------------------


def g_wood(d: ImageDraw.ImageDraw) -> None:
    for i, x in enumerate((56, 68, 56)):
        y = 96 + i * 34
        rbox(d, x, y, x + 128, y + 28, 14, WOOD_BARK)
        ell(d, x + 128 - 30, y - 1, x + 128 + 3, y + 29, GOOD["timber"])
        d.ellipse([x + 128 - 21, y + 6, x + 128 - 6, y + 22],
                  outline=INK_RGBA, width=WD)
        ink_ell(d, x + 128 - 16, y + 11, x + 128 - 11, y + 17)


def g_timber(d: ImageDraw.ImageDraw) -> None:
    for i, x in enumerate((56, 76, 56)):
        y = 100 + i * 34
        box(d, x, y, x + 128, y + 26, GOOD["timber"])
        box(d, x + 108, y, x + 128, y + 26, WOOD_PLANK, WD)
    detail(d, [(60, 117), (160, 117)])
    detail(d, [(80, 151), (180, 151)])


def g_fish(d: ImageDraw.ImageDraw) -> None:
    shape(d, [(176, 100), (176, 164), (220, 132)], GOOD["fish"])
    ell(d, 44, 98, 180, 166, GOOD["fish"])
    shape(d, [(96, 98), (120, 78), (132, 98)], GOOD["fish"], WD)
    d.arc([108, 112, 140, 152], start=300, end=60, fill=INK_RGBA, width=WD)
    ink_ell(d, 72, 120, 84, 132)


def g_wool(d: ImageDraw.ImageDraw) -> None:
    ell(d, 60, 128, 120, 188, GOOD["wool"])
    ell(d, 136, 128, 196, 188, GOOD["wool"])
    ell(d, 96, 96, 160, 172, GOOD["wool"])
    box(d, 72, 150, 184, 188, GOOD["wool"])
    d.arc([84, 150, 116, 182], start=0, end=180, fill=INK_RGBA, width=WD)
    d.arc([112, 150, 144, 182], start=0, end=180, fill=INK_RGBA, width=WD)
    d.arc([140, 150, 172, 182], start=0, end=180, fill=INK_RGBA, width=WD)


def g_homespun(d: ImageDraw.ImageDraw) -> None:
    rbox(d, 96, 100, 200, 168, 8, GOOD["homespun"])
    ell(d, 44, 96, 120, 172, GOOD["homespun"])
    d.ellipse([58, 110, 106, 158], outline=INK_RGBA, width=W)
    d.arc([70, 122, 94, 146], start=40, end=320, fill=INK_RGBA, width=WD)
    ink_ell(d, 76, 128, 88, 140)
    detail(d, [(120, 122), (196, 122)])
    detail(d, [(120, 146), (196, 146)])


def _wheat_grain(d: ImageDraw.ImageDraw, sx: float, sy: float,
                 dx: float) -> None:
    shape(d, [(sx, sy), (sx + dx, sy - 16), (sx + dx * 1.7, sy - 34),
              (sx + dx * 0.7, sy - 36), (sx - dx * 0.3, sy - 18)],
          GOOD["grain"], WD)


def _wheat_ear(d: ImageDraw.ImageDraw, x: float, base: float,
               h: float = 62) -> None:
    stroke(d, [(x, base), (x, base - h)], WD)
    for i in range(4):
        y = base - 10 - i * 13
        shape(d, [(x, y), (x - 17, y - 13), (x - 7, y - 20)],
              FIELD_GRAIN, 4)
        shape(d, [(x, y - 6), (x + 17, y - 19), (x + 7, y - 26)],
              FIELD_GRAIN, 4)
    shape(d, [(x, base - h - 18), (x - 8, base - h),
              (x + 8, base - h)], FIELD_GRAIN, 4)


def g_grain(d: ImageDraw.ImageDraw) -> None:
    stroke(d, [(128, 212), (128, 60)])
    for i in range(5):
        y = 88 + i * 24
        _wheat_grain(d, 128, y, -22)
        _wheat_grain(d, 128, y + 12, 22)
    shape(d, [(128, 44), (112, 72), (128, 84), (144, 72)], GOOD["grain"], WD)


def g_flour(d: ImageDraw.ImageDraw) -> None:
    shape(d, [(96, 108), (160, 108), (182, 150), (172, 200), (84, 200),
              (74, 150)], GOOD["flour"])
    band(d, [(92, 108), (164, 108)], 14, PROP_ROPE, WD)
    shape(d, [(112, 94), (144, 94), (128, 72)], GOOD["flour"], WD)
    detail(d, [(100, 150), (156, 150)])


def g_bread(d: ImageDraw.ImageDraw) -> None:
    rbox(d, 48, 126, 208, 180, 26, GOOD["bread"])
    for x in (92, 128, 164):
        detail(d, [(x - 13, 134), (x + 9, 166)])


def g_stone(d: ImageDraw.ImageDraw) -> None:
    shape(d, [(52, 170), (52, 110), (110, 88), (150, 120), (140, 170)],
          GOOD["stone"])
    shape(d, [(150, 160), (158, 116), (204, 108), (210, 160)], GOOD["stone"])
    detail(d, [(86, 100), (104, 130), (92, 160)])


def g_apples(d: ImageDraw.ImageDraw) -> None:
    ell(d, 76, 118, 134, 190, GOOD["apples"])
    ell(d, 122, 118, 180, 190, GOOD["apples"])
    stroke(d, [(128, 120), (128, 92)], WD)
    shape(d, [(132, 92), (170, 72), (168, 96), (134, 104)], LEAF_MID, WD)


def g_cider(d: ImageDraw.ImageDraw) -> None:
    ell(d, 92, 92, 120, 116, PAPER_HI, WD)
    ell(d, 112, 84, 142, 112, PAPER_HI, WD)
    ell(d, 132, 92, 160, 116, PAPER_HI, WD)
    rbox(d, 88, 108, 164, 196, 10, GOOD["cider"])
    d.arc([150, 124, 198, 176], start=270, end=90, fill=INK_RGBA, width=W)
    detail(d, [(88, 140), (164, 140)])


def g_clay(d: ImageDraw.ImageDraw) -> None:
    shape(d, [(66, 186), (52, 138), (92, 100), (152, 96), (198, 128),
              (190, 178), (132, 198)], GOOD["clay"])
    d.arc([104, 126, 154, 168], start=195, end=345, fill=INK_RGBA,
            width=W)


def g_pottery(d: ImageDraw.ImageDraw) -> None:
    box(d, 110, 62, 146, 78, GOOD["pottery"])
    box(d, 116, 78, 140, 102, GOOD["pottery"])
    shape(d, [(116, 102), (92, 142), (106, 184), (128, 198), (150, 184),
              (164, 142), (140, 102)], GOOD["pottery"])
    d.arc([84, 96, 122, 148], start=90, end=270, fill=INK_RGBA, width=WD)
    d.arc([134, 96, 172, 148], start=270, end=90, fill=INK_RGBA, width=WD)
    detail(d, [(104, 150), (152, 150)])


# --- Buildings (28) -----------------------------------------------------------


def b_woodcutters_hut(d: ImageDraw.ImageDraw) -> None:
    wall(d, 76, 180, 120, 200, WOOD_PLANK)
    gable(d, 76, 180, 120, 62)
    door(d, 112, 200)
    window_sq(d, 152, 152)
    rbox(d, 150, 196, 214, 214, 8, WOOD_BARK, WD)
    ell(d, 206, 198, 226, 212, GOOD["timber"], WD)


def b_sawpit(d: ImageDraw.ImageDraw) -> None:
    for x in (72, 184):
        box(d, x - 7, 110, x + 7, 204, WOOD_BEAM)
    gable(d, 60, 196, 110, 58, light=ROOF_SHINGLE, shade=STONE_M)
    stroke(d, [(96, 204), (160, 140)], W)
    stroke(d, [(96, 140), (160, 204)], W)
    box(d, 88, 196, 168, 208, WOOD_BEAM, WD)


def b_fishing_hut(d: ImageDraw.ImageDraw) -> None:
    wall(d, 64, 156, 120, 200, WOOD_PLANK)
    gable(d, 64, 156, 120, 66)
    door(d, 100, 200)
    stroke(d, [(168, 60), (168, 150)], WD)
    shape(d, [(168, 80), (132, 170), (204, 170)], PROP_NET, WD)
    detail(d, [(156, 120), (180, 120)])
    detail(d, [(148, 145), (188, 145)])


def b_sheep_farm(d: ImageDraw.ImageDraw) -> None:
    box(d, 48, 176, 208, 208, FIELD_PASTURE)
    for x in (64, 104, 144, 184):
        box(d, x - 5, 146, x + 5, 182, WOOD_BEAM, WD)
    detail(d, [(52, 156), (204, 156)])
    ell(d, 84, 96, 156, 150, GOOD["wool"])
    ell(d, 148, 108, 172, 136, INK)
    for x in (100, 118, 136, 150):
        stroke(d, [(x, 148), (x, 172)], WD)


def b_weavers_cottage(d: ImageDraw.ImageDraw) -> None:
    wall(d, 68, 172, 118, 200, WALL_WATTLE)
    gable(d, 68, 172, 118, 60)
    door(d, 104, 200)
    window_sq(d, 144, 150)
    stroke(d, [(176, 96), (216, 96)], WD)
    box(d, 180, 96, 212, 150, GOOD["homespun"], WD)


def b_grain_farm(d: ImageDraw.ImageDraw) -> None:
    shape(d, [(48, 208), (208, 208), (184, 130), (72, 130)], FIELD_SOIL)
    detail(d, [(70, 184), (186, 184)])
    detail(d, [(74, 162), (182, 162)])
    for x in (92, 128, 164):
        _wheat_ear(d, x, 138, 52)


def b_windmill(d: ImageDraw.ImageDraw) -> None:
    shape(d, [(96, 204), (160, 204), (148, 120), (108, 120)], WALL_WATTLE)
    box(d, 92, 176, 164, 204, STONE_M, WD)
    door(d, 128, 204, 26, 40)
    shape(d, [(104, 120), (152, 120), (128, 96)], ROOF_SHINGLE)
    hub = (128, 96)
    for dx, dy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
        shape(d, [(hub[0], hub[1]),
                  (hub[0] + dx * 44, hub[1] + dy * 44),
                  (hub[0] + dx * 44 + dy * -12,
                   hub[1] + dy * 44 + dx * 12),
                  (hub[0] + dy * -12, hub[1] + dx * 12)], PROP_SAIL, WD)
    ink_ell(d, 120, 88, 136, 104)


def b_bakery(d: ImageDraw.ImageDraw) -> None:
    wall(d, 64, 168, 122, 200, WALL_WATTLE)
    gable(d, 64, 168, 122, 64)
    chimney(d, 148, 84)
    shape(d, [(88, 200), (88, 160), (108, 146), (128, 160), (128, 200)],
          BRICK)
    door(d, 148, 200, 26, 48)


def b_quarry(d: ImageDraw.ImageDraw) -> None:
    box(d, 52, 160, 140, 204, STONE_M)
    box(d, 120, 116, 204, 164, STONE_L)
    box(d, 84, 72, 164, 120, STONE_M)
    detail(d, [(100, 160), (100, 204)])
    detail(d, [(150, 116), (150, 164)])
    detail(d, [(120, 84), (140, 108)])


def b_clay_pit(d: ImageDraw.ImageDraw) -> None:
    box(d, 40, 184, 216, 210, FIELD_SOIL)
    box(d, 52, 118, 168, 186, CLAY_RAW)
    detail(d, [(56, 142), (164, 142)])
    detail(d, [(56, 164), (164, 164)])
    shape(d, [(150, 186), (212, 186), (196, 146), (168, 146)], CLAY_RAW)
    band(d, [(196, 96), (172, 160)], 10, WOOD_BEAM, WD)
    shape(d, [(160, 156), (184, 156), (172, 184)], STONE_D, WD)


def b_potters_kiln(d: ImageDraw.ImageDraw) -> None:
    d.pieslice([64, 96, 192, 224], start=180, end=360, fill=rgb(BRICK),
               outline=INK_RGBA, width=W)
    d.line([64, 224, 192, 224], fill=INK_RGBA, width=W, joint="curve")
    chimney(d, 128, 108, 44)
    shape(d, [(96, 224), (96, 190), (108, 182), (120, 190), (120, 224)],
          STONE_D)
    ell(d, 156, 188, 188, 220, GOOD["pottery"], WD)
    ell(d, 186, 196, 208, 220, GOOD["pottery"], WD)


def b_orchard(d: ImageDraw.ImageDraw) -> None:
    box(d, 48, 196, 208, 210, FIELD_PASTURE, WD)
    tree(d, 92, 196, 34, LEAF_MID)
    tree(d, 164, 196, 28, LEAF_DEEP)
    ink_ell(d, 84, 130, 90, 136)
    ink_ell(d, 104, 140, 110, 146)
    ink_ell(d, 158, 138, 164, 144)


def b_cider_press(d: ImageDraw.ImageDraw) -> None:
    wall(d, 52, 132, 128, 200, WOOD_PLANK)
    gable(d, 52, 132, 128, 78)
    box(d, 148, 96, 160, 204, WOOD_BEAM)
    box(d, 192, 96, 204, 204, WOOD_BEAM)
    box(d, 140, 104, 212, 120, WOOD_BEAM)
    stroke(d, [(176, 120), (176, 158)], W)
    box(d, 152, 158, 200, 204, WOOD_TIMBER)
    ell(d, 204, 176, 228, 204, GOOD["cider"], WD)


def b_founders_hall(d: ImageDraw.ImageDraw) -> None:
    wall(d, 52, 204, 128, 200, WOOD_TIMBER)
    gable(d, 52, 204, 128, 52, over=14, light=ROOF_SHINGLE, shade=STONE_M)
    door(d, 128, 200, 40, 66)
    window_sq(d, 84, 162)
    window_sq(d, 172, 162)
    detail(d, [(52, 128), (204, 128)])


def b_settler_house(d: ImageDraw.ImageDraw) -> None:
    wall(d, 72, 184, 122, 200, WALL_WATTLE)
    gable(d, 72, 184, 122, 62)
    door(d, 112, 200)
    window_sq(d, 152, 154)


def b_dirt_road(d: ImageDraw.ImageDraw) -> None:
    band(d, [(52, 196), (108, 150), (148, 118), (204, 60)], 40, ROAD_DIRT)


def b_cobbled_road(d: ImageDraw.ImageDraw) -> None:
    band(d, [(52, 196), (108, 150), (148, 118), (204, 60)], 44,
         ROAD_COBBLE)
    for x, y in ((92, 168), (122, 146), (150, 122), (176, 98)):
        ink_ell(d, x - 4, y - 4, x + 4, y + 4)


def b_storehouse(d: ImageDraw.ImageDraw) -> None:
    wall(d, 52, 204, 122, 200, WOOD_PLANK)
    gable(d, 52, 204, 122, 56, light=ROOF_SHINGLE, shade=STONE_M)
    box(d, 96, 148, 160, 200, WOOD_BEAM, WD)
    detail(d, [(128, 148), (128, 200)])


def b_stone_storehouse(d: ImageDraw.ImageDraw) -> None:
    wall(d, 52, 204, 122, 200, STONE_L)
    detail(d, [(56, 148), (200, 148)])
    detail(d, [(56, 172), (200, 172)])
    gable(d, 52, 204, 122, 56, light=STONE_L, shade=STONE_M)
    box(d, 108, 150, 148, 200, WOOD_BEAM, WD)
    detail(d, [(108, 168), (148, 168)])
    window_sq(d, 172, 160, 22)


def b_market(d: ImageDraw.ImageDraw) -> None:
    for x in (64, 128, 192):
        box(d, x - 6, 120, x + 6, 204, INK)
    shape(d, [(44, 120), (212, 120), (192, 72), (64, 72)], ROOF_CANVAS)
    for i, xs in enumerate(((84, 104), (118, 138), (152, 172))):
        shape(d, [(xs[0], 120), (xs[1], 120), (xs[1] - 6, 72),
                  (xs[0] - 6, 72)], POTTERY)
    box(d, 72, 160, 184, 178, WOOD_BEAM, WD)
    ell(d, 88, 144, 104, 160, GOOD["bread"], 4)
    ell(d, 112, 144, 128, 160, GOOD["fish"], 4)
    ell(d, 136, 144, 152, 160, GOOD["pottery"], 4)


def b_shrine(d: ImageDraw.ImageDraw) -> None:
    wall(d, 96, 160, 128, 196, WALL_PLASTER)
    shape(d, [(88, 128), (168, 128), (128, 88)], ROOF_STONE)
    stones_row(d, 128, 208, 76)
    flame(d, 128, 190, 52)


def b_scouts_lodge(d: ImageDraw.ImageDraw) -> None:
    wall(d, 56, 140, 130, 200, WOOD_PLANK)
    gable(d, 56, 140, 130, 80)
    box(d, 168, 60, 180, 204, WOOD_BEAM)
    box(d, 148, 96, 200, 108, WOOD_BEAM, WD)
    shape(d, [(180, 60), (224, 74), (180, 88)], PROP_TARP, WD)


def b_timber_bridge(d: ImageDraw.ImageDraw) -> None:
    for x in (72, 184):
        box(d, x - 7, 130, x + 7, 204, INK)
    band(d, [(48, 140), (128, 124), (208, 140)], 26, ROAD_BRIDGE)
    for x in (88, 128, 168):
        box(d, x - 5, 96, x + 5, 132, WOOD_BEAM, WD)
    detail(d, [(72, 108), (184, 108)])
    d.arc([88, 140, 168, 200], start=180, end=360, fill=INK_RGBA, width=WD)


def b_scribes_hall(d: ImageDraw.ImageDraw) -> None:
    wall(d, 60, 196, 126, 200, WALL_PLASTER)
    gable(d, 60, 196, 126, 58, light=ROOF_SHINGLE, shade=STONE_M)
    door(d, 128, 200, 34, 58)
    shape(d, [(96, 84), (126, 92), (126, 110), (96, 102)], PAPER_HI, WD)
    shape(d, [(156, 84), (126, 92), (126, 110), (156, 102)], PAPER_HI, WD)
    stroke(d, [(126, 88), (126, 110)], WD)


def b_storytellers_circle(d: ImageDraw.ImageDraw) -> None:
    stones_row(d, 128, 196, 72)
    stroke(d, [(104, 192), (152, 192)], W)
    stroke(d, [(108, 182), (148, 182)], W)
    flame(d, 128, 182, 66)


def b_festival_ground(d: ImageDraw.ImageDraw) -> None:
    for x in (56, 200):
        box(d, x - 6, 72, x + 6, 204, WOOD_BEAM)
    d.arc([40, 60, 216, 200], start=20, end=160, fill=INK_RGBA, width=WD)
    for i, fx in enumerate((84, 112, 140, 168)):
        t = i / 3
        fy = 128 + int(28 * math.sin(math.pi * t)) + 8
        shape(d, [(fx - 11, fy), (fx + 11, fy), (fx, fy + 22)],
              POTTERY if i % 2 else ROOF_CANVAS, 4)
    ell(d, 112, 176, 144, 204, WOOD_PLANK, WD)


def b_plaza(d: ImageDraw.ImageDraw) -> None:
    ell(d, 56, 158, 200, 210, STONE_M)
    ell(d, 76, 168, 180, 202, PAPER_SH, WD)
    rbox(d, 112, 72, 144, 178, 12, STONE_L)
    shape(d, [(112, 72), (144, 72), (128, 52)], STONE_L, WD)
    detail(d, [(120, 100), (136, 100)])
    detail(d, [(120, 122), (136, 122)])


def b_standing_stones(d: ImageDraw.ImageDraw) -> None:
    rbox(d, 60, 108, 96, 204, 14, STONE_M)
    rbox(d, 110, 72, 148, 204, 16, STONE_L)
    rbox(d, 160, 116, 196, 204, 14, STONE_M)
    detail(d, [(122, 100), (136, 100), (129, 118)])


# --- Tiers (2) and UI (8) ----------------------------------------------------


def t_settlers(d: ImageDraw.ImageDraw) -> None:
    shape(d, [(44, 170), (128, 78), (212, 170), (212, 136), (128, 48),
              (44, 136)], ROOF_THATCH_L)


def t_villagers(d: ImageDraw.ImageDraw) -> None:
    wall(d, 72, 184, 124, 200, WALL_PLASTER)
    gable(d, 72, 184, 124, 62)
    chimney(d, 152, 82, 34)
    door(d, 108, 200, 28, 50)
    box(d, 136, 162, 178, 178, WOOD_BEAM, WD)
    for cx, fill in ((144, UI_ACCENT), (157, PAPER_HI), (170, UI_ACCENT)):
        ink_ell(d, cx - 6, 148, cx + 6, 160)


def u_production(d: ImageDraw.ImageDraw) -> None:
    band(d, [(84, 200), (150, 110)], 24, WOOD_BEAM)
    shape(d, [(132, 60), (204, 84), (192, 124), (120, 100)], STONE_D)


def u_infrastructure(d: ImageDraw.ImageDraw) -> None:
    box(d, 68, 118, 94, 200, STONE_M)
    box(d, 162, 118, 188, 200, STONE_M)
    d.arc([80, 108, 176, 196], start=180, end=360, fill=INK_RGBA, width=W)
    d.arc([80, 108, 176, 196], start=180, end=360, fill=rgb(STONE_L),
            width=WD)
    band(d, [(52, 108), (204, 108)], 22, STONE_L)
    detail(d, [(64, 190), (104, 190)])
    detail(d, [(152, 190), (192, 190)])


def u_services(d: ImageDraw.ImageDraw) -> None:
    ink_ell(d, 120, 52, 136, 68)
    shape(d, [(112, 68), (144, 68), (168, 160), (88, 160)], FIELD_GRAIN)
    stroke(d, [(128, 160), (128, 178)], W)
    ink_ell(d, 118, 178, 138, 198)
    detail(d, [(112, 120), (144, 120)])


def u_knowledge(d: ImageDraw.ImageDraw) -> None:
    shape(d, [(128, 96), (64, 80), (64, 168), (128, 184)], UI_LINK)
    shape(d, [(128, 96), (192, 80), (192, 168), (128, 184)], UI_LINK)
    shape(d, [(128, 104), (78, 92), (78, 160), (128, 172)], PAPER_HI, WD)
    shape(d, [(128, 104), (178, 92), (178, 160), (128, 172)], PAPER_HI, WD)
    stroke(d, [(128, 96), (128, 184)], W)


def u_culture(d: ImageDraw.ImageDraw) -> None:
    stroke(d, [(76, 76), (76, 164)], W)
    stroke(d, [(180, 76), (180, 164)], W)
    band(d, [(76, 164), (128, 194), (180, 164)], 22, WOOD_TIMBER)
    ink_ell(d, 119, 160, 137, 178)
    for x in (100, 120, 140, 160):
        stroke(d, [(x, 76), (x, 168)], WD)
    band(d, [(66, 76), (190, 76)], 18, WOOD_TIMBER)


def u_residence(d: ImageDraw.ImageDraw) -> None:
    box(d, 84, 140, 172, 200, WALL_WATTLE)
    shape(d, [(68, 144), (128, 84), (188, 144), (188, 118), (128, 60),
              (68, 118)], ROOF_THATCH_L)
    door(d, 128, 200, 28, 48)


def u_roads(d: ImageDraw.ImageDraw) -> None:
    band(d, [(64, 204), (120, 150), (110, 110), (180, 70)], 36, ROAD_DIRT)


def u_monuments(d: ImageDraw.ImageDraw) -> None:
    box(d, 56, 186, 200, 206, STONE_M)
    box(d, 72, 166, 184, 188, STONE_L, WD)
    rbox(d, 96, 56, 148, 170, 18, STONE_L)
    detail(d, [(112, 84), (132, 84)])
    detail(d, [(112, 106), (132, 106)])
    detail(d, [(112, 128), (124, 128)])
    rbox(d, 156, 118, 188, 170, 10, STONE_M, WD)


# --- Registry -----------------------------------------------------------------

ICONS: dict[tuple[str, str], object] = {
    ("goods", "wood"): g_wood,
    ("goods", "timber"): g_timber,
    ("goods", "fish"): g_fish,
    ("goods", "wool"): g_wool,
    ("goods", "homespun"): g_homespun,
    ("goods", "grain"): g_grain,
    ("goods", "flour"): g_flour,
    ("goods", "bread"): g_bread,
    ("goods", "stone"): g_stone,
    ("goods", "apples"): g_apples,
    ("goods", "cider"): g_cider,
    ("goods", "clay"): g_clay,
    ("goods", "pottery"): g_pottery,
    ("buildings", "woodcutters_hut"): b_woodcutters_hut,
    ("buildings", "sawpit"): b_sawpit,
    ("buildings", "fishing_hut"): b_fishing_hut,
    ("buildings", "sheep_farm"): b_sheep_farm,
    ("buildings", "weavers_cottage"): b_weavers_cottage,
    ("buildings", "grain_farm"): b_grain_farm,
    ("buildings", "windmill"): b_windmill,
    ("buildings", "bakery"): b_bakery,
    ("buildings", "quarry"): b_quarry,
    ("buildings", "clay_pit"): b_clay_pit,
    ("buildings", "potters_kiln"): b_potters_kiln,
    ("buildings", "orchard"): b_orchard,
    ("buildings", "cider_press"): b_cider_press,
    ("buildings", "founders_hall"): b_founders_hall,
    ("buildings", "settler_house"): b_settler_house,
    ("buildings", "dirt_road"): b_dirt_road,
    ("buildings", "cobbled_road"): b_cobbled_road,
    ("buildings", "storehouse"): b_storehouse,
    ("buildings", "stone_storehouse"): b_stone_storehouse,
    ("buildings", "market"): b_market,
    ("buildings", "shrine"): b_shrine,
    ("buildings", "scouts_lodge"): b_scouts_lodge,
    ("buildings", "timber_bridge"): b_timber_bridge,
    ("buildings", "scribes_hall"): b_scribes_hall,
    ("buildings", "storytellers_circle"): b_storytellers_circle,
    ("buildings", "festival_ground"): b_festival_ground,
    ("buildings", "plaza"): b_plaza,
    ("buildings", "standing_stones"): b_standing_stones,
    ("tiers", "settlers"): t_settlers,
    ("tiers", "villagers"): t_villagers,
    ("ui", "production"): u_production,
    ("ui", "infrastructure"): u_infrastructure,
    ("ui", "services"): u_services,
    ("ui", "knowledge"): u_knowledge,
    ("ui", "culture"): u_culture,
    ("ui", "residence"): u_residence,
    ("ui", "roads"): u_roads,
    ("ui", "monuments"): u_monuments,
}


def draw_icon(kind: str, name: str) -> Image.Image:
    """Draws one icon master (used by main and the review sheet)."""
    image = canvas()
    painter = ImageDraw.Draw(image)
    ICONS[(kind, name)](painter)
    return image


def main(argv: list[str]) -> int:
    """Generates every icon master into ``--output``."""
    parser = argparse.ArgumentParser(description="ART-02 icon generator")
    parser.add_argument("--output", default="game/assets/icons")
    parser.add_argument("--review", default=None)
    args = parser.parse_args(argv)

    random.seed(SEED)  # reserved: jitter, if a later pass needs it

    total = 0
    for (kind, name) in sorted(ICONS):
        image = draw_icon(kind, name)
        path = os.path.join(args.output, kind, f"{name}.png")
        os.makedirs(os.path.dirname(path), exist_ok=True)
        image.save(path)
        total += 1

    print(f"icons: wrote {total} masters to {args.output} (seed {SEED})")

    if args.review is not None:
        write_review(args.review)

    return 0


def write_review(outdir: str) -> None:
    """A contact sheet plus a 16 px downscale row per icon (not committed)."""
    os.makedirs(outdir, exist_ok=True)
    ordered = sorted(ICONS)
    cols = 8
    cell = 96
    rows = (len(ordered) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cell, rows * (cell + 18)), (244, 234, 210))
    label = ImageDraw.Draw(sheet)
    for i, (kind, name) in enumerate(ordered):
        image = draw_icon(kind, name)
        thumb = image.resize((64, 64), Image.LANCZOS)
        x = (i % cols) * cell + (cell - 64) // 2
        y = (i // cols) * (cell + 18) + 4
        sheet.paste(thumb, (x, y), thumb)
        label.text((x - 8, y + 68), f"{kind}/{name}"[:22], fill=(46, 38, 32))
    sheet.save(os.path.join(outdir, "contact.png"))

    strip = Image.new("RGB", (len(ordered) * 20, 20), (244, 234, 210))
    for i, (kind, name) in enumerate(ordered):
        tiny = draw_icon(kind, name).resize((16, 16), Image.LANCZOS)
        strip.paste(tiny, (i * 20 + 2, 2))
    strip.save(os.path.join(outdir, "silhouette-16px.png"))
    print(f"icons: review sheets in {outdir}")


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
