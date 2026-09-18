# -*- coding: utf-8 -*-
"""Renders the app icon candidates and (optionally) switches the adopted icon. Requires Pillow.

    python scripts/make_icon.py              regenerate every candidate under src/TileTerm/IconCandidates/<ID>/
    python scripts/make_icon.py --use S2-c   render candidate S2-c into src/TileTerm/Assets/ (the adopted icon)

Candidates (IDs match the design study "TileTerm アイコン案"):
  S5-a..f  "gradient orb": a base disc plus four blurred color blobs, cut into three panes by two thin lines
  S2-a..f  "sliced disc":  a gradient disc cut by parallel diagonal lines, each slice nudged along the cut

Each candidate folder holds the two files the app uses, already named as in Assets/ so switching is a plain copy:
  TileTerm.ico           multi-size icon for the exe, taskbar and Alt+Tab (TileTerm.csproj: ApplicationIcon)
  TileTerm-titlebar.png  96px bitmap for the custom title bar, shown at ~18 DIP (Icons.App())

Cuts/gaps are drawn slightly wider for small sizes so they stay visible at 16-32px.
The adopted icon is S5-a; changing it is: run with --use <ID> (or copy the two files), then rebuild the app.
"""
import io
import os
import struct
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "src" / "TileTerm" / "Assets"
CANDIDATES = ROOT / "src" / "TileTerm" / "IconCandidates"

S = 16   # supersampling: px per design unit (the design lives in a 64-unit square)
M = 20   # margin in units so the blur is not cut off at the canvas edge
SIZES = [16, 24, 32, 48, 64, 128, 256]
TITLEBAR_SIZE = 96
TITLEBAR_CUT = 3.4


def rgb(hexstr):
    h = hexstr.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


# --- S5: gradient orb ---------------------------------------------------------------------------
S5_BLOBS = [((20, 18), 20), ((48, 22), 18), ((26, 52), 22), ((52, 50), 15)]
S5 = {
    "a": ("#4F46E5", ["#22D3EE", "#A78BFA", "#F472B6", "#FB923C"]),   # indigo multi-color (adopted)
    "b": ("#1E40AF", ["#22D3EE", "#60A5FA", "#34D399", "#A7F3D0"]),   # ocean
    "c": ("#B91C1C", ["#FBBF24", "#FB923C", "#F472B6", "#FDE68A"]),   # sunset
    "d": ("#065F46", ["#34D399", "#22D3EE", "#A78BFA", "#BEF264"]),   # aurora
    "e": ("#374151", ["#E5E7EB", "#9CA3AF", "#F9FAFB", "#6B7280"]),   # monotone
    "f": ("#581C87", ["#F472B6", "#C084FC", "#FB7185", "#FDBA74"]),   # berry
}


def s5_color_layer(variant):
    base, blob_colors = S5[variant]
    n = (64 + 2 * M) * S
    img = Image.new("RGB", (n, n), rgb(base))
    d = ImageDraw.Draw(img)
    for ((cx, cy), r), col in zip(S5_BLOBS, blob_colors):
        x, y, rr = (cx + M) * S, (cy + M) * S, r * S
        d.ellipse((x - rr, y - rr, x + rr, y + rr), fill=rgb(col))
    img = img.filter(ImageFilter.GaussianBlur(7 * S))
    return img.crop((M * S, M * S, (M + 64) * S, (M + 64) * S))


def s5_mask(cut_width):
    n = 64 * S
    m = Image.new("L", (n, n), 0)
    d = ImageDraw.Draw(m)
    d.ellipse((2 * S, 2 * S, 62 * S, 62 * S), fill=255)
    w = max(1, int(round(cut_width * S)))
    d.line(((36 * S, -2 * S), (30 * S, 66 * S)), fill=0, width=w)
    d.line(((33 * S, 40 * S), (66 * S, 34 * S)), fill=0, width=w)
    return m


