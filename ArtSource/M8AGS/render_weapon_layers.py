"""Preserve the supplied mesh; export independent M35 and roof M2HB at 1024."""
from pathlib import Path
import json, math, hashlib
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
points=[o.matrix_world@Vector(c) for o in mount for c in o.bound_box]
mgpivot=Vector(((min(v.x for v in points)+max(v.x for v in points))/2,
                (min(v.y for v in points)+max(v.y for v in points))/2,
                min(v.z for v in points)))
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
          ortho_scale=camera.data.ortho_scale,m2hb_objects=[o.name for o in roof],directions={})
for direction,(azimuth,top_rotation) in dict(east=(180,-90),north=(90,180),west=(0,90),south=(270,0)).items():
    record={}
    for layer in ['hull','turret','barrel','m2hb']:
        for obj in meshes:
            visible=(obj['layer']=='hull') if layer=='hull' else (obj in roof) if layer=='m2hb' else (obj['layer']=='barrel') if layer=='barrel' else (obj['layer']=='turret' and obj not in roof)
            occluder=layer=='barrel' and obj['layer']=='turret' and obj not in roof
            obj.hide_render=not(visible or occluder)
            for i,m in enumerate(saved[obj.name]):obj.data.materials[i]=hold if occluder else m
        if layer=='hull':
            el=math.radians(report['render']['hull_elevation']);az=math.radians(azimuth)
            camera.location=center+Vector((math.cos(el)*math.cos(az),math.cos(el)*math.sin(az),math.sin(el)))*distance
            camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler()
        else:
            camera.location=(mgpivot if layer=='m2hb' else pivot)+Vector((0,0,distance))
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
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'vehicle.blend'),compress=True)
(OUT/'pivots.json').write_text(json.dumps(meta,indent=2)+'\n')
print('M8_WEAPON_LAYERS_PASS',meta['m2hb_objects'],flush=True)
