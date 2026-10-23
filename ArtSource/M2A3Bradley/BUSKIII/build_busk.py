"""Separate cosmetic BUSK/BRAT exterior. No explosive internals or source edits."""
from pathlib import Path
from collections import Counter
import hashlib
import json
import math
import sys

import bpy
from mathutils import Matrix, Vector

HERE = Path(__file__).resolve().parent
BRADLEY = HERE.parent
sys.path.insert(0, str(BRADLEY))
sys.path.insert(0, str(BRADLEY/'EmissionTrial/GrilleGrid'))
from height_palette import final_paint
from grid_material import prepare_export

cfg = json.loads((HERE/'settings.json').read_text(encoding='utf-8'))
source = (HERE/cfg['source']).resolve()
assert hashlib.sha256(source.read_bytes()).hexdigest() == cfg['source_sha256']
bpy.ops.wm.open_mainfile(filepath=str(source))
scene = bpy.context.scene
scene.frame_set(1)
bpy.context.view_layer.update()
originals = [o for o in scene.objects if o.type == 'MESH']
paint = json.loads((BRADLEY/'part_contrast.json').read_text())['height_first_paint']
OUT = HERE/'output'
OUT.mkdir(exist_ok=True)
(OUT/'Tiles').mkdir(exist_ok=True)


def fingerprint(obj):
    return hashlib.sha256(repr((
        [tuple(v.co) for v in obj.data.vertices], [tuple(p.vertices) for p in obj.data.polygons],
        [tuple(obj.data.materials[p.material_index].diffuse_color) for p in obj.data.polygons],
        obj.parent.name if obj.parent else None, [tuple(row) for row in obj.matrix_world]
    )).encode()).hexdigest()


baseline = {o.name: fingerprint(o) for o in originals}
base_matrices = {o.name:o.matrix_world.copy() for o in originals}
kit = bpy.data.collections.new('BUSK III | removable exterior kit')
scene.collection.children.link(kit)
materials = {}
records = []
contact_errors = []
changes = []


def fixed(y, name='TAN'):
    key = (y, name)
    if key in materials:
        return materials[key]
    rgb, _, _ = final_paint(Vector((0, 0, 1)), 'hull_assembly', 1, paint, final_value=y)
    mat = bpy.data.materials.new(f'BUSK {name} Y{y}')
    mat.use_nodes = True
    mat.diffuse_color = (*rgb, 1)
    mat['fixed_emission'] = True
    mat['display_luminance'] = y
    nodes = mat.node_tree.nodes
    nodes.clear()
    e = nodes.new('ShaderNodeEmission')
    e.inputs['Color'].default_value = (*rgb, 1)
    e.inputs['Strength'].default_value = 1
    output = nodes.new('ShaderNodeOutputMaterial')
    mat.node_tree.links.new(e.outputs[0], output.inputs['Surface'])
    materials[key] = mat
    return mat


def root(name, parent):
    obj = bpy.data.objects.new(name, None)
    kit.objects.link(obj)
    obj.parent = parent
    obj.matrix_world = Matrix.Identity(4)
    return obj


hull_parent = root('BUSK_HULL_KIT', bpy.data.objects['HULL'])
turret_parent = root('BUSK_TURRET_KIT', bpy.data.objects['TURRET_YAW'])


def box_mesh(vertices, faces, low, high, transform=None):
    offset = len(vertices)
    x, y, z = low
    X, Y, Z = high
    points = [(x,y,z),(X,y,z),(X,Y,z),(x,Y,z),(x,y,Z),(X,y,Z),(X,Y,Z),(x,Y,Z)]
    vertices.extend([tuple(transform@Vector(p)) if transform else p for p in points])
    for face in [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]:
        faces.append(tuple(offset+i for i in face))


def object_mesh(name, vertices, faces, parent, palette):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    kit.objects.link(obj)
    obj.parent = parent
    obj.matrix_world = Matrix.Identity(4)
    for value in palette:
        mesh.materials.append(fixed(value))
    for poly in mesh.polygons:
        normal = poly.normal
        poly.material_index = 0 if normal.z > .45 else 3 if normal.z < -.45 else 1 if abs(normal.y) > .5 else 2
    return obj


