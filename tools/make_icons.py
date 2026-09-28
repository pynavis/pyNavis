# -*- coding: utf-8 -*-
"""Regenerates every ribbon icon for the pyNavis extension.

Run with any CPython that has Pillow:

    python tools/make_icons.py
    python tools/make_icons.py --sheet out.png     (contact sheet for review)

This is a dev-time tool. It never ships into Navisworks and is never loaded by
an engine, so it may use modern CPython syntax.

Design language:
  * neutral ink draws the THING, a solid accent mark draws the ACTION, so colour
    always means "what this tool does"
  * 0..96 coordinate space, drawn at 4x and downsampled; one stroke weight per
    density, rounded caps
  * shared family marks carry cohesion: the open tray is the memory register and
    appears in eight Memory glyphs; the spark marks a collision
  * alert red appears exactly ONCE in the whole set, on Purge, because it is the
    only destructive tool. Do not add a second.
  * four files per bundle: icon.png / icon.dark.png at 96px for large buttons,
    icon.small.png / icon.small.dark.png at 32px for stacked and menu buttons.
    The small files are separate, simplified drawings, not resized copies.

Never hand-edit an icon. Change the drawing here and re-run.
"""
import argparse
import math
import os

from PIL import Image, ImageDraw

S = 4
SIZE = 96 * S
LARGE_PX = 96
SMALL_PX = 32

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TAB = os.path.join(ROOT, 'extensions', 'pyNavis.extension', 'pyNavis.tab')
SMOKE_TAB = os.path.join(ROOT, 'tests', 'fixtures', 'Smoke.extension', 'Smoke.tab')

LIGHT = {'ink': (61, 68, 81, 255), 'accent': (0, 120, 212, 255),
         'alert': (196, 43, 28, 255), 'paper': (255, 255, 255, 255)}
DARK = {'ink': (232, 232, 232, 255), 'accent': (76, 194, 255, 255),
        'alert': (255, 95, 82, 255), 'paper': (32, 32, 32, 255)}

THEMES = [('', LIGHT), ('.dark', DARK)]
WEIGHT_LARGE = 8
WEIGHT_SMALL = 14


