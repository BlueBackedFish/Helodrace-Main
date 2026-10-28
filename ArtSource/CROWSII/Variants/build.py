"""Mount three supplied FBX weapons on the preserved CROWS station, with fitted belts."""
from pathlib import Path
from collections import defaultdict
import bpy,bmesh,sys,os,json,math,hashlib
from mathutils import Vector,Matrix
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';OUT.mkdir(parents=True,exist_ok=True)
sys.path.insert(0,str(ROOT.parents[1]/'PropAssets'));import modeling as m;import surface_paint
sys.path.insert(0,str(ROOT.parent));import feed_alignment
CONFIG=json.loads((ROOT/'weapons.json').read_text())
EXT=Path(os.environ.get('HELODRACE_REFERENCE_EXT',str(Path.home()/'Downloads/Dagor-Asset-Explorer-1.0.0/Dagor-Asset-Explorer-1.0.0/ext')))/'machine gun'
STATION=ROOT.parent/'output/crows_ii_m2.blend'
WORLD_PER_M=None
M2_CALIBRATION=None

def calibrate_m2():
    global WORLD_PER_M,M2_CALIBRATION
    bpy.ops.wm.open_mainfile(filepath=str(STATION));scene=bpy.context.scene;scene.frame_set(1);bpy.context.view_layer.update()
    points=[o.matrix_world@v.co for o in scene.objects if o.type=='MESH' and o.get('donor_object') and o.get('layer') in ['body','barrel'] for v in o.data.vertices]
    length=max(p.y for p in points)-min(p.y for p in points)
    reference=json.loads((ROOT.parents[1]/'HeavyMachineGuns/scale_reference.json').read_text())
    real_length=reference['weapon_length_m']['M2HB'];WORLD_PER_M=length/real_length
    M2_CALIBRATION=dict(source=str(STATION),source_sha256=hashlib.sha256(STATION.read_bytes()).hexdigest(),m2_length_tiles=length,m2_length_m=real_length,world_per_m=WORLD_PER_M,reference=reference['sources']['M2HB'])
    (OUT/'scale_calibration.json').write_text(json.dumps(M2_CALIBRATION,indent=2)+'\n')

def components(vertices,polygons):
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
    for key,faces in groups.items():
        points=[vertices[i] for f in faces for i in polygons[f].vertices]
        result[key]=dict(faces=faces,lo=Vector([min(p[j] for p in points) for j in range(3)]),hi=Vector([max(p[j] for p in points) for j in range(3)]))
    return result

def extract_source(kind,config):
    scene=m.start();source=EXT/config['source'];bpy.ops.import_scene.fbx(filepath=str(source));bpy.context.view_layer.update()
    o=scene.objects[config['object']];vertices=[o.matrix_world@v.co for v in o.data.vertices];parts=components(vertices,o.data.polygons)
    # Explicitly reviewed source selections: the HMMWV body/ring stay outside
    # the Mk19 object; its side box/belt and infantry bipods are replaced.
    records=[];removed=[]
    for key,part in parts.items():
        remove=key in config['remove'] or (kind=='MK19' and part['hi'].y<-.34)
        if remove:removed.append(dict(id=key,faces=len(part['faces']),reason='source ammunition/side box' if kind=='MK19' else 'deployed bipod or infantry optic'));continue
        layer='barrel' if part['hi'][config['length_axis']]<config['barrel_limit'] else 'body'
        for index in part['faces']:
            p=o.data.polygons[index]
            records.append(dict(source_face=index,component=key,layer=layer,indices=list(p.vertices),
                uv={uv.name:[tuple(uv.data[i].uv) for i in p.loop_indices] for uv in o.data.uv_layers}))
    kept={i for f in records for i in f['indices']};axis=config['length_axis'];length=max(vertices[i][axis] for i in kept)-min(vertices[i][axis] for i in kept)
    scale=config['length_m']*WORLD_PER_M/length;rotation=Matrix.Rotation(math.radians(config['rotation_z']),4,'Z')
    feed=Vector(config['feed_native']);rotated=rotation@feed
    shift=Vector((-config['axis_native_x']*scale,-.009-rotated.y*scale,.855-rotated.z*scale))
    transform=Matrix.Translation(shift)@rotation@Matrix.Diagonal((scale,scale,scale,1))
    target=transform@feed
    data=dict(vertices=vertices,records=records,transform=transform,scale=scale,feed=target,removed=removed,
        source_faces=len(o.data.polygons),source_vertices=len(o.data.vertices),source=str(source),source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),source_length=length)
    return data

