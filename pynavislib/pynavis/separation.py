"""How far one triangle mesh must slide along a world axis to stop touching
another.

Pure: plain tuples in, plain dicts out, no Navisworks import. Resolve Clash
feeds it the two selected items' world triangles (pynavis.geometry) and
applies the answer as a permanent transform.

The answer is exact for the chosen direction and works on any closed or
open mesh, convex or not. Slide the mover along the axis and every pair of
triangles (one from each mesh) is in contact for one closed range of
travel; the required move is the first travel past every range that
reaches back to zero. The ranges come from the contact events of the pair
seen down the axis: a vertex of one over the face of the other, a vertex on
an edge, or two edges crossing. Working in the projected plane keeps
vertical faces (those parallel to the axis) ordinary: they simply project
to segments and contribute through their edges.

Touching counts as contact, the way a hard clash at zero tolerance does,
so a result of "clear" means the two meshes share no point at all. Two
solids where one sits entirely inside the other share no surface point
either, and read as clear here just as they do in Clash Detective.

Clearance is measured along the axis moved: a move of "gap plus clearance"
leaves that much air in the direction of travel.
"""

import math
import time

# Six axis directions: the letter picks the world axis, the sign the way.
AXES = ('+x', '-x', '+y', '-y', '+z', '-z')

_INDEX = {'x': 0, 'y': 1, 'z': 2}

# Any single triangle spanning more than this many broadphase cells is kept
# on a short "always test" list instead of being stamped into every cell.
_BIG_TRIANGLE_CELLS = 256

# How often the pair loop looks at the clock.
_CLOCK_EVERY = 2000


class TooDetailed(Exception):
    """Raised when the pair walk runs past the caller's time budget."""


def resolve(mover, obstacle, axes=None, clearance=0.0, eps=None, budget_seconds=None):
    """The shortest axis-aligned move that takes mover clear of obstacle by
    at least clearance, measured along the axis moved.

    mover, obstacle: [((x,y,z), (x,y,z), (x,y,z)), ...] in the same frame.
    axes: which of AXES to try; None tries all six.
    budget_seconds: raise TooDetailed once the walk has run this long.

    Returns {'status': 'clash' | 'tight' | 'clear',
             'axis': best axis, 'move': length along it (clearance included),
             'vector': (dx, dy, dz), 'gap': nearest contact along the best
             axis before moving (0 when touching, None when they never meet
             along it), 'candidates': [(axis, move, exact), ...] shortest
             first; exact False marks a lower bound from an axis abandoned
             once it could not beat the best}.
    'clash' means the meshes touch or cross now; 'tight' means they are
    apart but by less than the clearance along some axis; 'clear' means
    nothing to do, and then axis and gap describe the nearest contact found
    (gap None when no axis meets the obstacle at all).
    """
    if not mover or not obstacle:
        raise ValueError('both meshes need at least one triangle')
    if axes is None:
        axes = AXES
    if eps is None:
        eps = _epsilon(mover, obstacle)
    deadline = None if budget_seconds is None else time.time() + budget_seconds

    # Tightest bounding-box bound first, so later axes can give up early.
    ordered = sorted(axes, key=lambda a: (_upper_bound(mover, obstacle, a), AXES.index(a)))
    results = []
    best = None
    for axis in ordered:
        r = along(mover, obstacle, axis, eps=eps, clearance=clearance,
                  abort_at=best, deadline=deadline)
        results.append(r)
        if r['exact'] and (best is None or r['move'] < best):
            best = r['move']

    exact = [r for r in results if r['exact']]
    if any(r['contact'] for r in results):
        chosen = min(exact, key=lambda r: (r['move'], AXES.index(r['axis'])))
        # Touching with no clearance asked for needs no move at all.
        status = 'clash' if chosen['move'] > eps else 'clear'
    else:
        tight = [r for r in exact
                 if r['gap'] is not None and r['gap'] < clearance - eps and r['move'] > eps]
        if tight:
            status = 'tight'
            chosen = min(tight, key=lambda r: (r['move'], AXES.index(r['axis'])))
        else:
            status = 'clear'
            nearest = [r for r in results if r['gap'] is not None]
            chosen = (min(nearest, key=lambda r: (r['gap'], AXES.index(r['axis'])))
                      if nearest else exact[0])
    move = chosen['move'] if status != 'clear' else 0.0
    ranked = sorted(results, key=lambda r: (r['move'], AXES.index(r['axis'])))
    return {
        'status': status,
        'axis': chosen['axis'],
        'move': move,
        'vector': _vector(chosen['axis'], move),
        'gap': chosen['gap'],
        'candidates': [(r['axis'], r['move'], r['exact']) for r in ranked],
    }


