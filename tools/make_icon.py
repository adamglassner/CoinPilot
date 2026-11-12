"""Generates the CoinPilot icon: src/CoinPilot.Desktop/Assets/coinpilot.ico + .png and assets/coinpilot-256.png.
Run: python tools/make_icon.py   (needs Pillow)

A coin seen face-on (green ring) with a pilot's heading arrow pointing up, on a dark ink-green tile.
"""
from PIL import Image, ImageDraw, ImageFilter
import os

S = 1024
here = os.path.dirname(os.path.abspath(__file__))


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

# Rounded-square tile with a subtle vertical gradient (ink green)
grad = Image.new("RGBA", (S, S))
gd = ImageDraw.Draw(grad)
for y in range(S):
    gd.line([(0, y), (S, y)], fill=lerp((36, 56, 47, 255), (16, 26, 22, 255), y / S))
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle([24, 24, S - 24, S - 24], radius=220, fill=255)
img.paste(grad, (0, 0), mask)

cx, cy, R = S / 2, S / 2 + 10, 330

# Soft glow behind the coin
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(glow).ellipse([cx - R - 10, cy - R - 10, cx + R + 10, cy + R + 10], fill=(95, 211, 155, 70))
glow = glow.filter(ImageFilter.GaussianBlur(40))
img.alpha_composite(glow)

d = ImageDraw.Draw(img)
# Coin: green disc, darker face, mint rim
d.ellipse([cx - R, cy - R, cx + R, cy + R], fill=(31, 107, 79, 255))
d.ellipse([cx - R + 46, cy - R + 46, cx + R - 46, cy + R - 46], fill=(22, 74, 55, 255))
d.ellipse([cx - R, cy - R, cx + R, cy + R], outline=(95, 211, 155, 255), width=30)

# Heading arrow (a stylised paper plane / compass needle) pointing up
top = (cx, cy - 215)
left = (cx - 150, cy + 165)
right = (cx + 150, cy + 165)
notch = (cx, cy + 85)
d.polygon([top, right, notch], fill=(233, 238, 233, 255))
d.polygon([top, left, notch], fill=(255, 255, 255, 255))

assets = os.path.join(here, "..", "src", "CoinPilot.Desktop", "Assets")
os.makedirs(assets, exist_ok=True)
img.resize((256, 256), Image.LANCZOS).save(os.path.join(assets, "coinpilot.png"))
img.save(os.path.join(assets, "coinpilot.ico"),
         sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])
root_assets = os.path.join(here, "..", "assets")
os.makedirs(root_assets, exist_ok=True)
img.resize((256, 256), Image.LANCZOS).save(os.path.join(root_assets, "coinpilot-256.png"))
img.resize((512, 512), Image.LANCZOS).save(os.path.join(root_assets, "coinpilot-512.png"))
print("ok")
