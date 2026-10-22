"""Check fixed Emission, actual cross-view pixels, source preservation and motion."""
import bpy,sys,json,hashlib,struct
import numpy as np
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from bpy_extras.object_utils import world_to_camera_view
ROOT=Path(__file__).resolve().parent.parent;OUT=Path(__file__).resolve().parent;sys.path.insert(0,str(ROOT))
from height_palette import display,WEIGHTS
report=json.loads((OUT/'manifest.json').read_text())
for name,digest in report['production_source_sha256'].items():
    assert hashlib.sha256((ROOT/name).read_bytes()).hexdigest()==digest,('Production changed',name)
bpy.ops.wm.open_mainfile(filepath=str(OUT/'bradley_emission_trial.blend'));s=bpy.context.scene;s.frame_set(1)
meshes=[o for o in s.objects if o.type=='MESH']
h=hashlib.sha256()
for o in sorted(meshes,key=lambda o:o.name):
    h.update(o.name.encode());h.update(str([tuple(v.co) for v in o.data.vertices]).encode())
    h.update(str([list(p.vertices) for p in o.data.polygons]).encode())
    h.update(o.parent.name.encode());h.update(str([tuple(row) for row in o.matrix_world]).encode())
assert h.hexdigest()==report['geometry_sha256_including_parent_and_world_matrix']
used={m.name:m for o in meshes for m in o.data.materials if m}
fixed=[m for m in used.values() if m.get('fixed_emission')]
assert len(fixed)==report['trial_fixed_paint_materials']
for m in used.values():
    assert not any(n.type.startswith('BSDF_') for n in m.node_tree.nodes)
    output=next(n for n in m.node_tree.nodes if n.type=='OUTPUT_MATERIAL')
    assert output.inputs['Surface'].links[0].from_node.type=='EMISSION'
for m in fixed:
    assert {n.type for n in m.node_tree.nodes}=={'EMISSION','OUTPUT_MATERIAL'}
    e=next(n for n in m.node_tree.nodes if n.type=='EMISSION')
    assert not e.inputs['Color'].links and e.inputs['Strength'].default_value==1
assert not any(o.type=='LIGHT' for o in s.objects)
assert s.render.film_transparent
for mesh,control,frame in [('WT_bone_turret','TURRET_YAW',40),('WT_bone_gun','GUN_ELEVATION',40),('WT_rear_ramp','REAR_RAMP_HINGE',80),('WT_hatch_04','PERSONNEL_DOOR_HINGE',25)]:
    s.frame_set(1);o=bpy.data.objects[mesh];before=o.matrix_world.copy();assert o.parent.name==control
    s.frame_set(frame);assert max(abs(a-b) for ra,rb in zip(before,o.matrix_world) for a,b in zip(ra,rb))>.1
s.frame_set(1)
def under(obj):
    while obj:
        if obj.name=='TURRET_YAW':return True
        obj=obj.parent
    return False
visible=[o for o in meshes if under(o) and not o.name.startswith('WT_antenna')]
verts=[];faces=[];index=[]
for o in visible:
    offset=len(verts);verts.extend(o.matrix_world@v.co for v in o.data.vertices)
    for p in o.data.polygons:faces.append([offset+i for i in p.vertices]);index.append((o.name,p.index))
