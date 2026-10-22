"""Role-driven turret rebuild; preserve the frozen stage-one hull geometry."""
import bpy,bmesh,json,math,os,sys,hashlib,shutil
from pathlib import Path
from mathutils import Vector,Matrix
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'RimWorld'
cfg=json.loads((ROOT/'turret_stage2.json').read_text());roles=json.loads((ROOT/cfg['role_registry']).read_text(encoding='utf-8'))
digest=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
assert digest(ROOT/roles['source_binding']['blend'])==roles['source_binding']['blend_sha256']
baseline=ROOT/cfg['baseline']
if not baseline.exists():
    assert digest(ROOT/roles['current_snapshot']['blend'])==roles['current_snapshot']['blend_sha256']
    shutil.copyfile(ROOT/roles['current_snapshot']['blend'],baseline)
roles['current_snapshot']['blend']=cfg['baseline']
roles['current_snapshot']['description']='Frozen stage-one baseline, not the latest rebuilt output'
roles['policy']['production_simplifier_uses_registry']=True
roles['policy']['production_registry_scope']='turret'
roles['stage_status']='stage2_turret_build_in_progress'
(ROOT/cfg['role_registry']).write_text(json.dumps(roles,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
inventory=json.loads((ROOT/'Audit/Stage1/source_inventory.json').read_text())
byrole={}
for entry in roles['entries']:byrole.setdefault(entry['role'],[]).append(entry)
def source_name(role):return byrole[role][0]['object']
bpy.ops.wm.open_mainfile(filepath=str(ROOT/roles['source_binding']['blend']))
bpy.context.scene.frame_set(1)
source={}
for entry in roles['entries']:
    if entry['parent'] not in {'TURRET_YAW','GUN_ELEVATION'}:continue
    o=bpy.data.objects[entry['object']];me=o.data
    source[o.name]={'verts':[tuple(o.matrix_world@v.co) for v in me.vertices],
        'faces':[list(p.vertices) for p in me.polygons],
        'normals':[tuple(p.normal) for p in me.polygons],
        'materials':[me.materials[p.material_index].name for p in me.polygons],
        'role':entry['role'],'parent':entry['parent']}
bpy.ops.wm.open_mainfile(filepath=str(baseline));scene=bpy.context.scene;scene.frame_set(1)
def under(o,name):
    while o:
        if o.name==name:return True
        o=o.parent
    return False
def geometry_hash(objects):
    h=hashlib.sha256()
    for o in sorted(objects,key=lambda q:q.name):
        h.update(o.name.encode());h.update(str([tuple(v.co) for v in o.data.vertices]).encode());h.update(str([list(p.vertices) for p in o.data.polygons]).encode())
    return h.hexdigest()
hull=[o for o in scene.objects if o.type=='MESH' and not under(o,'TURRET_YAW')]
hull_hash=geometry_hash(hull)
for o in list(scene.objects):
    if o.type=='MESH' and under(o,'TURRET_YAW'):bpy.data.objects.remove(o,do_unlink=True)
mats={}
for role,rgb in cfg['palette_srgb'].items():
    m=bpy.data.materials.new('Turret face | '+role);m.use_nodes=True;m['face_role']=role
    lin=tuple(c/255/12.92 if c/255<=.04045 else ((c/255+.055)/1.055)**2.4 for c in rgb)+(1,)
    m.diffuse_color=lin;n=m.node_tree.nodes;n.clear();e=n.new('ShaderNodeEmission');e.inputs['Color'].default_value=lin;out=n.new('ShaderNodeOutputMaterial');m.node_tree.links.new(e.outputs[0],out.inputs[0]);mats[role]=m
created=[]
def mesh(name,verts,faces,role,parent='TURRET_YAW',face_roles=None):
    me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.update()
    o=bpy.data.objects.new(name,me);scene.collection.objects.link(o);o.parent=bpy.data.objects[parent];o.matrix_parent_inverse=o.parent.matrix_world.inverted();o['art_role']=role;o['build_stage']=2
    for m in mats.values():me.materials.append(m)
    keys=list(mats)
    for p in me.polygons:
        r=face_roles[p.index] if face_roles else ('roof' if p.normal.z>.75 else 'front_slope' if p.normal.y<-.4 else 'rear_slope' if p.normal.y>.4 else 'side_slope' if p.normal.z>.1 else 'wall')
        p.material_index=keys.index(r);p.use_smooth=False
    created.append(o);return o
def box(name,center,size,role,parent='TURRET_YAW',top='equipment_top'):
    x,y,z=center;a,b,c=(v/2 for v in size)
    verts=[(x+sx*a,y+sy*b,z+sz*c) for sx,sy,sz in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
    colors=['optics']*6 if role=='optics' else ['recess','front_slope','wall','wall','wall',top]
    return mesh(name,verts,[(0,3,2,1),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7),(4,5,6,7)],role,parent,colors)
def cylinder(name,a,b,radius,role,parent='TURRET_YAW',color='metal',segments=12):
    a,b=Vector(a),Vector(b);axis=(b-a).normalized();u=axis.cross(Vector((0,0,1)))
    if u.length<.01:u=axis.cross(Vector((0,1,0)))
    u.normalize();v=axis.cross(u);verts=[]
    for end in (a,b):
        for i in range(segments):verts.append(tuple(end+radius*(u*math.cos(i*2*math.pi/segments)+v*math.sin(i*2*math.pi/segments))))
    faces=[tuple(reversed(range(segments))),tuple(range(segments,2*segments))]
    faces += [(i,(i+1)%segments,(i+1)%segments+segments,i+segments) for i in range(segments)]
    return mesh(name,verts,faces,role,parent,[color]*len(faces))
def port(name,center,radius,normal,role,parent='TURRET_YAW'):
    c=Vector(center);n=Vector(normal).normalized();u=n.cross(Vector((0,0,1)))
    if u.length<.01:u=n.cross(Vector((0,1,0)))
    u.normalize();v=n.cross(u);N=cfg['geometry']['port_segments']
    verts=[tuple(c+radius*factor*(u*math.cos(i*2*math.pi/N)+v*math.sin(i*2*math.pi/N))) for factor in (1,.73) for i in range(N)]
    faces=[(i,(i+1)%N,(i+1)%N+N,i+N) for i in range(N)]+[tuple(range(N,2*N))]
    return mesh(name,verts,faces,role,parent,['rim']*N+['optics'])
def copied(role,indices=None,name=None):
    source_obj=source_name(role);data=source[source_obj];indices=range(len(data['faces'])) if indices is None else indices
    indices=list(indices);used=sorted({i for pi in indices for i in data['faces'][pi]});lookup={i:k for k,i in enumerate(used)}
    face_roles=[]
    for pi in indices:
        n=Vector(data['normals'][pi]);mat=data['materials'][pi]
        face_roles.append('optics' if 'Optics' in mat else 'roof' if n.z>.75 else 'front_slope' if n.y<-.4 else 'rear_slope' if n.y>.4 else 'side_slope' if n.z>.1 else 'wall')
    return mesh(name or source_obj,[data['verts'][i] for i in used],[[lookup[i] for i in data['faces'][pi]] for pi in indices],role,data['parent'],face_roles)
# Preserve source armor boundary and roof contour, rather than collapsing it.
turret_name=source_name('turret_assembly');components=inventory[turret_name]['components']
shell=max(components,key=lambda c:c['faces']);copied('turret_assembly',shell['polygon_indices'])
for suffix,predicate in [
 ('Roof',lambda c:c['faces']==354),('Ring',lambda c:c['faces']==306),
 ('RearShelf',lambda c:c['faces']==252),('RearWall',lambda c:c['faces']==188),
 ('SightPlinth',lambda c:c['faces']==348)]:
    comp=next(c for c in components if predicate(c));copied('turret_assembly',comp['polygon_indices'],'Turret_'+suffix)
# Two broad, clean hatch lids with explicit sidewall/rim and representative handles.
for role in ('turret_hatch_a','turret_hatch_b'):
    entry=byrole[role][0];lo,hi=map(Vector,entry['source_bounds']);center=(lo+hi)/2;rx=(hi.x-lo.x)/2;ry=(hi.y-lo.y)/2
    z0=2.742;z1=2.79 if role.endswith('b') else 2.825;N=cfg['geometry']['hatch_segments'];rings=[]
    for scale,z in [(1,z0),(1,z0+.026),(.88,z1)]:
        rings.extend([(center.x+rx*scale*math.cos(i*2*math.pi/N),center.y+ry*scale*math.sin(i*2*math.pi/N),z) for i in range(N)])
    faces=[];colors=[]
    for ring in range(2):
        for i in range(N):faces.append((ring*N+i,ring*N+(i+1)%N,(ring+1)*N+(i+1)%N,(ring+1)*N+i));colors.append('wall' if ring==0 else 'hatch_rim')
    faces += [tuple(reversed(range(N))),tuple(range(2*N,3*N))];colors+=['recess','hatch_top']
    mesh(entry['object'],rings,faces,role,face_roles=colors)
    y=center.y+.08;zz=z1+.055;w=.22
    for side in (-1,1):box(role+'_HandleFoot'+str(side),(center.x+side*w/2,y,z1+.025),(.045,.045,.05),'hatch_handle',top='rim')
    box(role+'_Handle',(center.x,y,zz),(w+.04,.045,.035),'hatch_handle',top='rim')
    box(role+'_Hinge',(center.x,center.y+ry*.85,z0+.018),(.23,.07,.055),'hatch_hinge',top='rim')
# Reconstruct source front sight housing; preserve all four original optical faces.
box('GunnerSightBody',(.259,-.045,2.865),(.59,.30,.27),'gunner_sight')
optical=[i for i,m in enumerate(source[turret_name]['materials']) if 'Optics' in m]
copied('turret_assembly',optical,'GunnerSight_OriginalOptics')
# Original optical faces may sit behind the housing front; two broad apertures
# make the same equipment readable in the diagnostic render.
for x in (.10,.37):box('GunnerSight_Window'+str(x),(x,-.198,2.88),(.13,.009,.16),'optics',top='optics')
for role in ('turret_front_upper_cover','turret_front_lower_cover'):
    o=copied(role)
    bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00015)
    bmesh.ops.dissolve_limit(bm,angle_limit=math.radians(.1),verts=list(bm.verts),edges=list(bm.edges),delimit={'MATERIAL'});bm.to_mesh(o.data);bm.free()
entry=byrole['gun_mount'][0];lo,hi=map(Vector,entry['source_bounds'])
# A closed, broad trapezoidal mount replaces internal rods and torn source plates.
profile=[(lo.x,lo.z+.04),(hi.x,lo.z+.04),(hi.x,hi.z-.12),(hi.x-.10,hi.z),(lo.x+.12,hi.z),(lo.x,hi.z-.12)]
verts=[(x,y,z) for y in (-.95,-.20) for x,z in profile];N=len(profile)
faces=[tuple(reversed(range(N))),tuple(range(N,2*N))]+[(i,(i+1)%N,(i+1)%N+N,i+N) for i in range(N)]
mesh(entry['object'],verts,faces,'gun_mount','GUN_ELEVATION',['front_slope','wall']+['side_slope']*N)
entry=byrole['gun_upper_shroud'][0];lo,hi=map(Vector,entry['source_bounds']);c=(lo+hi)/2
box(entry['object'],tuple(c),tuple(hi-lo),'gun_upper_shroud','GUN_ELEVATION')
# Commander sight and its three-sided protective enclosure.
lo,hi=map(Vector,byrole['commander_sight'][0]['source_bounds']);c=(lo+hi)/2
box(source_name('commander_sight'),tuple(c),tuple(hi-lo),'commander_sight')
port('CommanderSight_MainLens',(c.x,lo.y-.006,c.z),.10,(0,-1,0),'optics')
port('CommanderSight_AuxLens',(c.x+.135,lo.y-.008,c.z-.025),.045,(0,-1,0),'optics')
guard=byrole['commander_sight_guard'][0];lo,hi=map(Vector,guard['source_bounds']);center=(lo+hi)/2
box(guard['object'],(center.x,hi.y-.013,center.z),(hi.x-lo.x,.026,hi.z-lo.z),'commander_sight_guard')
for x in (lo.x+.013,hi.x-.013):box('SightGuard_Side'+str(x),(x,center.y,center.z),(.026,hi.y-lo.y,hi.z-lo.z),'commander_sight_guard')
# TOW box follows original bounds; individual ports and retaining bands are clear.
entry=byrole['tow_launcher'][0];lo,hi=map(Vector,entry['source_bounds']);c=(lo+hi)/2
box(entry['object'],tuple(c),tuple(hi-lo),'tow_launcher')
for z in (c.z-.158,c.z+.158):port('TOW_Port'+str(z),(c.x,lo.y-.008,z),.124,(0,-1,0),'tow_port')
for y in (lo.y+.20,hi.y-.15):box('TOW_Band'+str(y),(c.x,y,hi.z+.012),(hi.x-lo.x+.025,.07,.025),'tow_strap',top='rim')
box(source_name('tow_mount'),(.732,-.16,2.48),(.17,.30,.47),'tow_mount')
cylinder('TOW_Hinge',(.68,-.07,2.64),(.84,-.07,2.64),.095,'tow_hinge',color='rim')
entry=byrole['tow_linkage'][0];lo,hi=map(Vector,entry['source_bounds']);c=(lo+hi)/2
cylinder(entry['object'],(lo.x,c.y,c.z),(hi.x,c.y,c.z),.035,'tow_linkage',color='rim')
box(source_name('tow_end_cover'),(.625,-.325,2.46),(.025,.265,.54),'tow_end_cover')
# Five open rear bins, attached to the original shelf; never treated as smoke pods.
for i,part in enumerate(roles['protected_components'],1):
    lo,hi=map(Vector,part['source_bounds']);center=(lo+hi)/2;t=cfg['geometry']['rear_bin_wall_thickness']
    angle=-math.atan2(center.x+.22,center.y-.40);rotation=Matrix.Rotation(angle,3,'Z')
    ids={v for pi in part['source_polygon_indices'] for v in source[turret_name]['faces'][pi]}
    points=[rotation.inverted()@(Vector(source[turret_name]['verts'][v])-center) for v in ids]
    local_lo=Vector(tuple(min(p[k] for p in points) for k in range(3)));local_hi=Vector(tuple(max(p[k] for p in points) for k in range(3)))
    size=local_hi-local_lo
    def bin_box(name,offset,dimensions,top='equipment_top'):
        o=box(name,tuple(center),dimensions,'rear_stowage_bin',top=top)
        offset=Vector(offset)
        for v in o.data.vertices:v.co=center+rotation@(v.co-center+offset)
        return o
    bin_box('RearBin'+str(i)+'_Floor',(0,0,local_lo.z+t/2),(size.x,size.y,t),top='recess')
    for y in (local_lo.y+t/2,local_hi.y-t/2):bin_box('RearBin'+str(i)+'_WallY'+str(y),(0,y,0),(size.x,t,size.z))
    for x in (local_lo.x+t/2,local_hi.x-t/2):bin_box('RearBin'+str(i)+'_WallX'+str(x),(x,0,0),(t,size.y,size.z))
    # Simple upright attachment from the existing shelf to each bin.
    box('RearBin'+str(i)+'_Bracket',(center.x,center.y-.03,2.44),(.045,.05,.24),'rear_bin_bracket',top='rim')
# Retain original barrels/antennae, remove their tiny detailing through role-specific
# simple replacement rather than a size filter shared by all objects.
entry=byrole['main_barrel'][0];lo,hi=map(Vector,entry['source_bounds']);c=(lo+hi)/2
cylinder(entry['object'],(c.x,hi.y,c.z),(c.x,lo.y,c.z),.032,'main_barrel','GUN_ELEVATION')
box('MainGun_Mantlet',(-.22,-.57,2.46),(.23,.38,.25),'gun_mount','GUN_ELEVATION')
port('MainGun_Muzzle',(c.x,lo.y-.002,c.z),.037,(0,-1,0),'muzzle','GUN_ELEVATION')
entry=byrole['coax_machine_gun'][0];lo,hi=map(Vector,entry['source_bounds']);c=(lo+hi)/2
cylinder(entry['object'],(c.x,hi.y,c.z),(c.x,lo.y,c.z),.025,'coax_machine_gun','GUN_ELEVATION')
for entry in byrole['antenna']:
    lo,hi=map(Vector,entry['source_bounds']);c=(lo+hi)/2
    cylinder(entry['object'],(c.x,c.y,lo.z),(c.x,c.y,hi.z),.023,'antenna',color='wall',segments=6)
    cylinder(entry['object']+'_Base',(c.x,c.y,lo.z-.06),(c.x,c.y,lo.z+.035),.055,'antenna_base',color='rim')
# General front-cheek location comes from labeled Bradley reference imagery.
# Fitted source coordinates are an art interpretation, not surveyed A3 mounts.
smoke=cfg['geometry']['smoke_pods']
scene.view_layers[0].update()
shell_obj=bpy.data.objects[turret_name]
smoke_anchors=[]
for side,points in [('R',[(-.99,-.53,2.46),(-.88,-.62,2.46),(-.77,-.69,2.46),(-.66,-.73,2.46)]),
                    ('L',[(.34,-.74,2.46),(.45,-.69,2.46),(.56,-.62,2.46),(.67,-.53,2.46)])]:
    for i,p in enumerate(points):
        hit,location,normal,_=shell_obj.ray_cast(Vector((p[0],-4,p[2])),Vector((0,1,0)))
        assert hit,('Smoke mount missed turret armor',p)
        if normal.y>0:normal=-normal
        axis=(normal*.8+Vector((0,-.3,.5))).normalized()
        a=location+normal*.012;b=a+axis*smoke['length']
        cylinder('Smoke_'+side+str(i),a,b,smoke['radius'],'smoke_launcher',color='side_slope',segments=8)
        port('Smoke_'+side+str(i)+'_Mouth',b+axis*.002,smoke['radius'],axis,'smoke_mouth')
        smoke_anchors.append({'side':side,'index':i,'armor_hit':list(location),'normal':list(normal),'base':list(a),'mouth':list(b)})
assert geometry_hash(hull)==hull_hash
assert all(len(o.data.polygons)>0 for o in created)
sys.path.insert(0,str(ROOT));from face_depth_tone import apply_face_depth_tone
scene.camera=bpy.data.objects['Camera_iso_front'];apply_face_depth_tone(scene)
report={'stage':2,'scope':cfg['scope'],'faces':sum(len(o.data.polygons) for o in scene.objects if o.type=='MESH'),
 'turret_faces':sum(len(o.data.polygons) for o in created),'turret_objects':len(created),
 'hull_geometry_sha256':hull_hash,'hull_geometry_unchanged':True,'source_armor_boundary_preserved':True,
 'restored_required':[source_name('tow_linkage'),source_name('turret_front_upper_cover')],
 'role_registry_consumed':True,'empty_turret_meshes':0,'smoke_tubes':8,'smoke_placement':smoke['placement_status'],
 'palette_status':cfg['palette_status'],'smoke_anchors':smoke_anchors,'created':[{'name':o.name,'role':o['art_role'],'parent':o.parent.name,'faces':len(o.data.polygons)} for o in created]}
# Full sprite materials remain in BLEND. Export uses fixed unlit emission fallback.
for collection in (bpy.data.meshes,bpy.data.materials,bpy.data.images):
    for item in list(collection):
        if item.users==0:collection.remove(item)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'bradley_rimworld.blend'),compress=True)
