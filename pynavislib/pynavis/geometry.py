"""World-space triangles of a model item.

The one route to an item's actual faces is the COM primitives walk that
pynavis.section already tamed (matrix layout detection, per-instance
fragment filtering, the swallowed-exception callback); this module reuses
those helpers and hands back plain tuples so callers stay pure. Face
Distance uses it to learn which faces a measured point sits on.
"""


def world_triangles(item, budget=200000, note=None):
    """[((x,y,z), (x,y,z), (x,y,z)), ...] in world coordinates for every
    triangle of the item and its geometry-carrying descendants.

    Returns [] (with a note) when the item declares more than budget
    primitives, when the COM walk yields nothing, or when no matrix layout
    fits: an empty list means "unknown", never "no faces".
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
    state = {'matrix': None}

    def take(v1, v2, v3):
        matrix = state['matrix']
        corners = []
        for vertex in (v1, v2, v3):
            x, y, z = section._coord_of(vertex)
            corners.append(section._apply_layout(matrix, x, y, z, layout))
        triangles.append(tuple(corners))

    failures = []
    collector = section._primitive_callback(take, failures)
    for node in nodes:
        path = ComApiBridge.ToInwOaPath(node)
        for fragment in section._fragments_of(path):
            state['matrix'] = list(fragment.GetLocalToWorldMatrix().Matrix)
            fragment.GenerateSimplePrimitives(bits, collector)
    if failures and note is not None:
        note('vertex callback threw: %s' % failures[0])
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