def hit_surface(hint, normal, targets, distance=3):
    origin = Vector(hint)+normal*distance
    candidates = []
    for obj in targets:
        inverse = obj.matrix_world.inverted()
        hit, co, n, face = obj.ray_cast(inverse@origin, inverse.to_3x3()@(-normal))
        if hit:
            world = obj.matrix_world@co
            wn = (obj.matrix_world.to_3x3()@n).normalized()
            if wn.dot(normal) > .12 and abs((world-Vector(hint)).dot(normal)) < 1.0:
                candidates.append(((world-origin).length, obj, world, wn, face))
    return min(candidates, key=lambda r:r[0]) if candidates else None


def tile(name, family, hint, normal, width, height, depth, targets, parent, side_plane=None,
         follow_surface_normal=True):
    n = Vector(normal).normalized()
    mount_y = height*(.40 if family == 'LOWER_SKIRT' else -.30) if side_plane is not None else 0
    probe = Vector(hint)+Vector((0,0,mount_y)) if side_plane is not None else Vector(hint)
    found = hit_surface(probe, n, targets)
    if not found:
        raise ValueError(f'{name}: missing actual armor backing at {hint}')
    _, receiver, surface, actual_normal, face_id = found
    if side_plane is None and follow_surface_normal:
        n = actual_normal
    u = Vector((0,0,1)).cross(n).normalized()
    v = n.cross(u).normalized()
    rotation = Matrix((u,v,n)).transposed()
    if side_plane is not None:
        center = Vector(hint)
        center.x = math.copysign(side_plane-depth/2, normal[0])
    else:
        center = surface+n*(.035+depth/2)
    push = 0
    for dx in [-width*.45, 0, width*.45]:
        for dy in [-height*.45, 0, height*.45]:
            rear = center+u*dx+v*dy-n*depth/2
            contact = hit_surface(rear, n, targets)
            if contact:
                push = max(push, (contact[2]-rear).dot(n)+.012)
    if side_plane is None:
        center += n*push
    elif push > .025:
        raise ValueError(f'{name}: proposed fixed side plane intersects the source by {push:.4f}')
    transform = Matrix.Translation(center)@rotation.to_4x4()
    vertices, faces = [], []
    box_mesh(vertices, faces, (-width/2,-height/2,-depth/2), (width/2,height/2,depth/2), transform)
    # Two independent rear standoffs reach the actual original armor. No tile is
    # accepted merely because its center looks near a vehicle bounding box.
    mounts = []
    for dx in [-width*.30, width*.30]:
        rear = center+u*dx+v*mount_y-n*depth/2
        contact = hit_surface(rear, n, targets)
        if contact is None:
            # A continuous rack can use a central load point near an outer corner.
            contact = found
            dx = 0
            rear = center+v*mount_y-n*depth/2
        _, backing, point, _, backing_face = contact
        gap = (rear-point).dot(n)
        if gap < -.006 or gap > .85:
            raise ValueError(f'{name}: case intersects armor / unsupported gap {gap:.4f}')
        end = rotation.transposed()@(point-center)
        box_mesh(vertices, faces, (dx-.018,mount_y-.025,end.z-.003),
                 (dx+.018,mount_y+.025,-depth/2+.006), transform)
        mounts.append({'receiver':backing.name,'face':backing_face,'gap':gap,'penetration':.003})
    values = [216,192,168,108] if parent == turret_parent else [192,168,144,96]
    if family == 'LOWER_SKIRT':
        values = [168,144,132,96]
    if family == 'LOWER_SKIRT':
        # The replica skirt has a horizontal division through each vertical bay.
        # Keep one removable skirt segment per bay; the seam is a painted visual detail.
        box_mesh(vertices,faces,(-width/2,-.008,depth/2-.001),
                 (width/2,.008,depth/2+.002),transform)
    obj = object_mesh(name, vertices, faces, parent, values)
    if family == 'LOWER_SKIRT':
        obj.data.materials.append(fixed(108))
        for poly in list(obj.data.polygons)[-6:]:
            poly.material_index = len(obj.data.materials)-1
        obj['skirt_visual_seam'] = 'included in this removable mesh'
    obj.data.transform(Matrix.Translation(-center))
    obj.matrix_world = Matrix.Translation(center)
    obj['removable_tile'] = True
    obj['visual_family'] = family
    obj['dimension_status'] = 'exterior estimate, not verified installation dimensions'
    obj['case_center'] = list(center)
    obj['case_rotation'] = [list(row) for row in rotation]
    obj['case_size'] = [width,height,depth]
    records.append({'name':name,'family':family,'parent':parent.name,'case_center':list(center),
                    'rotation':[list(row) for row in rotation], 'size':[width,height,depth],
                    'surface_fit_offset':push if side_plane is None else 0,
                    'mounts':mounts,'independent_file':'Tiles/'+name+'.glb'})
    return obj


