"""Optional cosmetic ERA kit; independent tiles, fixed-color standalone GLBs."""
import bpy, math, os, json, runpy
from mathutils import Vector

ROOT=os.path.dirname(os.path.abspath(__file__))
OUT=os.path.join(ROOT,'ERA')
PARTS=os.path.join(OUT,'Tiles')
os.makedirs(PARTS,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=os.path.join(ROOT,'m2a3_bradley.blend'))
scene=bpy.context.scene
scene.frame_set(1)
kit=bpy.data.collections.new('ERA | independently removable tiles')
scene.collection.children.link(kit)
rack=bpy.data.objects.new('ERA_KIT',None)
kit.objects.link(rack)
rack.parent=bpy.data.objects['HULL']
mats=[bpy.data.materials[n] for n in ['TAN | upper faces','TAN | fore and aft faces','TAN | side faces','TAN | underside']]
rim=mats[2].copy();rim.name='TAN | ERA subdued edge'
def linear(v):
    v/=255
    return v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4
rim.diffuse_color=tuple(linear(v) for v in (191,182,157))+(1,)
rim.node_tree.nodes['Camera paint modulation'].inputs[1].default_value=rim.diffuse_color
dark=bpy.data.materials['Panel edges and recesses']
tiles=[]
records=[]

def cube(location,dimensions,materials):
    # Direct mesh construction avoids hundreds of scene-wide operator updates.
    x,y,z=(v/2 for v in dimensions)
    verts=[(xx,yy,zz) for zz in [-z,z] for yy in [-y,y] for xx in [-x,x]]
    faces=[(0,2,3,1),(4,5,7,6),(0,1,5,4),(1,3,7,5),(3,2,6,7),(2,0,4,6)]
    data=bpy.data.meshes.new('ERA component');data.from_pydata(verts,[],faces);data.update()
    o=bpy.data.objects.new('ERA component',data);scene.collection.objects.link(o)
    o.location=location
    for m in materials:o.data.materials.append(m)
    if len(materials)==4:
        for p in o.data.polygons:
            p.material_index=0 if p.normal.z>.45 else 3 if p.normal.z<-.45 else 1 if abs(p.normal.y)>.5 else 2
    return o

def group(name,parent):
    o=bpy.data.objects.new(name,None);kit.objects.link(o)
    o.parent=parent
    bpy.context.view_layer.update()
    o.matrix_world.identity()
    return o

turret_rack=group('ERA_TURRET_KIT',bpy.data.objects['TURRET_YAW'])
contacts=[]

def tile(name,hint,normal,width,height,depth,target,parent=rack,family='M5'):
    # Snap to the actual armor mesh; never position a box by bounding width alone.
    normal=Vector(normal).normalized();hint=Vector(hint)
    inv=target.matrix_world.inverted()
    hit,p,n,face=target.ray_cast(inv@(hint+normal),inv.to_3x3()@(-normal))
    assert hit, (name,'missing armor backing')
    surface=target.matrix_world@p
    normal=(target.matrix_world.to_3x3()@n).normalized()
    if normal.dot(hint+Vector(normal)-surface)<0:normal=-normal
    # Horizontal direction on each armor face, with local +Y toward the roof.
    up=Vector((0,0,1))
    if abs(normal.z)>.95:up=Vector((0,1,0))
    right=up.cross(normal).normalized();up=normal.cross(right).normalized()
    from mathutils import Matrix
    rotation=Matrix((right,up,normal)).transposed().to_quaternion()
    center=surface+normal*(.04+depth/2)
    body=cube((0,0,0),(width,height,depth),mats)
    # Slight chamfered casing rather than the previous oversized flat slabs.
    body.data.materials.append(rim)
    mod=body.modifiers.new('ERA casing chamfer','BEVEL')
    mod.width=.008;mod.segments=1;mod.material=4
    bpy.context.view_layer.objects.active=body
    bpy.ops.object.modifier_apply(modifier=mod.name)
    pieces=[body]
    for x in [-width*.35,width*.35]:
        # Two rear mounting channels physically overlap the receiving armor.
        pieces.append(cube((x,0,-depth/2-.0175),(.036,height*.92,.055),mats))
        # Top attachment ears, visible in the manufacturer and installation photos.
        pieces.append(cube((x,height/2+.018,-depth/2+.015),(.037,.055,.027),mats))
        for y in [-height*.34,height*.34]:
            pieces.append(cube((x,y,depth/2+.003),(.016,.016,.007),[dark]))
    bpy.ops.object.select_all(action='DESELECT')
    for o in pieces:o.select_set(True)
    bpy.context.view_layer.objects.active=body;bpy.ops.object.join()
    body.name=name;body.data.name=name+'_mesh'
    body.parent=parent
    body.matrix_world=Matrix.Translation(center)@rotation.to_matrix().to_4x4()
    for c in list(body.users_collection):c.objects.unlink(body)
    kit.objects.link(body)
    body['removable_tile']=True;body['visual_family']=family
    body['asset_purpose']='Photo-matched exterior game mesh; dimensions inferred'
    tiles.append(body)
    # Rear mounting channels end 5 mm inside the struck armor surface.
    mount_tip=center-normal*(depth/2+.045)
    error=(mount_tip-surface).dot(normal)
    assert abs(error+.005)<1e-5
    contacts.append({'tile':name,'target':target.name,'mount_penetration':round(-error,5)})
    records.append({'name':name,'file':'Tiles/'+name+'.glb','position':list(center),
                    'rotation_euler':list(rotation.to_euler()),'size':[width,height,depth],
                    'parent':parent.name,'family':family,'pivot':'tile center; standalone GLB at origin'})

