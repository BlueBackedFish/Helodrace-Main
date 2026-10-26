"""Cosmetic game sprites only: orthographic exterior meshes, no working mechanisms."""
from pathlib import Path
import bpy,math,json,hashlib
from mathutils import Vector,Matrix
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';OUT.mkdir(exist_ok=True)
MASTER=2048

def material(name,rgb):
    def linear(c):
        c/=255;return c/12.92 if c<=.04045 else ((c+.055)/1.055)**2.4
    m=bpy.data.materials.new(name);m.use_nodes=True;m.node_tree.nodes.clear()
    e=m.node_tree.nodes.new('ShaderNodeEmission');e.inputs[0].default_value=tuple(linear(c) for c in rgb)+(1,)
    out=m.node_tree.nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(e.outputs[0],out.inputs[0]);return m

def box(name,location,size,mat,layer='body',bevel=0):
    bpy.ops.mesh.primitive_cube_add(size=1,location=location);o=bpy.context.object;o.name=name;o.scale=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);o.data.materials.append(mat);o['layer']=layer
    if bevel:
        mod=o.modifiers.new('Small exterior edge bevel','BEVEL');mod.width=min(bevel,min(size)*.3);mod.segments=1
        bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=mod.name)
    return o

def cylinder(name,a,b,radius,mat,layer='body',segments=32):
    a,b=Vector(a),Vector(b);delta=b-a
    bpy.ops.mesh.primitive_cylinder_add(vertices=segments,radius=radius,depth=delta.length,location=(a+b)*.5)
    o=bpy.context.object;o.name=name;o.rotation_euler=delta.to_track_quat('Z','Y').to_euler();o.data.materials.append(mat);o['layer']=layer
    return o

def beam(name,a,b,width,depth,mat,layer='mount'):
    a,b=Vector(a),Vector(b);delta=b-a
    o=box(name,(a+b)*.5,(width,depth,delta.length),mat,layer,bevel=.004)
    o.rotation_euler=delta.to_track_quat('Z','Y').to_euler();return o

def configure():
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=8;scene.cycles.use_denoising=False
    scene.render.resolution_x=scene.render.resolution_y=MASTER;scene.render.resolution_percentage=100
    scene.render.film_transparent=True;scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA';scene.render.image_settings.color_depth='8'
    scene.render.dither_intensity=0;scene.view_settings.view_transform='Standard';scene.view_settings.look='None';scene.view_settings.exposure=0;scene.view_settings.gamma=1
    bpy.ops.object.camera_add(location=(0,0,6));camera=bpy.context.object;camera.name='ORTHOGRAPHIC_90_DEGREES';camera.rotation_euler=(0,0,0);camera.data.type='ORTHO';scene.camera=camera
    bpy.context.preferences.filepaths.save_version=0
    return scene,camera

def mount(kind,mats):
    gray,light,dark=mats['mount'],mats['mount_top'],mats['dark']
    for label,foot in [('front',(0,.50,-.28)),('left',(-.39,-.47,-.28)),('right',(.39,-.47,-.28))]:
        beam('Tripod '+label+' leg',(0,0,-.12),foot,.043,.035,gray)
        box('Tripod '+label+' foot',(foot[0],foot[1],-.29),(.10,.12,.035),dark,'mount',.015)
        box('Tripod '+label+' foot top',(foot[0],foot[1],-.27),(.075,.09,.013),light,'mount',.008)
    cylinder('Pintle column',(0,0,-.18),(0,0,.08),.06,gray,'mount')
    cylinder('Pintle collar',(0,0,-.10),(0,0,-.075),.087,light,'mount')
    beam('Rear traverse bar',(-.33,-.34,-.19),(.33,-.34,-.19),.024,.026,gray)
    beam('Elevation brace',(0,-.32,-.16),(0,-.14,.02),.025,.027,light)
    cylinder('Traverse adjustment handwheel',(-.06,-.32,-.11),(-.06,-.32,-.08),.049,dark,'mount')
    cylinder('Elevation knob',(.06,-.17,.01),(.10,-.17,.01),.025,light,'mount')
    if kind=='M1917':
        for o in bpy.context.scene.objects:
            if o.type=='MESH' and o.get('layer')=='mount':o.matrix_world=Matrix.Diagonal((.84,.84,1,1))@o.matrix_world

