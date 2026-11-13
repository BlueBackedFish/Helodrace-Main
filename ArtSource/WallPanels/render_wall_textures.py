"""Blender background: render the sandwich wall linked atlas, panel overlays and menu icon.

Run from the repository root:
  blender -b --python-exit-code 1 --python ArtSource/WallPanels/render_wall_textures.py

Building texture rules applied (ArtSource/IndustrialMachines/PRESENTATION_CALIBRATION.md,
BUILDING_COLOR_THEME.md): fixed Emission colors, no lighting/shadow/AO, transparent
background, Standard view transform, black external outline of 0.0703125 game units.
Walls use the engine's linked atlas instead of 64-degree sprites, so the projection is
the linked-wall convention: plan view with height sheared northward.
"""
import bpy, bmesh, os, json, math, hashlib
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
TEX = os.path.join(ROOT, 'Textures', 'Buildings', 'WallPanels')
OVERLAY = os.path.join(TEX, 'Overlay')
REVIEW = os.path.join(HERE, 'Review')
for d in (TEX, OVERLAY, REVIEW):
    os.makedirs(d, exist_ok=True)

PX = 168                    # texture pixels per game tile (see validation notes)
OUTLINE = 12                # 0.0703125 units at 170.67 px/unit -> 11.8 px at 168
ATLAS = PX * 16 // 3        # 896: 4x4 cells of 224 px, inner 3/4 (168 px) sampled
CELL = ATLAS // 4
INSET = ATLAS // 32         # engine offset 0.03125, scale 0.1875
SHEAR = 0.2                 # projected northward shift per unit height
R, H = 0.09, 1.25           # half thickness, height (wall source model)
PANEL_T, PANEL_LEN, PANEL_H = 0.03, 0.9, H  # full height: a north-face lining shows as its top edge

def srgb(v): return [c / 255 for c in v]
def to_linear(c): return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

# BUILDING_COLOR_THEME.md "Working silver" top/front; white body for exposed foam.
PALETTE = {
    'metal_top': (193, 193, 190), 'metal_front': (180, 180, 177), 'metal_rib': (166, 166, 163),
    'foam_top': (239, 239, 237), 'foam_front': (228, 228, 225),
}
PANELS = {  # top colour; front is the theme's top->front ratio
    'MineralWool': (176, 161, 110), 'Styrofoam': (239, 239, 237), 'RHA': (105, 105, 105),
    'Aramid': (189, 150, 56), 'EMI': (184, 201, 207),
}
FRONT_RATIO = 180 / 193

scene = bpy.context.scene
for ob in list(bpy.data.objects): bpy.data.objects.remove(ob, do_unlink=True)
scene.render.engine = 'CYCLES'
scene.cycles.samples = 32
scene.cycles.use_denoising = False
scene.cycles.filter_width = 1.0
scene.render.film_transparent = True
scene.view_settings.view_transform = 'Standard'
scene.view_settings.look = 'None'
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
scene.render.image_settings.color_depth = '8'
scene.render.resolution_percentage = 100
scene.world = scene.world or bpy.data.worlds.new('World')
scene.world.use_nodes = True
scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0

_mats = {}
def emission(name, rgb):
    key = (name, rgb)
    if key in _mats: return _mats[key]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree; nt.nodes.clear()
    e = nt.nodes.new('ShaderNodeEmission')
    e.inputs['Color'].default_value = (*[to_linear(c) for c in srgb(rgb)], 1)
    e.inputs['Strength'].default_value = 1
    o = nt.nodes.new('ShaderNodeOutputMaterial')
    nt.links.new(e.outputs[0], o.inputs[0])
    _mats[key] = m
    return m

def holdout():
    if 'holdout' in _mats: return _mats['holdout']
    m = bpy.data.materials.new('Holdout'); m.use_nodes = True
    nt = m.node_tree; nt.nodes.clear()
    h = nt.nodes.new('ShaderNodeHoldout'); o = nt.nodes.new('ShaderNodeOutputMaterial')
    nt.links.new(h.outputs[0], o.inputs[0]); _mats['holdout'] = m
    return m