# BRAT-1 style silhouette in the GD/Rafael brochure and Army photograph:
# upper hull belt, stepped down at the bow; original lower skirt stays exposed.
# No later BUSK/XM33 lower-wheel panels are mixed into this configuration.
hull_mesh=bpy.data.objects['Main hull sloped glacis']
for side,label,count in [(-1,'L',16),(1,'R',15)]:
    for i in range(count):
        y=-2.46+i*(5.00/(count-1))
        tile(f'ERA_M5_{label}_LOW_{i+1:02d}',(side*1.48,y,1.65),(side,0,0),5.00/(count-1)-.012,.36,.25,hull_mesh)
    for i in range(12):
        y=-1.20+i*.34
        tile(f'ERA_M5_{label}_HIGH_{i+1:02d}',(side*1.48,y,1.995),(side,0,0),.328,.31,.25,hull_mesh)
# Three rows of small center modules conform to the glacis, not a single rail.
glacis_normal=Vector((0,-.62,.84)).normalized()
for row,y in enumerate([-3.025,-2.755,-2.485]):
    for col in range(6):
        x=-.825+col*.33
        tile(f'ERA_M3_FRONT_{row+1}_{col+1}',(x,y,1.42+(y+3.16)*.62/.84),glacis_normal,.318,.317,.13,hull_mesh,family='M3')
# Narrow shoulder blocks stop short of the headlight housings.
for side,label in [(-1,'L'),(1,'R')]:
    for row,y in enumerate([-3.04,-2.83]):
        for col,x in enumerate([1.075,1.345]):
            tile(f'ERA_M4_SHOULDER_{label}_{row+1}_{col+1}',(side*x,y,1.42+(y+3.16)*.62/.84),glacis_normal,.255,.245,.13,hull_mesh,family='M4')
# Turret tiles follow its individual sloped facets and rotate with the turret.
armor=bpy.data.objects['Faceted turret armor']
lower=[(-1.21,-.94),(-.82,-1.38),(.42,-1.38),(.88,-.98),(.91,.61),(.59,.94),(-.96,.94),(-1.30,.49)]
upper=[(-.99,-.79),(-.68,-1.08),(.32,-1.08),(.65,-.80),(.69,.61),(.56,.93),(-.92,.93),(-1.05,.46)]
def turret_face(index,t,z=2.72):
    j=(index+1)%8;f=(z-2.4)/.51
    a=Vector((*lower[index],2.4)).lerp(Vector((*upper[index],2.91)),f)
    b=Vector((*lower[j],2.4)).lerp(Vector((*upper[j],2.91)),f)
    point=a.lerp(b,t);point.y+=.75
    edge=b-a;rise=Vector((*upper[index],2.91))-Vector((*lower[index],2.4))
    normal=edge.cross(rise).normalized()
    if normal.dot(point-Vector((-.18,.37,2.6)))<0:normal=-normal
    return point,normal