def save_and_render(kind,scene,camera,model_length,metadata):
    # Preserve the old visible length while registering the new gun at its actual pintle.
    geometry=[o.matrix_world@v.co for o in scene.objects if o.type=='MESH' and o['layer']!='mount' for v in o.data.vertices]
    model_length=max(v.y for v in geometry)-min(v.y for v in geometry)
    world_length=2.1421875 if kind=='M2HB' else 1.8671875
    model_to_world=world_length/model_length;camera_scale=2.60 if kind=='M2HB' else 1.74
    gun_draw=camera_scale*model_to_world;base_draw=2 if kind=='M2HB' else 1.6
    folder=OUT/kind;folder.mkdir(exist_ok=True)
    meshes=[o for o in scene.objects if o.type=='MESH']
    camera.data.ortho_scale=camera_scale
    scene['art_purpose']='Nonfunctional exterior game model; true vertical orthographic sprites'
    scene['gun_draw_tiles']=gun_draw;scene['world_axis']='+Y = muzzle; +Z = height; origin = pintle'
    for part in ['body','barrel','gun','mount'] if kind=='M2HB' else ['gun','mount']:
        camera.data.ortho_scale=base_draw/model_to_world if part=='mount' else camera_scale
        for o in meshes:o.hide_render=(o['layer']!='mount') if part=='mount' else (o['layer']=='mount' or part not in ['gun'] and o['layer']!=part)
        scene.render.filepath=str(folder/(part+'_raw.png'));bpy.ops.render.render(write_still=True)
    camera.data.ortho_scale=camera_scale
    for o in meshes:o.hide_render=False
    bpy.ops.wm.save_as_mainfile(filepath=str(folder/'model.blend'),compress=True)
    metadata.update(master=MASTER,runtime=1024,projection='90 degree downward orthographic',camera_rotation_degrees=[0,0,0],camera_scale=camera_scale,
        gun_draw_tiles=gun_draw,mount_draw_tiles=base_draw,model_length=model_length,model_to_world=model_to_world,
        pivot=[0,0,0],outline_runtime_pixels=6,source_vertex_uv_preserved=kind=='M2HB',
        parts=[dict(name=o.name,layer=o['layer'],vertices=len(o.data.vertices),faces=len(o.data.polygons)) for o in meshes])
    (folder/'metadata.json').write_text(json.dumps(metadata,indent=2)+'\n')

def make_palette():
    return {name:material(name,color) for name,color in dict(
        receiver=(91,98,95),top=(126,132,125),side=(68,76,73),metal=(76,83,82),metal_top=(102,110,108),dark=(38,44,44),
        jacket=(100,110,94),jacket_top=(123,131,114),jacket_side=(78,89,76),bronze=(143,128,85),
        ammo=(79,96,52),ammo_top=(108,128,73),ammo_edge=(57,70,40),brass=(173,137,68),link=(62,64,54),wood=(102,69,44),wood_top=(131,95,62),
        mount=(70,82,73),mount_top=(99,113,98)).items()}

