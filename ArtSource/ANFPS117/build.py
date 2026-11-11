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
wall = s.pal('Equipment_building_cladding_', [(176,166,145),(159,149,129),(149,139,120)], False)
rib = s.pal('Cladding_vertical_rib_', [(157,147,127),(147,137,118),(140,130,112)], False)
trim = s.pal('Radome_mounting_ring_', [(177,183,179),(151,159,154),(141,149,144)], False)
door = s.pal('Service_door_', [(103,119,112),(83,99,92),(75,91,84)], False)
dark = s.pal('Insulator_recess_', [(57,64,60),(48,55,51),(44,51,47)], False)
silver = s.pal('Door_hardware_', [(188,193,188),(170,176,170),(157,163,157)], False)
seam = s.mat('Radome_panel_joint', (181,188,186), False)

s.cyl('01_Concrete_foundation', (0,0,.09), 1.49,.18,concrete,vertices=64)
s.cyl('02_Tall_equipment_building', (0,0,1.15), 1.38,1.94,wall,vertices=64)
s.cyl('03_Foundation_skirt', (0,0,.24), 1.42,.12,concrete,vertices=64)
# Vertical standing seams echo the reference's beige metal-clad equipment room.
for i in range(96):
    angle = i*math.tau/96
    x,y = 1.385*math.cos(angle),1.385*math.sin(angle)
    if y < -1.29 and abs(x)<.35:
        continue
    s.rod('03_Cladding_rib_%02d'%i,(x,y,.31),(x,y,2.08),.009,rib)
s.cyl('04_Roof_edge_fascia', (0,0,2.15), 1.80,.13,trim,vertices=64)
s.ring('04_Perimeter_maintenance_walkway', (0,0,2.23),1.86,1.37,.08,trim,axis='Z')
s.cyl('04_Radome_mounting_collar', (0,0,2.29),1.44,.12,trim,vertices=64)
for z in [2.46,2.70]:
    s.ring('14_Continuous_guardrail', (0,0,z),1.875,1.845,.028,dark,axis='Z')
for i in range(24):
    angle=i*math.tau/24
    x,y=1.86*math.cos(angle),1.86*math.sin(angle)
    s.rod('15_Guardrail_post_%02d'%i,(x,y,2.25),(x,y,2.72),.018,dark)
    if i%3==0:
        s.rod('16_Walkway_diagonal_brace_%02d'%i,
              (1.36*math.cos(angle),1.36*math.sin(angle),1.65),(x,y,2.17),.025,trim)
s.cube('05_Entry_frame', (0,-1.365,.83), (.65,.13,1.20), trim)
s.cube('06_Service_door', (0,-1.441,.81), (.51,.025,1.06), door)
s.cube('07_Door_latch', (.16,-1.465,.80), (.035,.025,.105), silver)
for i in range(3):
    s.cube('08_Entry_step', (0,-1.55-i*.13,.235-i*.045), (.78,.21,.09), concrete)
for x in [-.285,.285]:
    for z in [.48,1.12]:
        s.cube('09_Door_hinge', (x,-1.454,z), (.035,.035,.065), silver)
# Small high windows and low wall vents are separate from the dome shell.
for side in [-1,1]:
    s.cube('10_Side_vent_frame', (side*1.389,0,.65), (.035,.58,.31), trim)
    for i in range(3):
        s.cube('11_Vent_slot', (side*1.415,0,.57+i*.08), (.018,.46,.035), dark)
    s.cube('17_High_window_frame', (side*1.39,0,1.65), (.05,.43,.39), dark)
    s.cube('18_High_window_glass', (side*1.421,0,1.65), (.014,.33,.29), door)
# Fixed external ladder reaches the rear platform, as in the first reference.
for x in [-.17,.17]:
    s.rod('19_Ladder_stile',(x,1.90,.20),(x,1.90,2.72),.021,dark)
for i in range(12):
    z=.30+i*.20
    s.rod('20_Ladder_rung',(-.17,1.90,z),(.17,1.90,z),.018,silver)
for z in [.47,1.28,2.16]:
    for x in [-.17,.17]:
        s.rod('21_Ladder_wall_bracket',(x,1.37,z),(x,1.90,z),.022,trim)

RADIUS = 1.65
CENTER_Z = 3.16
CUT_Z = 2.33
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
    outward = (center-Vector((0,0,CENTER_Z))).normalized()
    inset = [center+(point-center)*.974+outward*.003 for point in polygon]
    s.quad('13_Radome_panel_%03d'%number, inset, palette[value])

before = s.geometry_hash()
from building_theme import mesh_shape_hash
shape_before = mesh_shape_hash(scene)
s.export(str(OUT),'HD_ANFPS117',(4,4),[
    'closed white geodesic radome; radar antenna intentionally enclosed',
    'near-spherical radome on a tall beige metal-clad equipment building',
    'perimeter maintenance catwalk, guardrails and diagonal support brackets',
    'service door, entry steps, high windows, ventilation and external access ladder',
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
    'radome_radius':RADIUS,
    'radome_center_height':CENTER_Z,
    'radome_cut_height':CUT_Z,
    'reference_form':'user photos: near-spherical dome, tall beige base, perimeter catwalk and railing',
})
(OUT/'validation.json').write_text(json.dumps(metadata,indent=2)+'\n')
print('ANFPS117_BUILD_PASS',len(polygons),'panels; 4x4; 1024px; four real 64-degree views')
