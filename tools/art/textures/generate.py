#!/usr/bin/env python3
"""ART-04 texture generator: painted textures for the hand-drawn look.

Draws every texture in ``docs/art/briefs/textures.md`` with NumPy and Pillow
only, deterministic from ``SEED``. Tiling textures are seamless: value noise
uses a wrapping lattice and every motif stroke is drawn at all nine wrapped
offsets, so opposite edges match exactly.

Usage (from a venv with ``requirements.txt`` installed)::

    python generate.py --output game/assets/textures --review <shots dir>

``--review`` additionally writes a contact sheet and a 3x3 tiling of each
tileable texture for the visual review; those files are not committed.
"""

from __future__ import annotations

import argparse
import math
import os
import random
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

SEED = 4104

# --- Palette tokens (docs/art/palette.md) ------------------------------------

PAPER_HIGHLIGHT = "#FFF8E8"
PAPER_SHADOW = "#E2D3B0"
WOOD_BEAM_DARK = "#4A3A26"
WOOD_TIMBER_LIGHT = "#D0A05C"
WALL_WATTLE = "#CDBF9B"
WALL_PLANK = "#A8895A"
ROOF_THATCH_LIGHT = "#D3A944"
ROOF_THATCH_SHADOW = "#A8812F"
STONE_MID = "#7A7A74"
STONE_DARK = "#55554F"
CLAY_RAW = "#8F5636"
BRICK_FIRED = "#B25A38"
LEAF_DEEP = "#2F5B2A"
FIELD_FALLOW = "#9A7B52"
WATER_SHALLOW = "#6FA8B8"
WATER_SEA = "#336B80"
WATER_FOAM = "#F2EFE2"
TERRAIN = {
    "plains": "#73A154",
    "forest": "#3D7342",
    "beach": "#D9C793",
    "hills": "#8C8559",
    "highlands": "#807766",
    "peaks": "#BFBDB8",
    "fen": "#8C9E66",
    "marsh": "#596B4D",
    "reef": "#409999",
    "sea": "#336B80",
    "deep": "#1A3B61",
    "river": "#5E93A3",
}


def rgb(token: str) -> tuple[int, int, int]:
    """Converts an sRGB hex token to an (R, G, B) tuple."""
    token = token.lstrip("#")
    return (int(token[0:2], 16), int(token[2:4], 16), int(token[4:6], 16))


def periodic_noise(size: int, cells: int, rng: random.Random) -> np.ndarray:
    """Tileable value noise in [-1, 1] from a wrapping lattice."""
    lattice = np.array([[rng.random() * 2.0 - 1.0 for _ in range(cells)] for _ in range(cells)])
    grid = np.arange(size, dtype=np.float64) / size * cells
    idx = grid.astype(int) % cells
    frac = grid - np.floor(grid / cells) * cells - idx
    smooth = frac * frac * (3.0 - 2.0 * frac)
    i0 = idx
    i1 = (idx + 1) % cells
    s0 = smooth
    s1 = 1.0 - smooth
    # Outer product separable over y/x via broadcasting.
    wy0 = s1[:, None]
    wy1 = s0[:, None]
    wx0 = s1[None, :]
    wx1 = s0[None, :]
    # l00 takes (1-x)(1-y) so each cell edge meets its neighbour exactly.
    return (
        lattice[i0[:, None], i0[None, :]] * wy0 * wx0
        + lattice[i0[:, None], i1[None, :]] * wy0 * wx1
        + lattice[i1[:, None], i0[None, :]] * wy1 * wx0
        + lattice[i1[:, None], i1[None, :]] * wy1 * wx1
    )


def modulate(base: np.ndarray, noises: list[tuple[np.ndarray, float]]) -> np.ndarray:
    """Multiplies an albedo array by (1 + amplitude * noise) terms, clipped."""
    factor = np.ones_like(noises[0][0])
    for noise, amplitude in noises:
        factor += noise * amplitude
    if base.ndim == 3:
        factor = factor[:, :, None]
    return np.clip(base * factor, 0.0, 255.0)


