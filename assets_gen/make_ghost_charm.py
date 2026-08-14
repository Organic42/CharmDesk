"""Prepares the user-supplied kawaii ghost artwork as a third CharmDesk charm variant.
Source has a flattened near-white checker background baked into the pixels (no real alpha),
so remove it via border-connected flood fill on brightness+saturation, then crop off the
artist's own chain the same way the gilded-eye charm was processed.
"""
import os
import numpy as np
from PIL import Image
from scipy import ndimage

SRC = r"C:\Users\ADMIN\Downloads\ChatGPT Image Aug 14, 2026, 07_04_16 PM.png"
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "charms", "cute-ghost")
OUT_DIR = os.path.abspath(OUT_DIR)

img = Image.open(SRC).convert("RGBA")
arr = np.array(img)
rgb = arr[:, :, :3].astype(int)

brightness = rgb.mean(axis=2)
saturation = rgb.max(axis=2) - rgb.min(axis=2)
bg_like = (brightness > 222) & (saturation < 22)

labels, n = ndimage.label(bg_like, structure=np.ones((3, 3)))
border_labels = set(labels[0, :]) | set(labels[-1, :]) | set(labels[:, 0]) | set(labels[:, -1])
border_labels.discard(0)

bg_mask = np.isin(labels, list(border_labels))
arr[:, :, 3] = np.where(bg_mask, 0, 255)

out = Image.fromarray(arr, "RGBA")
bbox = out.getbbox()
out = out.crop(bbox)
print(f"after bg removal: {out.size}")

# Crop off the artist's own beaded chain/clasp at the very top, same convention as the other
# charms - CharmDesk draws its own connecting string down to the top of this image.
a = np.array(out)[:, :, 3]
mask = a > 10
rows_with_content = np.where(mask.any(axis=1))[0]
widths = []
for y in range(rows_with_content.min(), rows_with_content.max() + 1):
    xs = np.where(mask[y])[0]
    widths.append((y, xs.max() - xs.min() if len(xs) else 0))

# Find the first row where content width jumps to "wide" (the ghost's head), i.e. leaves the
# narrow chain behind. Use a width threshold well above the chain's bead width.
chain_end_y = rows_with_content.min()
for y, w in widths:
    if w > 90:
        chain_end_y = max(rows_with_content.min(), y - 20)
        break

cropped = out.crop((0, chain_end_y, out.width, out.height))
bbox2 = cropped.getbbox()
pad = 6
x0 = max(0, bbox2[0] - pad)
y0 = max(0, bbox2[1] - pad)
x1 = min(cropped.width, bbox2[2] + pad)
y1 = min(cropped.height, bbox2[3] + pad)
final = cropped.crop((x0, y0, x1, y1))

os.makedirs(OUT_DIR, exist_ok=True)
final.save(os.path.join(OUT_DIR, "charm.png"))
print(f"charm.png -> {final.size}")

thumb = final.copy()
thumb.thumbnail((final.width, final.width), Image.NEAREST)
final.save(os.path.join(OUT_DIR, "thumbnail.png"))
print(f"thumbnail.png -> {final.size}")
