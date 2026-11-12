"""Scratch-built M3A3-type IFV model (new geometry, not derived from source faces).

The War Thunder M3A3 extract (`ext/m3a3_bradley_0.obj`) and the existing `ArtSource/M3A3Bradley`
result are used only as proportion and equipment-placement references: overall length/width/height,
turret yaw axis, wheel stations, TOW box, sight and hatch positions. No source vertex or face is
copied. Every solid is authored here as a convex block (outward winding guaranteed by the hull),
grouped by role so `configure_vehicle.py` can map groups without face-ID surgery.

Canonical space is written directly: forward -Y, up +Z, left +X, metres.
Run from anywhere; writes `model/m3a3_scratch.obj` next to this file.
"""
from pathlib import Path
import math
import numpy as np
from scipy.spatial import ConvexHull

ROOT = Path(__file__).resolve().parent
OUTPUT = ROOT/"model"/"m3a3_scratch.obj"

# Shared reference values (configure_vehicle.py imports these).
ROOF = 2.02
TURRET_PIVOT = (-0.19, 0.23, ROOF)
RAMP_HINGE = (0.0, 3.00, 0.62)
DOOR_HINGE = (0.62, 3.06, 1.30)
DRIVER_HATCH_HINGE = (0.70, -1.56, 2.09)
CARGO_HATCH_HINGE = (0.0, 2.66, 2.09)

WHEEL_Y = (-2.16, -1.47, -0.74, 0.20, 0.96, 1.70)
WHEEL_R, WHEEL_Z = 0.30, 0.36
SPROCKET = (-3.02, 0.735, 0.30)
IDLER = (2.35, 0.69, 0.27)
ROLLERS = ((-1.49, 0.865, 0.155), (-0.03, 0.865, 0.155), (1.13, 0.865, 0.155))
TRACK_X = (1.006, 1.548)
TRACK_T = 0.07


# ----------------------------------------------------------------------------------------------
# Geometry primitives
# ----------------------------------------------------------------------------------------------

def convex(points):
    """Convex solid as merged planar n-gons with outward (counter-clockwise) winding."""
    pts = np.unique(np.round(np.asarray(points, dtype=float), 9), axis=0)
    hull = ConvexHull(pts)
    parent = list(range(len(hull.simplices)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    eq = hull.equations
    for i, neighbours in enumerate(hull.neighbors):
        for j in neighbours:
            if eq[i, :3] @ eq[j, :3] > 1-1e-9 and abs(eq[i, 3]-eq[j, 3]) < 1e-7:
                parent[find(j)] = find(i)
    facets = {}
    for i, simplex in enumerate(hull.simplices):
        facets.setdefault(find(i), set()).update(int(v) for v in simplex)
    faces = []
    for root, vertex_ids in facets.items():
        n = eq[root, :3]/np.linalg.norm(eq[root, :3])
        ids = sorted(vertex_ids)
        co = pts[ids]
        c = co.mean(axis=0)
        u = co[0]-c
        u -= n*(u @ n)
        u /= np.linalg.norm(u)
        v = np.cross(n, u)
        angle = np.arctan2((co-c) @ v, (co-c) @ u)
        faces.append([ids[k] for k in np.argsort(angle)])
    return pts, faces


def box(lo, hi):
    return [(x, y, z) for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])]


def ring(center, axis, radius, segments, phase=0.0):
    axis = np.asarray(axis, float)/np.linalg.norm(axis)
    ref = np.array([0, 0, 1.0]) if abs(axis[2]) < .9 else np.array([1.0, 0, 0])
    u = np.cross(axis, ref)
    u /= np.linalg.norm(u)
    v = np.cross(axis, u)
    return [tuple(np.asarray(center)+radius*(math.cos(a)*u+math.sin(a)*v))
            for a in (phase+2*math.pi*k/segments for k in range(segments))]


def cylinder(a, b, radius, segments=16, phase=None):
    axis = np.subtract(b, a)
    phase = math.pi/segments if phase is None else phase
    return ring(a, axis, radius, segments, phase)+ring(b, axis, radius, segments, phase)


