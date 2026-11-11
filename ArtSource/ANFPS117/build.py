"""Build a stylized AN/FPS-117 radome building with the shared sprite pipeline."""
from pathlib import Path
import sys
import math
import json
import bpy
from mathutils import Vector

OUT = Path(__file__).resolve().parent
sys.path.insert(0, str(OUT.parent / 'IndustrialMachines'))
import sprite_common as s

s.period_start()
scene = bpy.context.scene
scene['design_period'] = 'AN/FPS-117 fixed radar, enclosed radome interpretation'
scene['north_azimuth'] = -90
concrete = s.pal('Concrete_plinth_', [(158,159,154),(140,142,137),(131,133,129)], False)
wall = s.pal('Concrete_equipment_building_', [(153,160,155),(125,134,129),(117,126,121)], False)
trim = s.pal('Radome_mounting_ring_', [(177,183,179),(151,159,154),(141,149,144)], False)
door = s.pal('Service_door_', [(103,119,112),(83,99,92),(75,91,84)], False)
dark = s.pal('Insulator_recess_', [(57,64,60),(48,55,51),(44,51,47)], False)
silver = s.pal('Door_hardware_', [(188,193,188),(170,176,170),(157,163,157)], False)
seam = s.mat('Radome_panel_joint', (181,188,186), False)

s.cube('01_Concrete_foundation', (0,0,.075), (3.82,3.82,.15), concrete)
s.cyl('02_Circular_equipment_room', (0,0,.46), 1.53,.62,wall,vertices=64)
s.cyl('03_Foundation_skirt', (0,0,.20), 1.59,.11,concrete,vertices=64)
s.cyl('04_Radome_mounting_collar', (0,0,.79), 1.60,.14,trim,vertices=64)
# A shallow entry projects from the circular room; no unrelated site furniture.
s.cube('05_Entry_frame', (0,-1.49,.45), (.69,.20,.60), trim)
s.cube('06_Service_door', (0,-1.601,.43), (.51,.025,.49), door)
s.cube('07_Door_latch', (.16,-1.625,.43), (.035,.025,.105), silver)
s.cube('08_Entry_step', (0,-1.67,.19), (.83,.38,.08), concrete)
for x in [-.285,.285]:
    for z in [.30,.57]:
        s.cube('09_Door_hinge', (x,-1.613,z), (.035,.035,.065), silver)
# Broad ventilation slots remain legible in the native image.
for side in [-1,1]:
    s.cube('10_Side_vent_frame', (side*1.526,0,.46), (.035,.65,.30), trim)
    for i in range(3):
        s.cube('11_Vent_slot', (side*1.55,0,.38+i*.08), (.018,.51,.035), dark)

RADIUS = 1.65
CENTER_Z = .96
CUT_Z = .84
bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=3, radius=RADIUS, location=(0,0,CENTER_Z))
source = bpy.context.object
source.name = 'Temporary_geodesic_layout'
source_points = [source.matrix_world @ v.co for v in source.data.vertices]
source_faces = [list(p.vertices) for p in source.data.polygons]
# Clip geodesic triangles at the collar, retaining a clean circular lower silhouette.
def clip(points):
    result = []
    for a,b in zip(points, points[1:]+points[:1]):
        inside_a, inside_b = a.z >= CUT_Z, b.z >= CUT_Z
        if inside_a:
            result.append(a)
        if inside_a != inside_b:
            result.append(a+(b-a)*((CUT_Z-a.z)/(b.z-a.z)))
    return result

polygons = [clip([source_points[i] for i in face]) for face in source_faces]
polygons = [p for p in polygons if len(p)>=3]
bpy.data.objects.remove(source, do_unlink=True)
verts, faces = [], []
for polygon in polygons:
    start = len(verts)
    verts.extend(polygon)
    faces.append(tuple(range(start,len(verts))))
mesh = bpy.data.meshes.new('Geodesic_joint_backing')
mesh.from_pydata(verts, [], faces)
mesh.update()
shell = bpy.data.objects.new('12_Continuous_joint_backing',mesh)
scene.collection.objects.link(shell)
mesh.materials.append(seam)
# Fixed face colors describe the dome curvature without lamps, AO or cast shadows.
# Narrow panel gaps are geometry, so they follow every actual camera consistently.
palette = {}
for number,polygon in enumerate(polygons):
    center = sum(polygon,Vector())/len(polygon)
    altitude = max(0,min(1,(center.z-CUT_Z)/(CENTER_Z+RADIUS-CUT_Z)))
    value = round(213+29*altitude)
    if value not in palette:
        palette[value] = s.mat('Radome_white_'+str(value),(value,value+1,value-1),False)
    inset = [center+(point-center)*.974+Vector((0,0,.002)) for point in polygon]
    s.quad('13_Radome_panel_%03d'%number, inset, palette[value])

before = s.geometry_hash()
from building_theme import mesh_shape_hash
shape_before = mesh_shape_hash(scene)
s.export(str(OUT),'HD_ANFPS117',(4,4),[
    'closed white geodesic radome; radar antenna intentionally enclosed',
    'low circular equipment building and mounting collar',
    'service door, entry step and two side ventilation grilles',
    'fixed face colors; no lights, cast shadows, ambient occlusion or texture noise',
])
assert mesh_shape_hash(bpy.context.scene) == shape_before, 'Export changed model geometry'
assert all(o.type != 'LIGHT' for o in bpy.context.scene.objects)
assert len([o for o in bpy.context.scene.objects if o.type == 'CAMERA']) == 4
assert all(n.type != 'BSDF_PRINCIPLED' for m in bpy.data.materials if m.use_nodes for n in m.node_tree.nodes)
metadata = json.loads((OUT/'validation.json').read_text())
metadata.update({
    'radome_panels':len(polygons),
    'enclosed_antenna':True,
    'lighting':'fixed RGB Emission only',
    'gameplay_definition_added':False,
    'interpretation':'stylized dome enclosure, not a surveyed site reconstruction',
    'source_geometry_palette_sha256':before,
    'saved_geometry_palette_sha256':s.geometry_hash(),
    'geometry_preserved_after_save_reload':True,
    'mesh_shape_sha256':shape_before,
})
(OUT/'validation.json').write_text(json.dumps(metadata,indent=2)+'\n')
print('ANFPS117_BUILD_PASS',len(polygons),'panels; 4x4; 1024px; four real 64-degree views')