def add_mesh(name, faces, offset=(0, 0)):
    """faces: list of (verts, material). Height is sheared into +Y."""
    me = bpy.data.meshes.new(name); bm = bmesh.new(); slots = []
    for verts, mat in faces:
        if mat not in slots: slots.append(mat)
        f = bm.faces.new([bm.verts.new((x + offset[0], y + offset[1] + SHEAR * z, z)) for x, y, z in verts])
        f.material_index = slots.index(mat)
    bm.to_mesh(me); bm.free()
    for m in slots: me.materials.append(m)
    ob = bpy.data.objects.new(name, me); scene.collection.objects.link(ob)
    return ob

def wall_faces(mask):
    """Same union outline as the original connection models: N=1 E=2 S=4 W=8."""
    # An unconnected module stops short of the cell edge so its outline and
    # end caps stay inside its own tile.
    coords = [-.41, -R, R, .41] if mask == 0 else [-.5, -R, R, .5]
    active = {(1, 1)}
    if mask == 0: active |= {(0, 1), (2, 1)}
    if mask & 1: active.add((1, 2))
    if mask & 2: active.add((2, 1))
    if mask & 4: active.add((1, 0))
    if mask & 8: active.add((0, 1))
    faces = []
    for i, j in sorted(active):
        x0, x1 = coords[i:i + 2]; y0, y1 = coords[j:j + 2]
        faces.append(([(x0, y0, H), (x1, y0, H), (x1, y1, H), (x0, y1, H)], emission('metal_top', PALETTE['metal_top'])))
        # A face on the cell boundary is a connection port: the neighbouring
        # module continues the wall there, so no facade is drawn.
        if (i, j - 1) in active or y0 < -.49: continue
        # Only south-facing vertical faces are visible in the sheared plan view.
        length = x1 - x0
        continuous = bool(mask & 10) or mask == 0
        cut = length < .181 and not continuous
        key = 'foam_front' if cut else 'metal_front'
        faces.append(([(x0, y0, 0), (x1, y0, 0), (x1, y0, H), (x0, y0, H)], emission(key, PALETTE[key])))
        if not cut:  # vertical ribs at fixed world spacing so runs stay continuous
            for k in range(-2, 3):
                rx = k * .25 + .125
                if x0 + .02 < rx - .007 and rx + .007 < x1 - .02:
                    faces.append(([(rx - .007, y0 - .0005, .03), (rx + .007, y0 - .0005, .03),
                                   (rx + .007, y0 - .0005, H - .03), (rx - .007, y0 - .0005, H - .03)],
                                  emission('metal_rib', PALETTE['metal_rib'])))
    return faces

def clear():
    for ob in list(scene.objects): bpy.data.objects.remove(ob, do_unlink=True)

def camera(width_tiles, height_tiles, center=(0, 0), px=PX):
    cam_data = bpy.data.cameras.new('Plan'); cam_data.type = 'ORTHO'
    cam_data.ortho_scale = max(width_tiles, height_tiles)
    cam = bpy.data.objects.new('Plan', cam_data); scene.collection.objects.link(cam)
    cam.location = (center[0], center[1], 20); cam.rotation_euler = (0, 0, 0)
    scene.camera = cam
    scene.render.resolution_x = round(width_tiles * px)
    scene.render.resolution_y = round(height_tiles * px)

def render():
    path = os.path.join(bpy.app.tempdir, 'wall_render.png')
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(path)
    w, h = img.size
    a = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)[::-1].copy()  # top row first
    bpy.data.images.remove(img)
    return a

def dilate(alpha, radius):
    out = alpha.copy(); h, w = alpha.shape
    pad = np.pad(alpha, radius)
    for dy in range(-radius, radius + 1):
        for dx in range(-radius, radius + 1):
            if dx * dx + dy * dy <= radius * radius + radius:
                np.maximum(out, pad[radius + dy:radius + dy + h, radius + dx:radius + dx + w], out=out)
    return out