def dome(center, radius, height, rim=0.03, rings=5, segments=20):
    cx, cy, cz = center
    pts = [(cx+radius*math.cos(a), cy+radius*math.sin(a), cz)
           for a in (2*math.pi*k/segments for k in range(segments))]
    for k in range(rings+1):
        phi = (math.pi/2)*k/rings
        r, z = radius*math.cos(phi), cz+rim+height*math.sin(phi)
        if r < 1e-6:
            pts.append((cx, cy, z))
            continue
        pts += [(cx+r*math.cos(a), cy+r*math.sin(a), z) for a in (2*math.pi*j/segments for j in range(segments))]
    return pts


def prism(outline_xy, z0, z1):
    return [(x, y, z) for x, y in outline_xy for z in (z0, z1)]


def octagon(cx, cy, half_x, half_y, cut):
    return [(cx-half_x+cut, cy-half_y), (cx+half_x-cut, cy-half_y), (cx+half_x, cy-half_y+cut),
            (cx+half_x, cy+half_y-cut), (cx+half_x-cut, cy+half_y), (cx-half_x+cut, cy+half_y),
            (cx-half_x, cy+half_y-cut), (cx-half_x, cy-half_y+cut)]


def mirror_x(points):
    return [(-x, y, z) for x, y, z in points]


# ----------------------------------------------------------------------------------------------
# Model container
# ----------------------------------------------------------------------------------------------

class Model:
    def __init__(self):
        self.groups = {}

    def solid(self, group, points, material="paint"):
        pts, faces = convex(points)
        self.groups.setdefault(group, []).append((pts, faces, material))

    def quad(self, group, corners, normal, material):
        """Single planar polygon facing `normal` (lenses, grille sheets)."""
        co = np.asarray(corners, float)
        c = co.mean(axis=0)
        n = np.asarray(normal, float)/np.linalg.norm(normal)
        u = co[0]-c
        u -= n*(u @ n)
        u /= np.linalg.norm(u)
        v = np.cross(n, u)
        order = list(np.argsort(np.arctan2((co-c) @ v, (co-c) @ u)))
        self.groups.setdefault(group, []).append((co, [order], material))

    def write(self, path):
        path.parent.mkdir(parents=True, exist_ok=True)
        lines = ["# Scratch-built M3A3-type IFV. Generated by build_model.py; do not edit by hand.",
                 "# Canonical axes: forward -Y, up +Z, metres."]
        offset, faces_total = 0, 0
        for name, pieces in self.groups.items():
            lines.append(f"o {name}")
            lines.append(f"g {name}")
            material = None
            for pts, faces, mat in pieces:
                lines += [f"v {x:.6f} {y:.6f} {z:.6f}" for x, y, z in pts]
                if mat != material:
                    lines.append(f"usemtl {mat}")
                    material = mat
                lines += ["f " + " ".join(str(offset+i+1) for i in face) for face in faces]
                offset += len(pts)
                faces_total += len(faces)
        path.write_text("\n".join(lines)+"\n", encoding="utf-8", newline="\n")
        return offset, faces_total


# ----------------------------------------------------------------------------------------------
# Vehicle
# ----------------------------------------------------------------------------------------------

def glacis_z(y):
    """Upper glacis: (y -3.36, z 1.22) -> (y -2.42, z ROOF); ~40 deg so it reads as a front slope."""
    return 1.22+(y+3.36)/0.94*(ROOF-1.22)


