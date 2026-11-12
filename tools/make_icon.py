"""Generates the CoinPilot icon: src/CoinPilot.Desktop/Assets/coinpilot.ico + .png and assets/coinpilot-256.png.
Run: python tools/make_icon.py   (needs Pillow)

A gold coin seen slightly from above: visible edge thickness, a reeded (ridged) rim, a raised inner
ring, and the CoinPilot heading arrow stamped in the middle.
"""
from PIL import Image, ImageDraw, ImageFilter, ImageChops
import math, os

S = 1024
here = os.path.dirname(os.path.abspath(__file__))


def lerp(a, b, t):
    t = max(0.0, min(1.0, t))
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


def radial(size, center, radius, inner, outer):
    """Radial gradient image (RGBA) from `inner` at `center` to `outer` at `radius`."""
    img = Image.new("RGBA", (size, size))
    px = img.load()
    cx, cy = center
    for y in range(size):
        for x in range(size):
            d = math.hypot(x - cx, y - cy) / radius
            px[x, y] = lerp(inner, outer, d)
    return img


def disc_mask(box):
    m = Image.new("L", (S, S), 0)
    ImageDraw.Draw(m).ellipse(box, fill=255)
    return m


img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
cx, R = S / 2, 430          # coin face radius
face_cy = S / 2 - 22        # face sits a little high...
edge = 44                   # ...so the coin's thickness shows below it

# Soft drop shadow
shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(shadow).ellipse([cx - R + 20, face_cy + edge + 30 - R, cx + R - 20, face_cy + edge + 30 + R], fill=(0, 0, 0, 120))
img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(28)))

# Coin edge (thickness): darker gold, with vertical reeding lines
edge_layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ed = ImageDraw.Draw(edge_layer)
ed.ellipse([cx - R, face_cy + edge - R, cx + R, face_cy + edge + R], fill=(150, 98, 18, 255))
ed.rectangle([cx - R, face_cy, cx + R, face_cy + edge], fill=(150, 98, 18, 255))
for i in range(0, 2 * int(R), 16):
    x = cx - R + i
    ed.line([(x, face_cy), (x, face_cy + edge + R)], fill=(122, 78, 12, 255), width=5)
edge_shape = disc_mask([cx - R, face_cy + edge - R, cx + R, face_cy + edge + R])
ImageDraw.Draw(edge_shape).rectangle([cx - R, face_cy, cx + R, face_cy + edge], fill=255)
img.paste(edge_layer, (0, 0), edge_shape)

# Coin face: warm gold, lit from the top left
face = radial(S, (cx - 160, face_cy - 180), R * 1.9, (255, 224, 120, 255), (196, 132, 26, 255))
img.paste(face, (0, 0), disc_mask([cx - R, face_cy - R, cx + R, face_cy + R]))

d = ImageDraw.Draw(img)
# Reeded rim: short radial ridges around the outside of the face
for k in range(120):
    a = 2 * math.pi * k / 120
    r1, r2 = R - 4, R - 34
    x1, y1 = cx + r1 * math.cos(a), face_cy + r1 * math.sin(a)
    x2, y2 = cx + r2 * math.cos(a), face_cy + r2 * math.sin(a)
    shade = lerp((255, 238, 170, 255), (170, 110, 20, 255), (math.sin(a + math.pi / 4) + 1) / 2)
    d.line([(x1, y1), (x2, y2)], fill=shade, width=7)

# Raised inner ring: dark groove + bright lip
ri = R - 70
d.ellipse([cx - ri - 6, face_cy - ri - 6, cx + ri + 6, face_cy + ri + 6], outline=(150, 96, 16, 255), width=12)
d.ellipse([cx - ri + 6, face_cy - ri + 6, cx + ri - 6, face_cy + ri - 6], outline=(255, 236, 160, 255), width=8)

# Inner field slightly darker than the rim area, so the stamp reads as raised
field = radial(S, (cx - 120, face_cy - 140), ri * 1.8, (247, 205, 96, 255), (190, 126, 24, 255))
img.paste(field, (0, 0), disc_mask([cx - ri + 10, face_cy - ri + 10, cx + ri - 10, face_cy + ri - 10]))

# Stamped heading arrow (the "pilot" mark): shadow, then two-tone gold for an embossed look
top = (cx, face_cy - 215)
left = (cx - 150, face_cy + 165)
right = (cx + 150, face_cy + 165)
notch = (cx, face_cy + 85)
off = 10
stamp_shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(stamp_shadow).polygon([(p[0] + off, p[1] + off) for p in (top, right, notch, left)], fill=(120, 74, 8, 200))
img.alpha_composite(stamp_shadow.filter(ImageFilter.GaussianBlur(6)))
d = ImageDraw.Draw(img)
d.polygon([top, left, notch], fill=(255, 241, 190, 255))   # lit side
d.polygon([top, right, notch], fill=(222, 160, 44, 255))   # shaded side

# Glossy highlight across the top left of the face
gloss = Image.new("L", (S, S), 0)
ImageDraw.Draw(gloss).ellipse([cx - R + 40, face_cy - R + 30, cx + R * 0.35, face_cy - R * 0.05], fill=70)
gloss = ImageChops.multiply(gloss.filter(ImageFilter.GaussianBlur(40)), disc_mask([cx - R, face_cy - R, cx + R, face_cy + R]))
img.paste(Image.new("RGBA", (S, S), (255, 255, 255, 255)), (0, 0), gloss)

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
