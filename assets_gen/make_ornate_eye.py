"""Prepares the user-supplied "gilded nazar" artwork as a second CharmDesk charm variant:
crops off the artist's own drawn rope/chain (CharmWindow renders its own connecting string),
keeping just the bow + pendant + dangles, and writes charm.png + thumbnail.png.
"""
import os
from PIL import Image

SRC = r"C:\Users\ADMIN\Downloads\ChatGPT Image Aug 14, 2026, 06_55_17 PM.png"
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "charms", "evil-eye-ornate")
OUT_DIR = os.path.abspath(OUT_DIR)

img = Image.open(SRC).convert("RGBA")

# The image already has real alpha. The top ~490px is the artist's own rope + a small bead +
# gold links; crop that off so CharmDesk's own string attaches right at the bow, same
# convention as the placeholder evil-eye charm's small gold loop.
CROP_TOP = 486
left, top, right, bottom = 0, CROP_TOP, img.width, img.height
cropped = img.crop((left, top, right, bottom))
bbox = cropped.getbbox()
pad = 6
x0 = max(0, bbox[0] - pad)
y0 = max(0, bbox[1] - pad)
x1 = min(cropped.width, bbox[2] + pad)
y1 = min(cropped.height, bbox[3] + pad)
final = cropped.crop((x0, y0, x1, y1))

os.makedirs(OUT_DIR, exist_ok=True)
final.save(os.path.join(OUT_DIR, "charm.png"))
print(f"charm.png -> {final.size}")

# Thumbnail: tighter crop on just the bow+eye (exclude the three bottom dangles) for a
# cleaner square-ish library card icon.
thumb_bottom = min(final.height, 660)
thumb = final.crop((0, 0, final.width, thumb_bottom))
thumb.save(os.path.join(OUT_DIR, "thumbnail.png"))
print(f"thumbnail.png -> {thumb.size}")