def outline_under(rgba, ring_alpha):
    """Composite rgba over black with ring_alpha coverage (premultiplied over)."""
    a = rgba[..., 3:4]; ring = ring_alpha[..., None]
    out_a = a + ring * (1 - a)
    rgb = np.where(out_a > 0, rgba[..., :3] * a / np.maximum(out_a, 1e-6), 0)
    return np.concatenate([rgb, out_a], axis=-1)

def save(rgba, path):
    h, w = rgba.shape[:2]
    img = bpy.data.images.new('out', w, h, alpha=True)
    img.pixels.foreach_set(np.clip(rgba[::-1], 0, 1).astype(np.float32).ravel())
    img.filepath_raw = path; img.file_format = 'PNG'; img.save()
    bpy.data.images.remove(img)

def place_wall(mask, offset):
    return add_mesh('Wall_%02d' % mask, wall_faces(mask), offset)

NEIGHBOR = {1: ((0, 1), 5), 2: ((1, 0), 10), 4: ((0, -1), 5), 8: ((-1, 0), 10)}

# ---- linked atlas -------------------------------------------------------
atlas = np.zeros((ATLAS, ATLAS, 4), dtype=np.float32)
cells = []
for mask in range(16):
    clear(); camera(3, 3)
    place_wall(mask, (0, 0))
    for bit, (off, straight) in NEIGHBOR.items():
        if mask & bit: place_wall(straight, off)
    img = render()
    img = outline_under(img, dilate(img[..., 3], OUTLINE))
    tile = img[PX:2 * PX, PX:2 * PX]
    cells.append(tile)
    # Engine sub-UV: offset ((i%4)*.25+1/32, (i/4)*.25+1/32), bottom-up rows.
    row = 3 - mask // 4; col = mask % 4
    padded = np.pad(tile, ((INSET, INSET), (INSET, INSET), (0, 0)), mode='edge')
    atlas[row * CELL:(row + 1) * CELL, col * CELL:(col + 1) * CELL] = padded
save(atlas, os.path.join(TEX, 'HD_SandwichWall_Atlas.png'))