def build_hull(m):
    # Upper hull spans the tracks; side tops chamfer inward to the roof, front corners bevelled.
    upper = []
    for y, z, w in [(-3.36, 1.12, 1.50), (-3.36, 1.22, 1.50), (-3.16, 1.12, 1.62), (-3.16, glacis_z(-3.16), 1.62),
                    (-2.6315, 1.84, 1.62), (-2.42, ROOF, 1.47), (2.96, ROOF, 1.47), (2.96, 1.84, 1.62),
                    (2.96, 1.12, 1.62)]:
        upper += [(w, y, z), (-w, y, z)]
    m.solid("hull", upper)
    # Lower hull between the tracks: steep under-nose plate and a short rear belly slope.
    lower = []
    for y, z in [(-3.36, 1.14), (-2.86, 0.44), (2.70, 0.44), (2.96, 0.70), (2.96, 1.14)]:
        lower += [(0.98, y, z), (-0.98, y, z)]
    m.solid("hull", lower)

    # Upper side applique armour: four bolted sections per side, 7 cm proud of the hull wall.
    for side, sx in (("l", 1), ("r", -1)):
        for y0, y1 in [(-3.10, -1.78), (-1.74, -0.40), (-0.36, 1.02), (1.06, 2.90)]:
            pts = box((1.62, y0, 1.18), (1.69, y1, 1.80))
            m.solid(f"side_armor_{side}", pts if sx > 0 else mirror_x(pts))
        # Track skirts: six panels, small gaps between them.
        edges = np.linspace(-3.28, 2.60, 7)
        for i, (y0, y1) in enumerate(zip(edges[:-1], edges[1:])):
            pts = box((1.625, y0+0.012, 0.54), (1.70, y1-0.012, 1.10))
            if i == 0:  # leading panel: lower front corner cut back
                pts = [p for p in pts if not (p[1] < y0+0.1 and p[2] < 0.6)]
                pts += [(1.625, y0+0.012, 0.74), (1.70, y0+0.012, 0.74), (1.625, y0+0.20, 0.54), (1.70, y0+0.20, 0.54)]
            m.solid(f"skirt_{side}_{i+1:02d}", pts if sx > 0 else mirror_x(pts))

    # Right-side engine exhaust louvre sheet on the applique (east view side).
    m.quad("exhaust_grille", [(-1.693, -1.62, 1.26), (-1.693, -0.92, 1.26), (-1.693, -0.92, 1.72), (-1.693, -1.62, 1.72)],
           (-1, 0, 0), "grille")

    # Engine deck (front right): raised frame with a louvre sheet on top.
    m.solid("engine_deck", box((-1.36, -2.34, ROOF-0.01), (-0.42, -1.26, ROOF+0.045)))
    m.quad("engine_grille", [(-1.30, -2.28, ROOF+0.046), (-0.48, -2.28, ROOF+0.046), (-0.48, -1.32, ROOF+0.046),
                             (-1.30, -1.32, ROOF+0.046)], (0, 0, 1), "grille")

    # Driver's hatch (front left) with a hinge block, a grab handle and three periscopes.
    m.solid("driver_hatch", prism(octagon(0.70, -1.92, 0.34, 0.34, 0.11), ROOF-0.01, ROOF+0.07))
    m.solid("driver_hatch", box((0.52, -1.60, ROOF+0.06), (0.88, -1.52, ROOF+0.11)))
    m.solid("driver_hatch_handle", box((0.62, -2.10, ROOF+0.07), (0.78, -2.06, ROOF+0.11)))
    for x in (0.44, 0.70, 0.96):
        block = [(x-0.09, -2.40, ROOF-0.01), (x+0.09, -2.40, ROOF-0.01), (x-0.09, -2.28, ROOF-0.01), (x+0.09, -2.28, ROOF-0.01),
                 (x-0.09, -2.37, ROOF+0.10), (x+0.09, -2.37, ROOF+0.10), (x-0.09, -2.28, ROOF+0.10), (x+0.09, -2.28, ROOF+0.10)]
        m.solid("driver_periscopes", block)
        m.quad("driver_periscopes", [(x-0.07, -2.395, ROOF+0.015), (x+0.07, -2.395, ROOF+0.015),
                                     (x+0.07, -2.375, ROOF+0.085), (x-0.07, -2.375, ROOF+0.085)], (0, -1, 0.3), "glass")

    # Rear cargo hatch: one large plate, rear hinge barrels, two handles.
    m.solid("cargo_hatch", prism(octagon(0.0, 2.19, 0.72, 0.47, 0.14), ROOF-0.01, ROOF+0.07))
    for x0, x1 in ((-0.56, -0.20), (0.20, 0.56)):
        m.solid("cargo_hatch", cylinder((x0, 2.66, ROOF+0.045), (x1, 2.66, ROOF+0.045), 0.045, 12))
    for x in (-0.34, 0.34):
        m.solid("cargo_hatch_handle", box((x-0.08, 1.84, ROOF+0.07), (x+0.08, 1.88, ROOF+0.11)))

    # Trim vane folded flat on the glacis, offset to clear both headlight guards.
    n = np.array([0, -(ROOF-1.22), 0.94])
    n /= np.linalg.norm(n)
    vane = []
    for y in (-3.30, -2.58):
        for x in (-1.04, 0.86):
            base = np.array([x, y, glacis_z(y)-0.01])
            vane += [tuple(base), tuple(base+n*0.07)]
    m.solid("trim_vane", vane)
    m.solid("trim_vane_handle", [tuple(np.array([x, -2.70, glacis_z(-2.70)])+n*d)
                                 for x in (-0.16, 0.16) for d in (0.06, 0.11)] +
            [tuple(np.array([x, -2.66, glacis_z(-2.66)])+n*d) for x in (-0.16, 0.16) for d in (0.06, 0.11)])

    # Headlight guards on the glacis corners; lens on the front face (destructible overlay).
    for side, sx in (("l", 1), ("r", -1)):
        guard = []
        for x in (1.10, 1.46):
            guard += [(x, -3.10, glacis_z(-3.10)-0.03), (x, -3.10, 1.80), (x, -2.84, 1.80), (x, -2.84, glacis_z(-2.84)-0.03)]
        m.solid(f"headlight_{side}", guard if sx > 0 else mirror_x(guard))
        lens = [(1.16, -3.104, 1.52), (1.40, -3.104, 1.52), (1.40, -3.104, 1.74), (1.16, -3.104, 1.74)]
        m.quad(f"headlight_{side}", lens if sx > 0 else mirror_x(lens), (0, -1, 0), "lens")

    # Front tow hooks and roof lifting eyes (highlight exceptions).
    for x in (-0.66, 0.66):
        m.solid("tow_hooks", box((x-0.07, -3.44, 1.02), (x+0.07, -3.35, 1.16)))
    for x in (-1.32, 1.32):
        for y in (-2.30, 2.84):
            m.solid("lifting_eyes", box((x-0.05, y-0.03, ROOF-0.01), (x+0.05, y+0.03, ROOF+0.07)))

    # Rear: ramp with an offset personnel door, stowage boxes either side.
    ramp = prism([(-0.80, 2.96), (0.80, 2.96), (0.80, 3.04), (-0.80, 3.04)], 0.62, 1.88)
    ramp += [(x, y, 1.96) for x in (-0.72, 0.72) for y in (2.96, 3.04)]
    m.solid("rear_ramp", ramp)
    m.solid("ramp_door", box((0.04, 3.03, 0.80), (0.62, 3.075, 1.80)))
    m.solid("ramp_door_handle", box((0.10, 3.07, 1.26), (0.14, 3.11, 1.40)))
    for side, sx in (("l", 1), ("r", -1)):
        stow = prism([(0.86, 2.96), (1.54, 2.96), (1.54, 3.12), (0.86, 3.12)], 1.06, 1.94)
        stow += [(x, 3.20, z) for x in (0.86, 1.54) for z in (1.06, 1.86)]
        m.solid(f"rear_stowage_{side}", stow if sx > 0 else mirror_x(stow))


