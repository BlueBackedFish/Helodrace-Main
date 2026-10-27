"""Render the user-provided ext models; retain their exterior geometry and UVs."""
from pathlib import Path
from collections import defaultdict
import bpy,math,json,hashlib,os
from mathutils import Vector,Matrix
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';OUT.mkdir(exist_ok=True)
EXT=Path(os.environ.get('HELODRACE_REFERENCE_EXT',str(Path.home()/'Downloads/Dagor-Asset-Explorer-1.0.0/Dagor-Asset-Explorer-1.0.0/ext')))/'machine gun'
MASTER=2048
DIMENSIONS=json.loads((ROOT/'scale_reference.json').read_text())
STYLE=json.loads((ROOT/'art_style.json').read_text())
PALETTE=dict(metal=(79,86,84),cover=(103,111,106),dark=(37,44,42),fde=tuple(STYLE['fde_rgb']),ammo=tuple(STYLE['fde_rgb']),wood=(102,75,48),brass=(173,142,73),copper=(145,102,68),link=(43,48,43),canvas=(104,99,70),mount=tuple(STYLE['mount_fde_rgb']))
# Original hinge pin, parallel to Y. The source lid is opened 154 degrees.
AMMO_HINGE=Vector((.4073,.2142,-.1239))
AMMO_LID_ROTATION=math.radians(-154.14966566) # Measured from the source lid's large planar face.

def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()

def material(name,rgb):
    def linear(c):
        c/=255;return c/12.92 if c<=.04045 else ((c+.055)/1.055)**2.4
    m=bpy.data.materials.new(name);m.use_nodes=True;m.node_tree.nodes.clear()
    e=m.node_tree.nodes.new('ShaderNodeEmission');e.inputs[0].default_value=tuple(linear(c) for c in rgb)+(1,)
    out=m.node_tree.nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(e.outputs[0],out.inputs[0]);return m

def components(vertices,polygons):
    """Join coincident UV seams for classification only; never weld the output mesh."""
    parent=list(range(len(vertices)));at={}
    def find(i):
        while parent[i]!=i:parent[i]=parent[parent[i]];i=parent[i]
        return i
    def union(a,b):parent[find(a)]=find(b)
    for i,v in enumerate(vertices):
        key=tuple(round(c,5) for c in v)
        if key in at:union(i,at[key])
        else:at[key]=i
    for p in polygons:
        for i in p.vertices[1:]:union(p.vertices[0],i)
    groups=defaultdict(list)
    for p in polygons:groups[find(p.vertices[0])].append(p.index)
    result={}
    for group,faces in groups.items():
        points=[vertices[i] for pi in faces for i in polygons[pi].vertices]
        lo=Vector([min(v[j] for v in points) for j in range(3)]);hi=Vector([max(v[j] for v in points) for j in range(3)])
        for pi in faces:result[pi]=(group,lo,hi)
    return result