class Ctx(object):
    """Drawing helper working in a 0..96 coordinate space."""

    def __init__(self, theme, weight):
        self.img = Image.new('RGBA', (SIZE, SIZE), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.img)
        self.t = theme
        self.w = int(weight * S)
        self.small = weight >= WEIGHT_SMALL

    def _xy(self, pts):
        return [(x * S, y * S) for x, y in pts]

    def line(self, pts, color=None, w=None):
        c = color or self.t['ink']
        w = int(w * S) if w else self.w
        p = self._xy(pts)
        self.d.line(p, fill=c, width=w, joint='curve')
        r = w / 2.0
        for x, y in p:                                  # round caps
            self.d.ellipse([x - r, y - r, x + r, y + r], fill=c)

    def circle(self, cx, cy, r, color=None, w=None, fill=None):
        w = int(w * S) if w is not None else self.w
        box = [(cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S]
        self.d.ellipse(box, outline=(color or self.t['ink']) if w else None,
                       width=w, fill=fill)

    def disc(self, cx, cy, r, color):
        self.d.ellipse([(cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S], fill=color)

    def rrect(self, x0, y0, x1, y1, rad=8, color=None, w=None, fill=None):
        w = int(w * S) if w is not None else self.w
        self.d.rounded_rectangle([x0 * S, y0 * S, x1 * S, y1 * S], radius=rad * S,
                                 outline=(color or self.t['ink']) if w else None,
                                 width=w, fill=fill)

    def poly(self, pts, color=None, fill=None, w=None):
        p = self._xy(pts)
        if fill:
            self.d.polygon(p, fill=fill)
        if w != 0:
            ww = int(w * S) if w else self.w
            closed = p + [p[0]]
            self.d.line(closed, fill=color or self.t['ink'], width=ww, joint='curve')
            r = ww / 2.0
            for x, y in closed:
                self.d.ellipse([x - r, y - r, x + r, y + r], fill=color or self.t['ink'])

    def arrow_v(self, x, y0, y1, color=None, w=None, head=16):
        c = color or self.t['accent']
        d = 1 if y1 > y0 else -1
        end = y1 - d * head * 0.9
        self.line([(x, y0), (x, end)], color=c, w=w)
        self.poly([(x - head * 0.6, end), (x + head * 0.6, end), (x, y1)],
                  color=c, fill=c, w=0)

    def png(self, px):
        return self.img.resize((px, px), Image.LANCZOS)


# --- shared family marks -----------------------------------------------------

def tray(c, x0=14, y0=52, x1=82, y1=86, color=None, w=None):
    """The memory register: an open-top container, so 'into' and 'out of' read."""
    c.line([(x0, y0), (x0, y1), (x1, y1), (x1, y0)], color=color, w=w)


def small_tray(c):
    tray(c, x0=8, y0=54, x1=88, y1=88)


def spark(c, cx, cy, arm, w):
    """Collision spark: the accent verb of the clash tools."""
    for dx, dy in ((0, 1), (0.87, 0.5), (0.87, -0.5)):
        c.line([(cx - arm * dx, cy - arm * dy), (cx + arm * dx, cy + arm * dy)],
               color=c.t['accent'], w=w)


def dots(c, y, r, color=None):
    for x in (22, 48, 74):
        c.disc(x, y, r, color or c.t['ink'])


def plus(c, cx, cy, arm, w):
    c.line([(cx - arm, cy), (cx + arm, cy)], color=c.t['accent'], w=w)
    c.line([(cx, cy - arm), (cx, cy + arm)], color=c.t['accent'], w=w)


def tilted_box(c, half_x=32, half_y=22, angle=20, color=None, w=None,
               cx=48, cy=48):
    """The fitted section box seen in plan: a rectangle square to the objects,
    not to the world. The tilt IS the tool, so every section glyph carries it.
    Corners come back in order, so edges 0-1 and 2-3 are the long pair."""
    a = math.radians(angle)
    cos_a, sin_a = math.cos(a), math.sin(a)
    pts = []
    for dx, dy in ((-half_x, -half_y), (half_x, -half_y),
                   (half_x, half_y), (-half_x, half_y)):
        pts.append((cx + dx * cos_a - dy * sin_a, cy + dx * sin_a + dy * cos_a))
    c.poly(pts, color=color, w=w)
    return pts


def edge_mark(c, p, q, offset, length, w, cx=48, cy=48):
    """A short accent line lying just outside an edge: the plane that edge
    stands for, standing off the objects by the padding."""
    dx, dy = q[0] - p[0], q[1] - p[1]
    span = math.hypot(dx, dy) or 1.0
    ux, uy = dx / span, dy / span
    mx, my = (p[0] + q[0]) / 2.0, (p[1] + q[1]) / 2.0
    nx, ny = uy, -ux
    if (mx - cx) * nx + (my - cy) * ny < 0:          # always push outward
        nx, ny = -nx, -ny
    ox, oy = mx + nx * offset, my + ny * offset
    half = length / 2.0
    c.line([(ox - ux * half, oy - uy * half), (ox + ux * half, oy + uy * half)],
           color=c.t['accent'], w=w)


# --- General panel -----------------------------------------------------------

def ic_console(c):
    if c.small:
        c.line([(14, 24), (44, 48), (14, 72)], color=c.t['accent'], w=15)
        c.line([(56, 74), (88, 74)], w=15)
    else:
        c.rrect(8, 16, 88, 80, rad=10)
        c.line([(26, 36), (46, 48), (26, 60)], color=c.t['accent'], w=9)
        c.line([(54, 62), (74, 62)], w=8)


def ic_reload(c):
    """Circular arrow with a gap at the top right, head tangent to the arc."""
    r = 32
    w = 15 if c.small else 9
    c.d.arc([(48 - r) * S, (48 - r) * S, (48 + r) * S, (48 + r) * S],
            start=-50, end=200, fill=c.t['accent'], width=int(w * S))
    hx, hy = 48 + r * 0.64, 48 - r * 0.77
    size = 17 if c.small else 12
    c.poly([(hx - size * 0.2, hy - size), (hx + size, hy + size * 0.25),
            (hx - size * 0.55, hy + size * 0.6)],
           color=c.t['accent'], fill=c.t['accent'], w=0)


def ic_settings(c):
    """Two sliders with accent knobs: the pyNavis settings are a handful of
    switches, not a machine, and a slider row still reads at 16px where a gear
    turns to mush."""
    if c.small:
        for y, knob in ((34, 64), (68, 34)):
            c.line([(10, y), (86, y)], w=12)
            c.disc(knob, y, 15, c.t['accent'])
    else:
        for y, knob in ((36, 62), (66, 36)):
            c.line([(14, y), (82, y)], w=8)
            c.disc(knob, y, 12, c.t['accent'])


def ic_panel_slots(c):
    """Two dock-pane rectangles in ink, a third drawn in accent with a plus:
    the slot the tool is about to add."""
    if c.small:
        c.rrect(4, 8, 34, 88, rad=6, w=12)
        c.rrect(40, 8, 70, 88, rad=6, w=12)
        c.rrect(76, 8, 92, 88, rad=5, color=c.t['accent'], w=12)
        plus(c, 84, 48, 8, 11)
    else:
        c.rrect(8, 12, 40, 84, rad=6)
        c.rrect(46, 12, 78, 84, rad=6)
        c.rrect(82, 12, 92, 84, rad=4, color=c.t['accent'])
        plus(c, 87, 48, 5, 7)


def ic_shortcuts(c):
    """Keycaps; the accent cap is the one being bound."""
    if c.small:
        c.rrect(8, 10, 88, 70, rad=12, w=13)
        c.line([(28, 88), (68, 88)], color=c.t['accent'], w=15)
    else:
        c.rrect(8, 18, 44, 54, rad=8)
        c.rrect(52, 18, 88, 54, rad=8)
        c.rrect(30, 62, 66, 92, rad=8, w=0, fill=c.t['accent'])


# --- Tools panel -------------------------------------------------------------

def ic_clash_report(c):
    if c.small:
        c.rrect(6, 6, 58, 90, rad=7, w=12)
        spark(c, 70, 68, 22, 12)
    else:
        c.rrect(10, 10, 62, 86, rad=6)
        c.line([(22, 32), (50, 32)], w=6)
        c.line([(22, 48), (50, 48)], w=6)
        c.line([(22, 64), (40, 64)], w=6)
        spark(c, 74, 62, 20, 9)


def ic_export_csv(c):
    if c.small:
        c.rrect(14, 6, 82, 50, rad=8, w=13)
        c.arrow_v(48, 56, 92, color=c.t['accent'], w=14, head=22)
    else:
        c.rrect(12, 8, 84, 52, rad=8)
        c.line([(12, 30), (84, 30)], w=6)
        c.line([(48, 8), (48, 52)], w=6)
        c.arrow_v(48, 60, 90, color=c.t['accent'], w=9, head=17)


def ic_sets_from_excel(c):
    """A spreadsheet grid in ink is the workbook; the accent two-headed arrow
    says the data moves both ways - out to Excel on export, back into new
    sets on import."""
    ax = 80
    if c.small:
        c.rrect(6, 6, 62, 90, rad=6, w=12)
        c.line([(6, 30), (62, 30)], w=12)
        c.line([(6, 62), (62, 62)], w=12)
        c.line([(34, 6), (34, 90)], w=12)
        c.line([(ax, 22), (ax, 74)], color=c.t['accent'], w=13)
        c.poly([(ax - 12, 30), (ax + 12, 30), (ax, 14)],
               color=c.t['accent'], fill=c.t['accent'], w=0)
        c.poly([(ax - 12, 66), (ax + 12, 66), (ax, 82)],
               color=c.t['accent'], fill=c.t['accent'], w=0)
    else:
        c.rrect(8, 10, 64, 86, rad=6)
        c.line([(8, 36), (64, 36)], w=6)
        c.line([(8, 62), (64, 62)], w=6)
        c.line([(36, 10), (36, 86)], w=6)
        c.line([(ax, 26), (ax, 70)], color=c.t['accent'], w=8)
        c.poly([(ax - 9, 32), (ax + 9, 32), (ax, 18)],
               color=c.t['accent'], fill=c.t['accent'], w=0)
        c.poly([(ax - 9, 64), (ax + 9, 64), (ax, 78)],
               color=c.t['accent'], fill=c.t['accent'], w=0)


def ic_grouper(c):
    """Two clashing parts gathered inside one accent group frame."""
    if c.small:
        c.disc(34, 40, 15, c.t['ink'])
        c.disc(58, 58, 15, c.t['ink'])
        c.rrect(6, 6, 90, 90, rad=12, w=11, color=c.t['accent'])
    else:
        c.circle(38, 40, 15, w=8)
        c.rrect(46, 48, 76, 78, rad=6, w=8)
        c.rrect(8, 10, 88, 90, rad=12, w=7, color=c.t['accent'])


def ic_true_distance(c):
    """Two slanted parallel faces in ink; the accent arrow crosses them at a
    right angle, which is the whole point: the true gap, not the world axis."""
    # faces lean 20 degrees so the icon reads 'rotated project'
    dx = 12
    if c.small:
        c.line([(22 + dx, 8), (22 - dx, 88)], w=12)
        c.line([(74 + dx, 8), (74 - dx, 88)], w=12)
        _perp_arrow(c, 48, 48, dx, w=12, head=18, span=22)
    else:
        c.line([(24 + dx, 8), (24 - dx, 88)], w=7)
        c.line([(72 + dx, 8), (72 - dx, 88)], w=7)
        _perp_arrow(c, 48, 48, dx, w=7, head=13, span=20)


def ic_resolve_clash(c):
    """A beam in ink with a box sunk into it; the accent arrow lifts the box
    clear, which is what the tool does."""
    if c.small:
        c.rrect(8, 58, 88, 88, rad=4, w=12)          # the obstacle
        c.rrect(28, 30, 68, 70, rad=4, w=12)         # the mover, sunk into it
        c.arrow_v(48, 30, 2, w=12, head=22)
    else:
        c.rrect(8, 56, 88, 88, rad=5, w=7)
        c.rrect(30, 32, 66, 70, rad=5, w=7)
        c.arrow_v(48, 30, 4, w=7, head=16)


def ic_set_gap(c):
    """A box floating above a beam with air between them; the double-headed
    accent arrow in the gap is the number the tool sets, tighter or apart."""
    if c.small:
        c.rrect(8, 70, 88, 90, rad=4, w=12)          # the other object
        c.rrect(22, 6, 74, 28, rad=4, w=12)          # the mover, clear of it
        c.arrow_v(48, 49, 30, w=12, head=20)
        c.arrow_v(48, 49, 68, w=12, head=20)
    else:
        c.rrect(8, 72, 88, 88, rad=5, w=7)
        c.rrect(24, 8, 72, 28, rad=5, w=7)
        c.arrow_v(48, 50, 30, w=7, head=14)
        c.arrow_v(48, 50, 70, w=7, head=14)


def _perp_arrow(c, cx, cy, lean, w, head, span):
    """Double-headed accent arrow through (cx, cy), perpendicular to faces
    that lean 'lean' units across 80 units of height."""
    import math
    ang = math.atan2(lean, 80.0)             # face tilt from vertical
    ux, uy = math.cos(ang), -math.sin(ang)   # unit normal of the faces
    px, py = -uy, ux                         # along the faces
    a = (cx - ux * span, cy - uy * span)
    b = (cx + ux * span, cy + uy * span)
    col = c.t['accent']
    c.line([a, b], color=col, w=w)
    for tip, sgn in ((a, -1), (b, 1)):
        base = (tip[0] - sgn * ux * head, tip[1] - sgn * uy * head)
        c.poly([tip,
                (base[0] + px * head * 0.55, base[1] + py * head * 0.55),
                (base[0] - px * head * 0.55, base[1] - py * head * 0.55)],
               color=col, fill=col, w=0)


# --- Viewpoints panel --------------------------------------------------------
# Family mark: the eye is the viewpoint; the accent draws what the tool DOES.

def eye(c, w=None):
    """Two arcs meeting at the corners, pupil left unfilled for the accent."""
    ww = int((w or (13 if c.small else 8)) * S)
    box = [10 * S, 14 * S, 86 * S, 82 * S]
    c.d.arc(box, start=200, end=340, fill=c.t['ink'], width=ww)
    c.d.arc(box, start=20, end=160, fill=c.t['ink'], width=ww)


def ic_vp_renamer(c):
    if c.small:
        eye(c)
        c.line([(30, 88), (92, 26)], color=c.t['accent'], w=14)
    else:
        eye(c)
        c.disc(48, 48, 9, c.t['ink'])
        # rename stroke: a pencil line crossing out of the eye
        c.line([(38, 84), (88, 34)], color=c.t['accent'], w=9)
        c.poly([(88, 34), (94, 28), (90, 42)], color=c.t['accent'],
               fill=c.t['accent'], w=0)


def ic_vp_deleter(c):
    """The eye with the accent X: remove viewpoints (alert red stays Purge's)."""
    if c.small:
        eye(c)
        c.line([(58, 60), (92, 92)], color=c.t['accent'], w=14)
        c.line([(92, 60), (58, 92)], color=c.t['accent'], w=14)
    else:
        eye(c)
        c.disc(48, 48, 9, c.t['ink'])
        c.line([(64, 62), (90, 88)], color=c.t['accent'], w=9)
        c.line([(90, 62), (64, 88)], color=c.t['accent'], w=9)


def ic_vp_manager(c):
    """The eye with accent order bars: organize the viewpoint tree."""
    if c.small:
        eye(c)
        c.line([(56, 66), (92, 66)], color=c.t['accent'], w=13)
        c.line([(56, 88), (80, 88)], color=c.t['accent'], w=13)
    else:
        eye(c)
        c.disc(48, 48, 9, c.t['ink'])
        c.line([(60, 62), (92, 62)], color=c.t['accent'], w=8)
        c.line([(60, 76), (84, 76)], color=c.t['accent'], w=8)
        c.line([(60, 90), (76, 90)], color=c.t['accent'], w=8)


# --- Viewpoints panel: section tools -----------------------------------------
# Family mark: the tilted box in ink is the fit; the accent says what happens
# to it - planes on, look down, off.

def ic_section_fit(c):
    """The box with an accent plane standing off each long face. Only the long
    pair: four marks turned into a fence at 16px and the tilt stopped reading."""
    if c.small:
        pts = tilted_box(c, half_x=30, half_y=17, w=13)
        edge_mark(c, pts[0], pts[1], 17, 52, 13)
        edge_mark(c, pts[2], pts[3], 17, 52, 13)
    else:
        pts = tilted_box(c, half_x=28, half_y=15, w=8)
        edge_mark(c, pts[0], pts[1], 15, 50, 8)
        edge_mark(c, pts[2], pts[3], 15, 50, 8)


def ic_section_plan(c):
    """The box pushed down so the accent arrow has clear air above it: the
    camera dropping onto the fitted box, not an arrow tangled in its outline."""
    if c.small:
        tilted_box(c, half_x=34, half_y=20, w=13, cy=64)
        c.arrow_v(48, 4, 36, w=13, head=20)
    else:
        tilted_box(c, half_x=30, half_y=18, w=8, cy=64)
        c.arrow_v(48, 6, 34, w=9, head=16)


def ic_section_clear(c):
    """The fitted box with the accent X: sectioning off (alert red stays
    Purge's, and the X is the mark Memory Clear and Deleter already use)."""
    if c.small:
        tilted_box(c, half_x=36, half_y=24, w=13)
        c.line([(30, 30), (66, 66)], color=c.t['accent'], w=14)
        c.line([(66, 30), (30, 66)], color=c.t['accent'], w=14)
    else:
        tilted_box(c, half_x=32, half_y=22, w=8)
        c.line([(33, 33), (63, 63)], color=c.t['accent'], w=9)
        c.line([(63, 33), (33, 63)], color=c.t['accent'], w=9)


# --- Viewpoints panel: state tools -------------------------------------------
# Family mark: the sheet is the copied state; the accent draws where it goes -
# off the original for Copy, onto the clipboard for Paste.

def ic_state_copy(c):
    """The classic copy pair: the original sheet behind in ink, the accent
    sheet in front being the copy taken off it."""
    if c.small:
        c.line([(36, 22), (36, 6), (90, 6), (90, 60), (74, 60)], w=13)
        c.rrect(6, 24, 60, 90, rad=8, color=c.t['accent'], w=13)
    else:
        c.line([(38, 24), (38, 12), (84, 12), (84, 58), (72, 58)], w=8)
        c.rrect(14, 26, 64, 84, rad=6, color=c.t['accent'], w=8)


def ic_state_paste(c):
    """The clipboard in ink with the accent sheet landing on it."""
    if c.small:
        c.rrect(14, 10, 82, 90, rad=8, w=13)
        c.rrect(32, 2, 64, 18, rad=4, w=0, fill=c.t['ink'])
        c.rrect(38, 42, 90, 94, rad=8, color=c.t['accent'], w=13)
    else:
        c.rrect(20, 12, 76, 88, rad=6)
        c.rrect(38, 4, 58, 20, rad=4, w=0, fill=c.t['ink'])
        c.rrect(42, 40, 86, 92, rad=6, color=c.t['accent'], w=8)


# --- Viewpoints panel: speed tools -------------------------------------------
# Family mark: the gauge arc in ink is the speed; the accent needle says the
# tool puts it back where you want it.

def gauge(c, cx=48, cy=62, r=32, w=None):
    """An open speedometer arc: the dial both speed tools set."""
    ww = int((w or (13 if c.small else 8)) * S)
    box = [(cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S]
    c.d.arc(box, start=175, end=365, fill=c.t['ink'], width=ww)


def needle(c, cx=48, cy=62, r=26, w=None):
    """The accent needle snapped back to the low end of the dial."""
    c.line([(cx, cy), (cx - r * 0.72, cy - r * 0.62)], color=c.t['accent'], w=w)
    c.disc(cx, cy, 6 if c.small else 5, c.t['accent'])


def ic_reset_speeds(c):
    """The dial with the accent needle put back: one press and the speed is
    whatever you decided it should be."""
    if c.small:
        gauge(c, cy=64, r=36, w=13)
        needle(c, cy=64, r=30, w=13)
    else:
        gauge(c)
        needle(c)


def ic_reset_viewpoints(c):
    """The viewpoints family eye over the speed family dial: the same reset, aimed
    at views already saved in the document rather than the one on screen."""
    if c.small:
        c.d.arc([10*S, 6*S, 86*S, 58*S], start=200, end=340, fill=c.t['ink'], width=13*S)
        c.d.arc([10*S, 6*S, 86*S, 58*S], start=20, end=160, fill=c.t['ink'], width=13*S)
        c.disc(48, 32, 11, c.t['ink'])
        gauge(c, cy=88, r=26, w=13)
        needle(c, cy=88, r=21, w=13)
    else:
        c.d.arc([12*S, 8*S, 84*S, 56*S], start=200, end=340, fill=c.t['ink'], width=8*S)
        c.d.arc([12*S, 8*S, 84*S, 56*S], start=20, end=160, fill=c.t['ink'], width=8*S)
        c.disc(48, 32, 8, c.t['ink'])
        gauge(c, cy=86, r=24, w=7)
        needle(c, cy=86, r=19, w=7)


def ic_viewpoint_tracker(c):
    """The viewpoints family eye with an accent pulse beside it: the panel is
    watching which view you are on. This is the ribbon toggle's OFF art, the
    panel closed."""
    if c.small:
        eye(c)
        c.disc(48, 48, 12, c.t['ink'])
        c.line([(60, 88), (92, 88)], color=c.t['accent'], w=13)
    else:
        eye(c)
        c.disc(48, 48, 9, c.t['ink'])
        c.line([(58, 88), (74, 74)], color=c.t['accent'], w=8)
        c.line([(74, 74), (86, 88)], color=c.t['accent'], w=8)


def _nudge_glyph(c, plane_color):
    """A box outline in ink, a section plane through it, and the accent
    arrow pushing the plane: keyboard nudging, not drag handles."""
    w = 12 if c.small else 7
    c.rrect(10, 22, 86, 78, rad=5, w=w)
    c.line([(60, 12), (60, 88)], color=plane_color, w=w)
    col = c.t['accent']
    head = 16 if c.small else 13
    c.line([(24, 50), (50 - head * 0.6, 50)], color=col, w=w)
    c.poly([(52, 50), (52 - head, 50 - head * 0.6), (52 - head, 50 + head * 0.6)],
           color=col, fill=col, w=0)


def ic_section_nudge(c):
    _nudge_glyph(c, c.t['ink'])


def ic_section_nudge_on(c):
    _nudge_glyph(c, c.t['accent'])


def ic_viewpoint_tracker_on(c):
    """The same eye with an accent pupil: the ribbon toggle's ON art, shown
    while the panel is open. Large only, because a dockpane button is never
    drawn at the small size."""
    eye(c)
    c.disc(48, 48, 13, c.t['accent'])


def sparkle(c, cx, cy, r, color=None):
    """Four-point star: the mark of a thing the assistant made."""
    col = color or c.t['accent']
    k = r * 0.3
    pts = [(cx, cy - r), (cx + k, cy - k), (cx + r, cy), (cx + k, cy + k),
           (cx, cy + r), (cx - k, cy + k), (cx - r, cy), (cx - k, cy - k)]
    c.poly(pts, fill=col, w=0)


def _bubble(c, fill=None, w=None):
    """A speech bubble: rounded body with a tail at the lower left."""
    if fill:
        c.rrect(8, 10, 88, 66, rad=14, fill=fill, w=0)
        c.poly([(24, 62), (24, 88), (46, 62)], fill=fill, w=0)
    else:
        c.rrect(8, 10, 88, 66, rad=14, w=w)
        c.line([(24, 64), (24, 88), (46, 64)], w=w)


def ic_ask_ai(c):
    """A speech bubble in ink with an accent sparkle inside: a chat that makes
    tools. This is the ribbon toggle's OFF art, the panel closed."""
    if c.small:
        _bubble(c, w=12)
        sparkle(c, 48, 38, 18)
    else:
        _bubble(c, w=7)
        sparkle(c, 48, 38, 15)


def ic_ask_ai_on(c):
    """The same bubble filled with accent, the sparkle punched out in paper:
    the ribbon toggle's ON art while the panel is open. Large only."""
    _bubble(c, fill=c.t['accent'])
    sparkle(c, 48, 38, 15, color=c.t['paper'])


def ic_ai_settings(c):
    """A key in ink, the bow left, teeth right, with the accent sparkle over the
    bow: the API key that lets the assistant in."""
    if c.small:
        c.circle(28, 52, 16, w=12)
        c.line([(44, 52), (90, 52)], w=12)
        c.line([(74, 52), (74, 70)], w=12)
        sparkle(c, 28, 52, 9)
    else:
        c.circle(28, 52, 14, w=7)
        c.line([(42, 52), (90, 52)], w=7)
        c.line([(70, 52), (70, 68)], w=7)
        c.line([(84, 52), (84, 66)], w=7)
        sparkle(c, 28, 52, 8)


def ic_open_ai_folder(c):
    """A folder in ink with the accent sparkle on its face: where the
    assistant's tools live on disk."""
    w = 12 if c.small else 7
    c.rrect(8, 30, 88, 82, rad=6, w=w)
    c.line([(8, 30), (12, 18), (40, 18), (48, 30)], w=w)
    sparkle(c, 62, 58, 16 if c.small else 14)


def ic_tracker_window(c):
    """The tracker eye inside a floating frame: the same two lines, in a window
    that sits on top instead of docked."""
    if c.small:
        c.rrect(6, 14, 90, 82, rad=8, w=13)
        c.disc(48, 50, 13, c.t['accent'])
    else:
        c.rrect(8, 16, 88, 80, rad=8)
        c.line([(8, 30), (88, 30)], w=7)        # the window's titlebar
        c.disc(48, 56, 11, c.t['accent'])


# --- Memory panel ------------------------------------------------------------

def ic_memorize(c):
    if c.small:
        small_tray(c)
        c.arrow_v(48, 6, 44, color=c.t['accent'], w=15, head=24)
    else:
        tray(c)
        c.line([(30, 74), (66, 74)], w=6)
        c.arrow_v(48, 10, 42, color=c.t['accent'], w=9, head=17)


def ic_recall(c):
    if c.small:
        small_tray(c)
        c.arrow_v(48, 44, 6, color=c.t['accent'], w=15, head=24)
    else:
        tray(c)
        c.line([(30, 74), (66, 74)], w=6)
        c.arrow_v(48, 42, 10, color=c.t['accent'], w=9, head=17)


def ic_add(c):
    if c.small:
        small_tray(c)
        plus(c, 48, 24, 22, 15)
    else:
        tray(c)
        plus(c, 48, 26, 18, 10)


def ic_subtract(c):
    if c.small:
        small_tray(c)
        c.line([(26, 24), (70, 24)], color=c.t['accent'], w=15)
    else:
        tray(c)
        c.line([(30, 26), (66, 26)], color=c.t['accent'], w=10)


def ic_intersect(c):
    if c.small:
        c.circle(36, 48, 28, w=12)
        c.disc(60, 48, 28, c.t['accent'])
    else:
        c.circle(36, 48, 26, w=8)
        c.circle(60, 48, 26, w=8, color=c.t['accent'])
        c.d.pieslice([(60 - 26) * S, (48 - 26) * S, (60 + 26) * S, (48 + 26) * S],
                     start=120, end=240, fill=c.t['accent'])


def ic_prev(c):
    if c.small:
        c.poly([(62, 12), (62, 84), (14, 48)], color=c.t['accent'], fill=c.t['accent'], w=0)
    else:
        dots(c, 82, 6)
        c.poly([(62, 16), (62, 66), (20, 41)], color=c.t['accent'], fill=c.t['accent'], w=0)


def ic_next(c):
    if c.small:
        c.poly([(34, 12), (34, 84), (82, 48)], color=c.t['accent'], fill=c.t['accent'], w=0)
    else:
        dots(c, 82, 6)
        c.poly([(34, 16), (34, 66), (76, 41)], color=c.t['accent'], fill=c.t['accent'], w=0)


def ic_clear(c):
    if c.small:
        small_tray(c)
        c.line([(28, 8), (68, 40)], color=c.t['accent'], w=14)
        c.line([(68, 8), (28, 40)], color=c.t['accent'], w=14)
    else:
        tray(c)
        c.line([(32, 12), (64, 38)], color=c.t['accent'], w=9)
        c.line([(64, 12), (32, 38)], color=c.t['accent'], w=9)


def ic_memory_menu(c):
    if c.small:
        small_tray(c)
        c.line([(26, 16), (48, 38), (70, 16)], color=c.t['accent'], w=15)
    else:
        tray(c)
        c.line([(30, 74), (66, 74)], w=6)
        c.line([(30, 18), (48, 36), (66, 18)], color=c.t['accent'], w=10)


def ic_show(c):
    if c.small:
        small_tray(c)
        c.line([(20, 14), (76, 14)], color=c.t['accent'], w=13)
        c.line([(20, 36), (58, 36)], color=c.t['accent'], w=13)
    else:
        tray(c)
        c.line([(24, 14), (72, 14)], color=c.t['accent'], w=9)
        c.line([(24, 30), (72, 30)], color=c.t['accent'], w=9)
        c.line([(24, 42), (54, 42)], color=c.t['accent'], w=9)


def ic_save_set(c):
    if c.small:
        small_tray(c)
        c.poly([(28, 6), (68, 6), (68, 42), (48, 30), (28, 42)],
               color=c.t['accent'], fill=c.t['accent'], w=0)
    else:
        tray(c)
        c.line([(30, 74), (66, 74)], w=6)
        c.poly([(32, 8), (64, 8), (64, 42), (48, 31), (32, 42)],
               color=c.t['accent'], fill=c.t['accent'], w=0)


def ic_purge(c):
    """The one destructive tool, and the only alert red in the whole set."""
    if c.small:
        c.line([(10, 24), (86, 24)], w=13)
        c.rrect(20, 30, 76, 90, rad=8, w=13)
        c.line([(34, 44), (62, 76)], color=c.t['alert'], w=13)
        c.line([(62, 44), (34, 76)], color=c.t['alert'], w=13)
    else:
        c.line([(12, 26), (84, 26)], w=8)
        c.line([(38, 14), (58, 14)], w=8)
        c.rrect(22, 30, 74, 88, rad=8)
        c.line([(36, 46), (60, 74)], color=c.t['alert'], w=9)
        c.line([(60, 46), (36, 74)], color=c.t['alert'], w=9)


# --- Smoke fixture -----------------------------------------------------------
# Icons for the Smoke.extension test fixture. Same design language, but these
# glyphs describe the CONTRACT under test, not a user-facing tool. The alert
# red stays reserved for Purge; nothing here may use it.

def ic_smoke_toggle_off(c):
    """Hollow ring: the toggle at rest."""
    w = 15 if c.small else 9
    c.circle(48, 48, 27 if c.small else 29, w=w)


def ic_smoke_toggle_on(c):
    """The same ring filled with accent: the toggle engaged."""
    c.disc(48, 48, 33 if c.small else 34, c.t['accent'])
    c.disc(48, 48, 12, c.t['paper'])


def ic_smoke_smart(c):
    """A button that redraws itself: the accent diamond is the self-applied
    change, sitting on the button's corner."""
    if c.small:
        c.rrect(8, 22, 74, 88, rad=10, w=13)
        c.poly([(74, 8), (92, 26), (74, 44), (56, 26)],
               color=c.t['accent'], fill=c.t['accent'], w=0)
    else:
        c.rrect(12, 26, 70, 84, rad=8)
        c.poly([(72, 8), (90, 26), (72, 44), (54, 26)],
               color=c.t['accent'], fill=c.t['accent'], w=0)


def _split_glyph(c, accent_row):
    """Header bar over a two-item list; the accent row is the child the
    header reruns (0 = first, 1 = last)."""
    if c.small:
        c.rrect(8, 8, 88, 40, rad=8, w=13)
        rows = (62, 84)
        c.line([(10, rows[accent_row]), (86, rows[accent_row])],
               color=c.t['accent'], w=14)
        c.line([(10, rows[1 - accent_row]), (86, rows[1 - accent_row])], w=14)
    else:
        c.rrect(12, 10, 84, 36, rad=6)
        rows = (56, 74)
        c.line([(20, rows[accent_row]), (76, rows[accent_row])],
               color=c.t['accent'], w=9)
        c.line([(20, rows[1 - accent_row]), (76, rows[1 - accent_row])], w=8)


def ic_smoke_split_last(c):
    _split_glyph(c, accent_row=1)


def ic_smoke_split_fixed(c):
    _split_glyph(c, accent_row=0)


def _split_child(c, accent_row):
    """Two bars; the accent bar is this child's position in the menu."""
    rows = (34, 62)
    w = 16 if c.small else 10
    c.line([(14, rows[accent_row]), (82, rows[accent_row])],
           color=c.t['accent'], w=w)
    c.line([(14, rows[1 - accent_row]), (82, rows[1 - accent_row])], w=w)


def ic_smoke_child_first(c):
    _split_child(c, accent_row=0)


def ic_smoke_child_second(c):
    _split_child(c, accent_row=1)


def ic_smoke_url(c):
    """Globe with an accent arrow leaving it: opens outside the app."""
    if c.small:
        c.circle(42, 54, 32, w=13)
        c.line([(58, 38), (88, 8)], color=c.t['accent'], w=13)
        c.poly([(66, 6), (90, 6), (90, 30)],
               color=c.t['accent'], fill=c.t['accent'], w=0)
    else:
        c.circle(42, 54, 32)
        c.line([(10, 54), (74, 54)], w=6)
        c.d.ellipse([(42 - 14) * S, (54 - 32) * S, (42 + 14) * S, (54 + 32) * S],
                    outline=c.t['ink'], width=6 * S)
        c.line([(60, 36), (86, 10)], color=c.t['accent'], w=9)
        c.poly([(68, 6), (90, 6), (90, 28)],
               color=c.t['accent'], fill=c.t['accent'], w=0)


def ic_smoke_link(c):
    """Two chain links that do not meet: the target plugin is absent."""
    if c.small:
        c.circle(28, 28, 18, w=13)
        c.circle(68, 68, 18, w=13)
        c.line([(40, 56), (48, 48), (56, 56)], color=c.t['accent'], w=10)
    else:
        c.circle(28, 30, 17)
        c.circle(68, 66, 17)
        c.line([(40, 58), (46, 48), (42, 44)], color=c.t['accent'], w=7)
        c.line([(56, 38), (50, 48), (54, 52)], color=c.t['accent'], w=7)


def ic_smoke_lib(c):
    """A shelved book with the accent tick: the lib import resolved."""
    if c.small:
        c.rrect(10, 8, 56, 88, rad=6, w=13)
        c.line([(58, 60), (70, 76), (92, 40)], color=c.t['accent'], w=13)
    else:
        c.rrect(14, 10, 54, 86, rad=5)
        c.line([(14, 28), (54, 28)], w=6)
        c.line([(58, 60), (70, 74), (90, 42)], color=c.t['accent'], w=9)


def ic_smoke_selection(c):
    """Dashed marquee with the accent pointer: needs something selected."""
    dash_w = 12 if c.small else 7
    for a, b in (((16, 16), (36, 16)), ((48, 16), (68, 16)),
                 ((16, 16), (16, 36)), ((16, 48), (16, 68)),
                 ((68, 16), (68, 32)), ((16, 68), (32, 68))):
        c.line([a, b], w=dash_w)
    c.poly([(44, 44), (86, 60), (68, 68), (78, 86), (66, 92), (58, 72), (44, 84)],
           color=c.t['accent'], fill=c.t['accent'], w=0)


def ic_smoke_old(c):
    """Clock face: the version gate."""
    if c.small:
        c.circle(48, 48, 38, w=13)
        c.line([(48, 48), (48, 24)], color=c.t['accent'], w=12)
        c.line([(48, 48), (66, 58)], color=c.t['accent'], w=12)
    else:
        c.circle(48, 48, 36)
        c.line([(48, 48), (48, 22)], color=c.t['accent'], w=8)
        c.line([(48, 48), (68, 60)], color=c.t['accent'], w=8)


def ic_smoke_xaml(c):
    """Dialog window; the accent block is the button the XAML wires up."""
    if c.small:
        c.rrect(6, 12, 90, 84, rad=8, w=13)
        c.line([(6, 34), (90, 34)], w=13)
        c.rrect(50, 52, 78, 70, rad=4, w=0, fill=c.t['accent'])
    else:
        c.rrect(10, 14, 86, 82, rad=6)
        c.line([(10, 32), (86, 32)], w=6)
        c.line([(20, 48), (56, 48)], w=6)
        c.rrect(56, 58, 78, 72, rad=4, w=0, fill=c.t['accent'])


def ic_smoke_pane(c):
    """A docked panel against the edge in ink, with an accent content line:
    the live pane a dockpane bundle fills. This is the ribbon toggle's OFF art -
    the panel closed."""
    if c.small:
        c.rrect(4, 6, 60, 90, rad=6, w=12)
        c.line([(70, 20), (92, 20)], color=c.t['accent'], w=12)
        c.line([(70, 44), (92, 44)], color=c.t['accent'], w=12)
        c.line([(70, 68), (86, 68)], color=c.t['accent'], w=12)
    else:
        c.rrect(8, 10, 58, 86, rad=6)
        c.line([(68, 24), (90, 24)], color=c.t['accent'], w=7)
        c.line([(68, 46), (90, 46)], color=c.t['accent'], w=7)
        c.line([(68, 68), (84, 68)], color=c.t['accent'], w=7)


def ic_smoke_pane_on(c):
    """The same docked panel, filled solid with accent: the ribbon toggle's ON art,
    shown while the panel is open. Large only - a dockpane button is never shown
    at the small size, so there is no small on-art to draw."""
    c.rrect(8, 10, 58, 86, rad=6, w=0, fill=c.t['accent'])
    c.line([(68, 24), (90, 24)], color=c.t['ink'], w=7)
    c.line([(68, 46), (90, 46)], color=c.t['ink'], w=7)
    c.line([(68, 68), (84, 68)], color=c.t['ink'], w=7)


def brackets(c, color=None, w=None):
    """The element id family mark: the square brackets a design coordination
    model wraps its ids in ("Basic Wall [123456]"). Both element id glyphs
    carry it, and which half is accent says which way the tool runs."""
    if c.small:
        left, right, span, top, bottom = 12, 84, 14, 16, 80
    else:
        left, right, span, top, bottom = 16, 80, 12, 20, 76
    c.line([(left + span, top), (left, top), (left, bottom), (left + span, bottom)],
           color=color, w=w)
    c.line([(right - span, top), (right, top), (right, bottom), (right - span, bottom)],
           color=color, w=w)


def _id_block(c):
    """The element between the brackets, in the same box at both sizes."""
    return (34, 34, 62, 62) if c.small else (37, 37, 59, 59)


def ic_select_by_ids(c):
    # Ink brackets are the id you type; the accent block is the element you
    # get back.
    brackets(c)
    x0, y0, x1, y1 = _id_block(c)
    c.rrect(x0, y0, x1, y1, rad=4, w=0, fill=c.t['accent'])


def ic_element_id_settings(c):
    # The family brackets around a gear-ish knob: same mark as the two tools,
    # with the accent dot saying "the dial behind them".
    brackets(c)
    x0, y0, x1, y1 = _id_block(c)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    c.rrect(x0, y0, x1, y1, rad=(x1 - x0) / 2, w=6 if c.small else 5)
    r = 5 if c.small else 4
    c.rrect(cx - r, cy - r, cx + r, cy + r, rad=r, w=0, fill=c.t['accent'])


def ic_ids_of_selection(c):
    # The mirror: the ink block is the element you already have, and the
    # accent brackets are the ids the tool hands back.
    brackets(c, color=c.t['accent'])
    x0, y0, x1, y1 = _id_block(c)
    c.rrect(x0, y0, x1, y1, rad=4, w=6 if c.small else 5)


def sight(c, cy, radius, tick, w, arms):
    """The coordinate family mark: a ring with ticks standing off it, the
    sight both coordinate tools put on a point. Which ticks are drawn is up to
    the caller, because Go to Coordinates needs the air above the ring for its
    pin."""
    c.circle(48, cy, radius, w=w)
    for dx, dy in arms:
        c.line([(48 + dx * (radius + 5), cy + dy * (radius + 5)),
                (48 + dx * (radius + 5 + tick), cy + dy * (radius + 5 + tick))],
               w=w)


def _pin(c, cy, r, tip_y):
    """A map pin in accent: a head with a hole punched through it, tapering to
    a point. The hole is paper, not transparent, so the pin reads as a solid
    object sitting in front of the ring behind it."""
    col = c.t['accent']
    half = r * 0.74
    c.poly([(48 - half, cy + r * 0.52), (48 + half, cy + r * 0.52), (48, tip_y)],
           color=col, fill=col, w=0)
    c.disc(48, cy, r, col)
    c.disc(48, cy, r * 0.36, c.t['paper'])


def ic_get_coordinates(c):
    """The sight in ink with the accent dot at its centre: the tool reads the
    point off the measurement and hands it over."""
    if c.small:
        sight(c, 48, 22, 10, 13, ((0, -1), (0, 1), (-1, 0), (1, 0)))
        c.disc(48, 48, 11, c.t['accent'])
    else:
        sight(c, 48, 24, 11, 8, ((0, -1), (0, 1), (-1, 0), (1, 0)))
        c.disc(48, 48, 10, c.t['accent'])


def ic_go_to_coordinates(c):
    """The same sight, dropped to make room, with the accent pin landing in
    it: coordinates go in, the view arrives on them. The top ticks are gone
    rather than crossed, so nothing competes with the pin at 16px."""
    if c.small:
        sight(c, 74, 12, 9, 13, ((-1, 0), (1, 0)))
        _pin(c, 28, 19, 62)
    else:
        sight(c, 74, 12, 10, 8, ((-1, 0), (1, 0)))
        _pin(c, 28, 17, 62)


ICONS = {
    '01_pyNavis.panel/03_Console.pushbutton': ic_console,
    '01_pyNavis.panel/04_Reload.pushbutton': ic_reload,
    '01_pyNavis.panel/02_Shortcuts.pushbutton': ic_shortcuts,
    '01_pyNavis.panel/01_Settings.pushbutton': ic_settings,
    '01_pyNavis.panel/99_More.slideout/Panel_Slots.pushbutton': ic_panel_slots,
    '03_Clash.panel/01_Clash_Report.pushbutton': ic_clash_report,
    '04_Data.panel/01_Coordinates.stack/01_Get_Coordinates.pushbutton': ic_get_coordinates,
    '04_Data.panel/01_Coordinates.stack/02_Go_to_Coordinates.pushbutton': ic_go_to_coordinates,
    '04_Data.panel/02_Element_IDs.stack/01_Select_by_IDs.pushbutton': ic_select_by_ids,
    '04_Data.panel/02_Element_IDs.stack/02_IDs_of_Selection.pushbutton': ic_ids_of_selection,
    '04_Data.panel/99_More.slideout/Element_ID_Settings.pushbutton': ic_element_id_settings,
    '04_Data.panel/04_Export_Viewpoints_CSV.pushbutton': ic_export_csv,
    '03_Clash.panel/05_True_Distance.pushbutton': ic_true_distance,
    '03_Clash.panel/03_Clear_Clash.pushbutton': ic_resolve_clash,
    '03_Clash.panel/04_Set_Gap.pushbutton': ic_set_gap,
    '04_Data.panel/03_Excel_Sets.pushbutton': ic_sets_from_excel,
    '03_Clash.panel/02_Clash_Grouper.pushbutton': ic_grouper,
    'Viewpoints.panel/01_Manage.stack/01_Renamer.pushbutton': ic_vp_renamer,
    'Viewpoints.panel/01_Manage.stack/02_Deleter.pushbutton': ic_vp_deleter,
    'Viewpoints.panel/01_Manage.stack/03_Manager.pushbutton': ic_vp_manager,
    'Viewpoints.panel/02_Section.stack/01_Fit.pushbutton': ic_section_fit,
    'Viewpoints.panel/02_Section.stack/02_Plan.pushbutton': ic_section_plan,
    'Viewpoints.panel/02_Section.stack/03_Clear.pushbutton': ic_section_clear,
    'Viewpoints.panel/03_State.stack/01_Copy_State.pushbutton': ic_state_copy,
    'Viewpoints.panel/03_State.stack/02_Paste_State.pushbutton': ic_state_paste,
    'Viewpoints.panel/04_Speeds.stack/01_Apply_Speeds.pushbutton': ic_reset_speeds,
    'Viewpoints.panel/04_Speeds.stack/02_Speeds_to_Saved.pushbutton': ic_reset_viewpoints,
    'Viewpoints.panel/05_Viewpoint_Tracker.dockpane': ic_viewpoint_tracker,
    'Viewpoints.panel/06_Section_Nudge.dockpane': ic_section_nudge,
    'Viewpoints.panel/99_More.slideout/Tracker_Window.pushbutton': ic_tracker_window,
    '05_AI_(beta).panel/01_Ask_AI.dockpane': ic_ask_ai,
    '05_AI_(beta).panel/02_Setup.stack/01_AI_Settings.pushbutton': ic_ai_settings,
    '05_AI_(beta).panel/02_Setup.stack/02_Open_AI_Folder.pushbutton': ic_open_ai_folder,
    '02_Selection.panel/01_Remember.pushbutton': ic_memorize,
    '02_Selection.panel/02_Recall.pushbutton': ic_recall,
    '02_Selection.panel/03_Set.stack/01_Add.pushbutton': ic_add,
    '02_Selection.panel/03_Set.stack/02_Subtract.pushbutton': ic_subtract,
    '02_Selection.panel/03_Set.stack/03_Intersect.pushbutton': ic_intersect,
    '02_Selection.panel/04_Step.stack/01_Prev.pushbutton': ic_prev,
    '02_Selection.panel/04_Step.stack/02_Next.pushbutton': ic_next,
    '02_Selection.panel/04_Step.stack/03_Forget.pushbutton': ic_clear,
    '02_Selection.panel/05_More.pulldown': ic_memory_menu,
    '02_Selection.panel/05_More.pulldown/01_Show_Contents.pushbutton': ic_show,
    '02_Selection.panel/05_More.pulldown/02_Save_as_Set.pushbutton': ic_save_set,
    '02_Selection.panel/05_More.pulldown/03_Purge.pushbutton': ic_purge,
}

SMOKE_ICONS = {
    'Buttons.panel/02_Smart_Button.smartbutton': ic_smoke_smart,
    'Buttons.panel/03_Split_LastUsed.splitbutton': ic_smoke_split_last,
    'Buttons.panel/03_Split_LastUsed.splitbutton/01_Alpha.pushbutton': ic_smoke_child_first,
    'Buttons.panel/03_Split_LastUsed.splitbutton/02_Beta.pushbutton': ic_smoke_child_second,
    'Buttons.panel/04_Split_Fixed.splitpushbutton': ic_smoke_split_fixed,
    'Buttons.panel/04_Split_Fixed.splitpushbutton/01_First.pushbutton': ic_smoke_child_first,
    'Buttons.panel/04_Split_Fixed.splitpushbutton/02_Second.pushbutton': ic_smoke_child_second,
    'Buttons.panel/05_Url_Demo.urlbutton': ic_smoke_url,
    'Buttons.panel/06_Link_Missing.linkbutton': ic_smoke_link,
    'Buttons.panel/07_Needs_Selection.pushbutton': ic_smoke_selection,
    'Buttons.panel/09_Old_Only.pushbutton': ic_smoke_old,
    'Buttons.panel/10_Lib_Check.pushbutton': ic_smoke_lib,
    'Dialogs.panel/Xaml_Dialog.pushbutton': ic_smoke_xaml,
    'Panes.panel/01_Pane_Demo.dockpane': ic_smoke_pane,
}

# (tab root, bundle) -> (off drawing, on drawing). Toggles get icon.off/.on
# pairs instead of a plain icon.png; the small files render the off state.
TOGGLES = {
    (SMOKE_TAB, 'Buttons.panel/01_Toggle_Demo.toggle'):
        (ic_smoke_toggle_off, ic_smoke_toggle_on),
}

# (tab root, bundle) -> on drawing. A *.dockpane keeps its ordinary icon.png as the
# standard four (registered in ICONS/SMOKE_ICONS above, drawn OFF/closed) and gets
# ONE extra pair, icon.on(.dark).png, for its ribbon toggle's pressed state - large
# only, since a dockpane button is never shown at the small size.
DOCKPANE_ON = {
    (SMOKE_TAB, 'Panes.panel/01_Pane_Demo.dockpane'): ic_smoke_pane_on,
    (TAB, 'Viewpoints.panel/05_Viewpoint_Tracker.dockpane'): ic_viewpoint_tracker_on,
    (TAB, 'Viewpoints.panel/06_Section_Nudge.dockpane'): ic_section_nudge_on,
    (TAB, '05_AI_(beta).panel/01_Ask_AI.dockpane'): ic_ask_ai_on,
}

GROUPS = [(TAB, ICONS), (SMOKE_TAB, SMOKE_ICONS)]


def render(fn, theme, small):
    c = Ctx(theme, WEIGHT_SMALL if small else WEIGHT_LARGE)
    fn(c)
    return c


def write_all():
    missing = [b for root, icons in GROUPS for b in icons
               if not os.path.isdir(os.path.join(root, b.replace('/', os.sep)))]
    missing += [b for root, b in TOGGLES
                if not os.path.isdir(os.path.join(root, b.replace('/', os.sep)))]
    missing += [b for root, b in DOCKPANE_ON
                if not os.path.isdir(os.path.join(root, b.replace('/', os.sep)))]
    if missing:
        raise SystemExit('bundle folder(s) missing:\n  ' + '\n  '.join(sorted(missing)))

    written = 0
    bundles = 0
    for root, icons in GROUPS:
        for bundle, fn in sorted(icons.items()):
            folder = os.path.join(root, bundle.replace('/', os.sep))
            for suffix, theme in THEMES:
                render(fn, theme, False).png(LARGE_PX).save(
                    os.path.join(folder, 'icon%s.png' % suffix))
                render(fn, theme, True).png(SMALL_PX).save(
                    os.path.join(folder, 'icon.small%s.png' % suffix))
                written += 2
            bundles += 1
            print('  ' + bundle)
    for (root, bundle), (off_fn, on_fn) in sorted(TOGGLES.items()):
        folder = os.path.join(root, bundle.replace('/', os.sep))
        for suffix, theme in THEMES:
            render(off_fn, theme, False).png(LARGE_PX).save(
                os.path.join(folder, 'icon.off%s.png' % suffix))
            render(on_fn, theme, False).png(LARGE_PX).save(
                os.path.join(folder, 'icon.on%s.png' % suffix))
            render(off_fn, theme, True).png(SMALL_PX).save(
                os.path.join(folder, 'icon.small%s.png' % suffix))
            written += 3
        bundles += 1
        print('  ' + bundle + ' (toggle)')
    for (root, bundle), on_fn in sorted(DOCKPANE_ON.items()):
        folder = os.path.join(root, bundle.replace('/', os.sep))
        for suffix, theme in THEMES:
            render(on_fn, theme, False).png(LARGE_PX).save(
                os.path.join(folder, 'icon.on%s.png' % suffix))
            written += 1
        bundles += 1
        print('  ' + bundle + ' (dockpane on-art)')
    print('done: %d bundles, %d files' % (bundles, written))


def write_sheet(path):
    """Contact sheet for reviewing the whole set without opening Navisworks."""
    entries = [(b, fn) for _, icons in GROUPS for b, fn in icons.items()]
    for (_, bundle), (off_fn, on_fn) in sorted(TOGGLES.items()):
        entries.append((bundle + '_off', off_fn))
        entries.append((bundle + '_on', on_fn))
    for (_, bundle), on_fn in sorted(DOCKPANE_ON.items()):
        entries.append((bundle + '_on', on_fn))
    img = Image.new('RGB', (560, 24 + len(entries) * 60), (243, 243, 243))
    d = ImageDraw.Draw(img)
    y = 14
    for bundle, fn in entries:
        label = bundle.rsplit('/', 1)[-1].split('.')[0]
        d.text((14, y + 20), label.lstrip('0123456789_')[:24], fill=(60, 60, 60))
        img.paste(Image.new('RGB', (170, 56), (31, 31, 31)), (330, y - 4))
        for x, theme in ((200, LIGHT), (350, DARK)):
            big = render(fn, theme, False).png(48)
            img.paste(big, (x, y), big)
            sm = render(fn, theme, True).png(16)
            img.paste(sm, (x + 62, y + 16), sm)
        y += 60
    img.save(path)
    print('sheet: ' + path)


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--sheet', help='also write a contact sheet here')
    ap.add_argument('--sheet-only', action='store_true',
                    help='write only the contact sheet, touch no bundle')
    args = ap.parse_args()

    if not args.sheet_only:
        write_all()
    if args.sheet:
        write_sheet(args.sheet)


if __name__ == '__main__':
    main()