def track_loop():
    """Track belt path: 2-D hull of sprocket, idler, road wheels and return rollers in (y, z)."""
    samples = []
    for cy, cz, r in [SPROCKET, IDLER, *[(y, WHEEL_Z, WHEEL_R) for y in WHEEL_Y], *ROLLERS]:
        samples += [(cy+r*math.cos(a), cz+r*math.sin(a)) for a in np.linspace(0, 2*math.pi, 48, endpoint=False)]
    pts = np.array(samples)
    hull = ConvexHull(pts)
    loop = pts[hull.vertices]  # counter-clockwise in (y, z)
    keep = [loop[0]]
    for p in loop[1:]:
        if np.linalg.norm(p-keep[-1]) > 0.06:
            keep.append(p)
    loop = np.array(keep)
    count = len(loop)
    normals = []
    for i in range(count):
        prev_edge, next_edge = loop[i]-loop[i-1], loop[(i+1) % count]-loop[i]
        n = np.array([prev_edge[1], -prev_edge[0]])/np.linalg.norm(prev_edge)+np.array([next_edge[1], -next_edge[0]])/np.linalg.norm(next_edge)
        normals.append(n/np.linalg.norm(n))
    return loop, np.array(normals)


def build_running_gear(m):
    loop, normals = track_loop()
    for side, sx in (("l", 1), ("r", -1)):
        x0, x1 = (TRACK_X[0], TRACK_X[1]) if sx > 0 else (-TRACK_X[1], -TRACK_X[0])
        for i in range(len(loop)):
            j = (i+1) % len(loop)
            seg = []
            for p, n in ((loop[i], normals[i]), (loop[j], normals[j])):
                for d in (0.0, TRACK_T):
                    y, z = p+n*d
                    seg += [(x0, y, z), (x1, y, z)]
            m.solid(f"track_{side}", seg, "track")
        mx = lambda pts: pts if sx > 0 else mirror_x(pts)
        for k, y in enumerate(WHEEL_Y):
            g = f"wheel_{side}_{k+1:02d}"
            m.solid(g, mx(cylinder((1.10, y, WHEEL_Z), (1.245, y, WHEEL_Z), WHEEL_R, 24)))
            m.solid(g, mx(cylinder((1.305, y, WHEEL_Z), (1.45, y, WHEEL_Z), WHEEL_R, 24)))
            m.solid(g, mx(cylinder((1.07, y, WHEEL_Z), (1.48, y, WHEEL_Z), 0.10, 12)))
            # Trailing torsion arm from the hull side to the hub.
            arm = [(0.98, y+0.30, 0.60), (1.08, y+0.30, 0.60), (0.98, y+0.36, 0.50), (1.08, y+0.36, 0.50),
                   (0.98, y-0.03, 0.42), (1.08, y-0.03, 0.42), (0.98, y+0.03, 0.31), (1.08, y+0.03, 0.31)]
            m.solid(f"suspension_{side}", mx(arm), "metal")
        cy, cz, r = SPROCKET
        m.solid(f"sprocket_{side}", mx(cylinder((1.11, cy, cz), (1.44, cy, cz), r, 24)))
        m.solid(f"sprocket_{side}", mx(cylinder((1.06, cy, cz), (1.48, cy, cz), 0.13, 12)))
        cy, cz, r = IDLER
        m.solid(f"idler_{side}", mx(cylinder((1.10, cy, cz), (1.44, cy, cz), r, 24)))
        m.solid(f"idler_{side}", mx(cylinder((1.06, cy, cz), (1.48, cy, cz), 0.10, 12)))
        for k, (cy, cz, r) in enumerate(ROLLERS):
            m.solid(f"roller_{side}_{k+1:02d}", mx(cylinder((1.12, cy, cz), (1.40, cy, cz), r, 16)))