def load_station():
    scene=m.start()
    with bpy.data.libraries.load(str(STATION),link=False) as (available,loaded):loaded.objects=list(available.objects)
    station=[]
    for o in loaded.objects:
        if o.type=='MESH' and o.get('layer') in ['base','frame']:
            scene.collection.objects.link(o);o.hide_render=False;station.append(o)
        else:bpy.data.objects.remove(o,do_unlink=True)
    bpy.context.view_layer.update()
    for o in station:
        world=o.matrix_world.copy();o.parent=None;o.matrix_world=world
    frame=next(o for o in station if o.get('source_object')=='Cylinder.003' and o.get('layer')=='frame')
    remove=[p.index for p in frame.data.polygons if frame.data.materials[p.material_index].name=='feed']
    selected=[frame.data.polygons[i] for i in remove]
    used=sorted({i for p in selected for i in p.vertices});mapping={v:i for i,v in enumerate(used)}
    mesh=bpy.data.meshes.new('Canonical M2 feed chute')
    mesh.from_pydata([frame.matrix_world@frame.data.vertices[i].co for i in used],[],[[mapping[i] for i in p.vertices] for p in selected]);mesh.update()
    mesh.materials.append(frame.data.materials[selected[0].material_index])
    for layer in frame.data.uv_layers:
        uv=mesh.uv_layers.new(name=layer.name)
        for dst,src in zip(mesh.polygons,selected):
            for a,b in zip(dst.loop_indices,src.loop_indices):uv.data[a].uv=layer.data[b].uv
    chute=bpy.data.objects.new('Canonical M2 feed chute',mesh);scene.collection.objects.link(chute);chute['layer']='feed'
    kept=[p for p in frame.data.polygons if p.index not in set(remove)]
    before=[(tuple(tuple(frame.data.vertices[i].co) for i in p.vertices),p.material_index) for p in kept]
    bm=bmesh.new();bm.from_mesh(frame.data);bm.faces.ensure_lookup_table();bmesh.ops.delete(bm,geom=[bm.faces[i] for i in remove],context='FACES');bm.to_mesh(frame.data);bm.free();frame.data.update()
    after=[(tuple(tuple(frame.data.vertices[i].co) for i in p.vertices),p.material_index) for p in frame.data.polygons]
    assert before==after,'Replacing the chute changed retained station faces'
    return scene,station,len(remove),chute

def weapon(kind,config,data):
    palette=surface_paint.palette();colors={k:m.material('Variant '+k,v) for k,v in palette.items()}
    colors['polymer']=m.material('Weapon black polymer',(37,43,40));colors['polymer_top']=m.material('Weapon polymer upper',(61,68,63))
    colors['barrel']=m.material('Weapon barrel wall',(59,67,63));colors['bore']=m.material('Weapon bore',(23,28,26));colors['rail']=m.material('Weapon rail cap',(99,107,102))
    made=[];keys=list(colors)
    for layer in ['body','barrel']:
        faces=[f for f in data['records'] if f['layer']==layer];used=sorted({i for f in faces for i in f['indices']});mapping={v:i for i,v in enumerate(used)}
        mesh=bpy.data.meshes.new(kind+' '+layer);mesh.from_pydata([data['transform']@data['vertices'][i] for i in used],[],[[mapping[i] for i in f['indices']] for f in faces]);mesh.update()
        for key in keys:mesh.materials.append(colors[key])
        for p,f in zip(mesh.polygons,faces):
            src=[data['vertices'][i] for i in f['indices']];center=sum(src,Vector())/len(src)
            # Broad receiver planes use fixed support values. Barrel and other
            # curved small parts have uniform walls, with separate end colors.
            key='metal_cover' if p.normal.z>.65 else 'metal_side' if abs(p.normal.x)>.65 else 'metal_front' if p.normal.y>.65 else 'metal'
            if layer=='barrel':key='barrel' if abs(p.normal.y)<.9 else 'bore' if p.normal.y>.9 else 'metal'
            if kind=='M240B' and center.x>2.5:key='polymer_top' if p.normal.z>.65 else 'polymer'
            if kind=='M249' and center.y>3.1:key='polymer_top' if p.normal.z>.65 else 'polymer'
            if kind=='MK19' and abs(p.normal.z)>.65:key='metal_cover'
            p.material_index=keys.index(key)
        for name in faces[0]['uv']:
            uv=mesh.uv_layers.new(name=name)
            for p,f in zip(mesh.polygons,faces):
                for index,co in zip(p.loop_indices,f['uv'][name]):uv.data[index].uv=co
        source_vertex=mesh.attributes.new('source_vertex','INT','POINT');source_face=mesh.attributes.new('source_face','INT','FACE')
        for a,i in zip(source_vertex.data,used):a.value=i
        for a,f in zip(source_face.data,faces):a.value=f['source_face']
        o=bpy.data.objects.new(kind+' / '+layer,mesh);bpy.context.scene.collection.objects.link(o);o['layer']=layer;o['source_object']=config['object'];made.append(o)
        for v,i in zip(mesh.vertices,used):assert (v.co-data['transform']@data['vertices'][i]).length<1e-6
    # Fixed adaptor supports a smaller receiver without changing its width or
    # rescaling the CROWS frame. The infantry stocks remain present.
    for x in [-.075,.075]:
        o=m.box(kind+' receiver adapter',(x,-.018,.731),(.04,.24,.074),colors['mount_top'],.004);o['layer']='frame';made.append(o)
    return made