class WrappedDraw:
    """Draws each stroke at all nine wrapped offsets so tiles stay seamless."""

    def __init__(self, image: Image.Image) -> None:
        self._draw = ImageDraw.Draw(image)
        self._size = image.size[0]

    def _offsets(self, x: float, y: float) -> list[tuple[float, float]]:
        size = self._size
        return [(x + dx, y + dy) for dx in (-size, 0, size) for dy in (-size, 0, size)]

    def arc(self, x: float, y: float, r: float, start: float, end: float, **kwargs) -> None:
        """Draws an arc centred on (x, y) with radius r, wrapped."""
        for ox, oy in self._offsets(x, y):
            self._draw.arc([ox - r, oy - r, ox + r, oy + r], start, end, **kwargs)

    def line(self, x0: float, y0: float, x1: float, y1: float, **kwargs) -> None:
        """Draws a line segment, wrapped."""
        for ox, oy in self._offsets(0.0, 0.0):
            self._draw.line([x0 + ox, y0 + oy, x1 + ox, y1 + oy], **kwargs)

    def ellipse(self, x: float, y: float, r: float, **kwargs) -> None:
        """Draws a dot of radius r at (x, y), wrapped."""
        for ox, oy in self._offsets(x, y):
            self._draw.ellipse([ox - r, oy - r, ox + r, oy + r], **kwargs)


def paper_grain(rng: random.Random) -> Image.Image:
    """Mid-grey tileable fine grain for the full-screen multiply."""
    size = 512
    grain = periodic_noise(size, 48, rng) * 6.0 + periodic_noise(size, 160, rng) * 4.0
    return Image.fromarray(np.clip(128.0 + grain, 0, 255).astype(np.uint8), "L")


def wash(rng: random.Random) -> Image.Image:
    """Mid-grey tileable watercolour blotches for large flat areas."""
    size = 512
    blotch = periodic_noise(size, 4, rng) * 12.0 + periodic_noise(size, 9, rng) * 7.0 + periodic_noise(size, 48, rng) * 3.0
    return Image.fromarray(np.clip(128.0 + blotch, 0, 255).astype(np.uint8), "L")


def ripples(rng: random.Random) -> Image.Image:
    """Hand-drawn ripple stroke mask: still-water grey with pale crests."""
    size = 512
    image = Image.new("L", (size, size), 128)
    draw = WrappedDraw(image)
    for _ in range(44):
        x = rng.random() * size
        y = rng.random() * size
        r = 12.0 + rng.random() * 36.0
        start = rng.random() * 360.0
        draw.arc(x, y, r, start, start + 40.0 + rng.random() * 80.0, fill=235, width=2)
    for _ in range(30):
        x = rng.random() * size
        y = rng.random() * size
        angle = rng.random() * math.pi
        length = 6.0 + rng.random() * 8.0
        dx = math.cos(angle) * length / 2.0
        dy = math.sin(angle) * length / 2.0
        draw.line(x - dx, y - dy, x + dx, y + dy, fill=220, width=2)
    return image


def terrain_base(name: str, rng: random.Random) -> Image.Image:
    """Token fill with low-contrast periodic variation (shared by all biomes)."""
    size = 256
    base = np.zeros((size, size, 3), dtype=np.float64)
    base[:, :] = rgb(TERRAIN[name])
    low = periodic_noise(size, 4, rng)
    fine = periodic_noise(size, 32, rng)
    varied = modulate(base, [(low, 0.04), (fine, 0.03)])
    return Image.fromarray(varied.astype(np.uint8), "RGB")