tree=BVHTree.FromPolygons(verts,faces,all_triangles=False)
def rgb_display(a):return np.array([display(float(c)) for c in a])
def read_view(name,pose):
    data=bpy.data.cameras.new('Pixel audit '+name);data.type='ORTHO';data.ortho_scale=pose['ortho_scale']
    camera=bpy.data.objects.new('Pixel audit '+name,data);s.collection.objects.link(camera)
    camera.location=pose['location'];camera.rotation_euler=pose['rotation_euler'];s.camera=camera
    size=pose['size'];s.render.resolution_x=s.render.resolution_y=size;bpy.context.view_layer.update()
    im=bpy.data.images.load(str(OUT/(name+'.png')),check_existing=False);assert im.is_float
    pixels=np.array(im.pixels[:],dtype=np.float32).reshape(size,size,4);bpy.data.images.remove(im)
    samples={};toward=camera.matrix_world.to_quaternion()@Vector((0,0,1))
    for o in visible:
        for p in o.data.polygons:
            mat=o.data.materials[p.material_index]
            if not mat.get('fixed_emission') or p.area<.0005:continue
            world=o.matrix_world@p.center
            hit,normal,pi,distance=tree.ray_cast(world+toward*20,-toward)
            if pi is None or index[pi]!=(o.name,p.index):continue
            uv=world_to_camera_view(s,camera,world);x,y=int(uv.x*size),int(uv.y*size)
            if not 2<=x<size-2 or not 2<=y<size-2:continue
            # A centroid may project onto a subpixel sliver. Require the full
            # sample footprint to hit this face before comparing its color.
            footprint=[]
            for py in range(y-1,y+2):
                for px in range(x-1,x+2):
                    origin=camera.matrix_world@Vector((((px+.5)/size-.5)*pose['ortho_scale'],((py+.5)/size-.5)*pose['ortho_scale'],0))
                    hit,n,pi,d=tree.ray_cast(origin,-toward)
                    footprint.append(pi is not None and index[pi]==(o.name,p.index))
            if not all(footprint):continue
            patch=pixels[y-1:y+2,x-1:x+2]
            if patch[:,:,3].min()<.999:continue
            values=np.array([rgb_display(c) for c in patch[:,:,:3].reshape(-1,3)])
            if np.max(np.ptp(values,axis=0))>.8:continue
            actual=np.median(values,axis=0);expected=rgb_display(mat.diffuse_color[:3])
            assert np.max(abs(actual-expected))<1.5,(name,o.name,p.index,actual,expected)
            samples[(o.name,p.index)]=actual
    return samples
poses=json.loads((OUT/'camera_views.json').read_text());a=read_view('closeup_front',poses['closeup_front']);b=read_view('closeup_angle',poses['closeup_angle'])
common=set(a)&set(b);assert len(common)>30,len(common)
errors=[float(np.max(abs(a[k]-b[k]))) for k in common];assert max(errors)<1.5,max(errors)
plate=report['brighter_plate'];keys=[k for k in common if k[0]==plate['object'] and k[1] in plate['faces']]
assert keys,'Brightened plate must be verified in both camera angles'
plate_values=[float(np.dot(a[k],WEIGHTS)) for k in keys]
assert all(abs(y-plate['display_luminance'])<1 for y in plate_values)
with open(OUT/'bradley_emission_trial.glb','rb') as f:
    assert f.read(4)==b'glTF';f.read(8);length,kind=struct.unpack('<II',f.read(8));g=json.loads(f.read(length))
assert len(g['animations'])==4
assert all('KHR_materials_unlit' in m['extensions'] for m in g['materials'])
assert all('COLOR_0' not in p['attributes'] for mesh in g['meshes'] for p in mesh['primitives']),'Do not multiply constant unlit color by old vertex paint'
validation={'geometry_and_all_parent_matrices_unchanged':True,'production_sources_unchanged':True,
'constant_emission_paint_materials':len(fixed),'fixed_palette_no_vertex_interpolation':True,
'same_faces_sampled_in_two_camera_angles':len(common),'second_view_sun_energy':poses['closeup_angle']['sun_energy'],
'max_actual_pixel_srgb_channel_difference_0_to_255':max(errors),
'bright_plate_verified_in_both_views':{'faces':len(keys),'actual_display_y_range':[min(plate_values),max(plate_values)],'target':plate['display_luminance']},
'four_control_animations_and_glb_animations':True,'glb_unlit_without_vertex_color_multiplication':True,'transparent_no_scene_lights':True,
'in_game_validation':'not_performed'}
(OUT/'validation.json').write_text(json.dumps(validation,indent=2)+'\n')
print('FROZEN EMISSION PIXEL/MOTION VALIDATED',json.dumps(validation))