def belt(kind,config,data,chute):
    target=data['feed'].copy();target.x+=.004
    width=config['chute_width_m']*WORLD_PER_M
    report=feed_alignment.align(chute,chute.data.materials[0],target,width,24)
    chute.name=kind+' enclosed feed chute';chute['feed_style']='canonical M2 enclosed chute'
    report.update(style='canonical M2 enclosed chute',receiver_feed_gate=list(data['feed']),belt_terminal=list(target),terminal_overlap_tiles=.004,individual_cartridges=False)
    return [chute],report

def build(kind):
    if M2_CALIBRATION is None:calibrate_m2()
    config=CONFIG[kind];data=extract_source(kind,config);scene,station,removed_feed,chute=load_station();additions=weapon(kind,config,data);feed,feed_report=belt(kind,config,data,chute)
    meshes=station+additions+feed
    yaw=bpy.data.objects.new('CROWS_YAW',None);scene.collection.objects.link(yaw)
    for o in meshes:
        if o.get('layer')!='base':world=o.matrix_world.copy();o.parent=yaw;o.matrix_world=world
    bpy.context.view_layer.update();folder=OUT/kind;folder.mkdir(parents=True,exist_ok=True)
    top=m.camera('Camera_Top',0,90,3.2);top.location=(0,0,10);top.rotation_euler=(0,0,0)
    for part in ['frame','body','barrel','gun','feed','full']:
        for o in meshes:o.hide_render=not(part=='full' or part=='gun' and o.get('layer') in ['body','barrel'] or o.get('layer')==part)
        m.render(folder/(part+'_raw.png'),top,2048)
    # Visible belt overlay uses real occlusion by the fixed gun and frame.
    hold=bpy.data.materials.new('Variant belt holdout');hold.use_nodes=True;hold.node_tree.nodes.clear();h=hold.node_tree.nodes.new('ShaderNodeHoldout');output=hold.node_tree.nodes.new('ShaderNodeOutputMaterial');hold.node_tree.links.new(h.outputs[0],output.inputs['Surface'])
    backups={o:list(o.data.materials) for o in meshes}
    for o in meshes:
        o.hide_render=False
        if o.get('layer')!='feed':
            for i in range(len(o.data.materials)):o.data.materials[i]=hold
    m.render(folder/'feed_visible_raw.png',top,2048)
    for o,materials in backups.items():
        for i,mat in enumerate(materials):o.data.materials[i]=mat
    for direction,azimuth in [('north',-90),('east',0),('south',90),('west',180)]:m.render(folder/(direction+'_raw.png'),m.camera('Camera_'+direction,azimuth,64,3.6),2048)
    scene.camera=top;m.save(folder/('crows_'+kind.lower()+'.blend'))
    assert not any(o.type=='LIGHT' for o in scene.objects)
    metadata=dict(weapon=kind,source=data['source'],source_sha256=data['source_sha256'],selected_source_object=config['object'],source_faces=data['source_faces'],retained_faces=len(data['records']),removed_components=data['removed'],transform=[list(row) for row in data['transform']],uniform_scale=data['scale'],source_length=data['source_length'],length_m=config['length_m'],world_per_m=WORLD_PER_M,m2_calibration=M2_CALIBRATION,length_ratio_to_m2=config['length_m']/M2_CALIBRATION['m2_length_m'],reference=config['reference'],station_source=str(STATION),station_sha256=hashlib.sha256(STATION.read_bytes()).hexdigest(),station_feed_faces_replaced=removed_feed,station_other_faces_preserved=True,feed=feed_report,top_draw_size=[3.2,3.2],directional_draw_size=[3.6,3.6],pivot=[0,0],master_resolution=2048,native_resolution=1024,outline_world_tiles=.026,barrel_is_fixed=True,gameplay_added=False)
    (folder/'metadata.json').write_text(json.dumps(metadata,indent=2)+'\n')
    print('CROWS_VARIANT_BUILD_PASS',kind,'source faces',len(data['records']),'M2 chute faces',feed_report['source_faces'],flush=True)

if __name__=='__main__':
    wanted=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else list(CONFIG)
    for kind in wanted:build(kind)