def m2():
    source=ROOT.parent/'M8AGS/output/WeaponLayers/vehicle.blend'
    bpy.ops.wm.open_mainfile(filepath=str(source))
    original=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.name.startswith('g0044')]
    points=[o.matrix_world@v.co for o in original for v in o.data.vertices]
    front=[p.x for p in points if p.y< -1.08];axis=sum(front)/len(front)
    meta=dict(source=str(source.relative_to(ROOT.parent.parent)),source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),source_meshes=[o.name for o in original],axis_x=axis)
    records=[]
    for o in original:
        vertices=[o.matrix_world@v.co for v in o.data.vertices]
        polygons=[(tuple(p.vertices),p.material_index,{uv.name:[tuple(uv.data[i].uv) for i in p.loop_indices] for uv in o.data.uv_layers}) for p in o.data.polygons]
        records.append((o.name,vertices,polygons,[m.name for m in o.data.materials]))
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    scene,camera=configure();mats=make_palette()
    for name,vertices,polygons,oldmaterials in records:
        groups={'body':[],'barrel':[]}
        # The long external tube is separate from the receiver and perforated support sleeve.
        for indices,mi,uv in polygons:
            center=sum((vertices[i] for i in indices),Vector())/len(indices)
            groups['barrel' if center.y<-.47 else 'body'].append((indices,mi,uv))
        for layer,faces in groups.items():
            if not faces:continue
            used=sorted(set(i for face,_,_ in faces for i in face));mapping={old:new for new,old in enumerate(used)}
            positions=[(-(vertices[i].x-axis),-vertices[i].y,vertices[i].z-2.82) for i in used]
            mesh=bpy.data.meshes.new(name+' '+layer);mesh.from_pydata(positions,[],[tuple(mapping[i] for i in face) for face,_,_ in faces]);mesh.update()
            for uvname in faces[0][2]:
                uv=mesh.uv_layers.new(name=uvname)
                for p,(_,_,sourceuv) in zip(mesh.polygons,faces):
                    for index,coord in zip(p.loop_indices,sourceuv[uvname]):uv.data[index].uv=coord
            obj=bpy.data.objects.new(name+' '+layer,mesh);scene.collection.objects.link(obj);obj['layer']=layer
            keys=list(mats);[mesh.materials.append(mats[k]) for k in keys]
            for p,(_,oldmi,_) in zip(mesh.polygons,faces):
                old=oldmaterials[oldmi]
                center=sum((mesh.vertices[i].co for i in p.vertices),Vector())/len(p.vertices)
                if 'olive' in old:key='ammo_top' if 'top' in old else 'ammo_edge' if 'bottom' in old else 'ammo'
                elif 'gunmetal' in name and center.x<-.10 and -.24<center.y<.12:key='ammo_edge'
                elif 'gunmetal' in name and center.y<-.43 and abs(center.x)>.025:key='wood_top' if p.normal.z>.5 else 'wood'
                elif 'gunmetal' in name:key='metal_top' if p.normal.z>.65 else 'metal' if p.normal.z>-.1 else 'dark'
                else:key='top' if p.normal.z>.6 else 'receiver'
                p.material_index=keys.index(key)
    # Render-only cosmetic plate details are fixed colors, independent of lights and camera.
    box('Feed cover top plate',(0,-.065,.235),(.095,.18,.012),mats['top'],'body',.009)
    box('Feed cover rear latch',(0,-.168,.247),(.041,.027,.019),mats['dark'],'body',.003)
    for x in [-.045,.045]:box('Cover side rim '+str(x),(x,-.065,.248),(.009,.15,.008),mats['receiver'],'body')
    for x in [-.019,.019]:
        for y in [.17,.22,.27,.32,.37,.42]:
            o=cylinder('Exterior sleeve cooling recess',(x,y,.248),(x,y,.251),.010,mats['dark'],'body',segments=24);o.scale.y=1.3
    for i in range(10):
        x=-.445+i*.033
        cylinder('Visible .50 belt round '+str(i),(x,-.18,.257),(x,.095,.257),.009,mats['brass'],'body',segments=12)
        box('Visible .50 belt link '+str(i),(x,-.035,.270),(.025,.027,.010),mats['link'],'body')
    mount('M2HB',mats)
    length=max(-v.y for _,vs,_,_ in records for v in vs)-min(-v.y for _,vs,_,_ in records for v in vs)
    meta['source_geometry_note']='Exterior vertices and every UV loop preserved under a rigid 180-degree yaw; render meshes split by external tube region. Added cosmetic cover/recess overlays are separate objects.'
    save_and_render('M2HB',scene,camera,length,meta)

