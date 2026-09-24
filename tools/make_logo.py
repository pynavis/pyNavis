# -*- coding: utf-8 -*-
"""Regenerates the pyNavis logo files from the master drawing.

Run with any CPython that has Pillow:

    python tools/make_logo.py

This is a dev-time tool. It never ships into Navisworks and is never loaded by
an engine, so it may use modern CPython syntax.

The master is assets/logo/logo.svg: a flat list of filled <polygon>s, each with
an optional translate(). It is read once, fitted into a 0..512 space, then
written both as SVG and (drawn at 4x and downsampled) as PNG, so the vector and
raster files can never drift apart.

To change the logo, replace logo.svg and re-run. Never hand-edit an output file.
"""
import math
import os
import re

from PIL import Image, ImageDraw, ImageFont

S = 4
UNIT = 512

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, 'assets', 'logo')
MASTER = os.path.join(OUT, 'logo.svg')
FONTS = os.path.join(os.environ.get('WINDIR', r'C:\Windows'), 'Fonts')

TILE = '#1F2329'
ACCENT = '#178571'         # "Navis" on a light background (the diagonal's teal)
ACCENT_DARK = '#1FA38A'    # "Navis" on a dark background (the stems' teal)
INK_LIGHT = '#3D4451'      # "py" on a light background
INK_DARK = '#E8E8E8'       # "py" on a dark background

TILE_FILL = 0.74           # share of the tile the mark's longer side covers
BARE_FILL = 0.94           # same, with no tile behind it

MARK_PNG_SIZES = (512, 256, 96, 32)
ICO_SIZES = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (256, 256)]
LOGO_PNG_HEIGHT = 256
TEXT_PX = 250
TEXT_BASELINE = 345
TEXT_GAP = 56


# --- master ------------------------------------------------------------------

_POLY_RE = re.compile(r'<polygon\b([^>]*)>')
_ATTR_RE = re.compile(r'(\w[\w-]*)="([^"]*)"')
_NUM_RE = re.compile(r'-?\d+(?:\.\d+)?(?:e-?\d+)?')


def load_master():
    """[(points, color)] in master units, and the master viewBox (x, y, w, h)."""
    with open(MASTER, encoding='utf-8') as f:
        src = f.read()
    src = re.sub(r'<metadata>[\s\S]*?</metadata>', '', src)
    vb = [float(n) for n in _NUM_RE.findall(re.search(r'viewBox="([^"]+)"', src).group(1))]
    polys = []
    for m in _POLY_RE.finditer(src):
        attrs = dict(_ATTR_RE.findall(m.group(1)))
        nums = [float(n) for n in _NUM_RE.findall(attrs['points'])]
        tx = ty = 0.0
        t = re.search(r'translate\(([^)]*)\)', attrs.get('transform', ''))
        if t:
            parts = [float(n) for n in _NUM_RE.findall(t.group(1))] + [0.0]
            tx, ty = parts[0], parts[1]
        pts = [(nums[i] + tx, nums[i + 1] + ty) for i in range(0, len(nums), 2)]
        polys.append((pts, attrs.get('fill', '#000000')))
    if not polys:
        raise SystemExit('No <polygon> found in %s' % MASTER)
    return polys, vb


def mark(fill):
    """The master fitted and centred into a UNIT square, `fill` of it covered."""
    polys, _ = load_master()
    xs = [x for pts, _ in polys for x, _ in pts]
    ys = [y for pts, _ in polys for _, y in pts]
    x0, y0, w, h = min(xs), min(ys), max(xs) - min(xs), max(ys) - min(ys)
    k = UNIT * fill / max(w, h)
    ox, oy = (UNIT - w * k) / 2.0 - x0 * k, (UNIT - h * k) / 2.0 - y0 * k
    return [([(ox + x * k, oy + y * k) for x, y in pts], c) for pts, c in polys]


# --- SVG ---------------------------------------------------------------------

