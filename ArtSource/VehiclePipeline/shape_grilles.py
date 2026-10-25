"""Corner-fitted, finite grille artwork. No source geometry or shared UV is changed.

Pure numpy functions are usable outside Blender; binding imports bpy only on demand.
Vehicle-specific face selections and pattern choices belong in the caller's settings.
"""
import math
import numpy as np


def hull(points):
    points = sorted(set(tuple(p) for p in points))
    if len(points) < 3:
        raise ValueError("A grille needs a nonzero panel outline")
    def cross(a, b, c):
        return (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
    lower, upper = [], []
    for point in points:
        while len(lower) > 1 and cross(lower[-2], lower[-1], point) <= 1e-12:
            lower.pop()
        lower.append(point)
    for point in reversed(points):
        while len(upper) > 1 and cross(upper[-2], upper[-1], point) <= 1e-12:
            upper.pop()
        upper.append(point)
    return np.array(lower[:-1]+upper[:-1], dtype=float)


def fit_panel(points, normal=None, primary=None):
    points = np.unique(np.asarray(points, dtype=float), axis=0)
    if len(points) < 3 or not np.isfinite(points).all():
        raise ValueError("Invalid grille coordinates")
    center = points.mean(axis=0)
    if normal is None:
        _, _, axes = np.linalg.svd(points-center, full_matrices=False)
        normal = axes[-1]
        if normal[np.argmax(abs(normal))] < 0:
            normal = -normal
    normal = np.asarray(normal, dtype=float)
    normal /= np.linalg.norm(normal)
    primary = np.array(primary if primary is not None else ([1, 0, 0] if abs(normal[0]) < .9 else [0, 1, 0]), dtype=float)
    u = primary-normal*np.dot(primary, normal)
    if np.linalg.norm(u) < 1e-9:
        raise ValueError("Panel orientation is parallel to its normal")
    u /= np.linalg.norm(u)
    v = np.cross(normal, u)
    xy = np.column_stack(((points-center)@u, (points-center)@v))
    low, high = xy.min(axis=0), xy.max(axis=0)
    extent = high-low
    if min(extent) < 1e-7:
        raise ValueError("Grille surface has zero projected area")
    boundary = hull(xy)
    return dict(center=center.tolist(), u=u.tolist(), v=v.tolist(), low=low.tolist(),
                extent=extent.tolist(), outline=((boundary-low)/extent).tolist(),
                plane_deviation=float(np.max(abs((points-center)@normal))))


def project(points, frame):
    points = np.asarray(points)-frame["center"]
    return (np.column_stack((points@frame["u"], points@frame["v"]))-frame["low"])/frame["extent"]


def artwork(frame, spec, colors, size=256):
    """Return bottom-origin display RGBA; every pattern is local to this panel."""
    if size < 32 or spec.get("pattern") not in {"louvers", "slots", "diamond", "radial"}:
        raise ValueError("Choose a supported finite grille pattern")
    extent = np.array(frame["extent"])
    outline = np.array(frame["outline"])*extent
    yy, xx = np.mgrid[:size, :size]
    points = np.stack(((xx+.5)/size*extent[0], (yy+.5)/size*extent[1]), axis=-1)
    inside = np.ones((size, size), dtype=bool)
    distance = np.full((size, size), np.inf)
    for a, b in zip(outline, np.roll(outline, -1, axis=0)):
        edge = b-a
        inside &= edge[0]*(points[..., 1]-a[1])-edge[1]*(points[..., 0]-a[0]) >= -1e-9
        t = np.clip(np.sum((points-a)*edge, axis=-1)/np.dot(edge, edge), 0, 1)
        distance = np.minimum(distance, np.linalg.norm(points-(a+t[..., None]*edge), axis=-1))
    border = float(spec.get("border_fraction", .075))*min(extent)
    inner = inside & (distance > border)
    uv = (points-border)/np.maximum(extent-2*border, 1e-9)
    rows, columns = int(spec.get("rows", 5)), int(spec.get("columns", 1))
    if min(rows, columns) < 1 or not 0 < border < min(extent)/3:
        raise ValueError("Grille counts and edge frame must be positive")
    pattern = spec["pattern"]
    if pattern in {"louvers", "slots"}:
        phase = (uv[..., 1]*rows) % 1
        bars = phase < float(spec.get("bar_fraction", .42))
        if pattern == "slots":
            bars |= (uv[..., 0]*columns) % 1 < .24
        elif columns > 1:
            bars |= abs((uv[..., 0]*columns) % 1-.5) < .06
    elif pattern == "diamond":
        a = (uv[..., 0]*columns+uv[..., 1]*rows) % 1
        b = (uv[..., 0]*columns-uv[..., 1]*rows) % 1
        bars = (np.minimum(a, 1-a) < .12) | (np.minimum(b, 1-b) < .12)
    else:
        p = (points-extent/2)/(min(extent)/2)
        radius = np.linalg.norm(p, axis=-1)
        angle = (np.arctan2(p[..., 1], p[..., 0])+radius*.55)*int(spec.get("spokes", 8))/(2*math.pi)
        bars = ((angle % 1) < .30) | (radius < float(spec.get("hub", .18)))
        for ring in np.linspace(.45, .83, int(spec.get("rings", 2))):
            bars |= abs(radius-ring) < .035
    rgb = np.array(colors, dtype=float)
    if rgb.shape != (3, 3):
        raise ValueError("Supply recess, bar and edge-frame display RGB")
    result = np.zeros((size, size, 4), dtype=np.float32)
    result[..., :3] = rgb[0]/255
    result[inner & bars, :3] = rgb[1]/255
    result[inside & ~inner, :3] = rgb[2]/255
    # Opaque backing: source faces themselves clip the artwork exactly, including
    # concave edges or holes within the conservative outer boundary.
    result[..., 3] = 1
    return result


def bind(obj, face_ids, spec, colors, label, normal=None, primary=None):
    import bpy
    mesh = obj.data
    points = [list(obj.matrix_world@mesh.vertices[v].co) for i in face_ids for v in mesh.polygons[i].vertices]
    frame = fit_panel(points, normal=normal, primary=primary)
    uv_name = "Fitted grille " + label
    uv = mesh.uv_layers.get(uv_name) or mesh.uv_layers.new(name=uv_name)
    for i in face_ids:
        polygon = mesh.polygons[i]
        values = project([list(obj.matrix_world@mesh.vertices[v].co) for v in polygon.vertices], frame)
        for loop, value in zip(polygon.loop_indices, values):
            uv.data[loop].uv = value
    image = bpy.data.images.new(label+" fitted grille", 256, 256)
    image.colorspace_settings.name = "sRGB"
    image.pixels.foreach_set(artwork(frame, spec, colors).ravel())
    image.pack()
    mat = bpy.data.materials.new(label+" fixed grille")
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    emission = nodes.new("ShaderNodeEmission")
    texture = nodes.new("ShaderNodeTexImage")
    texture.image = image
    texture.extension = "EXTEND"
    mapping = nodes.new("ShaderNodeUVMap")
    mapping.uv_map = uv_name
    mat.node_tree.links.new(mapping.outputs["UV"], texture.inputs["Vector"])
    mat.node_tree.links.new(texture.outputs["Color"], emission.inputs["Color"])
    mat.node_tree.links.new(emission.outputs[0], output.inputs["Surface"])
    mat["fitted_grille"] = True
    mesh.materials.append(mat)
    for i in face_ids:
        mesh.polygons[i].material_index = len(mesh.materials)-1
    return dict(material_name=mat.name, uv_name=uv_name, pattern=spec,
                frame=frame, face_count=len(face_ids), texture_extension="EXTEND",
                anchored_to_panel_corners=True)


def prepare_export(material):
    if not material.get("fitted_grille"):
        return False
    texture = next(n for n in material.node_tree.nodes if n.type == "TEX_IMAGE")
    output = next(n for n in material.node_tree.nodes if n.type == "OUTPUT_MATERIAL")
    material.node_tree.links.new(texture.outputs["Color"], output.inputs["Surface"])
    return True
