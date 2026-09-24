# -*- coding: utf-8 -*-
"""Section planes fitted to a selection: the pure fit engine plus the API half.

Navisworks' own Fit Section to Selection gives you a world-aligned box, so a
duct run on the diagonal gets a box far bigger than the run and four planes
that meet it at an angle. This fits the box to the OBJECTS instead: the
minimum-area rectangle of their footprint, rotated about Z only so the
horizontal cut stays level, then written to the six individual clip planes
rather than to Box mode - which is the point, because six separate planes can
be switched off one at a time in the Sectioning tab.

The pure half (convex_hull / min_area_yaw / fit / faces / compact) works on
plain tuples and runs anywhere; the API half touches Navisworks and lazy-imports
it, so importing this module never needs Navisworks.

Box dict: {'yaw': radians about Z, 'min', 'max', 'center': (x,y,z) IN THE
YAW-ROTATED LOCAL FRAME, 'size': (dx,dy,dz) already padded}. Local, not world:
every extent is measured along the box's own axes, and to_world() puts a point
or a direction back into world coordinates.
"""
import math

TOOL = 'section_planes'
DEFAULTS = {'padding_mm': 150.0, 'snap_degrees': 1.5}

# Which way a clip plane's normal points to keep the volume behind it. The .NET
# API documents neither AlignToPick's sign nor its Custom alignment, so this is
# the one empirical constant in the module.
KEEP_INWARD = True


class Result(object):
    """What a section action did, ready for the button script to toast."""

    def __init__(self, level, message, detail='', count=0):
        self.level = level          # 'success' | 'error' | 'info' | 'warning'
        self.message = message
        self.detail = detail
        self.count = count


def convex_hull(points):
    """The counter-clockwise convex hull of (x, y) points, Andrew's monotone
    chain. Collinear points are dropped and the first point is not repeated, so
    every returned edge is a real edge for the calipers pass below."""
    pts = sorted(set(points))
    if len(pts) <= 2:
        return list(pts)

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])

    lower = []
    for p in pts:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(p)
    upper = []
    for p in reversed(pts):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(p)
    return lower[:-1] + upper[:-1]


def min_area_yaw(points, snap_degrees=1.5, snap_ratio=0.01):
    """The yaw in radians of the smallest-area rectangle enclosing the points'
    XY footprint, in [0, pi/2) because a rectangle repeats every 90 degrees.

    Rotating calipers: the minimum-area rectangle always has a side flush with
    a hull edge, so trying every hull edge is exact, not a search.

    Returns 0.0 when the fit is not worth having - within snap_degrees of an
    axis, or no more than snap_ratio better than the axis-aligned box. Without
    that, anything orthogonal comes out two degrees askew and every plane the
    user then drags is off-square for no gain.
    """
    hull = convex_hull([(p[0], p[1]) for p in points])
    if len(hull) < 3:
        return 0.0

    best_area = None
    best_angle = 0.0
    count = len(hull)
    for index in range(count):
        ax, ay = hull[index]
        bx, by = hull[(index + 1) % count]
        dx, dy = bx - ax, by - ay
        length = math.hypot(dx, dy)
        if length < 1e-12:
            continue
        ux, uy = dx / length, dy / length
        min_u = min_v = None
        max_u = max_v = None
        for px, py in hull:
            u = px * ux + py * uy
            v = -px * uy + py * ux
            if min_u is None or u < min_u: min_u = u
            if max_u is None or u > max_u: max_u = u
            if min_v is None or v < min_v: min_v = v
            if max_v is None or v > max_v: max_v = v
        area = (max_u - min_u) * (max_v - min_v)
        if best_area is None or area < best_area:
            best_area = area
            best_angle = math.atan2(uy, ux)

    if best_area is None:
        return 0.0

    xs = [p[0] for p in hull]
    ys = [p[1] for p in hull]
    axis_area = (max(xs) - min(xs)) * (max(ys) - min(ys))
    if axis_area <= best_area * (1.0 + snap_ratio):
        return 0.0

    quarter = math.pi / 2.0
    yaw = best_angle % quarter
    limit = math.radians(snap_degrees)
    if yaw < limit or yaw > quarter - limit:
        return 0.0
    return yaw


