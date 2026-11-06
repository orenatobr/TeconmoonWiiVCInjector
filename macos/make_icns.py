"""Convert an .ico to .icns: python3 make_icns.py in.ico out.icns (needs Pillow, sips-free, uses iconutil)."""
import os, subprocess, sys, tempfile
from PIL import Image

src, out = sys.argv[1:3]
im = Image.open(src).convert("RGBA")
side = max(im.size)
sq = Image.new("RGBA", (side, side), (0, 0, 0, 0))
sq.paste(im, ((side - im.width) // 2, (side - im.height) // 2))
with tempfile.TemporaryDirectory() as t:
    s = os.path.join(t, "i.iconset")
    os.mkdir(s)
    for n in (16, 32, 128, 256, 512):
        sq.resize((n, n), Image.LANCZOS).save(f"{s}/icon_{n}x{n}.png")
        sq.resize((n * 2, n * 2), Image.LANCZOS).save(f"{s}/icon_{n}x{n}@2x.png")
    subprocess.run(["iconutil", "-c", "icns", s, "-o", out], check=True)