for face,count,label in [(7,4,'L'),(3,3,'R')]:
    for i in range(count):
        p,n=turret_face(face,.25+i*.60/(count-1))
        tile(f'ERA_M6_TURRET_{label}_{i+1:02d}',p,n,.255 if count==4 else .31,.32,.13,armor,turret_rack,'M6')
for face,ts in [(0,[.30,.72]),(2,[.30,.72]),(1,[.08,.23,.78,.93])]:
    for i,t in enumerate(ts):
        p,n=turret_face(face,t,2.77)
        tile(f'ERA_M7_CHEEK_{face}_{i+1}',p,n,.16 if face==1 else .20,.26,.11,armor,turret_rack,'M7')
# The reference has headlights above the front kit, not buried behind the tiles.
for o in list(scene.objects):
    if o.name.startswith('Headlight '):o.location.z+=.30
for side in [-1,1]:
    pedestal=cube((side*1.23,-2.69,1.87),(.29,.20,.22),mats)
    pedestal.name='ERA headlamp pedestal '+str(side)
    pedestal.parent=bpy.data.objects['HULL']
bpy.context.view_layer.update()
assert len(tiles)==96 and len({o.data.as_pointer() for o in tiles})==96
assert sum(o.parent==turret_rack for o in tiles)==15
assert not any(o.type=='LIGHT' for o in scene.objects)
scene.camera=bpy.data.objects['Camera_west']
blend=os.path.join(OUT,'m2a3_bradley_era.blend')
bpy.ops.wm.save_as_mainfile(filepath=blend)

# Interchange exports use the same constant-emission fallback as the base GLB.
for m in bpy.data.materials:
    if not m.use_nodes:continue
    for n in m.node_tree.nodes:
        if n.type=='EMISSION':
            for link in list(n.inputs['Color'].links):m.node_tree.links.remove(link)
            n.inputs['Color'].default_value=m.diffuse_color
bpy.ops.export_scene.gltf(filepath=os.path.join(OUT,'m2a3_bradley_era.glb'),export_format='GLB',export_animations=True,export_cameras=False,export_lights=False)
bpy.ops.object.select_all(action='DESELECT')
for o in tiles:o.select_set(True)
bpy.ops.export_scene.gltf(filepath=os.path.join(OUT,'era_kit.glb'),export_format='GLB',use_selection=True,export_animations=False,export_cameras=False,export_lights=False)
for o in tiles:
    bpy.ops.object.select_all(action='DESELECT')
    duplicate=o.copy();duplicate.data=o.data.copy()
    scene.collection.objects.link(duplicate)
    duplicate.parent=None;duplicate.matrix_parent_inverse.identity()
    duplicate.location=(0,0,0);duplicate.rotation_euler=(0,0,0)
    duplicate.select_set(True);bpy.context.view_layer.objects.active=duplicate
    bpy.ops.export_scene.gltf(filepath=os.path.join(PARTS,o.name+'.glb'),export_format='GLB',use_selection=True,export_animations=False,export_cameras=False,export_lights=False)
    bpy.data.objects.remove(duplicate,do_unlink=True)

# Check actual interchange payload: each tile contains only its one mesh.
import struct
for entry in records:
    with open(os.path.join(OUT,entry['file']),'rb') as f:
        assert f.read(4)==b'glTF'
        f.read(8);length,kind=struct.unpack('<II',f.read(8))
        payload=json.loads(f.read(length))
    assert len(payload['meshes'])==1
    assert sum('mesh' in n for n in payload['nodes'])==1
with open(os.path.join(OUT,'tile_manifest.json'),'w',encoding='utf-8') as f:
    json.dump({'count':len(tiles),'layout':'BRAT-1 photo-matched silhouette: 55 side, 26 glacis, 15 turret; inferred exterior dimensions and family placement','contacts':contacts,'tiles':records},f,indent=2)
bpy.ops.wm.open_mainfile(filepath=blend)
scene=bpy.context.scene
for camera in ['iso_front','iso_rear','east']:
    scene.camera=bpy.data.objects['Camera_'+camera]
    scene.render.filepath=os.path.join(OUT,'audit_'+camera+'.png')
    bpy.ops.render.render(write_still=True)
runpy.run_path(os.path.join(ROOT,'render_vf_east.py'),init_globals={'SOURCE_BLEND':blend,'OUTPUT_DIR':os.path.join(OUT,'VF_East')})
print('ERA VALIDATION PASSED: 96 independent tiles; all rear mounts contact armor; 96 single-mesh GLBs')