def fit(points, padding=0.0, snap_degrees=1.5, snap_ratio=0.01):
    """The oriented box for world (x, y, z) points, or None when there are
    none. Extents are measured in the yaw-rotated frame; padding grows all six
    faces, so every size gains twice the padding."""
    if not points:
        return None

    yaw = min_area_yaw(points, snap_degrees, snap_ratio)
    cos_y, sin_y = math.cos(yaw), math.sin(yaw)

    low = [None, None, None]
    high = [None, None, None]
    for x, y, z in points:
        local = (x * cos_y + y * sin_y, -x * sin_y + y * cos_y, z)
        for axis in range(3):
            value = local[axis]
            if low[axis] is None or value < low[axis]: low[axis] = value
            if high[axis] is None or value > high[axis]: high[axis] = value

    low = tuple(v - padding for v in low)
    high = tuple(v + padding for v in high)
    return {
        'yaw': yaw,
        'min': low,
        'max': high,
        'center': tuple((low[i] + high[i]) / 2.0 for i in range(3)),
        'size': tuple(high[i] - low[i] for i in range(3)),
    }


def to_world(box, point):
    """A point in the box's local frame, back in world coordinates. Also
    correct for direction vectors, because the transform is a rotation about Z
    with no translation in it."""
    cos_y, sin_y = math.cos(box['yaw']), math.sin(box['yaw'])
    x, y, z = point
    return (x * cos_y - y * sin_y, x * sin_y + y * cos_y, z)


def faces(box, inward=None):
    """The six (origin, normal) world pairs, one per box face, ordered
    +X, -X, +Y, -Y, top, bottom. Each origin sits at the centre of its face.
    With inward=True the normal points at the volume the section keeps."""
    if inward is None:
        inward = KEEP_INWARD
    low, high, center = box['min'], box['max'], box['center']
    sign = 1.0 if inward else -1.0
    local = [
        ((high[0], center[1], center[2]), (-1.0, 0.0, 0.0)),
        ((low[0], center[1], center[2]), (1.0, 0.0, 0.0)),
        ((center[0], high[1], center[2]), (0.0, -1.0, 0.0)),
        ((center[0], low[1], center[2]), (0.0, 1.0, 0.0)),
        ((center[0], center[1], high[2]), (0.0, 0.0, -1.0)),
        ((center[0], center[1], low[2]), (0.0, 0.0, 1.0)),
    ]
    out = []
    for origin, normal in local:
        turned = to_world(box, normal)
        out.append((to_world(box, origin),
                    (turned[0] * sign, turned[1] * sign, turned[2] * sign)))
    return out


def world_aabb(box):
    """World axis-aligned (min, max) enclosing the box's eight corners - what
    ZoomBox and ClipPlaneSet.Range want, since both take a plain box."""
    low, high = box['min'], box['max']
    lows = [None, None, None]
    highs = [None, None, None]
    for x in (low[0], high[0]):
        for y in (low[1], high[1]):
            for z in (low[2], high[2]):
                corner = to_world(box, (x, y, z))
                for axis in range(3):
                    value = corner[axis]
                    if lows[axis] is None or value < lows[axis]: lows[axis] = value
                    if highs[axis] is None or value > highs[axis]: highs[axis] = value
    return tuple(lows), tuple(highs)


def compact(points):
    """The points reduced to what fit() actually reads: the XY hull, plus the
    lowest and highest point. Identical fit, bounded memory, which is what lets
    the geometry walk in the API half run over a huge selection without piling
    up millions of vertices."""
    if len(points) < 4:
        return list(points)
    by_xy = {}
    for point in points:
        by_xy.setdefault((point[0], point[1]), point)
    kept = [by_xy[xy] for xy in convex_hull(list(by_xy.keys()))]
    kept.append(min(points, key=lambda p: p[2]))
    kept.append(max(points, key=lambda p: p[2]))
    return kept


