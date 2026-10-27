"""Crop the generated badge, round its corners onto transparency, and write app.ico."""
import sys
from PIL import Image, ImageDraw

src, dst = sys.argv[1], sys.argv[2]
img = Image.open(src).convert("RGBA")
box = (128, 118, 898, 902)  # badge bounds in the 1024px render
badge = img.crop(box)
side = max(badge.size)
canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
mask = Image.new("L", badge.size, 0)
ImageDraw.Draw(mask).rounded_rectangle((0, 0, badge.size[0] - 1, badge.size[1] - 1), radius=150, fill=255)
canvas.paste(badge, ((side - badge.size[0]) // 2, (side - badge.size[1]) // 2), mask)
canvas = canvas.resize((256, 256), Image.LANCZOS)
canvas.save(dst, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
canvas.save(dst.replace(".ico", ".png"))