def m1917():
    bpy.ops.wm.read_factory_settings(use_empty=True);scene,camera=configure();m=make_palette()
    box('Receiver exterior',(0,-.175,.08),(.135,.405,.165),m['receiver'],bevel=.009)
    box('Receiver top cover',(0,-.14,.174),(.123,.335,.020),m['top'],bevel=.006)
    for x in [-.057,.057]:box('Receiver cover rail '+str(x),(x,-.15,.191),(.008,.32,.012),m['side'])
    box('Cover hinge',(0,.045,.192),(.15,.025,.022),m['metal_top'],bevel=.004)
    box('Rear sight folded base',(0,-.27,.204),(.075,.11,.016),m['side'],bevel=.004)
    box('Rear sight folded leaf',(0,-.27,.219),(.046,.083,.011),m['metal_top'],bevel=.003)
    box('Rear sight aperture',(0,-.298,.227),(.020,.015,.006),m['dark'])
    # Hollow appearance is painted on the exterior caps, without internal weapon geometry.
    jacket=cylinder('Smooth water jacket',(0,.045,.09),(0,.665,.09),.101,m['jacket'],segments=64)
    jacket.data.materials.append(m['jacket_top']);jacket.data.materials.append(m['jacket_side'])
    rotation=jacket.rotation_euler.to_matrix()
    for p in jacket.data.polygons:
        normal=rotation@p.normal;p.material_index=1 if normal.z>.75 else 2 if normal.z<.25 else 0
    for y in [.055,.645]:cylinder('Water jacket end ring '+str(y),(0,y,.09),(0,y+.022,.09),.108,m['bronze'])
    cylinder('Front end plate',(0,.666,.09),(0,.678,.09),.093,m['jacket_side'])
    cylinder('Muzzle exterior stub',(0,.67,.09),(0,.711,.09),.025,m['dark'])
    box('Front sight blade',(0,.636,.214),(.014,.028,.023),m['dark'],bevel=.002)
    cylinder('Water filler neck',(-.041,.11,.171),(-.041,.11,.211),.022,m['bronze'])
    cylinder('Water filler cap',(-.041,.11,.211),(-.041,.11,.224),.032,m['bronze'])
    box('Filler cap tab',(-.041,.11,.233),(.049,.011,.009),m['jacket_top'])
    # Carry handle and its exterior band are prominent on the museum reference.
    cylinder('Carry band',(0,.36,.09),(0,.38,.09),.110,m['bronze'])
    beam('Carry handle stem',(.055,.37,.15),(.055,.37,.32),.021,.021,m['side'],'body')
    cylinder('Wood carry handle',(.055,.19,.32),(.055,.36,.32),.026,m['wood'])
    box('Carry handle top paint',(.055,.275,.343),(.027,.14,.008),m['wood_top'],bevel=.005)
    beam('Rear pistol grip',(0,-.36,.06),(0,-.455,-.01),.050,.061,m['wood'],'body')
    box('Pistol grip upper edge',(0,-.415,.079),(.045,.09,.013),m['wood_top'],bevel=.005)
    cylinder('Charging handle stem',(.065,-.155,.10),(.12,-.155,.10),.011,m['dark'])
    cylinder('Charging handle knob',(.12,-.18,.10),(.12,-.135,.10),.016,m['metal'])
    box('Ammunition box',(-.155,-.035,-.005),(.19,.24,.18),m['ammo'],bevel=.009)
    box('Ammunition box lid',(-.155,-.035,.091),(.195,.248,.018),m['ammo_top'],bevel=.006)
    box('Ammo lid folded handle',(-.18,-.03,.108),(.015,.085,.012),m['ammo_edge'],bevel=.003)
    for i in range(7):
        x=-.24+i*.025
        cylinder('Visible belt cartridge '+str(i),(x,-.045,.12),(x,.028,.12),.009,m['brass'],segments=12)
        box('Canvas belt link '+str(i),(x,-.015,.132),(.021,.023,.008),m['link'])
    for x in [-.057,.057]:
        for y in [-.32,-.08,.005]:cylinder('Receiver exterior fastener',(x,y,.183),(x,y,.191),.008,m['metal_top'],segments=12)
    mount('M1917',m)
    save_and_render('M1917',scene,camera,1.166,dict(source='Original cosmetic exterior model from US Army museum photograph',reference='https://commons.wikimedia.org/wiki/File:Browning1917.jpg',reference_variant='M1917A1 exterior; represented by the existing generic M1917 game definition',nonfunctional=True))

m2();m1917();print('HEAVY_MACHINE_GUN_MODELS_PASS',flush=True)