# --- API half (Navisworks only; lazy imports keep the module pure) -----------
#
# The triangle walk subclasses a COM interface, which only the IronPython
# engine can do. The extension defaults to engine: ironpython; do not run these
# bundles under CPython.

# Above this many primitives the triangle walk stops paying for itself: every
# vertex crosses the COM boundary one call at a time, and on a whole-floor
# selection that is minutes of frozen UI. Per-fragment world boxes still trace
# the direction of a long run, so the yaw survives the downgrade.
TRIANGLE_BUDGET = 150000


def pick_source(primitive_count, has_com, budget=TRIANGLE_BUDGET):
    """Which point source to use: 'triangles' when COM is available and the
    selection is small enough, 'fragments' when it is not, 'boxes' when there
    is no COM at all or nothing has geometry."""
    if primitive_count <= 0:
        return 'boxes'
    if not has_com:
        return 'boxes'
    return 'triangles' if primitive_count <= budget else 'fragments'


def agrees(points, box_min, box_max, slack=0.05):
    """True when the collected points land inside the API's own bounding box,
    give or take slack (a fraction of the box's longest side).

    This is the guard on the COM walk. GetLocalToWorldMatrix's storage order is
    undocumented, and getting it wrong does not raise - it silently scatters
    points, which would section the model somewhere the user did not select.
    """
    if not points:
        return False
    span = max(box_max[i] - box_min[i] for i in range(3))
    margin = max(abs(span) * slack, 1e-6)
    for point in points:
        for axis in range(3):
            if point[axis] < box_min[axis] - margin: return False
            if point[axis] > box_max[axis] + margin: return False
    return True


def _geometry_nodes(items):
    """Every descendant of the selection that actually carries geometry, with
    the total primitive count so the source can be chosen before any walking."""
    nodes = []
    total = 0
    for item in items:
        for node in item.DescendantsAndSelf:
            if not node.HasGeometry:
                continue
            nodes.append(node)
            try:
                total += node.Geometry.PrimitiveCount
            except Exception:
                total += 1
    return nodes, total


def _api_bounds(items):
    """The selection's world bounding box as plain tuples, for agrees()."""
    low = [None, None, None]
    high = [None, None, None]
    for item in items:
        box = item.BoundingBox()
        pairs = ((box.Min.X, box.Max.X), (box.Min.Y, box.Max.Y),
                 (box.Min.Z, box.Max.Z))
        for axis, (lo, hi) in enumerate(pairs):
            if low[axis] is None or lo < low[axis]: low[axis] = lo
            if high[axis] is None or hi > high[axis]: high[axis] = hi
    if low[0] is None:
        return None, None
    return tuple(low), tuple(high)


# How GetLocalToWorldMatrix stores its 16 doubles. The COM API does not say,
# and guessing wrong does not raise - it silently scatters every vertex - so
# the layout is measured at run time rather than assumed. 'world' covers the
# case where the vertices arrive already in world coordinates.
_LAYOUTS = ('row', 'column', 'world')


def _apply_layout(matrix, x, y, z, layout):
    """One fragment-local vertex to world, under a candidate matrix layout.

    'row' puts the translation in 12..14 (the layout the published Navisworks
    COM samples use), 'column' in 3/7/11, which is its transpose.
    """
    if layout == 'world':
        return (x, y, z)
    if layout == 'column':
        wx = x * matrix[0] + y * matrix[1] + z * matrix[2] + matrix[3]
        wy = x * matrix[4] + y * matrix[5] + z * matrix[6] + matrix[7]
        wz = x * matrix[8] + y * matrix[9] + z * matrix[10] + matrix[11]
        w = x * matrix[12] + y * matrix[13] + z * matrix[14] + matrix[15]
    else:
        wx = x * matrix[0] + y * matrix[4] + z * matrix[8] + matrix[12]
        wy = x * matrix[1] + y * matrix[5] + z * matrix[9] + matrix[13]
        wz = x * matrix[2] + y * matrix[6] + z * matrix[10] + matrix[14]
        w = x * matrix[3] + y * matrix[7] + z * matrix[11] + matrix[15]
    if w and w != 1.0:
        return (wx / w, wy / w, wz / w)
    return (wx, wy, wz)


