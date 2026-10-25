"""LVADS visual reference: vented olive cargo canopies, Type-V-like deck and honeycomb."""
from pathlib import Path
import hashlib,json,math,sys
import bpy,numpy as np
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view

HERE=Path(__file__).resolve().parent;cfg=json.loads((HERE/'settings.json').read_text())
variant=sys.argv[sys.argv.index('--')+1] if '--' in sys.argv else 'm8'
spec=cfg['variants'][variant];source=HERE.parent/spec['path']
assert hashlib.sha256(source.read_bytes()).hexdigest()==spec['sha256']
bpy.ops.wm.open_mainfile(filepath=str(source));scene=bpy.context.scene
scene.frame_set(1);bpy.context.view_layer.update()
vehicle=[o for o in scene.objects if o.type=='MESH' and o.data.polygons]
def under(o,name):
    while o:
        if o.name==name:return True
        o=o.parent
    return False
hull=[o for o in vehicle if o.get('layer')=='hull'] if spec['kind']=='m8' else [o for o in vehicle if not under(o,'TURRET_YAW')]
points=[o.matrix_world@Vector(c) for o in hull for c in o.bound_box]
lo=Vector([min(p[i] for p in points) for i in range(3)]);hi=Vector([max(p[i] for p in points) for i in range(3)])
origin=Vector(((lo.x+hi.x)/2,(lo.y+hi.y)/2,lo.z))
def signature(objects):
    digest=hashlib.sha256()
    for o in objects:
        a=np.empty(len(o.data.vertices)*3,np.float32);o.data.vertices.foreach_get('co',a);digest.update(a.tobytes())
        for uv in o.data.uv_layers:
            a=np.empty(len(uv.data)*2,np.float32);uv.data.foreach_get('uv',a);digest.update(a.tobytes())
        digest.update(repr([tuple(p.vertices) for p in o.data.polygons]).encode())
    return digest.hexdigest()
before=signature(vehicle)
root=bpy.data.objects.new('DROP_VEHICLE_UNIFORM_SCALE',None);scene.collection.objects.link(root)
for obj in list(scene.objects):
    if obj!=root and obj.type not in ['CAMERA','LIGHT'] and obj.parent is None:
        world=obj.matrix_world.copy();obj.parent=root;obj.matrix_world=world
root.scale=(spec['world_scale'],)*3;root.location=-origin*spec['world_scale']
for obj in vehicle:
    if obj.animation_data:obj.animation_data_clear()
bpy.context.view_layer.update()
groups={name:[] for name in ['pallet','restraints','lines','canopy']}
def material(name,color):
    def lin(c):
        c=c/255;return c/12.92 if c<=.04045 else ((c+.055)/1.055)**2.4
    m=bpy.data.materials.new('DROP '+name);m.use_nodes=True;m.node_tree.nodes.clear()
    e=m.node_tree.nodes.new('ShaderNodeEmission');e.inputs[0].default_value=tuple(lin(v) for v in color)+(1,)
    out=m.node_tree.nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(e.outputs[0],out.inputs[0]);return m
deck=material('aluminum deck',(170,174,164));rail=material('edge rails',(99,107,99))
cushion=material('honeycomb kraft paper',(174,142,94));cushion_edge=material('honeycomb layer joints',(121,96,59))
strap=material('olive web restraint',(80,86,57));cord=material('suspension cord',(177,173,144))
cloth=[material('olive canopy panel '+str(i),rgb) for i,rgb in enumerate([(107,117,77),(97,107,69),(67,78,48)])]
def track(obj,group):obj.name='DROP_'+obj.name;groups[group].append(obj);return obj
def cube(name,position,dimensions,mat,group='pallet'):
    bpy.ops.mesh.primitive_cube_add(size=1,location=position);o=bpy.context.object;o.name=name;o.dimensions=dimensions
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);o.data.materials.append(mat);return track(o,group)
def line(name,points,r,mat,group):
    curve=bpy.data.curves.new(name,'CURVE');curve.dimensions='3D';curve.bevel_depth=r;curve.bevel_resolution=1
    s=curve.splines.new('POLY');s.points.add(len(points)-1)
    for p,v in zip(s.points,points):p.co=(*v,1)
    obj=bpy.data.objects.new(name,curve);scene.collection.objects.link(obj);obj.data.materials.append(mat);return track(obj,group)
