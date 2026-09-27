#!/usr/bin/env python3
"""Generates the STORM OS branding assets (all original artwork, no third-party logos).

Output (relative to storm-os/branding):
  logos/storm-mark.png            512x512   app/brand mark (rounded tile with the Storm bolt)
  logos/storm-logo.png            ...x400   mark + "STORM OS" wordmark for dark backgrounds, transparent
  logos/storm-logo-dark.png       ...x400   the same for light backgrounds
  logos/StormOS-oem-logo.bmp      120x120   OEM logo for Settings > System > About (24-bit BMP, as Windows requires)
  logos/StormOS-oobe-logo.png     240x100   OEM logo shown during Windows OOBE (oobe.xml)
  wallpapers/storm-dark.jpg       3840x2160 default wallpaper
  wallpapers/storm-light.jpg      3840x2160 light wallpaper
  wallpapers/storm-lockscreen.jpg 3840x2160 lock screen image

Usage: python3 generate-branding.py   (requires Pillow)
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parent.parent
CYAN = (63, 193, 255)
CYAN_DARK = (11, 132, 198)
NAVY = (15, 19, 27)
SURFACE = (22, 28, 40)
WHITE = (255, 255, 255)
MUTED = (143, 163, 191)

# The Storm bolt, normalized to a 1x1 box (x right, y down).
BOLT = [(0.60, 0.00), (0.18, 0.56), (0.46, 0.56), (0.36, 1.00), (0.84, 0.40), (0.54, 0.40), (0.72, 0.00)]

FONT_CANDIDATES = [
    "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
    "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
    "C:/Windows/Fonts/segoeuib.ttf",
    "C:/Windows/Fonts/arialbd.ttf",
]


def font(size):
    for candidate in FONT_CANDIDATES:
        if Path(candidate).exists():
            return ImageFont.truetype(candidate, size)
    return ImageFont.load_default()


def bolt_points(x, y, w, h):
    return [(x + px * w, y + py * h) for px, py in BOLT]


def vertical_gradient(size, top, bottom):
    strip = Image.new("RGB", (1, 256))
    for i in range(256):
        t = i / 255
        strip.putpixel((0, i), tuple(int(top[c] + (bottom[c] - top[c]) * t) for c in range(3)))
    return strip.resize(size, Image.BILINEAR)


def diagonal_gradient(size, start, end):
    small = Image.new("RGB", (64, 36))
    for x in range(64):
        for y in range(36):
            t = (x / 63) * 0.7 + (y / 35) * 0.3
            small.putpixel((x, y), tuple(int(start[c] + (end[c] - start[c]) * t) for c in range(3)))
    return small.resize(size, Image.BICUBIC)


def glow(size, points, color, radius, strength):
    """Soft light around a polygon: drawn small, blurred, scaled up (fast at 4K)."""
    scale = 8
    small = Image.new("L", (size[0] // scale, size[1] // scale), 0)
    ImageDraw.Draw(small).polygon([(x / scale, y / scale) for x, y in points], fill=255)
    small = small.filter(ImageFilter.GaussianBlur(radius / scale))
    mask = small.resize(size, Image.BICUBIC).point(lambda v: int(v * strength))
    layer = Image.new("RGB", size, color)
    return layer, mask


def draw_streaks(image, color, count, seed, alpha):
    """Thin diagonal storm streaks."""
    import random

    rng = random.Random(seed)
    w, h = image.size
    overlay = Image.new("RGBA", image.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    for _ in range(count):
        x = rng.uniform(-0.2, 1.1) * w
        y = rng.uniform(-0.1, 1.0) * h
        length = rng.uniform(0.08, 0.35) * w
        width = max(1, int(rng.uniform(1, 3) * w / 3840))
        a = int(alpha * rng.uniform(0.3, 1.0))
        draw.line([(x, y), (x + length, y - length * 0.42)], fill=color + (a,), width=width)
    overlay = overlay.filter(ImageFilter.GaussianBlur(w / 3840))
    image.paste(overlay, (0, 0), overlay)


def draw_wordmark(draw, x, y, size, primary, accent, tracking=0.08):
    f = font(size)
    cursor = x
    for i, ch in enumerate("STORM OS"):
        fill = accent if i >= 6 else primary
        draw.text((cursor, y), ch, font=f, fill=fill)
        cursor += draw.textlength(ch, font=f) + (size * tracking if ch != " " else size * 0.12)
    return cursor


def mark(size):
    """Rounded tile with the bolt, like the STORM OS app icon."""
    s = 4
    big = Image.new("RGBA", (size * s, size * s), (0, 0, 0, 0))
    draw = ImageDraw.Draw(big)
    draw.rounded_rectangle([0, 0, size * s - 1, size * s - 1], radius=int(size * s * 0.22), fill=SURFACE + (255,))
    pad = size * s * 0.2
    draw.polygon(bolt_points(pad, pad, size * s - 2 * pad, size * s - 2 * pad), fill=CYAN + (255,))
    return big.resize((size, size), Image.LANCZOS)


def wordmark_width(size, tracking=0.08):
    probe = ImageDraw.Draw(Image.new("RGB", (1, 1)))
    f = font(size)
    return sum(probe.textlength(ch, font=f) + (size * tracking if ch != " " else size * 0.12) for ch in "STORM OS")


def logo(height, dark_text=False):
    """Mark + wordmark; the canvas width follows the measured text so nothing is clipped."""
    s = 2
    text_size = int(height * s * 0.5)
    gap = int(height * s * 0.18)
    tile_size = int(height * s * 0.9)
    width = tile_size + gap + int(wordmark_width(text_size)) + int(height * s * 0.05)
    image = Image.new("RGBA", (width, height * s), (0, 0, 0, 0))
    tile = mark(tile_size)
    image.paste(tile, (0, (height * s - tile_size) // 2), tile)
    draw = ImageDraw.Draw(image)
    text = (20, 24, 32) if dark_text else WHITE
    ascent, descent = font(text_size).getmetrics()
    draw_wordmark(draw, tile_size + gap, (height * s - ascent - descent) // 2 + int(text_size * 0.04), text_size, text, CYAN_DARK if dark_text else CYAN)
    return image.resize((width // s, height), Image.LANCZOS)


def gradient_polygon(size, points, top, bottom, alpha):
    """Polygon filled with a vertical gradient (clipped by a mask)."""
    fill = vertical_gradient(size, top, bottom).convert("RGBA")
    mask = Image.new("L", size, 0)
    ImageDraw.Draw(mask).polygon(points, fill=alpha)
    return fill, mask


def vignette(size, strength):
    small = Image.new("L", (64, 36), 0)
    for x in range(64):
        for y in range(36):
            dx = (x - 31.5) / 31.5
            dy = (y - 17.5) / 17.5
            small.putpixel((x, y), int(min(1.0, (dx * dx + dy * dy) / 2.0) * 255 * strength))
    return small.resize(size, Image.BICUBIC)


def wallpaper(dark=True, lock=False):
    size = (3840, 2160)
    if dark:
        image = diagonal_gradient(size, (9, 12, 19), (22, 30, 47)).convert("RGB")
    else:
        image = diagonal_gradient(size, (236, 241, 248), (208, 221, 238)).convert("RGB")
    bw, bh = (1400, 1780) if not lock else (1250, 1590)
    bx = 2250 if not lock else 380
    by = (size[1] - bh) // 2
    points = bolt_points(bx, by, bw, bh)
    accent = CYAN if dark else CYAN_DARK
    layer, mask = glow(size, points, accent, 260, 0.5 if dark else 0.3)
    image.paste(layer, (0, 0), mask)
    draw_streaks(image, accent, 90, 7 if not lock else 11, 90 if dark else 55)
    top = (120, 214, 255) if dark else (63, 193, 255)
    bottom = (18, 120, 196) if dark else (8, 96, 160)
    fill, mask = gradient_polygon(size, points, top, bottom, 250)
    image.paste(fill, (0, 0), mask)
    edge = Image.new("RGBA", size, (0, 0, 0, 0))
    ImageDraw.Draw(edge).line(points + [points[0]], fill=(255, 255, 255, 70 if dark else 90), width=6, joint="curve")
    edge = edge.filter(ImageFilter.GaussianBlur(2))
    image.paste(edge, (0, 0), edge)
    if dark:
        shade = Image.new("RGB", size, (4, 6, 10))
        image.paste(shade, (0, 0), vignette(size, 0.55))
    if not lock:
        draw = ImageDraw.Draw(image)
        draw_wordmark(draw, 180, size[1] - 260, 84, (200, 212, 228) if dark else (40, 52, 70), accent)
    return image


def main():
    (ROOT / "logos").mkdir(exist_ok=True)
    (ROOT / "wallpapers").mkdir(exist_ok=True)
    mark(512).save(ROOT / "logos/storm-mark.png", optimize=True)
    logo(400).save(ROOT / "logos/storm-logo.png", optimize=True)
    logo(400, dark_text=True).save(ROOT / "logos/storm-logo-dark.png", optimize=True)
    oobe = Image.new("RGBA", (240, 100), (0, 0, 0, 0))
    tile = mark(84)
    oobe.paste(tile, (8, 8), tile)
    draw = ImageDraw.Draw(oobe)
    draw.text((102, 18), "STORM", font=font(30), fill=WHITE)
    draw.text((102, 52), "OS", font=font(30), fill=CYAN)
    oobe.save(ROOT / "logos/StormOS-oobe-logo.png", optimize=True)
    oem = Image.new("RGB", (120, 120), NAVY)
    tile = mark(112)
    oem.paste(tile, (4, 4), tile)
    oem.save(ROOT / "logos/StormOS-oem-logo.bmp")
    wallpaper(dark=True).save(ROOT / "wallpapers/storm-dark.jpg", quality=90, optimize=True, progressive=True)
    wallpaper(dark=False).save(ROOT / "wallpapers/storm-light.jpg", quality=90, optimize=True, progressive=True)
    wallpaper(dark=True, lock=True).save(ROOT / "wallpapers/storm-lockscreen.jpg", quality=90, optimize=True, progressive=True)
    print("assets written to", ROOT)


if __name__ == "__main__":
    main()
