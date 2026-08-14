"""Generates the MSIX/Store icon set from the app's own pegboard palette + Evil Eye charm art,
so the package icon actually looks like the product instead of a generic placeholder.
"""
import os
from PIL import Image

ROOT = os.path.join(os.path.dirname(__file__), "..")
CHARM = os.path.join(ROOT, "charms", "evil-eye", "charm.png")
OUT_DIR = os.path.join(ROOT, "packaging", "Assets")

BG = (243, 238, 250, 255)      # matches the app's PegboardBrush base (#F3EEFA)
DOT = (227, 214, 246, 255)

# (filename, canvas W, H, charm-height as fraction of min(W,H))
TARGETS = [
    ("Square44x44Logo.png", 44, 44, 0.72),
    ("Square71x71Logo.png", 71, 71, 0.72),
    ("Square150x150Logo.png", 150, 150, 0.72),
    ("Square310x310Logo.png", 310, 310, 0.72),
    ("Wide310x150Logo.png", 310, 150, 0.62),
    ("StoreLogo.png", 50, 50, 0.72),
    ("SplashScreen.png", 620, 300, 0.5),
    ("LockScreenLogo.png", 24, 24, 0.8),
]


def render(path, w, h, charm_frac):
    scale = 4  # supersample
    img = Image.new("RGBA", (w * scale, h * scale), BG)
    # subtle dot texture, matching the app's pegboard background
    pitch = 22 * scale
    from PIL import ImageDraw
    draw = ImageDraw.Draw(img)
    r = 1.3 * scale
    for y in range(0, h * scale + pitch, pitch):
        for x in range(0, w * scale + pitch, pitch):
            draw.ellipse([x - r, y - r, x + r, y + r], fill=DOT)

    charm = Image.open(CHARM).convert("RGBA")
    target_h = int(min(w, h) * charm_frac * scale)
    ratio = target_h / charm.height
    charm = charm.resize((max(1, int(charm.width * ratio)), target_h), Image.NEAREST)
    cx = (w * scale - charm.width) // 2
    cy = (h * scale - charm.height) // 2
    img.alpha_composite(charm, (cx, cy))

    img = img.resize((w, h), Image.LANCZOS)
    img.save(path)
    print(f"wrote {path} ({w}x{h})")


if __name__ == "__main__":
    os.makedirs(OUT_DIR, exist_ok=True)
    for name, w, h, frac in TARGETS:
        render(os.path.join(OUT_DIR, name), w, h, frac)
