# Copyright (c) 2026 Marlon Andrés Guerrero Meriño
# SPDX-License-Identifier: GPL-3.0-only
"""Generates src/RemoteClipboard.App/Assets/app.ico (multi-resolution), Assets/icon.png (in-app logo)
and docs/images/icon.png.

Usage: python tools/generate-icon.py   (requires Pillow)
Design: blue gradient tile, white clipboard, blue two-way arrow (synchronization).
"""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "src" / "RemoteClipboard.App" / "Assets"
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
S = 1024  # master canvas, downscaled for every size

TOP = (59, 130, 246)     # #3B82F6
BOTTOM = (29, 78, 216)   # #1D4ED8
WHITE = (255, 255, 255, 255)


def master() -> Image.Image:
    # Gradient tile with rounded corners.
    gradient = Image.new("RGBA", (S, S))
    draw = ImageDraw.Draw(gradient)
    for y in range(S):
        t = y / (S - 1)
        color = tuple(round(TOP[i] + (BOTTOM[i] - TOP[i]) * t) for i in range(3)) + (255,)
        draw.line([(0, y), (S, y)], fill=color)
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([24, 24, S - 24, S - 24], radius=220, fill=255)
    tile = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    tile.paste(gradient, (0, 0), mask)

    d = ImageDraw.Draw(tile)
    # Clipboard board and clip.
    d.rounded_rectangle([272, 236, 752, 836], radius=64, fill=WHITE)
    d.rounded_rectangle([392, 168, 632, 316], radius=48, fill=WHITE)
    d.rounded_rectangle([432, 208, 592, 276], radius=26, fill=BOTTOM + (255,))

    # Two-way arrow across the board.
    y, x0, x1, w, head = 540, 360, 664, 44, 92
    d.rectangle([x0 + head - 10, y - w // 2, x1 - head + 10, y + w // 2], fill=BOTTOM + (255,))
    d.polygon([(x0, y), (x0 + head, y - head + 6), (x0 + head, y + head - 6)], fill=BOTTOM + (255,))
    d.polygon([(x1, y), (x1 - head, y - head + 6), (x1 - head, y + head - 6)], fill=BOTTOM + (255,))
    # Two text lines below (a "document" on the clipboard).
    d.rounded_rectangle([360, 680, 664, 716], radius=18, fill=TOP + (255,))
    d.rounded_rectangle([360, 748, 560, 784], radius=18, fill=TOP + (255,))
    return tile


def main() -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    image = master()
    (ROOT / "docs" / "images").mkdir(parents=True, exist_ok=True)
    image.resize((256, 256), Image.LANCZOS).save(ROOT / "docs" / "images" / "icon.png")
    image.resize((256, 256), Image.LANCZOS).save(OUT_DIR / "icon.png")
    image.save(OUT_DIR / "app.ico", sizes=[(s, s) for s in SIZES])
    print(f"Wrote {OUT_DIR / 'app.ico'}")


if __name__ == "__main__":
    main()