for m in bpy.data.materials:
    if not m.use_nodes:continue
    for n in m.node_tree.nodes:
        if n.type=='EMISSION':
            for link in list(n.inputs['Color'].links):m.node_tree.links.remove(link)
            n.inputs['Color'].default_value=m.diffuse_color
bpy.ops.export_scene.gltf(filepath=str(OUT/'bradley_rimworld.glb'),export_format='GLB',export_animations=True,export_cameras=False,export_lights=False)
(OUT/'stage2_manifest.json').write_text(json.dumps(report,indent=2)+'\n')
report['source_faces']=223389;report['face_reduction_percent']=round(100*(1-report['faces']/223389),1)
report['objects']={o.name:{'faces':len(o.data.polygons),'parent':o.parent.name,'build_stage':o.get('build_stage',1)} for o in scene.objects if o.type=='MESH'}
(OUT/'manifest.json').write_text(json.dumps(report,indent=2)+'\n')
roles['stage_status']='stage2_turret_geometry_complete_hull_pending'
roles['entries_state_snapshot']='stage1; runtime geometry is recorded separately'
roles['unresolved_required_roles']=[r for r in roles['unresolved_required_roles'] if r['role']!='smoke_launcher']
roles['additional_roles']=[{'role':'smoke_launcher','source_binding':'reference_layout_new_geometry',
    'status':'resolved_general_layout_exact_A3_mount_is_artist_fit','count':8,
    'evidence':smoke['layout_evidence'],'coordinates':'RimWorld/stage2_manifest.json#smoke_anchors'}]
roles['runtime_bindings']=[{'object':o.name,'role':o['art_role'],'parent':o.parent.name} for o in created]
roles['restored_stage1_required']=report['restored_required']
(ROOT/cfg['role_registry']).write_text(json.dumps(roles,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('STAGE2 TURRET BUILT',report['turret_faces'],'turret faces',report['faces'],'total; hull unchanged')
