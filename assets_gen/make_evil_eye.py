"""Generates a pixel-art Evil Eye (Nazar) charm as a transparent PNG.
Placeholder first-party asset for CharmDesk until a hand-authored asset is supplied.
"""
import math
import os
from PIL import Image

SCALE = 8          # each logical pixel becomes SCALE x SCALE real pixels (crisp pixel-art look)
GRID_W = 18
GRID_H = 22         # extra rows on top reserved for the loop/bail

# colors (RGBA)
TRANSPARENT = (0, 0, 0, 0)
OUTLINE     = (13, 15, 35, 255)     # near-black navy outline
NAVY        = (23, 43, 130, 255)    # dark cobalt ring
WHITE       = (238, 240, 245, 255)
TURQUOISE   = (46, 165, 189, 255)
LIGHT_BLUE  = (94, 205, 222, 255)
PUPIL       = (16, 18, 40, 255)
HIGHLIGHT   = (255, 255, 255, 235)
GOLD        = (196, 160, 68, 255)
GOLD_DARK   = (140, 110, 40, 255)

BEAD_CENTER = (GRID_W / 2 - 0.5, 14.0)
BEAD_RADIUS = 8.2


def bead_color(x, y):
    dx = x - BEAD_CENTER[0]
    dy = y - BEAD_CENTER[1]
    r = math.sqrt(dx * dx + dy * dy) / BEAD_RADIUS
    if r > 1.0:
        return TRANSPARENT
    if r > 0.90:
        return OUTLINE
    if r > 0.72:
        return NAVY
    if r > 0.56:
        return WHITE
    if r > 0.38:
        return TURQUOISE if (dx + dy * 0.3) > -2 else LIGHT_BLUE
    if r > 0.20:
        return PUPIL
    # small highlight glint near upper-left of pupil core
    if -0.95 < (dx + 2.4) < 1.6 and -0.9 < (dy + 2.4) < 1.1:
        return HIGHLIGHT
    return PUPIL


def loop_color(x, y):
    # small gold jump-ring/bail at the very top, centered
    cx, cy = GRID_W / 2 - 0.5, 2.4
    dx, dy = x - cx, y - cy
    r = math.sqrt(dx * dx + (dy * 1.35) ** 2)
    if 1.35 < r < 2.55:
        return GOLD if dy <= 0 else GOLD_DARK
    return None


def build_grid():
    grid = [[TRANSPARENT for _ in range(GRID_W)] for _ in range(GRID_H)]
    for y in range(GRID_H):
        for x in range(GRID_W):
            lc = loop_color(x, y)
            if lc is not None:
                grid[y][x] = lc
                continue
            if y >= 4:
                grid[y][x] = bead_color(x, y - 4 + 4)  # keep bead lower half of grid
    return grid


def render(grid, path, crop_to_bead=False):
    img = Image.new("RGBA", (GRID_W, GRID_H), TRANSPARENT)
    px = img.load()
    for y in range(GRID_H):
        for x in range(GRID_W):
            px[x, y] = grid[y][x]
    if crop_to_bead:
        img = img.crop((0, 4, GRID_W, GRID_H))
    img = img.resize((img.width * SCALE, img.height * SCALE), Image.NEAREST)
    img.save(path)
    print(f"wrote {path} ({img.width}x{img.height})")


if __name__ == "__main__":
    out_dir = os.path.join(os.path.dirname(__file__), "..", "charms", "evil-eye")
    out_dir = os.path.abspath(out_dir)
    os.makedirs(out_dir, exist_ok=True)

    grid = build_grid()
    render(grid, os.path.join(out_dir, "charm.png"), crop_to_bead=False)
    render(grid, os.path.join(out_dir, "thumbnail.png"), crop_to_bead=True)