def svg_shapes(shapes):
    return ['<polygon points="%s" fill="%s"/>'
            % (' '.join('%.1f,%.1f' % p for p in pts), c) for pts, c in shapes]


def svg_doc(width, body):
    return ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 %d %d" '
            'width="%d" height="%d">\n  <title>pyNavis</title>\n  %s\n</svg>\n'
            % (width, UNIT, width, UNIT, '\n  '.join(body)))


def svg_tile():
    return '<rect width="%d" height="%d" rx="112" fill="%s"/>' % (UNIT, UNIT, TILE)


def svg_text(x, ink, accent):
    return ('<text x="%d" y="%d" font-family="\'Segoe UI\', \'Helvetica Neue\', Arial, '
            'sans-serif" font-size="%d"><tspan font-weight="400" fill="%s">py</tspan>'
            '<tspan font-weight="600" fill="%s">Navis</tspan></text>'
            % (x, TEXT_BASELINE, TEXT_PX, ink, accent))


# --- PNG ---------------------------------------------------------------------

def render(width, tile, ink=None, accent=None):
    img = Image.new('RGBA', (width * S, UNIT * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    if tile:
        d.rounded_rectangle([0, 0, UNIT * S - 1, UNIT * S - 1], radius=112 * S, fill=TILE)
    for pts, c in mark(TILE_FILL if tile else BARE_FILL):
        d.polygon([(x * S, y * S) for x, y in pts], fill=c)
    if ink:
        x = (UNIT + TEXT_GAP) * S
        for text, face, color in (('py', 'segoeui.ttf', ink), ('Navis', 'seguisb.ttf', accent)):
            font = ImageFont.truetype(os.path.join(FONTS, face), TEXT_PX * S)
            d.text((x, TEXT_BASELINE * S), text, font=font, fill=color, anchor='ls')
            x += d.textlength(text, font=font)
    return img


def text_width():
    w = 0
    for text, face in (('py', 'segoeui.ttf'), ('Navis', 'seguisb.ttf')):
        w += ImageFont.truetype(os.path.join(FONTS, face), TEXT_PX).getlength(text)
    return int(math.ceil(w))


def scaled(img, height):
    width = int(round(img.width * height / float(img.height)))
    return img.resize((width, height), Image.LANCZOS)


# --- main --------------------------------------------------------------------

def write(name, text):
    with open(os.path.join(OUT, name), 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)


LOCKUPS = (('', INK_LIGHT, ACCENT), ('.dark', INK_DARK, ACCENT_DARK))


def main():
    os.makedirs(OUT, exist_ok=True)
    tiled = svg_shapes(mark(TILE_FILL))
    logo_w = UNIT + TEXT_GAP + text_width() + 24

    write('pynavis-mark.svg', svg_doc(UNIT, [svg_tile()] + tiled))
    write('pynavis-mark.transparent.svg', svg_doc(UNIT, svg_shapes(mark(BARE_FILL))))
    for suffix, ink, accent in LOCKUPS:
        write('pynavis-logo%s.svg' % suffix,
              svg_doc(logo_w, [svg_tile()] + tiled + [svg_text(UNIT + TEXT_GAP, ink, accent)]))

    tile_img = render(UNIT, tile=True)
    for px in MARK_PNG_SIZES:
        scaled(tile_img, px).save(os.path.join(OUT, 'pynavis-mark.%d.png' % px))
    scaled(render(UNIT, tile=False), 512).save(
        os.path.join(OUT, 'pynavis-mark.transparent.512.png'))
    scaled(tile_img, 256).save(os.path.join(OUT, 'pynavis.ico'), sizes=ICO_SIZES)
    for suffix, ink, accent in LOCKUPS:
        scaled(render(logo_w, tile=True, ink=ink, accent=accent), LOGO_PNG_HEIGHT).save(
            os.path.join(OUT, 'pynavis-logo%s.png' % suffix))
    print('Wrote %s' % OUT)


if __name__ == '__main__':
    main()
