"""
Regenerate the extension's icons (original artwork). Requires Pillow:  pip install pillow
Usage (from the repo root):  python tools/make_icons.py

Writes:
  ClaudeUsageDock/Assets/gauge-*.png          status gauge (theme-aware + alert colors + sign-in)
  ClaudeUsageDock/Assets/Rings/ring-<level>-<pct>.png   303 ring gauges: ok|warn|crit x 0..100 %
  ClaudeUsageDock/Assets/*Logo*.png, SplashScreen      MSIX package logos
The ring track is a translucent grey so one image works on light and dark Docks.
Colors match the design canvas: ok #4CC2FF, warn #FFB547, crit #FF6F61.
"""
import math
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "ClaudeUsageDock" / "Assets"
RINGS = ASSETS / "Rings"
SS = 8  # supersampling

LEVELS = {"ok": "4CC2FF", "warn": "FFB547", "crit": "FF6F61"}


def rgba(h, a=255):
    h = h.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


def ring(size, pct, color):
    big = size * SS
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    stroke = big * 0.135
    r = big / 2 - stroke / 2 - big * 0.06
    c = big / 2
    box = [c - r - stroke / 2, c - r - stroke / 2, c + r + stroke / 2, c + r + stroke / 2]
    d.ellipse(box, outline=rgba("808080", 120), width=int(round(stroke)))  # track
    if pct >= 100:
        d.ellipse(box, outline=color, width=int(round(stroke)))
    elif pct > 0:
        start, end = -90, -90 + 360 * pct / 100
        d.arc(box, start=start, end=end, fill=color, width=int(round(stroke)))
        for ang in (start, end):  # round caps
            a = math.radians(ang)
            x, y = c + r * math.cos(a), c + r * math.sin(a)
            h = stroke / 2
            d.ellipse([x - h, y - h, x + h, y + h], fill=color)
    return img.resize((size, size), Image.LANCZOS)


def gauge(size, color, badge=None, pad=0.0):
    big = size * SS
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    u = big / 16.0
    k = (16 - 2 * pad) / 16.0

    def P(x, y):
        return ((pad + x * k) * u, (pad + y * k) * u)

    stroke = 1.7 * k * u
    cx, cy, r = 8.0, 10.6, 5.9
    x0, y0 = P(cx - r, cy - r)
    x1, y1 = P(cx + r, cy + r)
    d.arc([x0, y0, x1, y1], start=180, end=360, fill=color, width=int(round(stroke)))
    for ex in (cx - r, cx + r):
        px, py = P(ex, cy)
        h = stroke / 2
        d.ellipse([px - h, py - h, px + h, py + h], fill=color)
    nx, ny = P(cx, cy)
    tx, ty = P(cx + 3.3, cy - 4.0)
    d.line([nx, ny, tx, ty], fill=color, width=int(round(stroke)))
    h = stroke / 2
    d.ellipse([tx - h, ty - h, tx + h, ty + h], fill=color)
    hr = 1.25 * k * u
    d.ellipse([nx - hr, ny - hr, nx + hr, ny + hr], fill=color)
    if badge:
        bx, by = P(13.1, 3.4)
        br = 2.9 * k * u
        d.ellipse([bx - br, by - br, bx + br, by + br], fill=badge)
        ink = (20, 20, 20, 255)
        w = 0.9 * k * u
        d.line([bx, by - 1.55 * k * u, bx, by + 0.35 * k * u], fill=ink, width=int(round(w)))
        dr = 0.55 * k * u
        d.ellipse([bx - dr, by + 1.15 * k * u - dr, bx + dr, by + 1.15 * k * u + dr], fill=ink)
    return img.resize((size, size), Image.LANCZOS)


def tile(w, h, name, frac=0.6):
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    g = int(min(w, h) * frac)
    img.alpha_composite(gauge(g, rgba("4CC2FF")), ((w - g) // 2, (h - g) // 2))
    img.save(ASSETS / name, optimize=True)


def main():
    RINGS.mkdir(parents=True, exist_ok=True)
    for level, hexc in LEVELS.items():
        for pct in range(101):
            ring(64, pct, rgba(hexc)).save(RINGS / f"ring-{level}-{pct:03d}.png", optimize=True)

    gauge(64, rgba("1A1A1A")).save(ASSETS / "gauge-for-light-theme.png", optimize=True)
    gauge(64, rgba("F3F3F3")).save(ASSETS / "gauge-for-dark-theme.png", optimize=True)
    gauge(64, rgba("FFB547")).save(ASSETS / "gauge-warn.png", optimize=True)
    gauge(64, rgba("FF6F61")).save(ASSETS / "gauge-crit.png", optimize=True)
    gauge(64, rgba("ABABAB"), badge=rgba("FFB547")).save(ASSETS / "gauge-signin.png", optimize=True)

    tile(300, 300, "Square150x150Logo.scale-200.png")
    tile(88, 88, "Square44x44Logo.scale-200.png", 0.8)
    tile(24, 24, "Square44x44Logo.targetsize-24_altform-unplated.png", 1.0)
    tile(50, 50, "StoreLogo.png", 0.9)
    tile(48, 48, "LockScreenLogo.scale-200.png", 0.9)
    tile(620, 300, "Wide310x150Logo.scale-200.png")
    tile(1240, 600, "SplashScreen.scale-200.png", 0.4)
    print("icons written to", ASSETS)


if __name__ == "__main__":
    main()
