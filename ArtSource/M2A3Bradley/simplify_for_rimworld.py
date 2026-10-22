"""Derive a clean, unlit game-sprite model from the preserved OBJ reference."""
import bpy, bmesh, math, os, json, runpy
import numpy as np
ROOT=os.path.dirname(os.path.abspath(__file__))
OUT=os.path.join(ROOT,'RimWorld');os.makedirs(OUT,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=os.path.join(ROOT,'ReferenceFit','m3a3_bradley_refit.blend'))
scene=bpy.context.scene;scene.frame_set(1)
report={'source_faces':223389,'removed_objects':[],'removed_small_components':0,'thickened_components':0,'objects':{}}
palette={}
def material(rgb):
    key=tuple(round(max(0,min(255,c))) for c in rgb)
    if key in palette:return palette[key]
    m=bpy.data.materials.new('Game TAN '+str(key));m.use_nodes=True
    linear=tuple((c/255/12.92 if c/255<=.04045 else ((c/255+.055)/1.055)**2.4) for c in key)+(1,)
    m.diffuse_color=linear;n=m.node_tree.nodes;n.clear()
    e=n.new('ShaderNodeEmission');e.inputs['Color'].default_value=linear
    out=n.new('ShaderNodeOutputMaterial');m.node_tree.links.new(e.outputs[0],out.inputs[0])
    palette[key]=m;return m
for o in list(scene.objects):
    if o.type!='MESH':continue
    if o.name=='WT_turret_inside':
        report['removed_objects'].append(o.name);bpy.data.objects.remove(o,do_unlink=True);continue
    before=len(o.data.polygons)
    bm=bmesh.new();bm.from_mesh(o.data)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00015)
    pending=set(bm.verts);remove=[]
    while pending:
        seed=pending.pop();component={seed};stack=[seed]
        while stack:
            v=stack.pop()
            for edge in v.link_edges:
                q=edge.other_vert(v)
                if q in pending:pending.remove(q);component.add(q);stack.append(q)
        ext=sorted(max(v.co[k] for v in component)-min(v.co[k] for v in component) for k in range(3))
        if ext[2]<.16 or (ext[1]<.035 and ext[2]<.45):
            remove.extend(component);report['removed_small_components']+=1
        elif ext[2]>3*max(ext[1],.001) and ext[1]<.14:
            # Enlarge cross-sections in principal axes; leave length and endpoints
            # along the main axis unchanged. Applies to antennae and retained rods.
            verts=list(component);points=np.array([tuple(v.co) for v in verts]);center=points.mean(axis=0)
            _,axes=np.linalg.eigh(np.cov((points-center).T))
            local=(points-center)@axes;spans=np.ptp(local,axis=0);changed=False
            for axis in (0,1):
                if .001<spans[axis]<.055:
                    local[:,axis]*=min(4,.055/spans[axis]);changed=True
            if changed:
                for v,point in zip(verts,local@axes.T+center):v.co=point
                report['thickened_components']+=1
    if remove:bmesh.ops.delete(bm,geom=remove,context='VERTS')
    bmesh.ops.dissolve_limit(bm,angle_limit=math.radians(3),use_dissolve_boundaries=False,verts=list(bm.verts),edges=list(bm.edges),delimit={'MATERIAL','NORMAL'})
    bm.to_mesh(o.data);bm.free();o.data.update()
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    if len(o.data.polygons)>250:
        mod=o.modifiers.new('Sprite simplification','DECIMATE');mod.ratio=.32
        bpy.ops.object.modifier_apply(modifier=mod.name)
    old=list(o.data.materials)
    # Face shading is baked as flat colors: broad roofs, intermediate slopes,
    # darker skirts and undersides, with a subtle local height step for hatches.
    colors=[];turret=any(p.name=='TURRET_YAW' for p in [o.parent,o.parent.parent if o.parent else None] if p)
    for p in o.data.polygons:
        name=old[p.material_index].name if p.material_index<len(old) else ''
        z=p.center.z;n=p.normal
        if 'rubber' in name:rgb=(48,47,43)
        elif 'Track metal' in name:rgb=(77,75,66)
        elif 'Optics' in name:rgb=(55,68,68)
        elif 'Headlamp' in name:rgb=(224,220,192)
        elif 'recesses' in name:rgb=(77,71,57)
        elif 'net_f' in name:rgb=(100,96,80)
        else:
            brightness=1 if n.z>.88 else .78 if n.z>.40 else .55 if n.z>-.35 else .36
            if n.z<=.40 and n.x<-.35:brightness+=.06
            brightness+=.045 if turret else 0
            if o.name.startswith('WT_hatch_'):
                brightness=(.79 if n.z>.35 else .49 if n.z>-.35 else .34)+(.045 if turret else 0)
            # Higher deck fixtures retain a small readable brightness difference.
            if n.z>.4:brightness-=.025 if z<(2.80 if turret else 2.10) else 0
            rgb=tuple(c*brightness for c in (221,207,172))
        m=material(rgb)
        if 'net_f' in name:
            if 'Game grille' not in bpy.data.materials:
                grille=m.copy();grille.name='Game grille';nodes=grille.node_tree.nodes;links=grille.node_tree.links
                tex=nodes.new('ShaderNodeTexCoord');tex.object=bpy.data.objects['BRADLEY_ROOT']
                wave=nodes.new('ShaderNodeTexWave');wave.bands_direction='Y';wave.inputs['Scale'].default_value=1.8
                links.new(tex.outputs['Object'],wave.inputs['Vector'])
                ramp=nodes.new('ShaderNodeValToRGB')
                ramp.color_ramp.interpolation='CONSTANT';ramp.color_ramp.elements[1].position=.58
                ramp.color_ramp.elements[0].color=(.09,.085,.063,1);ramp.color_ramp.elements[1].color=(.19,.18,.14,1)
                links.new(wave.outputs['Fac'],ramp.inputs[0]);links.new(ramp.outputs[0],next(n for n in nodes if n.type=='EMISSION').inputs['Color'])
            m=bpy.data.materials['Game grille']
        colors.append(m)
    o.data.materials.clear();slots={}
    for m in colors:
        if m.name not in slots:slots[m.name]=len(slots);o.data.materials.append(m)
    for p,m in zip(o.data.polygons,colors):p.material_index=slots[m.name];p.use_smooth=False
    report['objects'][o.name]={'before':before,'after':len(o.data.polygons),'parent':o.parent.name}