hull = [bpy.data.objects[n] for n in ['WT_@root','WT_hatch_06','WT_hatch_05']]
armor = [bpy.data.objects['WT_bone_turret']]
half_width = cfg['replica_envelope_width']/2
# Vehicle-left is source +X (TOW side); do not mirror labels from an arbitrary camera.
for side,label,count in [(1,'L',15),(-1,'R',16)]:
    for i in range(count):
        pitch = 5.25/(count-1)
        tile(f'BRAT_M5_{label}_LOW_{i+1:02d}','M5',(side*1.55,-2.62+i*pitch,1.56),
             (side,0,0),pitch-.014,.35,.24,hull,hull_parent,half_width)
    for i in range(12):
        tile(f'BRAT_M5_{label}_HIGH_{i+1:02d}','M5',(side*1.48,-.95+i*.326,1.91),
             (side,0,0),.312,.34,.24,hull,hull_parent,half_width)
    # These are visible outer skirt subdivisions counted in the replica drawing,
    # not an invented claim about the real XM33 kit's ammunition inventory.
    for i in range(cfg['lower_skirt_columns_per_side']):
        pitch = 5.05/cfg['lower_skirt_columns_per_side']
        tile(f'BUSK_LOWER_{label}_{i+1:02d}','LOWER_SKIRT',(side*1.59,-2.37+(i+.5)*pitch,1.10),
             (side,0,0),pitch-.008,.53,.035,hull,hull_parent,half_width)

for row,y in enumerate([-3.025,-2.775,-2.525]):
    for col in range(6):
        x = -.80+col*.32
        tile(f'BRAT_M3_FRONT_{row+1}_{col+1}','M3',(x,y,1.45+(y+3.0)*.65),
             (0,-.65,.76),.307,.30,.13,hull,hull_parent)
for side,label in [(1,'L'),(-1,'R')]:
    for row,y in enumerate([-3.015,-2.76]):
        for col,x in enumerate([1.09,1.35]):
            tile(f'BRAT_M4_SHOULDER_{label}_{row+1}_{col+1}','M4',(side*x,y,1.47+(y+3.0)*.65),
                 (0,-.65,.76),.24,.30,.13,hull,hull_parent)

# Published vehicle data describes turret front and right-side ERA. Keep the
# left TOW launcher clear; the former generic 4-left/3-right turret split is not reused.
for i in range(7):
    # A consistent rack inclination avoids snapping a case onto a small basket
    # bracket's unrelated normal while still ray-fitting its rear standoffs.
    tile(f'BRAT_M6_TURRET_R_{i+1:02d}','M6',(-1.3,-.35+i*.28,2.53),(-.877,0,.480),
         .266,.34,.13,armor,turret_parent,follow_surface_normal=False)
for label,xs in [('R',[-1.08,-.84]),('L',[.19,.43])]:
    for row,z in enumerate([2.43,2.64]):
        for col,x in enumerate(xs):
            tile(f'BRAT_M7_FRONT_{label}_{row+1}_{col+1}','M7',(x,-.70,z),(0,-1,.10),
                 .215,.195,.105,armor,turret_parent)

# A low-profile belly plate and mounting rails are exterior-only BUSK shapes.
vertices, faces = [], []
box_mesh(vertices,faces,(-1.10,-2.30,.31),(1.10,2.30,.365))
object_mesh('BUSK_BELLY_EXTERNAL_PLATE',vertices,faces,hull_parent,[120,108,96,84])

# Transparent-armor appearance is painted as opaque, fixed optical color so the
# game sprite stays independent of lights/view. The source hatch remains separate.
vertices,faces=[],[]
cx,cy = -.53,.61
for angle in [math.radians(v) for v in [-45,0,45,90,135]]:
    midpoint=Vector((cx+math.cos(angle)*.47,cy+math.sin(angle)*.47,3.12))
    transform=Matrix.Translation(midpoint)@Matrix.Rotation(angle,4,'Z')
    for low,high in [((-.025,-.19,-.18),(.025,.19,-.16)), ((-.025,-.19,.16),(.025,.19,.18)),
                     ((-.025,-.19,-.16),(.025,-.17,.16)), ((-.025,.17,-.16),(.025,.19,.16))]:
        box_mesh(vertices,faces,low,high,transform)
    glass_vertices,glass_faces=[],[]
    box_mesh(glass_vertices,glass_faces,(-.014,-.17,-.16),(.014,.17,.16),transform)
    glass=object_mesh('BUSK_CPS_GLASS_'+str(round(math.degrees(angle))),glass_vertices,glass_faces,turret_parent,[96,96,96,96])