def _primitive_callback(on_triangle, failures=None):
    """A live InwSimplePrimitivesCB whose Triangle calls on_triangle(v1,v2,v3).

    Built here rather than at module scope because the base type only exists
    once the COM interop assembly is loaded.

    An exception raised inside a COM callback CANNOT travel back out through
    the COM boundary: GenerateSimplePrimitives just returns as if nothing
    happened, and the walk reports zero vertices. That is precisely how a
    broken vertex read hid here once. So the first failure is captured into
    the failures list instead of vanishing, and the caller reports it.
    """
    from Autodesk.Navisworks.Api.Interop.ComApi import InwSimplePrimitivesCB

    class Collector(InwSimplePrimitivesCB):
        def Triangle(self, v1, v2, v3):
            try:
                on_triangle(v1, v2, v3)
            except Exception as error:
                if failures is not None and not failures:
                    failures.append('%s: %s' % (type(error).__name__, error))

        def Line(self, v1, v2):
            pass

        def Point(self, v1):
            pass

        def SnapPoint(self, v1):
            pass

    return Collector()


# What to ask GenerateSimplePrimitives for. The published samples pass eNORMAL
# and it is tried first: a build that generates nothing for eNONE is
# indistinguishable from a callback that was never wired up.
_VERTEX_BITS = ('eNORMAL', 'eNONE')


def _coord_of(vertex):
    """(x, y, z) of an InwSimpleVertex, in fragment-local coordinates.

    The type library declares coord as InwLPos3f, but at run time it arrives
    as a raw 1-based Single[] SAFEARRAY (measured in the field: reading
    .data1 threw AttributeError on every vertex, silently, because COM
    callbacks swallow exceptions). Iteration works whatever the array base.
    """
    values = list(vertex.coord)
    return values[0], values[1], values[2]


def _path_key(path):
    """A path's ArrayData as a comparable tuple, or None when unreadable."""
    try:
        return tuple(path.ArrayData)
    except Exception:
        return None


def _fragments_of(path):
    """Only the fragments that belong to THIS instance of the item.

    Fragments() returns the fragments of every instance of the shared
    geometry definition: one 15 ft item measured here yielded 50 fragments
    spanning 100 ft of model, the other 49 belonging to sibling placements.
    Sectioning to those would cover the whole array of parts, so each
    fragment's own path is checked against the item's.
    """
    want = _path_key(path)
    for fragment in path.Fragments():
        if want is not None:
            got = _path_key(fragment.path)
            if got is not None and got != want:
                continue
        yield fragment


def _sample_fragment(nodes, note=None):
    """(node, matrix, [local vertices], bits) for the first fragment that
    yields any, or None. One COM walk, so the layout can then be decided in
    plain python instead of by walking the selection once per candidate.

    On failure the note separates the ways this goes wrong - no fragments at
    all, fragments that iterate to nothing, or a callback that never fires -
    because they have completely different causes.
    """
    from pynavis import _com                     # loads the COM assemblies
    from Autodesk.Navisworks.Api.ComApi import ComApiBridge
    from Autodesk.Navisworks.Api.Interop.ComApi import nwEVertexProperty

    local = []

    def take(v1, v2, v3):
        if len(local) < 900:
            for vertex in (v1, v2, v3):
                local.append(_coord_of(vertex))

    failures = []
    collector = _primitive_callback(take, failures)
    declared = walked = 0
    for bits_name in _VERTEX_BITS:
        bits = getattr(nwEVertexProperty, bits_name)
        declared = walked = 0
        for node in nodes:
            path = ComApiBridge.ToInwOaPath(node)
            try:
                declared += path.Fragments().Count
            except Exception:
                pass
            for fragment in _fragments_of(path):
                walked += 1
                del local[:]
                matrix = list(fragment.GetLocalToWorldMatrix().Matrix)
                fragment.GenerateSimplePrimitives(bits, collector)
                if local:
                    return node, matrix, list(local), bits_name
    if note is not None:
        if failures:
            note('the vertex callback fired but threw: %s' % failures[0])
        else:
            note('COM walk produced no vertices: %d node(s), Fragments() '
                 'declared %d, iterated %d' % (len(nodes), declared, walked))
    return None