w=cfg['pallet']['width'];length=cfg['pallet']['length']
cube('load_deck',(0,0,-.39),(w,length,.12),deck)
for x in [-w/2+.09,w/2-.09]:
    cube('load_rail',(x,0,-.24),(.18,length,.22),rail)
    cube('rail_top',(x,0,-.12),(.22,length,.025),deck)
    for i in range(10):
        y=-length/2+.25+i*(length-.5)/9
        cube('rail_lashing_slot',(x,y,-.101),(.09,.14,.018),strap)
for i in range(15):cube('deck_joint',(0,-length/2+.25+i*(length-.5)/14,-.324),(w-.36,.025,.012),rail)
for x in [-1.08,1.08]:
    for y in [-2.15,-.75,.75,2.15]:
        cube('track_honeycomb_stack',(x,y,-.17),(.91,1.13,.30),cushion)
        for z in [-.29,-.23,-.17,-.11,-.05]:cube('honeycomb_layer_joint',(x,y,z),(.92,1.14,.009),cushion_edge)
        for j in range(9):
            for side in [-1,1]:cube('honeycomb_exposed_cells',(x+side*.46,y-.48+j*.12,-.165),(.008,.026,.020),cushion_edge)
for y in [-length/2+.08,length/2-.08]:
    cube('end_cross_member',(0,y,-.22),(w,.15,.24),rail)
    for x in [-1.35,-.9,-.45,0,.45,.9,1.35]:cube('end_fastener',(x,y,-.09),(.09,.08,.035),deck)
for x in [-1.51,1.51]:
    for y in [-2.55,2.55]:
        cube('packed_bag',(x,y,.19),(.37,.50,.27),strap)
        cube('bag_band',(x,y,.335),(.11,.51,.025),cord)
# Restraint arches sample the vehicle roof before any gear is visible to rays.
for obj in sum(groups.values(),[]):obj.hide_set(True);obj.hide_render=True
bpy.context.view_layer.update()
half=(hi.x-lo.x)*spec['world_scale']/2
for y in [-1.65,1.65]:
    path=[Vector((-w/2+.04,y,.12))]
    for i in range(17):
        x=-half+.05+(2*half-.10)*i/16
        hit,location,normal,index,obj,matrix=scene.ray_cast(bpy.context.evaluated_depsgraph_get(),Vector((x,y,10)),Vector((0,0,-1)),distance=12)
        z=max(.2,location.z+.04) if hit and (obj in vehicle or obj.original in vehicle) else .22
        path.append(Vector((x,y,z)))
    path.append(Vector((w/2-.04,y,.12)))
    line('roof_webbing',path,.025,strap,'restraints')
for obj in sum(groups.values(),[]):obj.hide_set(False);obj.hide_render=False
# Four round cargo parachutes: center vent, radial gores and circumferential tapes.
# Fixed panel colors describe construction, never dynamic light/shading.
for sx in [-1,1]:
    for sy in [-1,1]:
        center=Vector((sx*2.1,sy*2.4,0));r=cfg['canopy']['radius'];sectors=32;rings=10
        verts=[];faces=[];vent=.105*r
        for j in range(rings+1):
            t=j/rings;rho=vent+(r-vent)*t
            z=cfg['canopy']['rim_height']+(cfg['canopy']['apex_height']-cfg['canopy']['rim_height'])*math.cos(t*math.pi/2)
            for i in range(sectors):
                a=i*math.tau/sectors;verts.append((center.x+rho*math.cos(a),center.y+rho*math.sin(a),z))
        for j in range(rings):
            for i in range(sectors):
                k=(i+1)%sectors;faces.append((j*sectors+i,j*sectors+k,(j+1)*sectors+k,(j+1)*sectors+i))
        mesh=bpy.data.meshes.new('canopy');mesh.from_pydata(verts,[],faces);mesh.update()
        assert all(p.area>1e-9 for p in mesh.polygons), 'Degenerate canopy face'
        obj=bpy.data.objects.new('canopy',mesh);scene.collection.objects.link(obj)
        for m in cloth:mesh.materials.append(m)
        for p in mesh.polygons:p.material_index=1 if (p.index%sectors)%8==0 else 0
        track(obj,'canopy')
        junction=Vector((center.x,center.y,7.2))
        line('lift_riser',[Vector((sx*(w/2-.12),sy*(length/2-.20),.12)),junction],.025,cord,'lines')
        for i in range(16):
            a=i*math.tau/16;rim=Vector((center.x+r*math.cos(a),center.y+r*math.sin(a),cfg['canopy']['rim_height']))
            line('suspension_line',[junction,rim],.011,cord,'lines')
        for i in range(32):
            a=i*math.tau/32;path=[]
            for j in range(rings+1):
                rho=vent+(r-vent)*j/rings;z=cfg['canopy']['rim_height']+(cfg['canopy']['apex_height']-cfg['canopy']['rim_height'])*math.cos(j/rings*math.pi/2)+.006
                path.append(Vector((center.x+rho*math.cos(a),center.y+rho*math.sin(a),z)))
            line('radial_reinforcement_tape',path,.007,cloth[2],'canopy')
        for t in [0,.35,.72]:
            rho=vent+(r-vent)*t;z=cfg['canopy']['rim_height']+(cfg['canopy']['apex_height']-cfg['canopy']['rim_height'])*math.cos(t*math.pi/2)+.008
            ring=[Vector((center.x+rho*math.cos(i*math.tau/96),center.y+rho*math.sin(i*math.tau/96),z)) for i in range(97)]
            line('vent_or_circumferential_tape',ring,.012 if t==0 else .009,cloth[2],'canopy')
        ring=[Vector((center.x+r*math.cos(i*math.tau/64),center.y+r*math.sin(i*math.tau/64),cfg['canopy']['rim_height']+.004)) for i in range(65)]
        line('skirt_seam',ring,.022,cloth[2],'canopy')