def add_motif(name: str, image: Image.Image, rng: random.Random) -> None:
    """Draws the biome's sparse street-zoom motif, wrapped for seamlessness."""
    size = image.size[0]
    draw = WrappedDraw(image)
    if name == "plains":
        for _ in range(60):
            x = rng.random() * size
            y = rng.random() * size
            for blade in (-2.0, 0.0, 2.0):
                draw.line(x + blade, y, x + blade * 1.5, y - 3.0 - rng.random() * 3.0,
                          fill=rgb(LEAF_DEEP), width=1)
    elif name == "forest":
        for _ in range(90):
            draw.ellipse(rng.random() * size, rng.random() * size,
                         2.0 + rng.random() * 3.0, fill=rgb(LEAF_DEEP))
    elif name == "beach":
        for _ in range(40):
            draw.ellipse(rng.random() * size, rng.random() * size,
                         1.0 + rng.random() * 2.0, fill=rgb(STONE_MID))
    elif name == "hills":
        for _ in range(50):
            x = rng.random() * size
            y = rng.random() * size
            draw.line(x, y, x + 3.0 + rng.random() * 4.0, y - 2.0 - rng.random() * 2.0,
                      fill=rgb(FIELD_FALLOW), width=1)
    elif name == "highlands":
        for _ in range(40):
            x = rng.random() * size
            y = rng.random() * size
            draw.line(x, y, x + 4.0 + rng.random() * 5.0, y + 1.0 + rng.random() * 2.0,
                      fill=rgb(STONE_DARK), width=2)
    elif name == "peaks":
        highlight = rgb(PAPER_HIGHLIGHT)
        for _ in range(30):
            x = rng.random() * size
            y = rng.random() * size
            draw.line(x - 2.0, y, x + 2.0, y, fill=highlight, width=1)
            draw.line(x, y - 2.0, x, y + 2.0, fill=highlight, width=1)
    elif name in ("fen", "marsh"):
        for _ in range(40):
            x = rng.random() * size
            y = rng.random() * size
            for stem in range(5):
                height = 6.0 + rng.random() * 6.0
                draw.line(x + stem - 2.0, y, x + stem - 2.0, y - height,
                          fill=rgb(LEAF_DEEP), width=1)
        if name == "marsh":
            for _ in range(25):
                x = rng.random() * size
                y = rng.random() * size
                draw.line(x, y, x + 4.0 + rng.random() * 4.0, y,
                          fill=rgb(WATER_SHALLOW), width=1)
    elif name == "reef":
        for _ in range(25):
            x = rng.random() * size
            y = rng.random() * size
            r = 6.0 + rng.random() * 10.0
            start = rng.random() * 360.0
            draw.arc(x, y, r, start, start + 60.0 + rng.random() * 60.0,
                     fill=rgb(WATER_FOAM), width=1)
    elif name in ("sea", "deep"):
        # Soft patchy wash toward a lighter token: low-frequency periodic
        # noise only, no directional bands, so nothing stripes when tiled.
        band_token = WATER_SHALLOW if name == "sea" else WATER_SEA
        pixels = image.load()
        assert pixels is not None
        band = rgb(band_token)
        base = rgb(TERRAIN[name])
        blotch = periodic_noise(size, 3, rng)
        peak = max(1e-6, max(float(blotch.max()), -float(blotch.min())))
        for y in range(size):
            for x in range(size):
                blend = 0.10 + 0.08 * (0.5 + 0.5 * blotch[y, x] / peak)
                pixels[x, y] = tuple(
                    round(base[c] + (band[c] - base[c]) * blend) for c in range(3))
    elif name == "river":
        base = np.array(image, dtype=np.float64)
        xs = np.arange(size, dtype=np.float64)
        lift = 1.0 + 0.06 * np.exp(-(((xs - size / 2.0) / (size * 0.18)) ** 2))
        base *= lift[None, :, None]
        # Centre lighten only: hard bank lines would stripe when tiled,
        # so the banks stay a soft wash the river shader edges.
        image.paste(Image.fromarray(np.clip(base, 0, 255).astype(np.uint8), "RGB"))