def _detect_layout(nodes, note=None):
    """(layout, bits) this build needs, or None when nothing fits.

    Scored against the sampled fragment's OWN item bounding box, which is far
    more discriminating than the whole selection's: a wrong layout has to land
    inside one small box to fool it.
    """
    sample = _sample_fragment(nodes, note)
    if sample is None:
        return None
    node, matrix, local, bits_name = sample

    box = node.BoundingBox()
    low = (box.Min.X, box.Min.Y, box.Min.Z)
    high = (box.Max.X, box.Max.Y, box.Max.Z)
    for layout in _LAYOUTS:
        points = [_apply_layout(matrix, x, y, z, layout) for x, y, z in local]
        if agrees(points, low, high):
            return layout, bits_name

    if note is not None:
        # The two x ranges are the whole diagnosis: same magnitude means a
        # translation or transpose problem, wildly different means units.
        note('%d vertices sampled (%s), no layout fits: local x %.3f..%.3f, '
             'item box x %.3f..%.3f'
             % (len(local), bits_name,
                min(p[0] for p in local), max(p[0] for p in local),
                low[0], high[0]))
    return None


def _triangle_points(nodes, layout, bits_name, compact_every=20000):
    """World vertices of every triangle in the nodes, via the COM primitives
    callback. Compacted as it goes, so the list never outgrows the hull."""
    from pynavis import _com                     # loads the COM assemblies
    from Autodesk.Navisworks.Api.ComApi import ComApiBridge
    from Autodesk.Navisworks.Api.Interop.ComApi import nwEVertexProperty

    bits = getattr(nwEVertexProperty, bits_name)
    points = []
    state = {'matrix': None}

    def take(v1, v2, v3):
        matrix = state['matrix']
        for vertex in (v1, v2, v3):
            x, y, z = _coord_of(vertex)
            points.append(_apply_layout(matrix, x, y, z, layout))

    collector = _primitive_callback(take)
    for node in nodes:
        path = ComApiBridge.ToInwOaPath(node)
        for fragment in _fragments_of(path):
            state['matrix'] = list(fragment.GetLocalToWorldMatrix().Matrix)
            fragment.GenerateSimplePrimitives(bits, collector)
        if len(points) > compact_every:
            points[:] = compact(points)
    return compact(points)


def _fragment_points(nodes):
    """The eight corners of every fragment's world box. Cheap, and on a long
    run the string of boxes still traces the direction the run goes."""
    from pynavis import _com                     # loads the COM assemblies
    from Autodesk.Navisworks.Api.ComApi import ComApiBridge

    points = []
    for node in nodes:
        path = ComApiBridge.ToInwOaPath(node)
        for fragment in _fragments_of(path):
            box = fragment.GetWorldBox()
            low, high = box.min_pos, box.max_pos
            for x in (low.data1, high.data1):
                for y in (low.data2, high.data2):
                    for z in (low.data3, high.data3):
                        points.append((x, y, z))
        if len(points) > 20000:
            points[:] = compact(points)
    return compact(points)


def _box_points(items):
    """The corners of each item's API bounding box: the floor everything else
    falls back to. Axis-aligned per item, so the yaw it yields is only as good
    as the spread of the items, but it never fails."""
    points = []
    for item in items:
        box = item.BoundingBox()
        for x in (box.Min.X, box.Max.X):
            for y in (box.Min.Y, box.Max.Y):
                for z in (box.Min.Z, box.Max.Z):
                    points.append((x, y, z))
    return compact(points)