shield=object_mesh('BUSK_COMMANDER_PROTECTION_SHIELD',vertices,faces,turret_parent,[192,156,156,120])
shield['dimension_status']='MENG exterior silhouette estimate; not measured ballistic glass dimensions'

# The installed front cases need the raised headlamp brackets visible in the
# reference drawing. Move only the two existing housings within this variant.
for name in ['WT_ex_lantern_l_01','WT_ex_lantern_r_01']:
    lamp=bpy.data.objects[name]
    coordinates=[lamp.matrix_world@v.co for v in lamp.data.vertices]
    x=(min(v.x for v in coordinates)+max(v.x for v in coordinates))/2
    y=(min(v.y for v in coordinates)+max(v.y for v in coordinates))/2
    z=min(v.z for v in coordinates)
    lamp.location.z += .32
    vertices,faces=[],[]
    for dx in [-.08,.08]:
        box_mesh(vertices,faces,(x+dx-.02,y-.065,z-.008),(x+dx+.02,y+.065,z+.325))
    object_mesh('BUSK_RAISED_LAMP_BRACKET_'+name,vertices,faces,hull_parent,[192,168,144,108])
    changes.append({'object':name,'world_raise_z':.32,'reason':'Front ERA clearance; source housing geometry and paint preserved'})

counts=Counter(r['family'] for r in records)
assert {key:counts[key] for key in cfg['upper_counts']} == cfg['upper_counts']
assert counts['LOWER_SKIRT']==28
bpy.context.view_layer.update()
allowed={change['object'] for change in changes}
for name,digest in baseline.items():
    obj=bpy.data.objects[name]
    current=obj.matrix_world.copy()
    if name in allowed:
        obj.matrix_world=base_matrices[name]
    assert fingerprint(obj)==digest,('Unexpected base geometry/paint/parent/transform change',name)
    obj.matrix_world=current
assert not any(o.type=='LIGHT' for o in scene.objects)
source_faces=sum(len(o.data.polygons) for o in originals)
manifest={'source':str(source),'source_sha256':cfg['source_sha256'],'base_faces':source_faces,
          'base_meshes':len(originals),'base_geometry_paint_parent_unchanged':True,
          'base_transforms_unchanged_except':changes,
          'counts':dict(counts),'upper_total':96,'lower_visual_segments':28,
          'dimension_status':'provisional: no dimensioned full-size installation blueprint',
          'family_position_status':'photo/replica-derived; family quantities confirmed, exact mapping not certified',
          'target_busk_kit_inventory_verified':False,
          'objects':records,'settings':cfg}
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
scene.camera=bpy.data.objects['Camera_west']
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'bradley_busk_iii.blend'))

# Preserve the existing packed unlit square grille in GLB, too.
for mat in bpy.data.materials:
    if not mat.use_nodes or prepare_export(mat):continue
    output=next((n for n in mat.node_tree.nodes if n.type=='OUTPUT_MATERIAL'),None)
    emission=next((n for n in mat.node_tree.nodes if n.type=='EMISSION'),None)
    if output and emission:
        rgb=mat.node_tree.nodes.new('ShaderNodeRGB')
        rgb.outputs[0].default_value=emission.inputs['Color'].default_value
        mat.node_tree.links.new(rgb.outputs[0],output.inputs['Surface'])
bpy.ops.export_scene.gltf(filepath=str(OUT/'bradley_busk_iii.glb'),export_format='GLB',export_cameras=False,export_lights=False,export_animations=True)
for record in records:
    original=bpy.data.objects[record['name']]
    duplicate=original.copy();duplicate.data=original.data.copy();scene.collection.objects.link(duplicate)
    # The mesh is already centered on the case. Reset only its placement so a
    # standalone file rotates about the case center, rather than the vehicle.
    duplicate.parent=None;duplicate.matrix_world=Matrix.Identity(4)
    bpy.ops.object.select_all(action='DESELECT');duplicate.select_set(True)
    bpy.ops.export_scene.gltf(filepath=str(OUT/record['independent_file']),export_format='GLB',use_selection=True,
                              export_cameras=False,export_lights=False,export_animations=False)
    bpy.data.objects.remove(duplicate,do_unlink=True)
print('BUSK EXTERIOR BUILT: 96 independent BRAT cases + 28 provisional outer skirt segments')
