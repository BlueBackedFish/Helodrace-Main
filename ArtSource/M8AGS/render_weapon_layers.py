"""Preserve the supplied mesh; export independent M35 and roof M2HB at 1024."""
from pathlib import Path
import json, math, hashlib
import numpy as np
import bpy
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view

ROOT=Path(__file__).resolve().parent
OUT=ROOT/'output/WeaponLayers'
OUT.mkdir(parents=True,exist_ok=True)
source=ROOT/'output/vehicle.blend'
bpy.ops.wm.open_mainfile(filepath=str(source))
scene=bpy.context.scene
report=json.loads((ROOT/'output/report.json').read_text())
camera=scene.camera
meshes=[o for o in scene.objects if o.type=='MESH' and 'layer' in o]
roof=[o for o in meshes if o.name.split('_',1)[0] in ['g0043','g0044'] or o.get('part_id','').split('_',1)[0] in ['g0043','g0044']]
# The common builder names each object after its part id.
assert len(roof)>=3, [(o.name,o.get('part_id')) for o in meshes]
mount=[o for o in roof if o.name.startswith('g0043')]
assert mount
# Fit the actual commander cupola rim, not the offset machine-gun pintle or bitmap bounds.
cupola=next(o for o in meshes if o.name.startswith('g0036 '))
points=np.unique(np.round([tuple(cupola.matrix_world@v.co) for v in cupola.data.vertices],5),axis=0)
ring=points[abs(points[:,2]-2.4845)<.00006,:2]
ring=ring[np.linalg.norm(ring-[-.4373,.6323],axis=1)<.43]
fit,*_=np.linalg.lstsq(np.column_stack((2*ring,np.ones(len(ring)))),(ring*ring).sum(axis=1),rcond=None)
radius=math.sqrt(fit[2]+np.dot(fit[:2],fit[:2]))
residual=float(abs(np.linalg.norm(ring-fit[:2],axis=1)-radius).max())
assert len(ring)==28 and .33<radius<.36 and residual<.001
mgpivot=Vector((float(fit[0]),float(fit[1]),2.4845))
# The supplied MG paint mesh contains both the receiver fittings and the ammunition can.
# Color only the connected can assembly identified from the preserved source geometry.
ammo=next(o for o in roof if o.name.startswith('g0044 '))
parents=list(range(len(ammo.data.vertices)))
def root_index(i):
    while parents[i]!=i:parents[i]=parents[parents[i]];i=parents[i]
    return i
keys={}
for i,v in enumerate(ammo.data.vertices):
    key=tuple(round(x,5) for x in v.co)
    if key in keys:parents[root_index(i)]=root_index(keys[key])
    else:keys[key]=i
for p in ammo.data.polygons:
    for i in p.vertices[1:]:parents[root_index(i)]=root_index(p.vertices[0])
components={}
for p in ammo.data.polygons:components.setdefault(root_index(p.vertices[0]),[]).append(p)
can_faces=[]
for polygons in components.values():
    p=[ammo.matrix_world@ammo.data.vertices[i].co for f in polygons for i in f.vertices]
    cx=(min(v.x for v in p)+max(v.x for v in p))/2
    if cx>-.30:can_faces.extend(polygons)
assert 300<len(can_faces)<500
def olive_material(name,rgb):
    def linear(v):
        v/=255;return v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4
    m=bpy.data.materials.new(name);m.use_nodes=True;m.node_tree.nodes.clear()
    e=m.node_tree.nodes.new('ShaderNodeEmission');e.inputs[0].default_value=tuple(linear(v) for v in rgb)+(1,)
    out=m.node_tree.nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(e.outputs[0],out.inputs[0]);return m
start=len(ammo.data.materials)
for name,rgb in [('top',(108,130,79)),('wall',(77,96,52)),('bottom',(52,67,37))]:ammo.data.materials.append(olive_material('M2HB olive ammo '+name,rgb))
for p in can_faces:p.material_index=start+(0 if p.normal.z>.5 else 2 if p.normal.z<-.5 else 1)
pivot=Vector(report['render']['turret_pivot'])
center=Vector(report['render']['hull_target'])
distance=camera.data.ortho_scale*4
scene.render.resolution_x=scene.render.resolution_y=2048
scene.render.resolution_percentage=100
scene.render.dither_intensity=0
scene.render.image_settings.color_depth='8'
saved={o.name:list(o.data.materials) for o in meshes}
hold=bpy.data.materials.new('Weapon export holdout');hold.use_nodes=True;hold.node_tree.nodes.clear()
h=hold.node_tree.nodes.new('ShaderNodeHoldout');out=hold.node_tree.nodes.new('ShaderNodeOutputMaterial');hold.node_tree.links.new(h.outputs[0],out.inputs[0])
meta=dict(source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),master=2048,
          ortho_scale=camera.data.ortho_scale,m2hb_objects=[o.name for o in roof],
          m2hb_pivot=list(mgpivot),pivot_basis='28-point commander cupola rim at Z=2.4845',
          cupola_fit_residual=residual,green_ammo_faces=len(can_faces),directions={})
for direction,(azimuth,top_rotation) in dict(east=(180,-90),north=(90,180),west=(0,90),south=(270,0)).items():
    record={}
    for layer in ['hull','turret','barrel','m2hb','m2hb_mount']:
        for obj in meshes:
            visible=(obj['layer']=='hull') if layer=='hull' else (obj in mount) if layer=='m2hb_mount' else (obj in roof and obj not in mount) if layer=='m2hb' else (obj['layer']=='barrel') if layer=='barrel' else (obj['layer']=='turret' and obj not in roof)
            occluder=layer=='barrel' and obj['layer']=='turret' and obj not in roof
            obj.hide_render=not(visible or occluder)
            for i,m in enumerate(saved[obj.name]):obj.data.materials[i]=hold if occluder else m
        if layer=='hull':
            el=math.radians(report['render']['hull_elevation']);az=math.radians(azimuth)
            camera.location=center+Vector((math.cos(el)*math.cos(az),math.cos(el)*math.sin(az),math.sin(el)))*distance
            camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler()
        else:
            camera.location=(mgpivot if layer.startswith('m2hb') else pivot)+Vector((0,0,distance))
            camera.rotation_euler=(0,0,math.radians(top_rotation))
        bpy.context.view_layer.update()
        if layer=='hull':
            p=world_to_camera_view(scene,camera,pivot)
            record['hull_turret_anchor']=[p.x*2048,(1-p.y)*2048]
        elif layer=='turret':
            p=world_to_camera_view(scene,camera,mgpivot)
            record['m2hb_anchor']=[p.x*2048,(1-p.y)*2048]
        scene.render.filepath=str(OUT/f'{layer}_{direction}.png')
        bpy.ops.render.render(write_still=True)
    meta['directions'][direction]=record
for obj in meshes:
    obj.hide_render=False
    for i,m in enumerate(saved[obj.name]):obj.data.materials[i]=m
root=bpy.data.objects.new('M2HB_YAW',None);scene.collection.objects.link(root);root.location=mgpivot
bpy.context.view_layer.update()
root.parent=bpy.data.objects.get('TURRET_YAW');root.matrix_world.translation=mgpivot
for obj in roof:
    world=obj.matrix_world.copy();obj.parent=root;obj.matrix_world=world;obj['layer']='m2hb'
scene['weapon_export']='M35 barrel and commander M2HB independently rendered; original source untouched'
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'vehicle.blend'),compress=True)
(OUT/'pivots.json').write_text(json.dumps(meta,indent=2)+'\n')
print('M8_WEAPON_LAYERS_PASS',meta['m2hb_objects'],flush=True)