class Scaled:
    """Plan-view scale about the turret yaw axis (heights unchanged) for every turret solid."""

    def __init__(self, model, factor):
        self.model, self.factor = model, factor

    def _map(self, points):
        px, py = TURRET_PIVOT[:2]
        return [(px+(x-px)*self.factor, py+(y-py)*self.factor, z) for x, y, z in points]

    def solid(self, group, points, material="paint"):
        self.model.solid(group, self._map(points), material)

    def quad(self, group, corners, normal, material):
        self.model.quad(group, self._map(corners), normal, material)


TURRET_SCALE = 1.15  # reference turret spans ~45% of hull length; 1.0 read too small beside RimThunder
GUN_DY = (0.23+(-0.80-0.23)*TURRET_SCALE)-(-0.80)  # keeps the mantlet on the scaled turret face
GUN_TRUNNION = (TURRET_PIVOT[0]+(-0.23-TURRET_PIVOT[0])*TURRET_SCALE, -0.86+GUN_DY, 2.44)
GUN_X, GUN_Z = GUN_TRUNNION[0], GUN_TRUNNION[2]
MUZZLE_Y = -2.45


def turret_point(x, y, z):
    px, py = TURRET_PIVOT[:2]
    return (round(px+(x-px)*TURRET_SCALE, 6), round(py+(y-py)*TURRET_SCALE, 6), z)


GUNNER_HATCH_HINGE = turret_point(0.08, 0.84, 2.765)
COMMANDER_HATCH_HINGE = turret_point(-0.58, 0.93, 2.72)