def read_source(kind,path):
    if path.suffix=='.blend':bpy.ops.wm.open_mainfile(filepath=str(path))
    else:
        bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(path))
    records=[]
    for o in bpy.context.scene.objects:
        if o.type!='MESH':continue
        vertices=[o.matrix_world@v.co for v in o.data.vertices];parts=components(vertices,o.data.polygons)
        component_faces=defaultdict(int)
        for part,_,_ in parts.values():component_faces[part]+=1
        faces=[]
        for p in o.data.polygons:
            uv={layer.name:[tuple(layer.data[i].uv) for i in p.loop_indices] for layer in o.data.uv_layers}
            group,lo,hi=parts.get(p.index,(0,None,None))
            center=sum((vertices[i] for i in p.vertices),Vector())/len(p.vertices)
            oldmat=o.data.materials[p.material_index].name
            flat=False
            if kind=='M2HB':
                layer='barrel' if o.name=='Barrel' else 'mount' if 'tripod' in o.name.lower() and oldmat!='Box_ammo' else 'body'
                category='ammo' if oldmat=='Box_ammo' else 'mount' if layer=='mount' else 'cover' if o.name=='Cover.007' else 'metal'
                pose='mount' if layer=='mount' else 'gun'
                flat=layer in ['barrel','mount'] or o.name=='Body_M2' and oldmat=='Set_001'
                if o.name=='Body_M2' and center.y>.69 and abs(center.x-.0109783)>.033:category='wood'
                if oldmat=='Box_ammo':
                    count=component_faces[group]
                    # These repeated complete source parts are cases, projectiles and links.
                    is_round=count in [206,140] and hi.x-lo.x<.035 and hi.y-lo.y<.10 and hi.x<.16
                    is_link=count==261 and hi.x<.16 and lo.y>.14 and hi.y<.31
                    if is_round or is_link:
                        pose='ammo_belt';category='link' if is_link else 'brass' if count==206 else 'copper'
                    else:
                        pose='ammo_lid' if lo.x>.405 else 'ammo_box';category='ammo'
                        # The folded carrying handle and lid latch are separate source parts.
                        if pose=='ammo_lid' and count in [480,158]:category='dark'
                    flat=True
            else:
                is_ammo=lo.x>.29 and hi.y<.4 and lo.z>-.65
                is_round=.20<hi.y-lo.y<.21 and hi.x-lo.x<.04 and hi.z-lo.z<.04
                layer='body' if is_round or hi.z>-.04 or is_ammo or lo.y>.7 and hi.z>-.33 else 'mount'
                category='mount' if layer=='mount' else 'fde' if lo.y< -2 else 'ammo' if is_ammo else 'cover' if lo.z>.15 else 'metal'
                if lo.y>1.3 and center.z<-.09:category='wood'
                if is_round:
                    along=(center.y-lo.y)/(hi.y-lo.y)
                    category='copper' if along<.24 else 'canvas' if .47<along<.62 else 'brass'
                    flat=True
                pose='mount' if layer=='mount' else 'gun'
                flat=flat or category in ['fde','mount']
            faces.append(dict(indices=tuple(p.vertices),uv=uv,layer=layer,pose=pose,category=category,flat=flat,source_face=p.index,component=group))
        poses={}
        if kind=='M2HB' and any(f['pose']=='ammo_belt' for f in faces):
            # Keep each cartridge and its link rigid, but bend the belt's path into
            # the level box. Pitching the whole original belt pushes its tail past
            # the box wall in the top view.
            gun_matrix=transform_for(kind,'gun');pieces=defaultdict(list)
            for f in faces:
                if f['pose']=='ammo_belt':pieces[f['component']].append(f)
            def bounds(fs):
                points=[gun_matrix@vertices[i] for f in fs for i in f['indices']]
                return Vector([min(p[i] for p in points) for i in range(3)]),Vector([max(p[i] for p in points) for i in range(3)])
            centers={group:(sum(bounds(fs),Vector())/2) for group,fs in pieces.items()}
            cases=sorted((g for g,fs in pieces.items() if fs[0]['category']=='brass'),key=lambda g:-centers[g].z)
            slots={case:list(pieces[case]) for case in cases}
            for category in ['copper','link']:
                groups=[g for g,fs in pieces.items() if fs[0]['category']==category]
                assert len(groups)==len(cases)
                # Links sit between neighboring rounds: independent nearest-point
                # assignment can give one round two links and another none.
                candidates=sorted(((centers[c].x-centers[g].x)**2+(centers[c].z-centers[g].z)**2,c,g) for c in cases for g in groups)
                used_cases=set();used_groups=set()
                for _,case,group in candidates:
                    if case not in used_cases and group not in used_groups:
                        slots[case].extend(pieces[group]);used_cases.add(case);used_groups.add(group)
            box_y=(transform_for(kind,'ammo_box')@Vector((.25,(.1446+.2841)/2,0))).y
            for index,case in enumerate(cases):
                fs=slots[case];lo,hi=bounds(fs);center=(lo+hi)/2
                # First rounds stay at the receiver; by the box mouth the whole
                # cartridge span is centered between the front and rear walls.
                source_x=sum((parts[f['source_face']][1].x+parts[f['source_face']][2].x)/2 for f in pieces[case])/len(pieces[case])
                t=max(0,min(1,(source_x-.03)/.09));t=t*t*(3-2*t)
                pose=f'ammo_belt_{index:02d}';poses[pose]=Matrix.Translation((0,(box_y-center.y)*t,0))@gun_matrix
                for f in fs:f['pose']=pose
        records.append(dict(name=o.name,vertices=vertices,faces=faces,pose_transforms=poses,source_vertices=len(vertices),source_faces=len(faces)))
    return records

