"""Generates the Inno Setup wizard bitmaps from Assets/coinpilot.png. Run: python tools/make_wizard_images.py"""
from PIL import Image, ImageDraw
import os

here = os.path.dirname(os.path.abspath(__file__))
icon = Image.open(os.path.join(here, "..", "src", "CoinPilot.Desktop", "Assets", "coinpilot.png")).convert("RGBA")
out = os.path.join(here, "..", "installer")
os.makedirs(out, exist_ok=True)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


for scale in (1, 2):
    w, h = 164 * scale, 314 * scale
    img = Image.new("RGB", (w, h))
    d = ImageDraw.Draw(img)
    for y in range(h):
        d.line([(0, y), (w, y)], fill=lerp((36, 56, 47), (14, 22, 19), y / h))
    s = 112 * scale
    ic = icon.resize((s, s), Image.LANCZOS)
    img.paste(ic, ((w - s) // 2, int(h * 0.30)), ic)
    img.save(os.path.join(out, f"wizard-large-{scale}.bmp"))

for size in (55, 110):
    img = Image.new("RGB", (size, size), (255, 255, 255))
    ic = icon.resize((size, size), Image.LANCZOS)
    img.paste(ic, (0, 0), ic)
    img.save(os.path.join(out, f"wizard-small-{size}.bmp"))
print("ok")
