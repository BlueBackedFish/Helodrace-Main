"""Preserve the supplied OBJ and render constant-color, exactly top-down passes."""
from pathlib import Path
import hashlib, json, math, sys
import bpy
import numpy as np
from mathutils import Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent/'VehiclePipeline'))
from pipeline import read_obj, transform, bounds, sha256

profile = json.loads((HERE/'vehicle.json').read_text(encoding='utf-8'))
style = json.loads((HERE/'style.json').read_text(encoding='utf-8'))
source = Path(profile['sources']['obj']['path'])
assert sha256(source)==profile['sources']['obj']['sha256']
assert profile['axes']['confirmed']
model = read_obj(source)
vertices = [transform(v,profile['axes']) for v in model['vertices']]
used = {i for face in model['faces'] for i in face['vertices']}
low,high = bounds([vertices[i] for i in used])
center = Vector(tuple((a+b)/2 for a,b in zip(low,high)))
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE'
scene.eevee.taa_render_samples = 64
scene.render.resolution_x = scene.render.resolution_y = style['master_size']
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
scene.render.image_settings.color_depth = '8'
scene.view_settings.view_transform = 'Standard'
scene.view_settings.look = 'None'
scene.view_settings.exposure = 0
scene.view_settings.gamma = 1
scene.world = bpy.data.worlds.new('Unlit transparent world')
scene.world.color = (0,0,0)

def linear(v):
    v=v/255
    return v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4

def material(name,rgb):
    mat=bpy.data.materials.new(name)
    mat.use_nodes=True
    mat.diffuse_color=tuple(linear(v) for v in rgb)+(1,)
    nodes=mat.node_tree.nodes;nodes.clear()
    paint=nodes.new('ShaderNodeEmission')
    paint.inputs['Color'].default_value=mat.diffuse_color
    output=nodes.new('ShaderNodeOutputMaterial')
    mat.node_tree.links.new(paint.outputs[0],output.inputs['Surface'])
    return mat

materials={role:material('Flat aircraft '+role,rgb) for role,rgb in style['roles'].items()}
parts={p['source_group']:p for p in profile['parts']}
objects=[]
report=[]
for group in model['groups']:
    part=parts[group['id']]
    assert part['confirmed'] and part['mode']=='flat'
    faces=[model['faces'][i] for i in group['faces']]
    ids=sorted({i for f in faces for i in f['vertices']})
    local={global_id:i for i,global_id in enumerate(ids)}
    coords=[vertices[i] for i in ids]
    polygons=[[local[i] for i in f['vertices']] for f in faces]
    mesh=bpy.data.meshes.new(group['id']+' source mesh')
    mesh.from_pydata(coords,[],polygons);mesh.update()
    uv=mesh.uv_layers.new(name='Original OBJ UV')
    for polygon,face in zip(mesh.polygons,faces):
        assert tuple(polygon.vertices)==tuple(local[i] for i in face['vertices'])
        for loop,uv_id in zip(polygon.loop_indices,face['uv']):
            if uv_id is not None:
                uv.data[loop].uv=model['uv'][uv_id]
    actual=np.empty(len(coords)*3,dtype=np.float32)
    mesh.vertices.foreach_get('co',actual)
    assert np.array_equal(actual.reshape(-1,3),np.asarray(coords,dtype=np.float32))
    obj=bpy.data.objects.new(group['id']+' '+group['group'],mesh)
    scene.collection.objects.link(obj)
    obj['source_group']=group['id'];obj['paint_role']=part['role']
    obj['source_faces']=len(faces)
    mesh.materials.append(materials[part['role']])
    objects.append(obj)
    report.append(dict(id=group['id'],label=group['group'],faces=len(faces),role=part['role']))
assert sum(len(o.data.polygons) for o in objects)==len(model['faces'])
data=bpy.data.cameras.new('Exact top orthographic')
data.type='ORTHO'
game_size=style['game_resolution']
frame_fill=(game_size-2*(style['silhouette_outline_game_pixels']+style['transparent_margin_game_pixels']))/game_size
data.ortho_scale=max(high[0]-low[0],high[1]-low[1])/frame_fill
camera=bpy.data.objects.new('Exact top orthographic',data)
scene.collection.objects.link(camera)
camera.location=(center.x,center.y,high[2]+500)
camera.rotation_euler=(0,0,math.pi)
scene.camera=camera
out=HERE/'output';out.mkdir(exist_ok=True)
bpy.context.view_layer.update()
bpy.ops.wm.save_as_mainfile(filepath=str(out/'aircraft.blend'),compress=True)

def render(name):
    target=out/name
    temp=out/('.'+target.stem+'.render.tmp.png')
    scene.render.filepath=str(temp)
    bpy.ops.render.render(write_still=True)
    temp.replace(target)

render('top_plain_4096.png')
white=material('Panel mask white',[255,255,255])
black=material('Panel mask occluder',[0,0,0])
for obj in objects:
    obj.data.materials[0]=white if obj['paint_role'] in style['panel_roles'] else black
render('panel_mask_4096.png')
for name,roles in style['surface_mask_roles'].items():
    for obj in objects:
        obj.data.materials[0]=white if obj['paint_role'] in roles else black
    render(name+'_mask_4096.png')
for obj in objects:
    obj.data.materials[0]=materials[obj['paint_role']]
assert not any(o.type=='LIGHT' for o in scene.objects)
assert sha256(source)==profile['sources']['obj']['sha256']
validation=dict(source_sha256=sha256(source),source_groups=len(objects),source_faces=len(model['faces']),
    preserved_faces=sum(len(o.data.polygons) for o in objects),omitted_faces=0,
    original_coordinates_and_uv_preserved=True,lighting='Fixed RGB to Emission; no light objects or image albedo',
    projection='Orthographic; camera rotation (0,0,180 degrees), directly above +Z',
    canonical_axes=profile['axes'],source_units='Unverified Roblox export units; ratios retained',
    used_bounds=[low,high],ortho_scale=data.ortho_scale,
    master_size=style['master_size'],camera_center=list(center),
    pixel_anchor=[style['master_size']/2]*2,
    draw_size_tiles=style['game_draw_size_tiles'],game_resolution=game_size,
    model_sha256=sha256(out/'aircraft.blend'),parts=report,
    game_validation_performed=False,blueprint_validation_performed=False)
(out/'model_validation.json').write_text(json.dumps(validation,indent=2)+'\n',encoding='utf-8')
print('C17_TOP_RENDER_PASS: all source faces/UVs retained, flat Emission, exact top projection',flush=True)
