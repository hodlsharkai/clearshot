"""Draws assets/clearshot.ico: a dark rounded square with viewfinder corners and one accent dot.
Each size is drawn separately so the small tray sizes stay crisp."""
from PIL import Image, ImageDraw

BG = (15, 23, 42, 255)
FG = (241, 245, 249, 255)
ACCENT = (56, 189, 248, 255)
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

def draw(size):
    ss = 8  # supersample, then downscale
    s = size * ss
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=int(s * 0.22), fill=BG)
    m = s * 0.22           # inset of the corners
    L = s * 0.2            # corner arm length
    w = max(ss * 1.6, s * 0.075)
    for cx, cy, dx, dy in [(m, m, 1, 1), (s - m, m, -1, 1), (m, s - m, 1, -1), (s - m, s - m, -1, -1)]:
        d.rectangle([min(cx, cx + dx * L), min(cy, cy + dy * w), max(cx, cx + dx * L), max(cy, cy + dy * w)], fill=FG)
        d.rectangle([min(cx, cx + dx * w), min(cy, cy + dy * L), max(cx, cx + dx * w), max(cy, cy + dy * L)], fill=FG)
    r = s * 0.11
    d.ellipse([s / 2 - r, s / 2 - r, s / 2 + r, s / 2 + r], fill=ACCENT)
    return img.resize((size, size), Image.LANCZOS)

images = [draw(n) for n in SIZES]
images[-1].save("assets/clearshot.ico", sizes=[(n, n) for n in SIZES], append_images=images[:-1])
images[-1].save("assets/clearshot-256.png")
print("wrote assets/clearshot.ico")