def configure():
    bpy.ops.wm.read_factory_settings(use_empty=True);scene=bpy.context.scene
    scene.render.engine='CYCLES';scene.cycles.samples=8;scene.cycles.use_denoising=False
    scene.render.resolution_x=scene.render.resolution_y=MASTER;scene.render.resolution_percentage=100
    scene.render.film_transparent=True;scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA';scene.render.image_settings.color_depth='8'
    scene.render.dither_intensity=0;scene.view_settings.view_transform='Standard';scene.view_settings.look='None';scene.view_settings.exposure=0;scene.view_settings.gamma=1
    bpy.ops.object.camera_add(location=(0,0,10));camera=bpy.context.object;camera.name='ORTHOGRAPHIC_90_DEGREES';camera.rotation_euler=(0,0,0);camera.data.type='ORTHO';scene.camera=camera
    bpy.context.preferences.filepaths.save_version=0
    return scene,camera

def transform_for(kind,pose):
    if kind=='M2HB':
        pivot=Vector((.0109782955,.171350077,-.059308596))
        # Source gun is elevated 16.32 degrees; keep the tripod on the ground.
        level=Matrix.Rotation(.2848380208,4,'X') if pose=='gun' else Matrix.Identity(4)
    else:pivot=Vector((0,.046,-.979));level=Matrix.Identity(4)
    matrix=Matrix.Rotation(math.pi,4,'Z')@level@Matrix.Translation(-pivot)
    if kind=='M2HB' and pose=='ammo_lid':
        matrix=matrix@Matrix.Translation(AMMO_HINGE)@Matrix.Rotation(AMMO_LID_ROTATION,4,'Y')@Matrix.Translation(-AMMO_HINGE)
    return matrix