def collect_points(items, notes=None):
    """World points describing the selection, and which source produced them.

    Degrades in one direction only: triangles, then fragment boxes, then item
    boxes. Anything that throws, or that lands outside the API's own bounding
    box, drops a level rather than sectioning the model somewhere wrong.

    Every drop appends its reason to notes, because the bottom tier cannot
    produce an angle at all: the corners of axis-aligned boxes are themselves
    axis-aligned, so the fit comes out square to the world no matter how the
    objects run. A silent fallback there looks like the tool working when it
    has actually given up.
    """
    if not items:
        return [], 'boxes'

    def note(text):
        if notes is not None:
            notes.append(text)

    nodes, total = _geometry_nodes(items)
    low, high = _api_bounds(items)
    if low is None:
        return [], 'boxes'

    has_com = True
    try:
        from pynavis import _com                 # loads the COM assemblies
    except Exception as error:
        has_com = False
        note('COM bridge unavailable: %s' % error)

    if not nodes:
        note('nothing in the selection carries geometry')

    source = pick_source(total, has_com and bool(nodes))
    if source == 'triangles':
        try:
            detected = _detect_layout(nodes, note)   # notes its own failure
            if detected is not None:
                layout, bits_name = detected
                points = _triangle_points(nodes, layout, bits_name)
                if agrees(points, low, high):
                    return points, 'triangles'
                note('triangle vertices landed outside the selection bounding '
                     'box under the %s layout' % layout)
        except Exception as error:
            note('triangle walk failed: %s: %s'
                 % (type(error).__name__, error))
        source = 'fragments'
    if source == 'fragments':
        try:
            points = _fragment_points(nodes)
            if agrees(points, low, high):
                return points, 'fragments'
            note('fragment boxes landed outside the item bounding box')
        except Exception as error:
            note('fragment walk failed: %s: %s'
                 % (type(error).__name__, error))
    return _box_points(items), 'boxes'


def describe(box, count, source, unit_scale):
    """The toast detail: what got fitted, how big it is in metres, and how far
    the box had to turn. Says so when the fit came from a blunter source, since
    that changes how much to trust the angle."""
    size = tuple(box['size'][i] * unit_scale for i in range(3))
    turn = ('square to the world' if box['yaw'] == 0.0
            else 'turned %.1f degrees' % math.degrees(box['yaw']))
    text = '%d %s, %.2f x %.2f x %.2f m, %s' % (
        count, 'item' if count == 1 else 'items',
        size[0], size[1], size[2], turn)
    if source == 'fragments':
        text += ', fitted to fragment boxes'
    elif source == 'boxes':
        text += ', fitted to bounding boxes'
    return text


def _doc(doc):
    from pynavis import doc as _document
    return doc if doc is not None else _document.get_doc()


def _unit_scale(document):
    """Document units per metre: what describe() needs to talk in metres."""
    from pynavis._api import Api
    return Api.UnitConversion.ScaleFactor(document.Units, Api.Units.Meters)


def _padding(document, values):
    """The stored millimetre padding, in document units."""
    from pynavis._api import Api
    per_mm = Api.UnitConversion.ScaleFactor(Api.Units.Millimeters, document.Units)
    return float(values.get('padding_mm', DEFAULTS['padding_mm'])) * per_mm


def _box_for(items, values, document):
    """(box, source, notes) for the selection; box is None when there is
    nothing with any extent to fit."""
    notes = []
    points, source = collect_points(items, notes)
    box = fit(points,
              padding=_padding(document, values),
              snap_degrees=float(values.get('snap_degrees',
                                            DEFAULTS['snap_degrees'])))
    return box, source, notes


def _api_box(low, high):
    from pynavis._api import Api
    return Api.BoundingBox3D(Api.Point3D(low[0], low[1], low[2]),
                             Api.Point3D(high[0], high[1], high[2]))


