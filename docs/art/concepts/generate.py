"""Concept-sheet generator for the Evenmere art bible (ART-01, round 2).

Deterministic (seeded) PIL/numpy paintings of the bible's own rules: flat token
fills, ink outlines, banded warm light, paper grain. Not committed art - these
are visual targets for ART-02/ART-03 and M2-29. Run:

    python3 docs/art/concepts/generate.py

Writes palette.png, icons.png, buildings.png, island.png into this folder.
See README.md for the exact requirements each image encodes.
"""

from __future__ import annotations

import math
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFont

SEED = 20261025
INK = (46, 38, 32)
INK_SOFT = (87, 73, 58)
INK_WATER = (47, 74, 85)

TOK = {
    "wood.bark.dark": "#59371D", "wood.beam.dark": "#4A3A26",
    "wood.plank.mid": "#8A6A45", "wood.timber.light": "#D0A05C",
    "wall.wattle": "#CDBF9B", "wall.plaster": "#EFE6D2", "wall.plank": "#A8895A",
    "roof.thatch.light": "#D3A944", "roof.thatch.shadow": "#A8812F",
    "roof.shingle": "#8D7B5F", "roof.canvas": "#E4DCC8", "roof.stone": "#7A7A74",
    "stone.light": "#9B9B95", "stone.mid": "#7A7A74", "stone.dark": "#55554F",
    "clay.raw": "#8F5636", "brick.fired": "#B25A38", "pottery.glaze": "#C46A4A",
    "prop.tarp": "#4A6B74", "prop.rope": "#8A7355", "prop.net": "#5E6E5A",
    "prop.sail": "#EFE6D2",
    "leaf.deep": "#2F5B2A", "leaf.mid": "#3D7342", "leaf.light": "#73A154",
    "leaf.orchard": "#5E8C3E", "leaf.fen": "#8C9E66", "leaf.marsh": "#596B4D",
    "field.soil": "#6B4E2E", "field.grain": "#D3A944",
    "field.pasture": "#7FA35A", "field.fallow": "#9A7B52",
    "terrain.plains": "#73A154", "terrain.forest": "#3D7342",
    "terrain.beach": "#D9C793", "terrain.hills": "#8C8559",
    "terrain.highlands": "#807766", "terrain.peaks": "#BFBDB8",
    "terrain.fen": "#8C9E66", "terrain.marsh": "#596B4D",
    "terrain.reef": "#409999", "terrain.sea": "#336B80",
    "terrain.deep": "#1A3B61", "terrain.river": "#5E93A3",
    "water.shallow": "#6FA8B8", "water.foam": "#F2EFE2",
    "road.dirt": "#9A7B52", "road.bridge": "#7A5C3D",
    "paper.highlight": "#FFF8E8", "paper.shadow": "#E2D3B0",
    "ui.panel": "#F4EAD2", "ui.text": "#2E2620", "ui.accent": "#8A2F1B",
    "ui.link": "#1F4E5F", "ui.good": "#3E6B34",
    "good.wood": "#59371D", "good.timber": "#D0A05C", "good.fish": "#3E7E96",
    "good.wool": "#D5CCB2", "good.homespun": "#A8895A",
    "good.grain": "#D3A944", "good.flour": "#F2EAD8", "good.bread": "#A9602F",
    "good.stone": "#9B9B95", "good.apples": "#BE3B28",
    "good.cider": "#C9851F", "good.clay": "#8F5636", "good.pottery": "#B25A38",
}


def rgb(h: str) -> tuple[int, int, int]:
    h = h.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16))


def mix(a: tuple, b: tuple, t: float) -> tuple:
    return tuple(max(0, min(255, int(x + (y - x) * t))) for x, y in zip(a, b))


def lit(c: tuple, t: float = 0.25) -> tuple:
    return mix(c, (255, 250, 235), t)


def dark(c: tuple, t: float = 0.25) -> tuple:
    return mix(c, (30, 24, 18), t)


def font(size: int):
    try:
        return ImageFont.load_default(size=size)
    except TypeError:  # very old Pillow
        return ImageFont.load_default()


