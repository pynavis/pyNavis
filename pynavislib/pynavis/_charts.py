"""Pure-python SVG chart renderers for the output window.

Charts theme with the output window instead of hard-coding colors: every
fill/stroke a chart uses comes from a CSS custom property already declared
on :root by HtmlPage's skeleton (``--chart-1`` .. ``--chart-6`` for series,
``--line`` for axes/grid, ``--muted``/``--fg`` for text). Series color N is
always ``var(--chart-N)`` (N wraps 1..6), so the SAME svg markup renders
correctly in both the light and dark output windows with no re-render - the
browser just re-resolves the variables when the stylesheet changes.

No gradients, no animation, no inline hex colors: that would defeat the
theming contract above. All user-supplied text (labels, titles) goes through
``pynavis._markdown.escape`` before landing in the markup. Numbers are
formatted with ``font-variant-numeric: tabular-nums`` so digits line up.

Written for IronPython 3.4: no f-strings, ``%``/``.format`` only.
"""

import math

from pynavis._markdown import escape

_SERIES_COLORS = 6

# A slice at least this wide is drawn as a full circle rather than an arc:
# past it the two arc endpoints round to the same %.2f coordinates and the
# path degenerates to nothing.
_FULL_CIRCLE_DEGREES = 359.9


def _color(index):
    return 'var(--chart-%d)' % ((index % _SERIES_COLORS) + 1)


def _svg_open(width, height):
    return ('<svg xmlns="http://www.w3.org/2000/svg" class="pynavis-chart" '
            'viewBox="0 0 %d %d" width="%d" height="%d">'
            % (width, height, width, height))


def _svg_close():
    return '</svg>'