def _write_planes(viewpoint, box):
    """The six planes onto a viewpoint's clip plane set. Returns how many
    planes were actually written, which is six unless the set is smaller.

    Planes mode, not Box mode: Navisworks' own Fit Section to Selection already
    gives a Box, and six separate planes is what lets the user switch one off
    afterwards without losing the fit.
    """
    from pynavis._api import Api

    planes = viewpoint.ClipPlanes
    planes.Mode = Api.ClipPlaneSetMode.Planes
    planes.Linked = False                 # each plane stays nudgeable on its own
    planes.Range = _api_box(*world_aabb(box))

    available = min(6, planes.Size())
    written = 0
    for index, (origin, normal) in enumerate(faces(box)):
        if index >= available:
            break
        plane = planes.Get(index)
        plane.AlignToPick(Api.ClipPlaneAlignment.Custom,
                          Api.Point3D(origin[0], origin[1], origin[2]),
                          Api.UnitVector3D(normal[0], normal[1], normal[2]))
        plane.State = Api.ClipPlaneState.Enabled
        plane.Enabled = True
        written += 1
    planes.Enabled = True
    return written


def _fitted(written, box, count, source, notes, document, verb):
    """The Result the fit and plan actions share.

    A fit that fell back to bounding boxes is reported as a WARNING, not a
    success: it cannot turn the box, so the planes come out square to the world
    and the tool has quietly done nothing that Navisworks' own Fit Section to
    Selection does not already do.
    """
    detail = describe(box, count, source, _unit_scale(document))
    if written < 6:
        return Result('warning', '%s with %d planes' % (verb, written),
                      detail + ', this view offers only %d' % written, written)
    if source == 'boxes':
        reason = notes[0] if notes else 'no geometry could be read'
        return Result('warning', '%s, but not turned' % verb,
                      detail + ' (' + reason + ')', written)
    return Result('success', verb, detail, written)


def fit_to_selection(items, values, doc=None):
    """Fits the six section planes to the selection and switches them on."""
    if not items:
        return Result('error', 'Nothing selected',
                      'Select the objects you want to section first.')

    document = _doc(doc)
    box, source, notes = _box_for(items, values, document)
    if box is None:
        return Result('error', 'Nothing to section',
                      'The selection has no geometry to measure.')

    # CreateCopy hands back a detached viewpoint; nothing reaches the view
    # until CopyFrom, which is also what makes this one undo step.
    viewpoint = document.CurrentViewpoint.CreateCopy()
    written = _write_planes(viewpoint, box)
    document.CurrentViewpoint.CopyFrom(viewpoint)
    return _fitted(written, box, len(items), source, notes, document,
                   'Section fitted')


def clear(doc=None):
    """Switches sectioning off without disturbing the camera."""
    document = _doc(doc)
    viewpoint = document.CurrentViewpoint.CreateCopy()
    planes = viewpoint.ClipPlanes
    if not planes.Enabled:
        return Result('info', 'Sectioning is already off')
    planes.Enabled = False
    document.CurrentViewpoint.CopyFrom(viewpoint)
    return Result('success', 'Sectioning off')


def plan_to_selection(items, values, doc=None):
    """Fits the planes, then looks straight down at them: an orthographic plan
    turned to the box, so the objects sit square on screen instead of skewed."""
    if not items:
        return Result('error', 'Nothing selected',
                      'Select the objects you want a plan of first.')

    document = _doc(doc)
    box, source, notes = _box_for(items, values, document)
    if box is None:
        return Result('error', 'Nothing to section',
                      'The selection has no geometry to measure.')

    from pynavis._api import Api

    viewpoint = document.CurrentViewpoint.CreateCopy()
    written = _write_planes(viewpoint, box)

    # No PointAt here. PointAt re-aims the camera at a target FROM WHERE IT
    # STANDS, so calling it after AlignDirection throws the straight-down
    # direction away and leaves the ViewCube at whatever oblique angle the
    # user happened to be at. ZoomBox does the positioning instead: it moves
    # the camera to fit the box along the direction already set.
    up = to_world(box, (0.0, 1.0, 0.0))
    viewpoint.Projection = Api.ViewpointProjection.Orthographic
    viewpoint.AlignDirection(Api.Vector3D(0.0, 0.0, -1.0))
    viewpoint.AlignUp(Api.Vector3D(up[0], up[1], up[2]))
    viewpoint.ZoomBox(_api_box(*world_aabb(box)))
    document.CurrentViewpoint.CopyFrom(viewpoint)
    return _fitted(written, box, len(items), source, notes, document,
                   'Plan set to selection')
