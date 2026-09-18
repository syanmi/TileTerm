# -*- coding: utf-8 -*-
"""Regenerates the app icon assets from the design's parameters (requires Pillow: pip install pillow).

Design ("gradient orb, split into three panes"): an indigo disc with four blurred color blobs
(cyan / violet / pink / orange), cut into three unequal panes by two thin transparent lines.

Outputs (next to the C# project, referenced by TileTerm.csproj / MainWindow.xaml / Icons.cs):
  src/TileTerm/Assets/TileTerm.ico           multi-size icon for the exe, taskbar and Alt+Tab
  src/TileTerm/Assets/TileTerm-titlebar.png  96px bitmap for the custom title bar (shown at ~18 DIP)

The cut lines are drawn slightly wider for small sizes so they stay visible at 16-32px.
"""
import io
import os
import struct
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ASSETS = Path(__file__).resolve().parent.parent / "src" / "TileTerm" / "Assets"

S = 16   # supersampling: px per design unit (the design lives in a 64-unit square)
M = 20   # margin in units so the blur is not cut off at the canvas edge
BASE = (0x4F, 0x46, 0xE5)
BLOBS = [((20, 18), 20, (0x22, 0xD3, 0xEE)),
         ((48, 22), 18, (0xA7, 0x8B, 0xFA)),
         ((26, 52), 22, (0xF4, 0x72, 0xB6)),
         ((52, 50), 15, (0xFB, 0x92, 0x3C))]
SIZES = [16, 24, 32, 48, 64, 128, 256]


def color_layer():
    n = (64 + 2 * M) * S
    img = Image.new("RGB", (n, n), BASE)
    d = ImageDraw.Draw(img)
    for (cx, cy), r, col in BLOBS:
        x, y, rr = (cx + M) * S, (cy + M) * S, r * S
        d.ellipse((x - rr, y - rr, x + rr, y + rr), fill=col)
    img = img.filter(ImageFilter.GaussianBlur(7 * S))
    return img.crop((M * S, M * S, (M + 64) * S, (M + 64) * S))


def alpha_mask(cut_width):
    n = 64 * S
    m = Image.new("L", (n, n), 0)
    d = ImageDraw.Draw(m)
    d.ellipse((2 * S, 2 * S, 62 * S, 62 * S), fill=255)
    w = max(1, int(round(cut_width * S)))
    d.line(((36 * S, -2 * S), (30 * S, 66 * S)), fill=0, width=w)
    d.line(((33 * S, 40 * S), (66 * S, 34 * S)), fill=0, width=w)
    return m


def cut_for(size):
    if size <= 24:
        return 4.2
    if size <= 32:
        return 3.6
    if size <= 48:
        return 3.0
    return 2.6


def render(size, color, cut=None):
    img = color.convert("RGBA")
    img.putalpha(alpha_mask(cut if cut is not None else cut_for(size)))
    return img.resize((size, size), Image.LANCZOS)


def build_ico(frames):
    blobs = []
    for size, im in frames:
        buf = io.BytesIO()
        im.save(buf, format="PNG")
        blobs.append((size, buf.getvalue()))
    head = struct.pack("<HHH", 0, 1, len(blobs))
    offset = 6 + 16 * len(blobs)
    entries, data = b"", b""
    for size, png in blobs:
        wh = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", wh, wh, 0, 0, 1, 32, len(png), offset)
        data += png
        offset += len(png)
    return head + entries + data


if __name__ == "__main__":
    color = color_layer()
    os.makedirs(ASSETS, exist_ok=True)
    (ASSETS / "TileTerm.ico").write_bytes(build_ico([(s, render(s, color)) for s in SIZES]))
    render(96, color, 3.4).save(ASSETS / "TileTerm-titlebar.png", format="PNG")
    print("wrote", ASSETS)
