"""Composes the README hero banner: the app's own pegboard palette/typography, with the three
bundled charms hanging from thin strings, so the repo's front door looks like the product.
"""
import os
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.join(os.path.dirname(__file__), "..")
CHARMS = os.path.join(ROOT, "charms")
FONTS = os.path.join(ROOT, "src", "CharmDesk", "Fonts")
OUT = os.path.join(ROOT, "docs", "banner.png")

W, H = 1200, 380
BG = (243, 238, 250)
DOT = (227, 214, 246)
INK = (36, 27, 58)
ACCENT = (109, 40, 217)

SCALE = 2  # supersample for crisp edges, then downsize
img = Image.new("RGBA", (W * SCALE, H * SCALE), BG)
draw = ImageDraw.Draw(img)

# Pegboard dot grid, matching the app's PegboardBrush tile (26px pitch, 1.6px dot)
pitch = 26 * SCALE
r = 1.6 * SCALE
for y in range(0, H * SCALE + pitch, pitch):
    for x in range(0, W * SCALE + pitch, pitch):
        draw.ellipse([x - r, y - r, x + r, y + r], fill=DOT)

# Wordmark
title_font = ImageFont.truetype(os.path.join(FONTS, "Fredoka-Bold.ttf"), 92 * SCALE)
tag_font = ImageFont.truetype(os.path.join(FONTS, "SpaceMono-Bold.ttf"), 20 * SCALE)

title = "CharmDesk"
tag = "COLLECTIBLE  DESKTOP  CHARMS  FOR  WINDOWS"

tx, ty = 64 * SCALE, 120 * SCALE
draw.text((tx, ty), title, font=title_font, fill=INK)
draw.text((tx + 4 * SCALE, ty + 108 * SCALE), tag, font=tag_font, fill=ACCENT)

# Hang the three charms from thin strings along the right side
charm_specs = [
    ("evil-eye", 780, 40, 150),
    ("evil-eye-ornate", 900, 10, 175),
    ("cute-ghost", 1030, 55, 150),
]
for name, cx, top, target_h in charm_specs:
    charm = Image.open(os.path.join(CHARMS, name, "charm.png")).convert("RGBA")
    ratio = target_h / charm.height
    charm = charm.resize((max(1, int(charm.width * ratio * SCALE)), int(target_h * SCALE)), Image.LANCZOS)
    anchor_x = cx * SCALE
    anchor_y = 8 * SCALE
    draw.line([(anchor_x, anchor_y), (anchor_x, top * SCALE)], fill=(184, 180, 172), width=max(1, SCALE))
    draw.ellipse([anchor_x - 3 * SCALE, anchor_y - 3 * SCALE, anchor_x + 3 * SCALE, anchor_y + 3 * SCALE], fill=(196, 160, 68))
    paste_x = anchor_x - charm.width // 2
    paste_y = top * SCALE
    img.alpha_composite(charm, (int(paste_x), int(paste_y)))

img = img.resize((W, H), Image.LANCZOS)
os.makedirs(os.path.dirname(OUT), exist_ok=True)
img.save(OUT)
print(f"wrote {OUT} {img.size}")