def palette_atlas(rng: random.Random) -> Image.Image:
    """One 1024 atlas: 4x4 grid of 256px material cells, Era I row first."""
    size = 1024
    cell = 256
    image = Image.new("RGB", (size, size), rgb(PAPER_SHADOW))
    draw = ImageDraw.Draw(image)

    def cell_origin(col: int, row: int) -> tuple[int, int]:
        return (col * cell, row * cell)

    def paint_cell(col: int, row: int, base_token: str) -> Image.Image:
        base = np.zeros((cell, cell, 3), dtype=np.float64)
        base[:, :] = rgb(base_token)
        grain = periodic_noise(cell, 8, rng)
        tile = Image.fromarray(modulate(base, [(grain, 0.05)]).astype(np.uint8), "RGB")
        image.paste(tile, cell_origin(col, row))
        return image

    # Timber: horizontal courses.
    paint_cell(0, 0, WOOD_TIMBER_LIGHT)
    beam = rgb(WOOD_BEAM_DARK)
    for i in range(4):
        y = 0 * cell + i * 64
        draw.line([0, y, cell, y], fill=beam, width=2)
    # Wattle: woven stripes over stakes.
    paint_cell(1, 0, WALL_WATTLE)
    clay = rgb(CLAY_RAW)
    for y in range(0, cell, 16):
        shade = clay if (y // 16) % 2 == 0 else rgb(WOOD_BEAM_DARK)
        draw.line([cell, y, 2 * cell, y], fill=shade, width=1)
    for x in range(cell, 2 * cell, 32):
        draw.line([x, 0, x, cell], fill=rgb(WOOD_BEAM_DARK), width=2)
    # Thatch: vertical stroke rows shading down.
    paint_cell(2, 0, ROOF_THATCH_LIGHT)
    shadow = rgb(ROOF_THATCH_SHADOW)
    thatch_rng = random.Random(SEED + 1)
    for row in range(8):
        y = row * 32
        for x in range(2 * cell, 3 * cell, 6):
            length = 14 + thatch_rng.randint(0, 10)
            draw.line([x, y, x, y + length], fill=shadow, width=2)
    # Plank: vertical seams and knots.
    paint_cell(3, 0, WALL_PLANK)
    for x in range(3 * cell, 4 * cell, 36):
        draw.line([x, 0, x, cell], fill=beam, width=2)
    knot_rng = random.Random(SEED + 2)
    for _ in range(6):
        x = 3 * cell + knot_rng.randint(8, cell - 8)
        y = knot_rng.randint(8, cell - 8)
        draw.ellipse([x - 4, y - 6, x + 4, y + 6], outline=beam, width=2)
    # Stone: ashlar courses with offset joints.
    paint_cell(0, 1, STONE_MID)
    joint = rgb(STONE_DARK)
    for row in range(4):
        y = cell + row * 64
        draw.line([0, y, cell, y], fill=joint, width=2)
        offset = 64 if row % 2 == 0 else 0
        for x in range(offset, cell + 1, 128):
            draw.line([x, y, x, y + 64], fill=joint, width=2)
    # Clay tile: overlapping rows with a shadow lip.
    paint_cell(1, 1, BRICK_FIRED)
    for y in range(cell, 2 * cell, 48):
        draw.line([cell, y, 2 * cell, y], fill=rgb(CLAY_RAW), width=4)
        draw.line([cell, y - 4, 2 * cell, y - 4], fill=rgb(PAPER_HIGHLIGHT), width=1)
    return image


def contact_sheet(textures: list[tuple[str, Image.Image]], path: str) -> None:
    """Labeled grid of every texture for the visual review."""
    thumb = 256
    cols = 4
    rows = math.ceil(len(textures) / cols)
    label = 28
    sheet = Image.new("RGB", (cols * thumb, rows * (thumb + label)), (244, 234, 210))
    canvas = ImageDraw.Draw(sheet)
    try:
        font = ImageFont.load_default(size=16)
    except TypeError:
        font = ImageFont.load_default()
    for i, (name, texture) in enumerate(textures):
        work = texture.convert("RGB").resize((thumb, thumb), Image.LANCZOS)
        x = (i % cols) * thumb
        y = (i // cols) * (thumb + label)
        sheet.paste(work, (x, y))
        canvas.text((x + 6, y + thumb + 5), name, fill=(46, 38, 32), font=font)
    sheet.save(path)


def tiled_view(name: str, texture: Image.Image, path: str) -> None:
    """3x3 tiling of one tileable texture to expose seams."""
    size = texture.size[0]
    view = Image.new(texture.mode, (size * 3, size * 3))
    tile = texture.convert("RGB") if texture.mode == "L" else texture
    for row in range(3):
        for col in range(3):
            view.paste(tile, (col * size, row * size))
    view.save(path)


def main(argv: list[str]) -> int:
    """Generates the textures and optionally the review images."""
    parser = argparse.ArgumentParser(description="Generate ART-04 textures.")
    parser.add_argument("--output", required=True, help="Texture root (game/assets/textures).")
    parser.add_argument("--review", default="", help="Review image dir (contact sheet + 3x3 views).")
    args = parser.parse_args(argv)

    rng = random.Random(SEED)
    outputs: list[tuple[str, Image.Image]] = [
        ("grain/paper_grain.png", paper_grain(rng)),
        ("grain/wash.png", wash(rng)),
        ("water/ripples.png", ripples(rng)),
    ]
    for name in TERRAIN:
        tile = terrain_base(name, rng)
        add_motif(name, tile, rng)
        outputs.append((f"terrain/{name}.png", tile))
    outputs.append(("materials/palette_atlas.png", palette_atlas(rng)))

    for rel, image in outputs:
        dest = os.path.join(args.output, rel)
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        image.save(dest)

    if args.review:
        os.makedirs(args.review, exist_ok=True)
        contact_sheet([(rel, img) for rel, img in outputs],
                      os.path.join(args.review, "contact_sheet.png"))
        for rel, img in outputs:
            if rel == "materials/palette_atlas.png":
                continue
            flat = "tiled_" + rel.replace("/", "_")
            tiled_view(rel, img, os.path.join(args.review, flat))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