# ---- menu icon: three straight modules ---------------------------------
clear(); camera(3, 3)
for mask, off in ((2, (-1, 0)), (10, (0, 0)), (8, (1, 0))): place_wall(mask, off)
img = render(); img = outline_under(img, dilate(img[..., 3], OUTLINE))
ys, xs = np.nonzero(img[..., 3] > 0.01)
crop = img[max(0, ys.min() - 2):ys.max() + 3, max(0, xs.min() - 2):xs.max() + 3]
icon = np.zeros((256, 256, 4), dtype=np.float32)
scale = 248 / max(crop.shape[:2])
ih, iw = round(crop.shape[0] * scale), round(crop.shape[1] * scale)
yy = (np.arange(ih) / scale).astype(int).clip(0, crop.shape[0] - 1)
xx = (np.arange(iw) / scale).astype(int).clip(0, crop.shape[1] - 1)
icon[(256 - ih) // 2:(256 - ih) // 2 + ih, (256 - iw) // 2:(256 - iw) // 2 + iw] = crop[yy][:, xx]
save(icon, os.path.join(TEX, 'HD_SandwichWall_MenuIcon.png'))

# ---- panel overlays -----------------------------------------------------
# Rotation N/E/S/W = the face the panel covers. Drawn at drawSize 1.5 on the host
# cell above the wall. Sandwich host: the real wall occludes it (holdout). Other
# walls/doors draw a flat atlas, so they occlude nothing and the panel sits on the
# cell edge.
DIRS = {'north': (0, 1), 'east': (1, 0), 'south': (0, -1), 'west': (-1, 0)}
def slab(dx, dy, dist, rgb):
    top = emission('panel_top', rgb)
    front = emission('panel_front', tuple(round(c * FRONT_RATIO) for c in rgb))
    if dx == 0:
        x0, x1 = -PANEL_LEN / 2, PANEL_LEN / 2
        y0, y1 = (dist, dist + PANEL_T) if dy > 0 else (-dist - PANEL_T, -dist)
    else:
        y0, y1 = -PANEL_LEN / 2, PANEL_LEN / 2
        x0, x1 = (dist, dist + PANEL_T) if dx > 0 else (-dist - PANEL_T, -dist)
    return [([(x0, y0, PANEL_H), (x1, y0, PANEL_H), (x1, y1, PANEL_H), (x0, y1, PANEL_H)], top),
            ([(x0, y0, 0), (x1, y0, 0), (x1, y0, PANEL_H), (x0, y0, PANEL_H)], front)]

OVER = round(1.5 * PX)  # 252 px canvas at drawSize 1.5
lo = (3 * PX - OVER) // 2
panel_alpha = {}
for host in ('Sandwich', 'Wall'):
    for d, (dx, dy) in DIRS.items():
        # host silhouette (including its outline) for the shared-outline rule
        clear(); camera(3, 3)
        # A panel on a north/south face sits on an east-west wall run and vice versa.
        straight = 10 if dx == 0 else 5
        run = [((k, 0) if dx == 0 else (0, k)) for k in (-1, 0, 1)]
        if host == 'Sandwich':
            for off in run: place_wall(straight, off)
            host_a = render()[..., 3]
        else:
            host_a = np.zeros((3 * PX, 3 * PX), dtype=np.float32); host_a[PX:2 * PX, PX:2 * PX] = 1
        host_ring = dilate(host_a, OUTLINE)
        for name, rgb in PANELS.items():
            clear(); camera(3, 3)
            if host == 'Sandwich':
                for off in run:
                    ob = place_wall(straight, off)
                    ob.data.materials.clear(); ob.data.materials.append(holdout())
                    for poly in ob.data.polygons: poly.material_index = 0
            add_mesh('Panel', slab(dx, dy, R if host == 'Sandwich' else .5, rgb))
            img = render()
            p = img[..., 3]
            ring = np.clip(dilate(np.maximum(p, host_a), OUTLINE) - host_ring, 0, 1) * (1 - p)
            img = outline_under(img, ring)
            out = img[lo:lo + OVER, lo:lo + OVER]
            save(out, os.path.join(OVERLAY, 'HD_Panel_%s_%s_%s.png' % (name, host, d)))
            panel_alpha['%s_%s_%s' % (name, host, d)] = float(out[..., 3].sum())

# ---- review sheet + validation -----------------------------------------
sheet = np.ones((4 * (PX + 8) + 8, 4 * (PX + 8) + 8, 4), dtype=np.float32) * [0.33, 0.31, 0.28, 1]
for mask, tile in enumerate(cells):
    y = 8 + (mask // 4) * (PX + 8); x = 8 + (mask % 4) * (PX + 8)
    a = tile[..., 3:4]; sheet[y:y + PX, x:x + PX, :3] = tile[..., :3] * a + sheet[y:y + PX, x:x + PX, :3] * (1 - a)
save(sheet, os.path.join(REVIEW, 'atlas_masks_review.png'))

def sha(path):
    with open(path, 'rb') as f: return hashlib.sha256(f.read()).hexdigest()
validation = {
    'atlas': {'size': ATLAS, 'cell': CELL, 'sampled_pixels_per_tile': PX, 'engine_uv_scale': CELL * 0.75 / ATLAS,
              'engine_uv_inset': INSET / ATLAS, 'bit_order': 'N=1 E=2 S=4 W=8', 'linkType': 'Basic',
              'sha256': sha(os.path.join(TEX, 'HD_SandwichWall_Atlas.png'))},
    'outline_pixels': OUTLINE, 'outline_game_units': OUTLINE / PX, 'shear_per_height': SHEAR,
    'palette': PALETTE, 'panel_palette': PANELS, 'panel_front_ratio': FRONT_RATIO,
    'overlay': {'canvas': OVER, 'drawSize': 1.5, 'count': len(panel_alpha),
                'empty': [k for k, v in panel_alpha.items() if v < 1]},
    'lighting': 'none (Emission only, Standard view transform, transparent film)',
}
assert abs(validation['atlas']['engine_uv_scale'] - 0.1875) < 1e-9 and abs(validation['atlas']['engine_uv_inset'] - 0.03125) < 1e-9
assert not validation['overlay']['empty'], validation['overlay']['empty']
with open(os.path.join(HERE, 'texture_validation.json'), 'w') as f: json.dump(validation, f, indent=2)
print('WALL_TEXTURES_COMPLETE', len(cells), len(panel_alpha))

# ---- panel-on-wall review: overlays composited over the atlas tiles -------
def over(dst, src):
    a = src[..., 3:4]
    dst[..., :3] = src[..., :3] * a + dst[..., :3] * (1 - a)
    dst[..., 3:4] = a + dst[..., 3:4] * (1 - a)
def load(path):
    img = bpy.data.images.load(path); w, h = img.size
    a = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)[::-1].copy(); bpy.data.images.remove(img); return a
names = list(PANELS); dirs = list(DIRS)
pad = (OVER - PX) // 2
review = np.ones((len(names) * (OVER + 8) + 8, len(dirs) * (OVER + 8) + 8, 4), dtype=np.float32) * [0.33, 0.31, 0.28, 1]
for r, name in enumerate(names):
    for c, d in enumerate(dirs):
        dx, dy = DIRS[d]; mask = 10 if dx == 0 else 5
        tile = np.zeros((OVER, OVER, 4), dtype=np.float32)
        tile[pad:pad + PX, pad:pad + PX] = cells[mask]
        over(tile, load(os.path.join(OVERLAY, 'HD_Panel_%s_Sandwich_%s.png' % (name, d))))
        y = 8 + r * (OVER + 8); x = 8 + c * (OVER + 8)
        over(review[y:y + OVER, x:x + OVER], tile)
save(review, os.path.join(REVIEW, 'panels_on_sandwich_review.png'))
print('PANEL_REVIEW_COMPLETE')

# ---- panel menu icons: the south lining on a straight sandwich wall --------
def fit_icon(img):
    ys, xs = np.nonzero(img[..., 3] > 0.01)
    crop = img[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    out = np.zeros((256, 256, 4), dtype=np.float32); s = 232 / max(crop.shape[:2])
    ih, iw = round(crop.shape[0] * s), round(crop.shape[1] * s)
    yy = (np.arange(ih) / s).astype(int).clip(0, crop.shape[0] - 1); xx = (np.arange(iw) / s).astype(int).clip(0, crop.shape[1] - 1)
    out[(256 - ih) // 2:(256 - ih) // 2 + ih, (256 - iw) // 2:(256 - iw) // 2 + iw] = crop[yy][:, xx]
    return out
for name in PANELS:
    clear(); camera(3, 3)
    add_mesh('Wall', wall_faces(0))
    add_mesh('Panel', slab(0, -1, R, PANELS[name]))
    img = render(); img = outline_under(img, dilate(img[..., 3], OUTLINE))
    save(fit_icon(img), os.path.join(TEX, 'HD_Panel_%s_MenuIcon.png' % name))
print('PANEL_ICONS_COMPLETE')

# ---- expanded polystyrene resource: two stacked foam boards ---------------
def board(x0, x1, y0, y1, z0, z1):
    top = emission('foam_top', PALETTE['foam_top']); front = emission('foam_front', PALETTE['foam_front'])
    return [([(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)], top),
            ([(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)], front)]
clear(); camera(1, 1)
add_mesh('Board', board(-.36, .30, -.30, .14, 0, .12))
add_mesh('Board', board(-.30, .36, -.24, .20, .12, .24))
img = render()
ITEM = os.path.join(ROOT, 'Textures', 'Items', 'HD_ExpandedPolystyrene.png')
save(outline_under(img, dilate(img[..., 3], OUTLINE)), ITEM)
print('EPS_ITEM_COMPLETE')