def _title_text(width, title):
    if not title:
        return ''
    return ('<text x="%d" y="20" text-anchor="middle" fill="var(--fg)" '
            'font-size="13" font-weight="600">%s</text>'
            % (width // 2, escape(str(title))))


def _label_text(x, y, text, anchor='middle'):
    return ('<text x="%.1f" y="%.1f" text-anchor="%s" fill="var(--muted)" '
            'font-size="12">%s</text>' % (x, y, anchor, escape(str(text))))


def _fmt_value(value):
    if value == int(value):
        return str(int(value))
    text = '%.2f' % value
    return text.rstrip('0').rstrip('.')


def _value_text(x, y, value, anchor='middle'):
    return ('<text x="%.1f" y="%.1f" text-anchor="%s" fill="var(--muted)" '
            'font-size="12" style="font-variant-numeric:tabular-nums">%s'
            '</text>' % (x, y, anchor, escape(_fmt_value(value))))


def _no_data_svg(width, height, title):
    parts = [_svg_open(width, height), _title_text(width, title)]
    parts.append('<rect x="8" y="8" width="%d" height="%d" fill="none" '
                  'stroke="var(--line)" rx="4"/>' % (width - 16, height - 16))
    parts.append(_label_text(width / 2.0, height / 2.0 + 4, 'no data'))
    parts.append(_svg_close())
    return ''.join(parts)


def _scale(values, pixels):
    """Proportional pixel lengths for values, 0..pixels. Never divides by
    zero: an empty/non-positive peak yields all-zero lengths."""
    peak = max(values) if values else 0
    if peak <= 0:
        return [0.0 for _ in values]
    return [max(0.0, v) * pixels / float(peak) for v in values]


def _is_degenerate(labels, values):
    return not labels or not values or max([0] + list(values)) <= 0


def _number(value):
    """Any cell of a chart's value list -> a finite float.

    None, NaN, infinities and anything that will not convert all become
    0.0. Charts are a display of data a script already has; a single bad
    cell must draw as an empty bar, never raise out of print_chart_bar()
    halfway through a report."""
    if value is None:
        return 0.0
    try:
        number = float(value)
    except (TypeError, ValueError):
        return 0.0
    if math.isnan(number) or math.isinf(number):
        return 0.0
    return number


def _paired(labels, values):
    """Labels and values truncated to the shorter of the two, values
    coerced with ``_number``. Mismatched lengths are a caller bug, but a
    silently shorter chart beats an IndexError out of the renderer."""
    labels = list(labels)
    values = [_number(value) for value in values]
    count = min(len(labels), len(values))
    return labels[:count], values[:count]


def bar_svg(labels, values, title=None, width=560, height=280):
    """Bar chart. Horizontal (categories stacked top to bottom) when there
    are more than 8 categories - long vertical label text overlaps past
    that; vertical (side-by-side columns) otherwise.

    labels and values are truncated to the shorter of the two, and
    None/NaN/infinite/unconvertible values are drawn as 0."""
    labels, values = _paired(labels, values)
    if _is_degenerate(labels, values):
        return _no_data_svg(width, height, title)
    if len(labels) > 8:
        return _bar_svg_horizontal(labels, values, title, width, height)
    return _bar_svg_vertical(labels, values, title, width, height)


def _bar_svg_vertical(labels, values, title, width, height):
    top = 34 if title else 16
    bottom, left, right = 30, 14, 14
    plot_w = width - left - right
    plot_h = height - top - bottom - 16  # room for value labels above bars
    n = len(labels)
    slot = plot_w / float(n)
    bar_w = slot * 0.6
    lengths = _scale(values, plot_h)
    base_y = top + 16 + plot_h

    parts = [_svg_open(width, height), _title_text(width, title)]
    parts.append('<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" '
                  'stroke="var(--line)"/>' % (left, base_y, left + plot_w, base_y))
    for i in range(n):
        cx = left + slot * i + slot / 2.0
        bar_h = lengths[i]
        bar_y = base_y - bar_h
        parts.append('<rect x="%.1f" y="%.1f" width="%.1f" height="%.1f" '
                      'fill="%s"/>' % (cx - bar_w / 2.0, bar_y, bar_w, bar_h, _color(i)))
        parts.append(_value_text(cx, bar_y - 4, values[i]))
        parts.append(_label_text(cx, base_y + 16, labels[i]))
    parts.append(_svg_close())
    return ''.join(parts)


def _bar_svg_horizontal(labels, values, title, width, height):
    top = 30 if title else 10
    bottom, label_col, right = 10, 100, 50
    plot_h = height - top - bottom
    plot_w = width - label_col - right
    n = len(labels)
    slot = plot_h / float(n)
    bar_h = slot * 0.6
    lengths = _scale(values, plot_w)
    axis_x = label_col

    parts = [_svg_open(width, height), _title_text(width, title)]
    parts.append('<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" '
                  'stroke="var(--line)"/>' % (axis_x, top, axis_x, top + plot_h))
    for i in range(n):
        cy = top + slot * i + slot / 2.0
        bar_w = lengths[i]
        parts.append('<rect x="%.1f" y="%.1f" width="%.1f" height="%.1f" '
                      'fill="%s"/>' % (axis_x, cy - bar_h / 2.0, bar_w, bar_h, _color(i)))
        parts.append(_label_text(axis_x - 8, cy + 4, labels[i], anchor='end'))
        parts.append(_value_text(axis_x + bar_w + 6, cy + 4, values[i], anchor='start'))
    parts.append(_svg_close())
    return ''.join(parts)


def line_svg(labels, series, title=None, width=560, height=280):
    """Line chart; every series in the ``{name: [values]}`` dict shares the
    label axis. Series are drawn in name-sorted order for a stable color
    assignment (dict order is not guaranteed on IronPython 3.4).

    Each series is truncated to the label count (a shorter series just
    ends early), and None/NaN/infinite/unconvertible values are plotted
    as 0."""
    labels = list(labels)
    n = len(labels)
    series = dict((name, [_number(v) for v in list(values)[:n]])
                  for name, values in dict(series).items())
    # Peak (and the degenerate check) must come from the SAME truncated
    # values _polyline actually draws - a series longer than the label axis
    # has a never-plotted tail that must not be allowed to scale the chart.
    all_values = [v for values in series.values() for v in values]
    if _is_degenerate(labels, all_values):
        return _no_data_svg(width, height, title)

    top = 34 if title else 16
    bottom, left, right = 30, 14, 14
    plot_w = width - left - right
    plot_h = height - top - bottom
    peak = max(all_values)
    step = plot_w / float(n - 1) if n > 1 else 0.0
    base_y = top + plot_h

    parts = [_svg_open(width, height), _title_text(width, title)]
    parts.append('<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" '
                  'stroke="var(--line)"/>' % (left, base_y, left + plot_w, base_y))
    for i, name in enumerate(sorted(series)):
        parts.append(_polyline(series[name], step, left, top, plot_h, peak, i))
    for i, label in enumerate(labels):
        # The end labels anchor inward. Centred on the first and last data
        # point they hang half their width outside the viewBox and the SVG
        # clips them, which cost the first and last category their name.
        anchor = 'start' if i == 0 else ('end' if i == n - 1 else 'middle')
        parts.append(_label_text(left + step * i, base_y + 16, label, anchor))
    parts.append(_svg_close())
    return ''.join(parts)


def _polyline(values, step, left, top, plot_h, peak, color_index):
    points = []
    for i, v in enumerate(values):
        x = left + step * i
        y = top + plot_h - (max(0.0, v) * plot_h / float(peak))
        points.append('%.1f,%.1f' % (x, y))
    return ('<polyline points="%s" fill="none" stroke="%s" stroke-width="2"/>'
            % (' '.join(points), _color(color_index)))


def _fmt_pct(value, total):
    pct = value * 100.0 / total
    if pct == int(pct):
        return '%d%%' % int(pct)
    return '%.1f%%' % pct


def pie_svg(labels, values, title=None, doughnut=False, width=560, height=280):
    """Pie/doughnut chart. Slice labels sit just outside the ring and carry
    the share as a percentage of the (positive) total.

    labels and values are truncated to the shorter of the two, and
    None/NaN/infinite/unconvertible values count as 0 (so they get no
    slice)."""
    labels, values = _paired(labels, values)
    total = sum(v for v in values if v > 0)
    if not labels or not values or total <= 0:
        return _no_data_svg(width, height, title)

    cx, cy = width / 2.0, height / 2.0 + (10 if title else 0)
    r = min(width, height) / 2.0 - 40
    inner_r = r * 0.55 if doughnut else 0.0

    parts = [_svg_open(width, height), _title_text(width, title)]
    angle = -90.0
    for i, (label, value) in enumerate(zip(labels, values)):
        if value <= 0:
            continue
        span = min(360.0, value * 360.0 / total)
        if span >= _FULL_CIRCLE_DEGREES:
            parts.append(_full_circle(cx, cy, r, inner_r, _color(i)))
        else:
            parts.append(_pie_slice(cx, cy, r, inner_r, angle, span, _color(i)))
        parts.append(_pie_label(cx, cy, r, angle + span / 2.0, label, value, total))
        angle += span
    parts.append(_svg_close())
    return ''.join(parts)


def _full_circle(cx, cy, r, inner_r, color):
    """The whole ring as one shape, for a slice that covers (nearly) all of
    it. An arc whose start and end points round to the same coordinates is
    zero-length, and SVG draws nothing at all for it - so a single-category
    pie rendered as a path would come out blank. A <circle> (or, for a
    doughnut, a circle stroked at the ring's width) has no endpoints to
    collapse."""
    if inner_r <= 0:
        return '<circle cx="%.2f" cy="%.2f" r="%.2f" fill="%s"/>' % (cx, cy, r, color)
    mid_r = (r + inner_r) / 2.0
    return ('<circle cx="%.2f" cy="%.2f" r="%.2f" fill="none" stroke="%s" '
            'stroke-width="%.2f"/>' % (cx, cy, mid_r, color, r - inner_r))


def _arc_point(cx, cy, r, degrees):
    rad = math.radians(degrees)
    return cx + r * math.cos(rad), cy + r * math.sin(rad)


def _pie_slice(cx, cy, r, inner_r, start, span, color):
    end = start + span
    large = 1 if span > 180.0 else 0
    x0, y0 = _arc_point(cx, cy, r, start)
    x1, y1 = _arc_point(cx, cy, r, end)
    if inner_r <= 0:
        path = ('M%.2f,%.2f L%.2f,%.2f A%.2f,%.2f 0 %d,1 %.2f,%.2f Z'
                % (cx, cy, x0, y0, r, r, large, x1, y1))
    else:
        ix1, iy1 = _arc_point(cx, cy, inner_r, end)
        ix0, iy0 = _arc_point(cx, cy, inner_r, start)
        path = ('M%.2f,%.2f A%.2f,%.2f 0 %d,1 %.2f,%.2f L%.2f,%.2f '
                'A%.2f,%.2f 0 %d,0 %.2f,%.2f Z'
                % (x0, y0, r, r, large, x1, y1, ix1, iy1,
                   inner_r, inner_r, large, ix0, iy0))
    return '<path d="%s" fill="%s"/>' % (path, color)


def _pie_label(cx, cy, r, mid_angle, label, value, total):
    lx, ly = _arc_point(cx, cy, r + 14, mid_angle)
    cos_a = math.cos(math.radians(mid_angle))
    anchor = 'start' if cos_a > 0.15 else ('end' if cos_a < -0.15 else 'middle')
    text = '%s %s' % (str(label), _fmt_pct(value, total))
    return _label_text(lx, ly + 4, text, anchor=anchor)