class P:
    """Small painter: wobbled ink lines, flat fills, grain finish."""

    def __init__(self, w: int, h: int, bg: tuple = (244, 234, 210)):
        self.rng = random.Random(SEED)
        self.img = Image.new("RGB", (w, h), bg)
        self.d = ImageDraw.Draw(self.img, "RGBA")

    def poly(self, pts, fill, outline=INK, width=2):
        pts = [(x + self.rng.uniform(-1.5, 1.5), y + self.rng.uniform(-1.5, 1.5)) for x, y in pts]
        self.d.polygon(pts, fill=fill, outline=outline, width=width)

    def ell(self, box, fill, outline=INK, width=2):
        self.d.ellipse(box, fill=fill, outline=outline, width=width)

    def rect(self, box, fill, outline=INK, width=2):
        self.d.rectangle(box, fill=fill, outline=outline, width=width)

    def line(self, a, b, fill=INK, width=2):
        self.d.line([a, b], fill=fill, width=width)

    def finish(self, grain_amt: float = 0.02, blotch_amt: float = 0.035) -> Image.Image:
        arr = np.asarray(self.img).astype(np.float32)
        rs = np.random.RandomState(SEED)
        # Paper grain lives at low spatial frequency so PNG stays small:
        # fine tooth (2 px cells) plus broad watercolour blotches (16 px cells).
        tooth = rs.normal(1.0, grain_amt / 2.5, (arr.shape[0] // 2 + 1, arr.shape[1] // 2 + 1))
        tooth = np.array(Image.fromarray((tooth * 100).astype(np.uint8)).resize(
            (arr.shape[1], arr.shape[0]), Image.BILINEAR)).astype(np.float32) / 100.0
        small = rs.normal(1.0, blotch_amt, (arr.shape[0] // 16 + 1, arr.shape[1] // 16 + 1))
        blotch = np.array(Image.fromarray((small * 100).astype(np.uint8)).resize(
            (arr.shape[1], arr.shape[0]), Image.BILINEAR)).astype(np.float32) / 100.0
        arr = np.clip(arr * tooth[:, :, None] * blotch[:, :, None], 0, 255).astype(np.uint8)
        return Image.fromarray(arr)


# ---------------------------------------------------------------- palette sheet

PALETTE_GROUPS = [
    ("Ink, light, paper", ["ink.strong", "ink.soft", "ink.water", "light.key",
                            "light.fill", "paper.highlight", "paper.shadow"]),
    ("UI", ["ui.panel", "ui.text", "ui.accent", "ui.link", "ui.good"]),
    ("Wood and walls", ["wood.bark.dark", "wood.beam.dark", "wood.plank.mid",
                        "wood.timber.light", "wall.wattle", "wall.plaster", "wall.plank"]),
    ("Roofs", ["roof.thatch.light", "roof.thatch.shadow", "roof.shingle",
               "roof.canvas", "roof.stone"]),
    ("Stone, clay, props", ["stone.light", "stone.mid", "stone.dark", "clay.raw",
                             "brick.fired", "pottery.glaze", "prop.tarp", "prop.rope",
                             "prop.net", "prop.sail"]),
    ("Foliage and fields", ["leaf.deep", "leaf.mid", "leaf.light", "leaf.orchard",
                             "leaf.fen", "leaf.marsh", "field.soil", "field.grain",
                             "field.pasture", "field.fallow"]),
    ("Terrain", ["terrain.plains", "terrain.forest", "terrain.beach", "terrain.hills",
                 "terrain.highlands", "terrain.peaks", "terrain.fen", "terrain.marsh",
                 "terrain.reef", "terrain.sea", "terrain.deep", "terrain.river"]),
    ("Water and roads", ["water.shallow", "water.foam", "road.dirt", "road.bridge"]),
    ("Goods", ["good.wood", "good.clay", "good.apples", "good.bread", "good.pottery",
                "good.fish", "good.homespun", "good.cider", "good.stone",
                "good.timber", "good.grain", "good.wool", "good.flour"]),
]

EXTRA_HEX = {"ink.strong": "#2E2620", "ink.soft": "#57493A", "ink.water": "#2F4A55",
             "light.key": "#FFE3B0", "light.fill": "#BCCFDF",
             "ui.panel": "#F4EAD2", "ui.text": "#2E2620", "ui.accent": "#8A2F1B",
             "ui.link": "#1F4E5F", "ui.good": "#3E6B34", "water.foam": "#F2EFE2",
             "water.shallow": "#6FA8B8", "road.dirt": "#9A7B52",
             "road.bridge": "#7A5C3D"}

ALL = dict(TOK)
ALL.update(EXTRA_HEX)


def paint_palette(path: str) -> None:
    p = P(1500, 1560)
    f_h, f_t = font(26), font(20)
    p.d.text((36, 20), "Evenmere palette - 81 tokens (palette.md)", fill=INK, font=f_h)
    y = 70
    for title, names in PALETTE_GROUPS:
        p.d.text((36, y), title, fill=INK_SOFT, font=f_t)
        y += 30
        x = 36
        for name in names:
            hexv = ALL[name]
            c = rgb(hexv)
            p.rect([x, y, x + 150, y + 44], fill=c, width=2)
            p.d.text((x, y + 48), name, fill=INK, font=font(15))
            x += 162
            if x + 150 > 1500:
                x = 36
                y += 84
        y += 92
    # The palette sheet keeps exact token fills: no grain pass, so every swatch
    # samples to its palette.md hex.
    p.img.save(path, optimize=True)


# ---------------------------------------------------------------- icon sheet

def icon_cell(p: P, ox: int, oy: int, s: int, name: str, kind: str) -> None:
    """Draw one icon in the 64-unit grid scaled by s, 2-unit ink."""
    u = s  # px per unit
    X = lambda gx: ox + gx * u
    Y = lambda gy: oy + gy * u
    for i in range(0, 65, 8):
        p.line((X(i), Y(0)), (X(i), Y(64)), fill=(200, 185, 160), width=1)
        p.line((X(0), Y(i)), (X(64), Y(i)), fill=(200, 185, 160), width=1)
    p.rect([X(0), Y(0), X(64), Y(64)], fill=None, width=3)
    w = max(2, int(2 * u))
    F = {k: rgb(v) for k, v in TOK.items()}
    d = p.d
    if kind == "fish":
        p.ell([X(10), Y(24), X(44), Y(42)], fill=F["good.fish"], width=w)
        p.poly([(X(42), Y(26)), (X(56), Y(33)), (X(42), Y(40))], fill=F["good.fish"], width=w)
        d.ellipse([X(16), Y(29), X(21), Y(34)], fill=INK)
        d.arc([X(30), Y(28), X(38), Y(38)], 300, 60, fill=INK, width=max(1, w // 2))
        d.line([X(40), Y(26), X(40), Y(40)], fill=INK, width=max(1, w // 2))  # gill
        for gx in (22, 28, 34):  # scale arcs
            d.arc([X(gx), Y(28), X(gx + 8), Y(38)], 270, 90,
                  fill=dark(F["good.fish"], 0.25), width=max(1, w // 2))
    elif kind == "bread":
        p.ell([X(10), Y(26), X(54), Y(44)], fill=F["good.bread"], width=w)
        for gx in (24, 32, 40):
            d.line([X(gx), Y(29), X(gx + 4), Y(36)], fill=lit(F["good.bread"], 0.4), width=w)
        d.arc([X(14), Y(30), X(50), Y(48)], 20, 160, fill=dark(F["good.bread"], 0.3), width=max(1, w // 2))
    elif kind == "homespun":  # rolled bolt of cloth with a weave hint
        p.rect([X(16), Y(28), X(52), Y(44)], fill=F["good.homespun"], width=w)
        p.ell([X(8), Y(26), X(24), Y(46)], fill=lit(F["good.homespun"], 0.2), width=w)  # roll end
        d.arc([X(11), Y(30), X(21), Y(42)], 90, 270, fill=INK, width=max(1, w // 2))  # spiral hint
        d.ellipse([X(14), Y(33), X(18), Y(39)], fill=dark(F["good.homespun"], 0.25), outline=None)
        for wx in (28, 34, 40, 46):  # warp threads
            d.line([X(wx), Y(29), X(wx), Y(43)], fill=dark(F["good.homespun"], 0.2), width=max(1, w // 2))
        d.line([X(17), Y(36), X(51), Y(36)], fill=lit(F["good.homespun"], 0.35), width=max(1, w // 2))  # weft
        p.poly([(X(52), Y(28)), (X(58), Y(32)), (X(52), Y(36))],
               fill=lit(F["good.homespun"], 0.3), width=max(1, w // 2))  # folded flap
    elif kind == "timber":
        for i, yy in enumerate((20, 29, 38)):
            xx = X(12 + (2 if i == 1 else 0))
            p.rect([xx, Y(yy), xx + 40 * u, Y(yy + 8)], fill=F["good.timber"] if i != 1 else dark(F["good.timber"], 0.12), width=w)
        d.ellipse([X(46), Y(38), X(52), Y(46)], fill=lit(F["good.timber"], 0.5), outline=INK, width=max(1, w // 2))
        for gy in (22, 32, 40):  # wood grain
            d.line([X(16), Y(gy), X(44), Y(gy)], fill=dark(F["good.timber"], 0.2), width=max(1, w // 2))
    elif kind == "settlers":
        p.poly([(X(10), Y(40)), (X(32), Y(18)), (X(54), Y(40))], fill=F["roof.thatch.light"], width=w)
        for tx in (24, 32, 40):  # thatch hatch
            d.line([X(tx), Y(40), X(tx - 4), Y(30)], fill=dark(F["roof.thatch.light"], 0.3), width=max(1, w // 2))
        p.rect([X(18), Y(40), X(46), Y(54)], fill=F["wall.wattle"], width=w)
        d.rectangle([X(29), Y(44), X(35), Y(54)], fill=INK)
    elif kind == "knowledge":
        p.poly([(X(10), Y(20)), (X(32), Y(26)), (X(32), Y(48)), (X(10), Y(42))], fill=lit(F["ui.link"], 0.35), width=w)
        p.poly([(X(54), Y(20)), (X(32), Y(26)), (X(32), Y(48)), (X(54), Y(42))], fill=F["ui.link"], width=w)
        d.line([X(32), Y(26), X(32), Y(48)], fill=INK, width=w)
        for py in (30, 36, 42):  # page lines
            d.line([X(14), Y(py), X(28), Y(py + 3)], fill=INK_SOFT, width=max(1, w // 2))
            d.line([X(50), Y(py), X(36), Y(py + 3)], fill=INK_SOFT, width=max(1, w // 2))
        d.rectangle([X(46), Y(14), X(50), Y(26)], fill=rgb(TOK["ui.accent"]), outline=INK, width=max(1, w // 2))
    p.d.text((ox, oy + 64 * u + 8), name, fill=INK, font=font(20))


def paint_icons(path: str) -> None:
    p = P(1560, 640)
    p.d.text((36, 16), "Icon style sheet - 64-unit grid, 2-unit ink, max 3 tones (icons.md)",
             fill=INK, font=font(26))
    cells = [("goods/fish", "fish"), ("goods/bread", "bread"),
             ("goods/homespun", "homespun"), ("goods/timber", "timber"),
             ("tiers/settlers", "settlers"), ("knowledge", "knowledge")]
    for i, (name, kind) in enumerate(cells):
        icon_cell(p, 60 + i * 250, 90, 3, name, kind)
    p.d.text((60, 560), "Master: 256 x 256 PNG, transparent ground, 8 px ink (= 2 units).",
             fill=INK_SOFT, font=font(20))
    p.finish().save(path, optimize=True)


# ---------------------------------------------------------------- iso buildings

PITCH_F = 0.62  # depth compression standing in for ~43 deg pitch at 30 deg yaw


def proj(x, y, z, ox, oy, s):
    return (ox + (x - y) * 0.866 * s, oy + (x + y) * 0.5 * PITCH_F * 2 * s * 0.5 - z * s)


def box(p: P, ox, oy, s, x0, y0, x1, y1, z0, z1, base, outline_w=3):
    """Iso box: top lit, y-face mid, x-face shaded (banded warm light)."""
    c = rgb(TOK[base]) if base in TOK else base
    A = proj(x0, y0, z1, ox, oy, s)
    B = proj(x1, y0, z1, ox, oy, s)
    C = proj(x1, y1, z1, ox, oy, s)
    D = proj(x0, y1, z1, ox, oy, s)
    A0 = proj(x0, y0, z0, ox, oy, s)
    B0 = proj(x1, y0, z0, ox, oy, s)
    C0 = proj(x1, y1, z0, ox, oy, s)
    D0 = proj(x0, y1, z0, ox, oy, s)
    p.poly([D0, C0, C, D], fill=dark(c, 0.18), outline=INK, width=outline_w)
    p.poly([B0, C0, C, B], fill=dark(c, 0.38), outline=INK, width=outline_w)
    p.poly([A, B, C, D], fill=lit(c, 0.18), outline=INK, width=outline_w)


def gable(p: P, ox, oy, s, x0, y0, x1, y1, z, rise, ridge_along_x, base,
          outline_w=3):
    c = rgb(TOK[base])
    if ridge_along_x:
        ym = (y0 + y1) / 2
        L0, L1 = proj(x0, y0, z, ox, oy, s), proj(x1, y0, z, ox, oy, s)
        R0, R1 = proj(x0, ym, z + rise, ox, oy, s), proj(x1, ym, z + rise, ox, oy, s)
        S0, S1 = proj(x0, y1, z, ox, oy, s), proj(x1, y1, z, ox, oy, s)
        p.poly([L0, L1, R1, R0], fill=lit(c, 0.22), outline=INK, width=outline_w)
        p.poly([R0, R1, S1, S0], fill=dark(c, 0.25), outline=INK, width=outline_w)
        T0 = proj(x0, y0, z, ox, oy, s)
        p.poly([proj(x0, y1, z, ox, oy, s), T0, R0], fill=dark(c, 0.1), outline=INK, width=outline_w)
    else:
        xm = (x0 + x1) / 2
        L0, L1 = proj(x0, y0, z, ox, oy, s), proj(x0, y1, z, ox, oy, s)
        R0, R1 = proj(xm, y0, z + rise, ox, oy, s), proj(xm, y1, z + rise, ox, oy, s)
        S0, S1 = proj(x1, y0, z, ox, oy, s), proj(x1, y1, z, ox, oy, s)
        p.poly([L0, L1, R1, R0], fill=lit(c, 0.22), outline=INK, width=outline_w)
        p.poly([R0, R1, S1, S0], fill=dark(c, 0.25), outline=INK, width=outline_w)


def course_lines(p: P, a0, a1, b0, b1, rows: int, color, width: int = 1) -> None:
    """Thatch/shingle courses: lines from the ridge edge (a0-a1) to the eave (b0-b1)."""
    for i in range(1, rows + 1):
        t = i / (rows + 1)
        s0 = (a0[0] + (b0[0] - a0[0]) * t, a0[1] + (b0[1] - a0[1]) * t)
        s1 = (a1[0] + (b1[0] - a1[0]) * t, a1[1] + (b1[1] - a1[1]) * t)
        p.d.line([s0, s1], fill=color, width=width)


def gable_courses(p: P, ox, oy, s, x0, y0, x1, y1, z, rise, color, rows: int = 4) -> None:
    """Course lines on both slopes of a ridge-along-x gable."""
    ym = (y0 + y1) / 2
    course_lines(p, proj(x0, ym, z + rise, ox, oy, s), proj(x1, ym, z + rise, ox, oy, s),
                 proj(x0, y0, z, ox, oy, s), proj(x1, y0, z, ox, oy, s), rows, color)
    course_lines(p, proj(x0, ym, z + rise, ox, oy, s), proj(x1, ym, z + rise, ox, oy, s),
                 proj(x0, y1, z, ox, oy, s), proj(x1, y1, z, ox, oy, s), rows, color)


def wall_frame(p: P, ox, oy, s, face: str, fixed, v0, v1, z0, z1, color, width: int = 2) -> None:
    """Vertical timber-frame lines on one wall face ('x' or 'y' plane)."""
    for v in (v0, v1):
        if face == "y":
            a, b = proj(v, fixed, z0, ox, oy, s), proj(v, fixed, z1, ox, oy, s)
        else:
            a, b = proj(fixed, v, z0, ox, oy, s), proj(fixed, v, z1, ox, oy, s)
        p.d.line([a, b], fill=color, width=width)


def ogee_end(p: P, eave0, eave1, apex, fill) -> None:
    """Gable end with an ogee (bell-curve) silhouette instead of straight rakes."""
    def side(base, tip):
        mx, my = (base[0] + tip[0]) / 2, (base[1] + tip[1]) / 2
        dx, dy = tip[0] - base[0], tip[1] - base[1]
        L = math.hypot(dx, dy) or 1.0
        nx, ny = -dy / L, dx / L
        belly = (base[0] + dx * 0.3 + nx * L * 0.10, base[1] + dy * 0.3 + ny * L * 0.10)
        waist = (base[0] + dx * 0.72 - nx * L * 0.06, base[1] + dy * 0.72 - ny * L * 0.06)
        return [belly, waist]
    pts = [eave0] + side(eave0, apex) + [apex] + side(eave1, apex)[::-1] + [eave1]
    p.poly(pts, fill=fill, width=3)


def shadow(p: P, ox, oy, s, cx, cy, rx, ry):
    sc = proj(cx, cy, 0, ox, oy, s)
    p.d.ellipse([sc[0] - rx * s + 18, sc[1] - ry * s + 12,
                 sc[0] + rx * s + 18, sc[1] + ry * s + 12],
                fill=(90, 70, 55, 90), outline=None)


def smoke(p: P, x, y, h=70):
    for i in range(4):
        yy = y - i * (h // 4)
        r = 5 + i * 3
        p.d.ellipse([x - r + i * 4, yy - r, x + r + i * 4, yy + r],
                    fill=(235, 230, 220, 150), outline=None)


def paint_buildings(path: str) -> None:
    p = P(1600, 980)
    p.d.text((36, 14), "Era I buildings at the default view (~43 deg pitch, 30 deg yaw)",
             fill=INK, font=font(26))
    panels = [
        ("settler_house - wattle + thatch 45 deg, flower box (Villager trim)",
         ["wall.wattle", "roof.thatch.light", "wood.beam.dark"]),
        ("market - striped awning, stall tables (the stripes read)",
         ["roof.canvas", "pottery.glaze", "wood.beam.dark"]),
        ("windmill - tapered tower, ogee cap, sail cross (motion = working)",
         ["wall.wattle", "stone.mid", "prop.sail"]),
        ("woodcutters_hut - plank hut, log piles (pale ends read)",
         ["wood.plank.mid", "wood.bark.dark", "roof.thatch.light"]),
    ]
    for i, (cap, _toks) in enumerate(panels):
        ox = 400 + (i % 2) * 800
        oy = 400 if i < 2 else 830
        s = 88
        capx = 60 + (i % 2) * 800
        capy = (oy - 300) if i < 2 else (oy - 360)
        p.d.text((capx, capy), cap, fill=INK_SOFT, font=font(18))
        shadow(p, ox, oy, s, 0, 0, 1.6, 0.8)
        if i == 0:  # settler house 2x2
            box(p, ox, oy, s, -1, -1, 1, 1, 0.12, 0.77, "wall.wattle")
            gable(p, ox, oy, s, -1.15, -1, 1.15, 1, 0.77, 0.65, True, "roof.thatch.light")
            gable_courses(p, ox, oy, s, -1.15, -1, 1.15, 1, 0.77, 0.65,
                          dark(rgb(TOK["roof.thatch.light"]), 0.25))
            wall_frame(p, ox, oy, s, "y", 1, -0.55, 0.55, 0.12, 0.77, rgb(TOK["wood.beam.dark"]))
            wall_frame(p, ox, oy, s, "x", 1, -0.55, 0.55, 0.12, 0.77, rgb(TOK["wood.beam.dark"]))
            d = p.d
            door = [proj(-0.25, 1, 0.12, ox, oy, s), proj(0.25, 1, 0.12, ox, oy, s),
                    proj(0.25, 1, 0.55, ox, oy, s), proj(-0.25, 1, 0.55, ox, oy, s)]
            p.poly(door, fill=rgb(TOK["wood.beam.dark"]), width=2)
            lint = proj(0, 1, 0.58, ox, oy, s)
            d.line([(lint[0] - 22, lint[1]), (lint[0] + 22, lint[1])],
                   fill=rgb(TOK["wood.timber.light"]), width=4)
            win = proj(0.55, 1, 0.45, ox, oy, s)
            d.rectangle([win[0] - 9, win[1] - 9, win[0] + 9, win[1] + 9],
                        fill=rgb(TOK["paper.highlight"]), outline=INK, width=2)
            d.line([(win[0], win[1] - 9), (win[0], win[1] + 9)], fill=INK, width=2)
            fl = proj(0.55, 1, 0.28, ox, oy, s)
            d.rectangle([fl[0] - 15, fl[1] - 5, fl[0] + 15, fl[1] + 5],
                        fill=rgb(TOK["brick.fired"]), outline=INK, width=2)
            for k in range(3):
                d.ellipse([fl[0] - 10 + k * 10, fl[1] - 12, fl[0] - 4 + k * 10, fl[1] - 5],
                          fill=rgb(TOK["good.apples"]))
            box(p, ox, oy, s, 0.45, -0.35, 0.72, -0.08, 1.05, 1.8, "brick.fired", outline_w=2)
            smoke(p, *proj(0.58, -0.22, 1.85, ox, oy, s))
        elif i == 1:  # market 3x3
            box(p, ox, oy, s, -1.5, -1.5, 1.5, 1.5, 0, 0.1, "field.fallow")
            for q in range(-1, 2):
                box(p, ox, oy, s, q - 0.4, -1.3, q + 0.4, -0.6, 0.1, 0.75, "wood.beam.dark")
            gable(p, ox, oy, s, -1.6, -1.5, 1.6, -0.4, 0.75, 0.35, True, "roof.thatch.light")
            gable_courses(p, ox, oy, s, -1.6, -1.5, 1.6, -0.4, 0.75, 0.35,
                          dark(rgb(TOK["roof.thatch.light"]), 0.3), rows=3)
            for k in range(7):  # awning stripes
                x0 = -1.5 + k * (3.0 / 7)
                p.poly([proj(x0, 0.2, 0.9, ox, oy, s), proj(x0 + 3.0 / 7, 0.2, 0.9, ox, oy, s),
                        proj(x0 + 3.0 / 7, 1.5, 0.35, ox, oy, s), proj(x0, 1.5, 0.35, ox, oy, s)],
                       fill=rgb(TOK["roof.canvas"] if k % 2 else TOK["pottery.glaze"]), width=2)
            for k, gt in enumerate(["good.bread", "good.fish", "good.pottery"]):
                t = proj(-0.9 + k * 0.9, 0.6, 0.35, ox, oy, s)
                p.d.ellipse([t[0] - 9, t[1] - 6, t[0] + 9, t[1] + 6], fill=rgb(TOK[gt]), outline=INK, width=2)
        elif i == 2:  # windmill: tapered 3-tier tower, ogee cap, sails on the cap front
            box(p, ox, oy, s, -0.8, -0.8, 0.8, 0.8, 0, 0.55, "stone.mid")
            box(p, ox, oy, s, -0.64, -0.64, 0.64, 0.64, 0.55, 1.2, "wall.wattle")
            box(p, ox, oy, s, -0.5, -0.5, 0.5, 0.5, 1.2, 1.8, "wall.wattle")
            wall_frame(p, ox, oy, s, "y", 0.64, -0.3, 0.3, 0.55, 1.2, rgb(TOK["wood.beam.dark"]))
            wall_frame(p, ox, oy, s, "x", 0.64, -0.3, 0.3, 0.55, 1.2, rgb(TOK["wood.beam.dark"]))
            # cap slopes (ridge along x) + ogee gable end facing the viewer (x1 end)
            c = rgb(TOK["roof.shingle"])
            ym = 0.0
            p.poly([proj(-0.66, -0.56, 1.8, ox, oy, s), proj(0.66, -0.56, 1.8, ox, oy, s),
                    proj(0.66, ym, 2.35, ox, oy, s), proj(-0.66, ym, 2.35, ox, oy, s)],
                   fill=lit(c, 0.2), width=3)
            p.poly([proj(-0.66, ym, 2.35, ox, oy, s), proj(0.66, ym, 2.35, ox, oy, s),
                    proj(0.66, 0.56, 1.8, ox, oy, s), proj(-0.66, 0.56, 1.8, ox, oy, s)],
                   fill=dark(c, 0.25), width=3)
            ogee_end(p, proj(0.66, -0.56, 1.8, ox, oy, s), proj(0.66, 0.56, 1.8, ox, oy, s),
                     proj(0.66, ym, 2.35, ox, oy, s), dark(c, 0.1))
            fin = proj(0.0, ym, 2.35, ox, oy, s)
            p.d.line([(fin[0], fin[1]), (fin[0], fin[1] - 22)], fill=INK, width=3)
            p.d.ellipse([fin[0] - 6, fin[1] - 30, fin[0] + 6, fin[1] - 18],
                        fill=rgb(TOK["wood.timber.light"]), outline=INK, width=2)
            # sails: hub on the cap's front gable, lattice blades in the screen plane
            hub = proj(0.68, 0.0, 2.0, ox, oy, s)
            R = 1.9 * s
            for ang in (45, 135, 225, 315):
                a = math.radians(ang)
                dx, dy = math.cos(a), math.sin(a)
                tip = (hub[0] + dx * R, hub[1] + dy * R)
                p.d.line([hub, tip], fill=rgb(TOK["wood.beam.dark"]), width=8)
                px, py = -dy, dx
                w0, w1 = 15, 30  # tapered blade: narrow at hub, wide at tip
                blade = [(hub[0] + px * w0, hub[1] + py * w0),
                         (tip[0] + px * w1, tip[1] + py * w1),
                         (tip[0] - px * w1, tip[1] - py * w1),
                         (hub[0] - px * w0, hub[1] - py * w0)]
                p.poly(blade, fill=rgb(TOK["prop.sail"]), width=2)
                for f in (0.35, 0.55, 0.75):  # lattice crossbars
                    bx, by = hub[0] + dx * R * f, hub[1] + dy * R * f
                    ww = w0 + (w1 - w0) * f
                    p.d.line([(bx + px * ww, by + py * ww), (bx - px * ww, by - py * ww)],
                             fill=rgb(TOK["wood.beam.dark"]), width=2)
            p.d.ellipse([hub[0] - 10, hub[1] - 10, hub[0] + 10, hub[1] + 10], fill=INK)
            door = [proj(-0.2, 0.8, 0, ox, oy, s), proj(0.2, 0.8, 0, ox, oy, s),
                    proj(0.2, 0.8, 0.45, ox, oy, s), proj(-0.2, 0.8, 0.45, ox, oy, s)]
            p.poly(door, fill=rgb(TOK["wood.beam.dark"]), width=2)
            sk = proj(1.0, 0.9, 0.1, ox, oy, s)
            for k in range(3):
                p.d.ellipse([sk[0] - 14 + k * 5, sk[1] - 10 - k * 14, sk[0] + 14 + k * 5, sk[1] + 10 - k * 14],
                            fill=rgb(TOK["good.grain"]), outline=INK, width=2)
        else:  # woodcutter hut
            box(p, ox, oy, s, -1, -1, 0.2, 1, 0.12, 0.77, "wood.plank.mid")
            gable(p, ox, oy, s, -1.1, -1, 0.3, 1, 0.77, 0.6, True, "roof.thatch.light")
            gable_courses(p, ox, oy, s, -1.1, -1, 0.3, 1, 0.77, 0.6,
                          dark(rgb(TOK["roof.thatch.light"]), 0.25))
            for v in (-0.7, -0.4, -0.1):  # plank seams on the long wall
                a, b = proj(v, 1, 0.12, ox, oy, s), proj(v, 1, 0.77, ox, oy, s)
                p.d.line([a, b], fill=dark(rgb(TOK["wood.plank.mid"]), 0.3), width=2)
            wall_frame(p, ox, oy, s, "y", 1, -0.85, 0.05, 0.12, 0.77, rgb(TOK["wood.beam.dark"]))
            for r in range(2):  # log piles: rows of end-grain circles
                for k in range(4):
                    c = proj(0.45 + r * 0.35, -0.75 + k * 0.42, 0.12 + (0.18 if k % 2 else 0), ox, oy, s)
                    p.d.ellipse([c[0] - 11, c[1] - 9, c[0] + 11, c[1] + 9],
                                fill=rgb(TOK["wood.timber.light"]), outline=INK, width=2)
                    p.d.ellipse([c[0] - 5, c[1] - 4, c[0] + 5, c[1] + 4],
                                fill=None, outline=rgb(TOK["wood.bark.dark"]), width=2)
            st = proj(-1.3, 0.4, 0.12, ox, oy, s)
            p.d.ellipse([st[0] - 14, st[1] - 10, st[0] + 14, st[1] + 10],
                        fill=rgb(TOK["wood.bark.dark"]), outline=INK, width=2)
            p.d.line([(st[0], st[1] - 8), (st[0] + 16, st[1] - 34)],
                     fill=rgb(TOK["wood.beam.dark"]), width=4)  # axe haft
            p.d.polygon([(st[0] + 16, st[1] - 34), (st[0] + 28, st[1] - 30),
                         (st[0] + 24, st[1] - 20), (st[0] + 12, st[1] - 24)],
                        fill=rgb(TOK["stone.dark"]), outline=INK)
    p.finish().save(path, optimize=True)


# ---------------------------------------------------------------- island scene

def tree(p: P, ox, oy, s, tx, ty, big=False):
    b = proj(tx, ty, 0, ox, oy, s)
    r = (26 if big else 18)
    p.d.ellipse([b[0] - r, b[1] - r * 2.2, b[0] + r, b[1] - r * 0.6],
                fill=rgb(TOK["leaf.mid"]), outline=INK, width=2)
    p.d.ellipse([b[0] - r * 0.6, b[1] - r * 2.7, b[0] + r * 0.5, b[1] - r * 1.4],
                fill=rgb(TOK["leaf.light"]), outline=None)


def house(p: P, ox, oy, s, hx, hy, plaster=False):
    wall = "wall.plaster" if plaster else "wall.wattle"
    box(p, ox, oy, s, hx - 1, hy - 1, hx + 1, hy + 1, 0, 0.65, wall, outline_w=2)
    gable(p, ox, oy, s, hx - 1.12, hy - 1, hx + 1.12, hy + 1, 0.65, 0.6, True,
          "roof.thatch.light", outline_w=2)
    if plaster:
        smoke(p, *proj(hx + 0.5, hy - 0.3, 1.4, ox, oy, s), h=50)


def paint_island(path: str) -> None:
    W, H = 1600, 1000
    p = P(W, H, bg=rgb(TOK["terrain.sea"]))
    ox, oy, s = 800, 560, 52
    # sea texture: ripple arcs across the whole sheet (island covers the middle)
    for _ in range(130):
        x = p.rng.uniform(0, W)
        y = p.rng.uniform(0, H)
        p.d.arc([x, y, x + 46, y + 14], 200, 340, fill=INK_WATER, width=2)
    # shallows apron, then island ground, then beach rim + broken foam
    apron = [proj(x, y, 0, ox, oy, s) for x, y in
             [(-8.6, -5.8), (4.0, -7.2), (8.6, -2.4), (7.2, 5.2), (-1.4, 7.4), (-8.6, 3.8)]]
    p.poly(apron, fill=rgb(TOK["water.shallow"]), outline=None, width=1)
    # island ground: big soft polygon
    ground = [proj(x, y, 0, ox, oy, s) for x, y in
              [(-7, -5), (3, -6), (7, -2), (6, 4), (-1, 6), (-7, 3)]]
    p.poly(ground, fill=rgb(TOK["terrain.plains"]), outline=None, width=1)
    # gentle hills: nested bands (banded light) with a shade crescent
    for hx, hy, hr in [(-4.3, -3.6, 2.1), (5.0, -2.7, 1.0)]:
        c0 = proj(hx, hy, 0, ox, oy, s)
        for k, (rr, hfill) in enumerate([(hr, rgb(TOK["terrain.hills"])),
                                        (hr * 0.66, lit(rgb(TOK["terrain.hills"]), 0.15)),
                                        (hr * 0.34, lit(rgb(TOK["terrain.hills"]), 0.3))]):
            rx, ry = rr * s, rr * s * 0.55
            p.d.ellipse([c0[0] - rx, c0[1] - ry, c0[0] + rx, c0[1] + ry],
                        fill=hfill, outline=INK if k == 0 else None, width=2)
        p.d.arc([c0[0] - hr * s * 0.8, c0[1] - hr * s * 0.45,
                 c0[0] + hr * s * 0.8, c0[1] + hr * s * 0.45],
                200, 340, fill=dark(rgb(TOK["terrain.hills"]), 0.3), width=3)
    # beach rim + foam
    rim = [proj(x, y, 0, ox, oy, s) for x, y in
           [(-7.8, -5.4), (3.6, -6.6), (7.8, -2.2), (6.6, 4.6), (-1.2, 6.8), (-7.8, 3.4)]]
    p.d.polygon(rim, fill=None, outline=rgb(TOK["terrain.beach"]), width=10)
    p.d.polygon(rim, fill=None, outline=rgb(TOK["water.foam"]), width=3)
    # grain farm: gold rows west
    for r in range(6):
        y0 = -4.6 + r * 0.55
        p.poly([proj(-6.4, y0, 0, ox, oy, s), proj(-2.6, y0, 0, ox, oy, s),
                proj(-2.6, y0 + 0.3, 0, ox, oy, s), proj(-6.4, y0 + 0.3, 0, ox, oy, s)],
               fill=rgb(TOK["field.grain"] if r % 2 == 0 else TOK["field.soil"]),
               outline=INK, width=1)
    # dirt road: rounded path through the settlement
    road_pts = [proj(x, y, 0.01, ox, oy, s) for x, y in
                [(-6, 2.5), (-3, 1.2), (0, 0.8), (2.5, 1.8), (4.5, 4.2)]]
    # dirt road with a soft shoulder: wide pale band, dirt ribbon, wheel ruts
    p.d.line(road_pts, fill=mix(rgb(TOK["road.dirt"]), rgb(TOK["terrain.plains"]), 0.45), width=34)
    p.d.line(road_pts, fill=rgb(TOK["road.dirt"]), width=22)
    p.d.line(road_pts, fill=rgb(TOK["field.soil"]), width=3)
    # houses along road
    house(p, ox, oy, s, -2.2, -0.6, plaster=False)
    house(p, ox, oy, s, 0.2, -1.0, plaster=True)
    house(p, ox, oy, s, 1.8, 0.2, plaster=True)
    # market with awning
    box(p, ox, oy, s, -0.6, 1.6, 1.4, 3.2, 0, 0.7, "wood.beam.dark", outline_w=2)
    for k in range(6):
        x0 = -0.6 + k * (2.0 / 6)
        p.poly([proj(x0, 1.6, 0.85, ox, oy, s), proj(x0 + 2.0 / 6, 1.6, 0.85, ox, oy, s),
                proj(x0 + 2.0 / 6, 3.2, 0.4, ox, oy, s), proj(x0, 3.2, 0.4, ox, oy, s)],
               fill=rgb(TOK["roof.canvas"] if k % 2 else TOK["pottery.glaze"]), width=2)
    # trees
    for tx, ty, big in [(-5, -1, True), (-4, 3.5, False), (3.5, -2.5, True),
                        (5.5, 0.5, False), (-0.5, -3.5, True), (2.8, -3.8, False),
                        (5.0, -2.7, False)]:  # last one crowns the east hillock
        tree(p, ox, oy, s, tx, ty, big)
    # stooks on the farm
    for sx, sy in [(-5.5, -1.0), (-4.2, -0.4), (-3.2, -1.4)]:
        b = proj(sx, sy, 0, ox, oy, s)
        p.poly([(b[0] - 10, b[1]), (b[0], b[1] - 26), (b[0] + 10, b[1])],
               fill=rgb(TOK["field.grain"]), width=2)
    # fenced paddock south of the road: posts + two rails
    fence = [(-5.5, 3.4), (-4.0, 2.8), (-2.5, 2.3), (-1.0, 2.0)]
    tops, mids = [], []
    for fx, fy in fence:
        fb = proj(fx, fy, 0, ox, oy, s)
        p.d.line([(fb[0], fb[1]), (fb[0], fb[1] - 22)], fill=rgb(TOK["wood.beam.dark"]), width=3)
        tops.append((fb[0], fb[1] - 22))
        mids.append((fb[0], fb[1] - 12))
    p.d.line(tops, fill=rgb(TOK["wood.plank.mid"]), width=2)
    p.d.line(mids, fill=rgb(TOK["wood.plank.mid"]), width=2)
    # hand cart by the market
    cb = proj(2.4, 2.8, 0, ox, oy, s)
    p.d.rectangle([cb[0] - 18, cb[1] - 26, cb[0] + 18, cb[1] - 12],
                  fill=rgb(TOK["wood.timber.light"]), outline=INK, width=2)
    for wx in (-12, 12):
        p.d.ellipse([cb[0] + wx - 8, cb[1] - 10, cb[0] + wx + 8, cb[1] + 6],
                    fill=rgb(TOK["wood.bark.dark"]), outline=INK, width=2)
    # log pile by the frame house
    for k in range(3):
        lb = proj(-3.6 + (k * 0.35), -0.2 + (k * 0.3), 0.1, ox, oy, s)
        p.d.ellipse([lb[0] - 9, lb[1] - 7, lb[0] + 9, lb[1] + 7],
                    fill=rgb(TOK["wood.timber.light"]), outline=INK, width=2)
    # herb rows by the plaster house
    for r in range(3):
        y0 = -2.7 + r * 0.4
        p.poly([proj(-0.6, y0, 0, ox, oy, s), proj(0.6, y0, 0, ox, oy, s),
                proj(0.6, y0 + 0.2, 0, ox, oy, s), proj(-0.6, y0 + 0.2, 0, ox, oy, s)],
               fill=rgb(TOK["leaf.fen"]), outline=INK, width=1)
    p.d.text((36, 20), "Default view: houses on a dirt road, market stripes, grain gold, coast foam",
             fill=(244, 234, 210), font=font(24))
    p.finish().save(path, optimize=True)


if __name__ == "__main__":
    import os
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)))
    paint_palette(os.path.join(out, "palette.png"))
    paint_icons(os.path.join(out, "icons.png"))
    paint_buildings(os.path.join(out, "buildings.png"))
    paint_island(os.path.join(out, "island.png"))
    print("wrote four concept sheets")