scene.render.engine='BLENDER_EEVEE';scene.eevee.taa_render_samples=64
scene.render.resolution_x=scene.render.resolution_y=cfg['master'];scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA';scene.render.film_transparent=True
scene.render.image_settings.color_depth='8';scene.render.dither_intensity=0;scene.render.use_compositing=False
scene.view_settings.view_transform='Standard';scene.view_settings.look='None';scene.view_settings.exposure=0;scene.view_settings.gamma=1
data=bpy.data.cameras.new('DROP registered camera');data.type='ORTHO';data.ortho_scale=cfg['draw_size']
camera=bpy.data.objects.new(data.name,data);scene.collection.objects.link(camera);scene.camera=camera
target=Vector((0,0,4));out=HERE/'output'/variant;out.mkdir(parents=True,exist_ok=True)
allgear=sum(groups.values(),[]);saved={o.name:list(o.data.materials) for o in vehicle}
hold=bpy.data.materials.new('DROP holdout');hold.use_nodes=True;hold.node_tree.nodes.clear()
h=hold.node_tree.nodes.new('ShaderNodeHoldout');o=hold.node_tree.nodes.new('ShaderNodeOutputMaterial');hold.node_tree.links.new(h.outputs[0],o.inputs[0])
report=dict(variant=variant,source_sha256=spec['sha256'],source_faces_preserved=sum(len(o.data.polygons) for o in vehicle),
    source_mesh_uv_signature=before,source_geometry_unchanged=True,uniform_world_scale=spec['world_scale'],
    master=cfg['master'],draw_size=cfg['draw_size'],directions={},canopy_count=4,game_only=True)
for direction,heading in cfg['directions'].items():
    az=math.radians(90+heading);el=math.radians(64)
    camera.location=target+Vector((math.cos(az)*math.cos(el),math.sin(az)*math.cos(el),math.sin(el)))*50
    camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();bpy.context.view_layer.update()
    projected=[world_to_camera_view(scene,camera,root.matrix_world@p) for p in points]
    pivot=[(min(p.x for p in projected)+max(p.x for p in projected))*cfg['master']/2,
        (2-min(p.y for p in projected)-max(p.y for p in projected))*cfg['master']/2]
    report['directions'][direction]=dict(vehicle_pivot=pivot)
    for layer in ['pallet','restraints','lines','canopy','preview']:
        for obj in allgear:obj.hide_render=layer!='preview' and obj not in groups[layer]
        for obj in vehicle:
            obj.hide_render=layer in ['pallet','canopy']
            for i,m in enumerate(saved[obj.name]):obj.data.materials[i]=hold if layer in ['restraints','lines'] else m
        path=out/f'{layer}_{direction}.png';tmp=out/('.'+path.stem+'.render.tmp.png')
        scene.render.filepath=str(tmp);bpy.ops.render.render(write_still=True);tmp.replace(path)
        print('DROP_RENDER',variant,direction,layer,flush=True)
for obj in vehicle:
    obj.hide_render=False
    for i,m in enumerate(saved[obj.name]):obj.data.materials[i]=m
for obj in allgear:obj.hide_render=False
assert signature(vehicle)==before
assert hashlib.sha256(source.read_bytes()).hexdigest()==spec['sha256']
assert not any(o.type=='LIGHT' for o in scene.objects)
bpy.ops.wm.save_as_mainfile(filepath=str(out/'vehicle_drop_rig.blend'),compress=True)
(out/'render.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print('HEAVY_DROP_MODEL_PASS',variant,flush=True)