def along(mover, obstacle, axis, eps=None, clearance=0.0, abort_at=None, deadline=None):
    """Contact along one axis: {'axis', 'move', 'contact', 'gap', 'exact'}.

    move: travel along the axis after which the meshes are apart by at
          least clearance (0 when they already are).
    contact: True when they touch or cross before moving.
    gap: unsigned distance along the axis to the nearest contact before
         moving, 0 when touching, None when they never meet along it.
    exact: False when abort_at was given and a contact already in progress
           runs to at least abort_at, so the walk stopped early and move is
           only a lower bound.
    """
    if not mover or not obstacle:
        raise ValueError('both meshes need at least one triangle')
    if eps is None:
        eps = _epsilon(mover, obstacle)
    k, sign = _INDEX[axis[1]], (1.0 if axis[0] == '+' else -1.0)
    frame_a = [_prepare(t, k, sign, eps) for t in mover]
    frame_b = [_prepare(t, k, sign, eps) for t in obstacle]

    intervals = []
    count = 0
    for tri_a, tri_b in _pairs(frame_a, frame_b, eps):
        count += 1
        if deadline is not None and count % _CLOCK_EVERY == 0 and time.time() > deadline:
            raise TooDetailed('ran out of time along %s after %d pairs' % (axis, count))
        span = _contact_range(tri_a, tri_b, eps)
        if span is None:
            continue
        intervals.append(span)
        if abort_at is not None and span[0] <= eps and span[1] + clearance >= abort_at - eps:
            return {'axis': axis, 'move': span[1] + clearance, 'contact': True,
                    'gap': 0.0, 'exact': False}

    contact = any(lo <= eps and hi >= -eps for lo, hi in intervals)
    move = _first_clear([(lo - clearance, hi + clearance) for lo, hi in intervals], eps)
    return {'axis': axis, 'move': move, 'contact': contact, 'gap': _gap(intervals, eps),
            'exact': True}


# --- frames and tolerances ---------------------------------------------------

def _epsilon(mover, obstacle):
    """Slack for on-edge and touching tests, scaled to the coordinates so
    float32 vertices far from the origin still register as touching."""
    biggest = 1.0
    for mesh in (mover, obstacle):
        for tri in mesh:
            for p in tri:
                for c in p:
                    if abs(c) > biggest:
                        biggest = abs(c)
    return 1e-6 * biggest


def _prepare(tri, k, sign, eps):
    """Triangle in the axis frame, with what every pair test needs:
    (points (u, v, w), signed doubled area, edge lengths, vertical flag).
    u and v run across the axis, w along it."""
    ku, kv = (k + 1) % 3, (k + 2) % 3
    pts = tuple((p[ku], p[kv], sign * p[k]) for p in tri)
    (au, av, _), (bu, bv, _), (cu, cv, _) = pts
    area2 = (bu - au) * (cv - av) - (cu - au) * (bv - av)
    lengths = (math.hypot(cu - bu, cv - bv),      # opposite a: edge b-c
               math.hypot(au - cu, av - cv),      # opposite b: edge c-a
               math.hypot(bu - au, bv - av))      # opposite c: edge a-b
    vertical = abs(area2) <= eps * (lengths[0] + lengths[1] + lengths[2])
    return pts, area2, lengths, vertical