def build_turret(model):
    m = Scaled(model, TURRET_SCALE)
    waist =[(-0.64, -0.80), (0.24, -0.80), (0.62, -0.42), (0.66, 0.98), (0.40, 1.28), (-0.98, 1.28), (-1.22, 0.98), (-1.18, -0.40)]
    roof = [(-0.56, -0.56), (0.16, -0.56), (0.40, -0.30), (0.42, 0.92), (0.30, 1.16), (-0.86, 1.16), (-1.00, 0.92), (-0.96, -0.28)]
    base = [(-0.60, -0.74), (0.20, -0.74), (0.56, -0.40), (0.60, 0.95), (0.37, 1.22), (-0.95, 1.22), (-1.16, 0.95), (-1.12, -0.38)]
    m.solid("turret", [(x, y, ROOF) for x, y in base]+[(x, y, 2.40) for x, y in waist]+[(x, y, 2.70) for x, y in roof])

    # Gunner's hatch (left, flat) and commander's domed hatch (right).
    m.solid("gunner_hatch", prism(octagon(0.08, 0.55, 0.27, 0.27, 0.09), 2.69, 2.76))
    m.solid("gunner_hatch", cylinder((-0.06, 0.84, 2.765), (0.22, 0.84, 2.765), 0.035, 10))
    m.solid("gunner_hatch_handle", box((0.00, 0.36, 2.76), (0.16, 0.40, 2.80)))
    m.solid("commander_hatch", dome((-0.58, 0.62, 2.69), 0.31, 0.12))
    m.solid("commander_hatch_handle", box((-0.66, 0.40, 2.75), (-0.50, 0.44, 2.80)))

    # Gunner's sight head ahead of the hatches (destructible overlay), glass on its front.
    isu = [(0.00, -0.30, 2.69), (0.18, -0.30, 2.69), (0.00, 0.12, 2.69), (0.18, 0.12, 2.69),
           (0.00, -0.30, 2.92), (0.18, -0.30, 2.92), (0.00, -0.22, 3.00), (0.18, -0.22, 3.00),
           (0.00, 0.12, 3.00), (0.18, 0.12, 3.00)]
    m.solid("isu_sight", isu)
    m.quad("isu_sight", [(0.025, -0.304, 2.78), (0.155, -0.304, 2.78), (0.155, -0.304, 2.90), (0.025, -0.304, 2.90)],
           (0, -1, 0), "glass")

    # Commander's independent viewer on an armoured pedestal at the right rear corner.
    m.solid("civ_guard", prism(octagon(-1.10, 1.00, 0.20, 0.19, 0.06), 2.56, 3.02))
    civ = [(-1.24, 0.86, 3.02), (-0.96, 0.86, 3.02), (-1.24, 1.14, 3.02), (-0.96, 1.14, 3.02),
           (-1.24, 0.86, 3.18), (-0.96, 0.86, 3.18), (-1.24, 0.94, 3.26), (-0.96, 0.94, 3.26),
           (-1.24, 1.14, 3.26), (-0.96, 1.14, 3.26)]
    m.solid("civ_head", civ)
    m.quad("civ_head", [(-1.21, 0.856, 3.05), (-0.99, 0.856, 3.05), (-0.99, 0.856, 3.16), (-1.21, 0.856, 3.16)],
           (0, -1, 0), "glass")

    # Smoke grenade dischargers: one four-tube bank on each front cheek, aimed out and up.
    for (ax, ay), (bx, by) in (((0.24, -0.80), (0.62, -0.42)), ((-0.64, -0.80), (-1.18, -0.40))):
        along = np.array([bx-ax, by-ay, 0.0])
        along /= np.linalg.norm(along)
        mid = np.array([(ax+bx)/2, (ay+by)/2, 2.40])
        out = np.array([along[1], -along[0], 0.0])
        if out @ (mid-np.array([-0.28, 0.25, 2.40])) < 0:  # point away from the turret centre
            out = -out
        aim = out+np.array([0, 0, 0.45])
        aim /= np.linalg.norm(aim)
        bank = [tuple(mid+along*s+out*o+np.array([0, 0, h])) for s in (-0.20, 0.20) for o in (-0.06, 0.04) for h in (-0.06, 0.04)]
        m.solid("smoke_launchers", bank)
        for s in (-0.15, -0.05, 0.05, 0.15):
            start = mid+along*s+out*0.02
            m.solid("smoke_launchers", cylinder(tuple(start), tuple(start+aim*0.20), 0.036, 10))

    # TOW launcher box on the left side with its arm and two tube mouths.
    tow = prism([(0.70, -0.36), (1.18, -0.36), (1.18, 0.78), (0.70, 0.78)], 2.22, 2.70)
    tow += [(x, -0.44, z) for x in (0.70, 1.18) for z in (2.26, 2.62)]
    m.solid("tow_launcher", tow)
    m.solid("tow_mount", box((0.58, -0.02, 2.30), (0.72, 0.52, 2.58)))
    for x in (0.82, 1.06):
        # Mouths sit flush with the front face: proud discs read as sub-pixel dark streaks from above.
        m.solid("tow_tubes", cylinder((x, -0.43, 2.44), (x, -0.441, 2.44), 0.085, 16), "recess")

    # Rear bustle basket wrapping the turret rear from the left corner to the CIV corner:
    # dark floor, low outer walls following the rear outline, stowage boxes inside.
    rear = [(0.66, 0.98), (0.40, 1.28), (-0.98, 1.28)]
    center = np.array([-0.28, 0.25])

    def push(p, d):
        v = np.array(p)-center
        return tuple(np.array(p)+v/np.linalg.norm(v)*d)
    outer = [push(p, 0.40) for p in rear]
    m.solid("bustle_floor", [(x, y, z) for x, y in rear+outer for z in (2.20, 2.24)], "recess")
    for a, b in zip(outer[:-1], outer[1:]):
        wall = [(x, y, z) for x, y in (a, b, push(a, -0.05), push(b, -0.05)) for z in (2.20, 2.52)]
        m.solid("bustle_rack", wall)
    for a, b in ((rear[0], outer[0]), (rear[-1], outer[-1])):  # end walls
        d = np.array(b)-np.array(a)
        n = np.array([d[1], -d[0]])/np.linalg.norm(d)*0.05
        m.solid("bustle_rack", [(x, y, z) for x, y in (a, b, tuple(a+n), tuple(b+n)) for z in (2.20, 2.52)])
    m.solid("bustle_stowage", box((-0.84, 1.36, 2.24), (-0.34, 1.62, 2.46)))
    m.solid("bustle_stowage", box((-0.22, 1.36, 2.24), (0.22, 1.62, 2.40)))
    m.solid("bustle_stowage", [(x, y, z) for x, y in ((0.56, 1.10), (0.78, 1.26), (0.62, 1.44), (0.42, 1.30)) for z in (2.24, 2.44)])

    # Antenna bases (whips themselves are sub-pixel and not modelled).
    for x, y in ((0.26, 1.04), (-0.66, 1.08)):
        m.solid("antenna_bases", cylinder((x, y, 2.69), (x, y, 2.80), 0.045, 10), "metal")


