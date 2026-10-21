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
    bpy.ops.mesh.primitive_cube_add(size=1,location=location)
    o=bpy.context.object
    o.dimensions=dimensions
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    for m in materials:o.data.materials.append(m)
    if len(materials)==4:
        for p in o.data.polygons:
            p.material_index=0 if p.normal.z>.45 else 3 if p.normal.z<-.45 else 1 if abs(p.normal.y)>.5 else 2
    return o

def tile(name,position,size,rotation=(0,0,0),side=None):
    # A complete tile, including visible fasteners, is one removable mesh.
    body=cube((0,0,0),size,mats)
    body.data.materials.append(rim)
    mod=body.modifiers.new('Soft painted tile edges','BEVEL')
    mod.width=.012;mod.segments=1;mod.material=4
    bpy.context.view_layer.objects.active=body
    bpy.ops.object.modifier_apply(modifier=mod.name)
    pieces=[body]
    if side is not None:
        for y in [-size[1]*.34,size[1]*.34]:
            for z in [-size[2]*.31,size[2]*.31]:
                pieces.append(cube((side*(size[0]/2+.004),y,z),(.009,.032,.032),[dark]))
    else:
        for x in [-size[0]*.32,size[0]*.32]:
            for y in [-size[1]*.32,size[1]*.32]:
                pieces.append(cube((x,y,size[2]/2+.004),(.028,.028,.009),[dark]))
    bpy.ops.object.select_all(action='DESELECT')
    for o in pieces:o.select_set(True)
    bpy.context.view_layer.objects.active=body
    bpy.ops.object.join()
    body.name=name;body.data.name=name+'_mesh'
    body.location=position;body.rotation_euler=rotation;body.parent=rack
    for c in list(body.users_collection):c.objects.unlink(body)
    kit.objects.link(body)
    body['removable_tile']=True
    body['asset_purpose']='Cosmetic game mesh; approximate exterior only'
    tiles.append(body)
    records.append({'name':name,'file':'Tiles/'+name+'.glb','position':list(position),'rotation_euler':list(rotation),'size':list(size),'pivot':'tile center; standalone GLB at origin'})

# Two spaced side rows, leaving the front lights and rear ramp clear.
for side,label in [(-1,'L'),(1,'R')]:
    for row,z in [('LOW',1.22),('HIGH',1.78)]:
        for i in range(7):
            tile(f'ERA_{label}_{row}_{i+1:02d}',(side*1.84,-2.24+i*.72,z),(.24,.68,.49),side=side)
# Six modules over the lower frontal glacis, below the driver and engine deck.
for i in range(6):
    tile(f'ERA_FRONT_{i+1:02d}',(-.95+i*.38,-2.76,1.85),(.35,.46,.18),(math.atan2(.62,.84),0,0))
bpy.context.view_layer.update()
assert len(tiles)==34 and len({o.data.as_pointer() for o in tiles})==34
assert all(o.parent==rack for o in tiles)
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
    json.dump({'count':len(tiles),'layout':'28 side modules and 6 frontal modules; cosmetic approximation, not an exact BRAT configuration','tiles':records},f,indent=2)
runpy.run_path(os.path.join(ROOT,'render_vf_east.py'),init_globals={'SOURCE_BLEND':blend,'OUTPUT_DIR':os.path.join(OUT,'VF_East')})
print('ERA VALIDATION PASSED: 34 independently removable meshes and 34 single-mesh GLBs')