report['faces']=sum(len(o.data.polygons) for o in scene.objects if o.type=='MESH')
assert report['faces']<80000
assert not any(o.type=='LIGHT' for o in scene.objects)
assert bpy.data.objects['WT_hatch_04'].parent.name=='PERSONNEL_DOOR_HINGE'
assert bpy.data.objects['WT_rear_ramp'].parent.name=='REAR_RAMP_HINGE'
report['face_reduction_percent']=round(100*(1-report['faces']/223389),1)
report['style_reference']='Legion Vehicles : Acies Gravitatis / 3786789710 / LGIFV east and turret textures; visual reference only, no pixels copied'
report['ERA']='deferred; no ERA geometry in this output'
report['low_resolution']={'small_component_cutoff_m':.16,'retained_rod_target_width_m':.055,'grille_wave_scale':1.8,'flat_face_levels':[1,.78,.55,.36]}
scene.camera=bpy.data.objects['Camera_iso_front']
for collection in [bpy.data.materials,bpy.data.meshes,bpy.data.images]:
    for item in list(collection):
        if collection==bpy.data.materials:item.use_fake_user=False
        if item.users==0:collection.remove(item)
blend=os.path.join(OUT,'bradley_rimworld.blend')
bpy.ops.wm.save_as_mainfile(filepath=blend,compress=True)
bpy.ops.export_scene.gltf(filepath=os.path.join(OUT,'bradley_rimworld.glb'),export_format='GLB',export_animations=True,export_cameras=False,export_lights=False)
with open(os.path.join(OUT,'manifest.json'),'w') as f:json.dump(report,f,indent=2)
print('SIMPLIFICATION COMPLETE',report['faces'],report['removed_small_components'],flush=True)