def _upper_bound(mover, obstacle, axis):
    """Bounding-box move that certainly clears: obstacle's far end minus the
    mover's near end along the axis."""
    k, sign = _INDEX[axis[1]], (1.0 if axis[0] == '+' else -1.0)
    a_min = min(sign * p[k] for t in mover for p in t)
    b_max = max(sign * p[k] for t in obstacle for p in t)
    return max(0.0, b_max - a_min)


def _vector(axis, move):
    k, sign = _INDEX[axis[1]], (1.0 if axis[0] == '+' else -1.0)
    v = [0.0, 0.0, 0.0]
    v[k] = sign * move
    return tuple(v)


# --- broadphase ---------------------------------------------------------------

def _uv_box(pts):
    us = (pts[0][0], pts[1][0], pts[2][0])
    vs = (pts[0][1], pts[1][1], pts[2][1])
    return min(us), min(vs), max(us), max(vs)


def _pairs(frame_a, frame_b, eps):
    """Triangle pairs whose projections overlap, via a uniform grid over the
    obstacle's projected bounds."""
    boxes_b = [_uv_box(t[0]) for t in frame_b]
    u0 = min(b[0] for b in boxes_b) - eps
    v0 = min(b[1] for b in boxes_b) - eps
    u1 = max(b[2] for b in boxes_b) + eps
    v1 = max(b[3] for b in boxes_b) + eps
    n = len(frame_b)
    divisions = max(1, min(64, int(math.sqrt(n))))
    cell = max((u1 - u0) / divisions, (v1 - v0) / divisions, eps * 10, 1e-12)
    columns = int((u1 - u0) // cell) + 1

    grid = {}
    big = []
    for index, (bu0, bv0, bu1, bv1) in enumerate(boxes_b):
        cu0, cv0 = int((bu0 - u0) // cell), int((bv0 - v0) // cell)
        cu1, cv1 = int((bu1 - u0) // cell), int((bv1 - v0) // cell)
        if (cu1 - cu0 + 1) * (cv1 - cv0 + 1) > _BIG_TRIANGLE_CELLS:
            big.append(index)
            continue
        for cu in range(cu0, cu1 + 1):
            base = cu * columns
            for cv in range(cv0, cv1 + 1):
                grid.setdefault(base + cv, []).append(index)

    for tri_a in frame_a:
        au0, av0, au1, av1 = _uv_box(tri_a[0])
        if au1 < u0 or au0 > u1 or av1 < v0 or av0 > v1:
            continue
        seen = set()
        cu0, cv0 = max(0, int((au0 - u0) // cell)), max(0, int((av0 - v0) // cell))
        cu1, cv1 = int((au1 - u0) // cell), int((av1 - v0) // cell)
        for cu in range(cu0, cu1 + 1):
            base = cu * columns
            for cv in range(cv0, cv1 + 1):
                for index in grid.get(base + cv, ()):
                    if index in seen:
                        continue
                    seen.add(index)
                    bu0, bv0, bu1, bv1 = boxes_b[index]
                    if au1 + eps >= bu0 and au0 - eps <= bu1 and av1 + eps >= bv0 and av0 - eps <= bv1:
                        yield tri_a, frame_b[index]
        for index in big:
            bu0, bv0, bu1, bv1 = boxes_b[index]
            if au1 + eps >= bu0 and au0 - eps <= bu1 and av1 + eps >= bv0 and av0 - eps <= bv1:
                yield tri_a, frame_b[index]


# --- one triangle pair --------------------------------------------------------

def _contact_range(tri_a, tri_b, eps):
    """[lo, hi] of travel t along w for which tri_a + t meets tri_b, or None.

    Every extreme of that range is a contact event visible in the (u, v)
    projection: a vertex of one triangle over the face of the other (edges
    inclusive), or two edges crossing. A vertical triangle has no face to
    speak of, so its vertices-on-edges are tested instead. Collect every
    event and take the ends.
    """
    p, area_p, lengths_p, vertical_p = tri_a
    q, area_q, lengths_q, vertical_q = tri_b
    events = []

    # vertices of q against p: t = q.w - w(p at q)
    if vertical_p:
        for vertex in q:
            for i in range(3):
                for w in _w_on_edge(vertex, p[i], p[(i + 1) % 3], eps):
                    events.append(vertex[2] - w)
    else:
        for vertex in q:
            w = _w_in_face(vertex, p, area_p, lengths_p, eps)
            if w is not None:
                events.append(vertex[2] - w)

    # vertices of p against q: t = w(q at p) - p.w
    if vertical_q:
        for vertex in p:
            for i in range(3):
                for w in _w_on_edge(vertex, q[i], q[(i + 1) % 3], eps):
                    events.append(w - vertex[2])
    else:
        for vertex in p:
            w = _w_in_face(vertex, q, area_q, lengths_q, eps)
            if w is not None:
                events.append(w - vertex[2])

    # proper crossings of an edge of p with an edge of q
    for i in range(3):
        a, b = p[i], p[(i + 1) % 3]
        r_u, r_v = b[0] - a[0], b[1] - a[1]
        for j in range(3):
            c, d = q[j], q[(j + 1) % 3]
            s_u, s_v = d[0] - c[0], d[1] - c[1]
            denominator = r_u * s_v - r_v * s_u
            if abs(denominator) <= eps * eps:
                continue
            qp_u, qp_v = c[0] - a[0], c[1] - a[1]
            t = (qp_u * s_v - qp_v * s_u) / denominator
            if t < 0.0 or t > 1.0:
                continue
            u = (qp_u * r_v - qp_v * r_u) / denominator
            if u < 0.0 or u > 1.0:
                continue
            events.append((c[2] + u * (d[2] - c[2])) - (a[2] + t * (b[2] - a[2])))

    if not events:
        return None
    return min(events), max(events)


def _w_on_edge(point, a, b, eps):
    """w values of edge a-b at point's (u, v) if the point lies on the
    projected edge: one value, or both ends when the edge projects to a
    point, or nothing."""
    du, dv = b[0] - a[0], b[1] - a[1]
    pu, pv = point[0] - a[0], point[1] - a[1]
    length2 = du * du + dv * dv
    if length2 <= eps * eps:
        if pu * pu + pv * pv <= eps * eps:
            return (a[2], b[2])
        return ()
    length = math.sqrt(length2)
    if abs(du * pv - dv * pu) > eps * length:
        return ()
    s = (pu * du + pv * dv) / length2
    if s < -eps / length or s > 1.0 + eps / length:
        return ()
    return (a[2] + s * (b[2] - a[2]),)


def _w_in_face(point, tri, area2, lengths, eps):
    """w of the plane of tri at point's (u, v) when the point projects inside
    the triangle or within eps of one of its edges, else None."""
    (au, av, aw), (bu, bv, bw), (cu, cv, cw) = tri
    pu, pv = point[0], point[1]
    sign = 1.0 if area2 > 0 else -1.0
    s0 = ((bu - pu) * (cv - pv) - (cu - pu) * (bv - pv)) * sign
    if s0 < -eps * lengths[0]:
        return None
    s1 = ((cu - pu) * (av - pv) - (au - pu) * (cv - pv)) * sign
    if s1 < -eps * lengths[1]:
        return None
    s2 = ((au - pu) * (bv - pv) - (bu - pu) * (av - pv)) * sign
    if s2 < -eps * lengths[2]:
        return None
    return (s0 * aw + s1 * bw + s2 * cw) / (area2 * sign)


# --- ranges to an answer --------------------------------------------------------

def _first_clear(intervals, eps):
    """Smallest t >= 0 inside no closed range, sweeping from zero."""
    t = 0.0
    for lo, hi in sorted(intervals):
        if lo > t + eps:
            break
        if hi > t:
            t = hi
    return t


def _gap(intervals, eps):
    """Unsigned distance from t=0 to the nearest range, 0 when a range holds
    zero, None when there are no ranges."""
    if not intervals:
        return None
    nearest = None
    for lo, hi in intervals:
        if lo <= eps and hi >= -eps:
            return 0.0
        distance = lo if lo > 0 else -hi
        if nearest is None or distance < nearest:
            nearest = distance
    return nearest