def make(kind,relative):
    source=EXT/relative;records=read_source(kind,source)
    mount_fit_y=1.0
    if kind=='M1917':
        points=defaultdict(list)
        for record in records:
            for f in record['faces']:
                points[f['layer']].extend(transform_for(kind,f['pose'])@record['vertices'][i] for i in f['indices'])
        gun_length=max(v.y for v in points['body'])-min(v.y for v in points['body'])
        mount_length=max(v.y for v in points['mount'])-min(v.y for v in points['mount'])
        target_mount_length=gun_length*DIMENSIONS['m1917_mount_fore_aft_length_m']/DIMENSIONS['weapon_length_m'][kind]
        mount_fit_y=target_mount_length/mount_length
        for record in records:
            # Preserve the top-down aspect ratio, including round feet. Scaling
            # only Y flattened the feet and spread of the source tripod.
            record['pose_transforms']['mount']=Matrix.Diagonal((mount_fit_y,mount_fit_y,1,1))@transform_for(kind,'mount')
    scene,camera=configure()
    materials={}
    for key,rgb in PALETTE.items():
        for tone,factor in [('top',1.0),('edge',.79),('underside',.60)]:
            materials[key+'_'+tone]=material(key+'_'+tone,tuple(round(c*factor) for c in rgb))
    keys=list(materials)
    for record in records:
        grouped=defaultdict(list)
        for face in record['faces']:grouped[(face['layer'],face['pose'])].append(face)
        for (layer,pose),faces in grouped.items():
            used=sorted({i for f in faces for i in f['indices']});remap={i:j for j,i in enumerate(used)};matrix=record['pose_transforms'].get(pose,transform_for(kind,pose))
            mesh=bpy.data.meshes.new(record['name']+' '+pose);mesh.from_pydata([matrix@record['vertices'][i] for i in used],[],[[remap[i] for i in f['indices']] for f in faces]);mesh.update()
            for uvname in faces[0]['uv']:
                uv=mesh.uv_layers.new(name=uvname)
                for p,f in zip(mesh.polygons,faces):
                    for i,coord in zip(p.loop_indices,f['uv'][uvname]):uv.data[i].uv=coord
            vi=mesh.attributes.new('source_vertex','INT','POINT')
            for v,old in zip(vi.data,used):v.value=old
            fi=mesh.attributes.new('source_face','INT','FACE')
            for p,f in zip(fi.data,faces):p.value=f['source_face']
            for key in keys:mesh.materials.append(materials[key])
            for p,f in zip(mesh.polygons,faces):
                tone='top' if f['flat'] or p.normal.z>.65 else 'edge' if p.normal.z>-.15 else 'underside'
                p.material_index=keys.index(f['category']+'_'+tone)
            obj=bpy.data.objects.new(record['name']+' / '+pose,mesh);scene.collection.objects.link(obj);obj['layer']=layer;obj['pose']=pose;obj['source_object']=record['name'];obj['source_transform']=sum((list(row) for row in matrix),[])
    meshes=[o for o in scene.objects if o.type=='MESH'];gun=[v.co for o in meshes if o['layer']!='mount' for v in o.data.vertices];stand=[v.co for o in meshes if o['layer']=='mount' for v in o.data.vertices]
    length=max(v.y for v in gun)-min(v.y for v in gun)
    world_length=DIMENSIONS['baseline_m2_visible_length_tiles']*DIMENSIONS['weapon_length_m'][kind]/DIMENSIONS['weapon_length_m']['M2HB'];model_to_world=world_length/length
    gun_scale=max(abs(v[i]) for v in gun for i in [0,1])*2*1.10
    base_scale=max(abs(v[i]) for v in stand for i in [0,1])*2*1.10
    folder=OUT/kind;folder.mkdir(exist_ok=True)
    for part in ['body','barrel','gun','mount'] if kind=='M2HB' else ['gun','mount']:
        camera.data.ortho_scale=base_scale if part=='mount' else gun_scale
        for o in meshes:o.hide_render=o['layer']!='mount' if part=='mount' else o['layer']=='mount' or part!='gun' and o['layer']!=part
        scene.render.filepath=str(folder/(part+'_raw.png'));bpy.ops.render.render(write_still=True)
    camera.data.ortho_scale=gun_scale
    for o in meshes:o.hide_render=False
    scene['world_axis']='+Y = muzzle; +Z = height; origin = original mount swivel';scene['gun_draw_tiles']=gun_scale*model_to_world
    bpy.ops.wm.save_as_mainfile(filepath=str(folder/'model.blend'),compress=True)
    metadata=dict(source=str(source),source_relative_to_ext='machine gun/'+relative,source_sha256=sha(source),master=MASTER,runtime=1024,projection='90 degree downward orthographic',camera_rotation_degrees=[0,0,0],camera_scale=gun_scale,
        gun_draw_tiles=gun_scale*model_to_world,mount_draw_tiles=base_scale*model_to_world,model_length=length,model_to_world=model_to_world,pivot=[0,0,0],outline_world_tiles=STYLE['outline_world_tiles'],icon_outline_pixels=STYLE['icon_outline_pixels'],texture_directory=STYLE['texture_directory'],
        source_geometry_note='Every source polygon and UV loop retained under recorded transforms; M1917 mount uses equal X/Y scaling with height retained; no added geometry.',
        weapon_length_m=DIMENSIONS['weapon_length_m'][kind],visible_gun_length_tiles=world_length,mount_planar_fit=mount_fit_y,
        style_note='Water jacket, M2 barrel/sleeve and tripod surfaces use uniform colors without normal-based side shading; cartridge cases, copper projectiles and links are distinct.',
        paint_finish=f"FDE {tuple(STYLE['fde_rgb'])} including ammunition boxes and lids; darker tripod FDE {tuple(STYLE['mount_fde_rgb'])}",
        ammo_box_pose='Level box and closed lid; individually rigid cartridges curve from receiver into the box interior, with the tail beneath the lid' if kind=='M2HB' else 'Original source pose',
        source_objects=[dict(name=r['name'],vertices=r['source_vertices'],faces=r['source_faces']) for r in records],
        parts=[dict(name=o.name,source_object=o['source_object'],layer=o['layer'],pose=o['pose'],vertices=len(o.data.vertices),faces=len(o.data.polygons)) for o in meshes])
    (folder/'metadata.json').write_text(json.dumps(metadata,indent=2)+'\n')

if __name__=='__main__':
    make('M2HB','browning-m2/M2/M2.blend');make('M1917','browning-m1917/source/browning m1917.fbx')
    print('HMG_EXT_MODELS_PASS',flush=True)
