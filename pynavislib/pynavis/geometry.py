"""World-space triangles of a model item.

The one route to an item's actual faces is the COM primitives walk that
pynavis.section already tamed (matrix layout detection, per-instance
fragment filtering, the swallowed-exception callback); this module reuses
those helpers and hands back plain tuples so callers stay pure. Face
Distance uses it to learn which faces a measured point sits on.
"""


def float_noise(local_max, matrix, layout):
    """How far off a world coordinate can be from float32 storage alone, in
    world units: one float32 step at the largest fragment-local coordinate,
    scaled by the matrix's linear part. The translation is double and adds
    nothing, so geometry placed far out by its matrix stays precise, while
    vertices that themselves sit far out do not."""
    if layout == 'world' or matrix is None:
        scale = 1.0
    else:
        rows = [(matrix[0], matrix[1], matrix[2]), (matrix[4], matrix[5], matrix[6]),
                (matrix[8], matrix[9], matrix[10])]
        columns = list(zip(*rows))
        scale = max((a * a + b * b + c * c) ** 0.5 for a, b, c in rows + columns)
    return local_max * scale * 2.0 ** -23


def world_triangles(item, budget=200000, note=None, stats=None):
    """[((x,y,z), (x,y,z), (x,y,z)), ...] in world coordinates for every
    triangle of the item and its geometry-carrying descendants.

    Returns [] (with a note) when the item declares more than budget
    primitives, when the COM walk yields nothing, or when no matrix layout
    fits: an empty list means "unknown", never "no faces".

    stats, a dict, gets 'noise': the largest float_noise of any fragment,
    which is how precise these coordinates really are.
    """
    from pynavis import _com                     # loads the COM assemblies
    from pynavis import section
    from Autodesk.Navisworks.Api.ComApi import ComApiBridge
    from Autodesk.Navisworks.Api.Interop.ComApi import nwEVertexProperty

    nodes, total = section._geometry_nodes([item])
    if not nodes:
        if note is not None:
            note('no geometry nodes')
        return []
    if total > budget:
        if note is not None:
            note('%d primitives is over the %d budget' % (total, budget))
        return []
    found = section._detect_layout(nodes, note)
    if found is None:
        return []
    layout, bits_name = found
    bits = getattr(nwEVertexProperty, bits_name)

    triangles = []
    state = {'matrix': None, 'local': 0.0}

    def take(v1, v2, v3):
        matrix = state['matrix']
        corners = []
        for vertex in (v1, v2, v3):
            x, y, z = section._coord_of(vertex)
            state['local'] = max(state['local'], abs(x), abs(y), abs(z))
            corners.append(section._apply_layout(matrix, x, y, z, layout))
        triangles.append(tuple(corners))

    failures = []
    noise = 0.0
    collector = section._primitive_callback(take, failures)
    for node in nodes:
        path = ComApiBridge.ToInwOaPath(node)
        for fragment in section._fragments_of(path):
            state['matrix'] = list(fragment.GetLocalToWorldMatrix().Matrix)
            state['local'] = 0.0
            fragment.GenerateSimplePrimitives(bits, collector)
            noise = max(noise, float_noise(state['local'], state['matrix'], layout))
    if failures and note is not None:
        note('vertex callback threw: %s' % failures[0])
    if stats is not None:
        stats['noise'] = noise
    return triangles


def translate(items, vector, doc=None, name='Move items'):
    """Slides items by (dx, dy, dz) world units, the way Item Tools >
    Transform does: a permanent override that composes with any transform
    already on them, is saved in the NWF, and undoes as ONE step. The
    source file is never touched. Returns how many items were moved."""
    from pynavis import viewpoints
    from pynavis._api import Api, Application
    d = doc if doc is not None else Application.ActiveDocument
    collection = Api.ModelItemCollection()
    for item in items:
        collection.Add(item)
    if collection.Count == 0:
        return 0
    move = Api.Transform3D.CreateTranslation(
        Api.Vector3D(float(vector[0]), float(vector[1]), float(vector[2])))
    with viewpoints.transaction(name, d):
        d.Models.OverridePermanentTransform(collection, move, False)
    return collection.Count


def reset_transform(items, doc=None, name='Reset transform'):
    """Removes every permanent transform override from items, putting them
    back where the source file has them. One undo step."""
    from pynavis import viewpoints
    from pynavis._api import Api, Application
    d = doc if doc is not None else Application.ActiveDocument
    collection = Api.ModelItemCollection()
    for item in items:
        collection.Add(item)
    if collection.Count == 0:
        return 0
    with viewpoints.transaction(name, d):
        d.Models.ResetPermanentTransform(collection)
    return collection.Count