def build_gun(m):
    # Mantlet / gun shield with a chamfered top front.
    dx, dy = GUN_X-(-0.23), GUN_DY
    mantlet = [(-0.55, -0.98, 2.24), (0.09, -0.98, 2.24), (-0.55, -0.98, 2.56), (0.09, -0.98, 2.56),
               (-0.55, -0.86, 2.66), (0.09, -0.86, 2.66), (-0.55, -0.76, 2.66), (0.09, -0.76, 2.66),
               (-0.55, -0.76, 2.22), (0.09, -0.76, 2.22)]
    m.solid("mantlet", [(x+dx, y+dy, z) for x, y, z in mantlet])
    front = -0.97+dy
    m.solid("gun_shroud", cylinder((GUN_X, front, GUN_Z), (GUN_X, front-0.35, GUN_Z), 0.075, 16), "metal")
    m.solid("gun_barrel", cylinder((GUN_X, front-0.33, GUN_Z), (GUN_X, MUZZLE_Y+0.12, GUN_Z), 0.042, 16), "metal")
    m.solid("gun_barrel", cylinder((GUN_X, MUZZLE_Y+0.14, GUN_Z), (GUN_X, MUZZLE_Y, GUN_Z), 0.055, 16), "metal")
    m.quad("gun_barrel", ring((GUN_X, MUZZLE_Y-0.0005, GUN_Z), (0, 1, 0), 0.026, 12), (0, -1, 0), "bore")
    m.solid("coax", cylinder((-0.46+dx, front, 2.40), (-0.46+dx, front-0.19, 2.40), 0.03, 10), "metal")


def main():
    m = Model()
    build_hull(m)
    build_running_gear(m)
    build_turret(m)
    build_gun(m)
    vertices, faces = m.write(OUTPUT)
    print(f"Wrote {OUTPUT} ({len(m.groups)} groups, {vertices} vertices, {faces} faces)")


if __name__ == "__main__":
    main()