# --- S2: sliced disc ----------------------------------------------------------------------------
S2 = {
    "a": ["#1E1B4B", "#6D28D9", "#DB2777", "#FB923C"],   # indigo -> orange
    "b": ["#0B1F4B", "#1D4ED8", "#06B6D4", "#A7F3D0"],   # ocean
    "c": ["#7F1D1D", "#DC2626", "#F97316", "#FDE68A"],   # sunset
    "d": ["#052E2B", "#047857", "#10B981", "#BEF264"],   # emerald
    "e": ["#111827", "#4B5563", "#9CA3AF", "#F3F4F6"],   # monotone
    "f": ["#4A044E", "#BE185D", "#FB7185", "#FED7AA"],   # rose
}
S2_STOPS = [0.0, 0.35, 0.7, 1.0]
S2_SHIFTS = [(-2.5, 2.5), (2.0, -2.0), (-1.0, 1.0)]   # along the cut direction (1, -1)


def s2_gradient(variant):
    """Diagonal gradient from (8,8) to (56,56) in design units (colors constant along x+y)."""
    stops = [rgb(c) for c in S2[variant]]

    def at(t):
        t = min(1.0, max(0.0, t))
        for i in range(3):
            if t <= S2_STOPS[i + 1]:
                u = (t - S2_STOPS[i]) / (S2_STOPS[i + 1] - S2_STOPS[i])
                return tuple(int(round(a + (b - a) * u)) for a, b in zip(stops[i], stops[i + 1]))
        return stops[-1]

    n = 64 * S
    img = Image.new("RGB", (n, n))
    d = ImageDraw.Draw(img)
    for k in range(2 * n):                     # k = (x + y) in supersampled pixels
        t = (k / S - 16) / 96.0                # (x-8)+(y-8) over 96 units
        d.line(((k, 0), (0, k)), fill=at(t), width=2)
    return img


def s2_slices(gap):
    """(mask, shift) per slice: the disc cut into three bands of x+y, separated by `gap` units."""
    n = 64 * S
    circle = Image.new("L", (n, n), 0)
    ImageDraw.Draw(circle).ellipse((3 * S, 3 * S, 61 * S, 61 * S), fill=255)
    edges = [(0, 52 - gap / 2), (52 + gap / 2, 78 - gap / 2), (78 + gap / 2, 140)]
    out = []
    for (a, b), shift in zip(edges, S2_SHIFTS):
        band = Image.new("L", (n, n), 0)
        pts = [(-64, a + 64), (128, a - 128), (128, b - 128), (-64, b + 64)]
        ImageDraw.Draw(band).polygon([(x * S, y * S) for x, y in pts], fill=255)
        out.append((ImageChops.multiply(circle, band), shift))
    return out


# --- shared -------------------------------------------------------------------------------------
def cut_for(size):
    if size <= 24:
        return 4.2
    if size <= 32:
        return 3.6
    if size <= 48:
        return 3.0
    return 2.6


class Candidate:
    def __init__(self, cid):
        self.id = cid
        self.kind, self.variant = cid.split("-")
        self._layer = s5_color_layer(self.variant) if self.kind == "S5" else s2_gradient(self.variant)

    def render(self, size, cut=None):
        cut = cut_for(size) if cut is None else cut
        if self.kind == "S5":
            img = self._layer.convert("RGBA")
            img.putalpha(s5_mask(cut))
        else:
            n = 64 * S
            img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
            # A slice is moved together with its gradient (source pixel q lands at q + shift), so the
            # colors run on continuously across the cuts only up to the small nudge - that is the effect.
            for mask, (dx, dy) in s2_slices(4.0 * cut / 2.6):   # 4.0 units of gap at the base cut width
                img.paste(self._layer, (int(round(dx * S)), int(round(dy * S))), mask)
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


def write_candidate(cand, folder):
    os.makedirs(folder, exist_ok=True)
    (folder / "TileTerm.ico").write_bytes(build_ico([(s, cand.render(s)) for s in SIZES]))
    cand.render(TITLEBAR_SIZE, TITLEBAR_CUT).save(folder / "TileTerm-titlebar.png", format="PNG")


def all_ids():
    return [f"S5-{v}" for v in S5] + [f"S2-{v}" for v in S2]


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "--use":
        cid = sys.argv[2]
        if cid not in all_ids():
            sys.exit(f"unknown candidate {cid!r}; choose from: {', '.join(all_ids())}")
        write_candidate(Candidate(cid), ASSETS)
        print(f"wrote {ASSETS} from {cid} - rebuild the app to pick it up")
    elif len(sys.argv) == 1:
        for cid in all_ids():
            write_candidate(Candidate(cid), CANDIDATES / cid)
            print("wrote", CANDIDATES / cid)
    else:
        sys.exit(__doc__)
