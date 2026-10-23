"""Four real camera directions, independent armor, and registered moving parts.

Run with Blender --python-exit-code 1 --python render_textures.py -- Standard|BUSK.
Only the loaded in-memory scene is modified; input .blend files are never saved.
"""
from pathlib import Path
import sys, json, hashlib, math, itertools
import bpy
import numpy as np
from mathutils import Matrix, Vector
from bpy_extras.object_utils import world_to_camera_view

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
from tow_pose_paint import prepare_tow_palette
cfg=json.loads((HERE/'settings.json').read_text())
variant=sys.argv[sys.argv.index('--')+1] if '--' in sys.argv else 'Standard'
tow_only='--tow-only' in sys.argv
spec=cfg['sources'][variant]
source=(HERE/spec['path']).resolve()
assert hashlib.sha256(source.read_bytes()).hexdigest()==spec['sha256']
OUT=HERE/'output/1024'/variant
OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(source))
scene=bpy.context.scene
scene.frame_set(1)
bpy.context.view_layer.update()
source_faces=sum(len(o.data.polygons) for o in scene.objects if o.type=='MESH')
for obj in scene.objects:
    if obj.animation_data:
        obj.animation_data_clear()
assert not any(o.type=='LIGHT' for o in scene.objects)
scene.render.engine='BLENDER_EEVEE'
scene.eevee.taa_render_samples=cfg['samples']
scene.render.resolution_x=scene.render.resolution_y=cfg['master']
scene.render.resolution_percentage=100
scene.render.film_transparent=True
scene.render.image_settings.file_format='PNG'
scene.render.image_settings.color_mode='RGBA'
scene.render.image_settings.color_depth='8'
scene.render.image_settings.compression=15
scene.render.use_file_extension=True

roof=bpy.data.objects[cfg['roof_hatch']['object']]
roof_axis=bpy.data.objects.new('EXPORT_ROOF_HATCH_HINGE',None)
scene.collection.objects.link(roof_axis)
roof_axis.parent=bpy.data.objects['HULL']
roof_axis.matrix_world=Matrix.Translation(Vector(cfg['roof_hatch']['pivot']))
world=roof.matrix_world.copy()
roof.parent=roof_axis
roof.matrix_world=world
ramp=bpy.data.objects[cfg['rear_ramp']['control']]
tow=bpy.data.objects[cfg['tow']['control']]
turret=bpy.data.objects['TURRET_YAW']
gun=bpy.data.objects['GUN_ELEVATION']


def under(obj,parent):
    while obj:
        if obj==parent:return True
        obj=obj.parent
    return False


meshes=[o for o in scene.objects if o.type=='MESH' and o.data.polygons]
era=[o for o in meshes if o.get('removable_tile')]
hull=[o for o in meshes if not under(o,turret) and o not in era]
top=[o for o in meshes if under(o,turret) and o not in era]
roof_parts=[roof]
ramp_parts=[o for o in hull if under(o,ramp)]
gun_parts=[o for o in top if under(o,gun)]
tow_parts=[bpy.data.objects[name] for name in cfg['tow']['moving_objects']]
fixed_hull=[o for o in hull if o not in roof_parts+ramp_parts]
fixed_top=[o for o in top if o not in gun_parts+tow_parts]
assert bpy.data.objects[cfg['tow']['fixed_front_plate']] in fixed_top
assert sum(map(len,[hull,top,era]))==len(meshes)
assert len(era)==(124 if variant=='BUSK' else 0)
assert len(ramp_parts)>=2
paint_settings=json.loads((HERE.parent/'part_contrast.json').read_text())['height_first_paint']
apply_tow_paint,tow_paint_report=prepare_tow_palette(tow,tow_parts,cfg['tow_deployed_paint'],paint_settings,cfg['tow']['deployed_degrees'])
saved_slots={o.name:list(o.data.materials) for o in meshes}
hold=bpy.data.materials.new('Export-only holdout')
hold.use_nodes=True
nodes=hold.node_tree.nodes;nodes.clear()
h=nodes.new('ShaderNodeHoldout');output=nodes.new('ShaderNodeOutputMaterial')
hold.node_tree.links.new(h.outputs[0],output.inputs['Surface'])
current_modes={}
jobs=[]
files={}
catalog={}
if tow_only:
    previous=json.loads((OUT/'render_manifest.json').read_text())
    assert previous['source_sha256']==spec['sha256']
    files=previous['files']
    catalog=previous['directions']


def pose(roof_open=False,ramp_open=False,tow_open=False):
    roof_axis.rotation_euler.x=math.radians(cfg['roof_hatch']['open_degrees']) if roof_open else 0
    ramp.rotation_euler.x=math.radians(cfg['rear_ramp']['open_degrees']) if ramp_open else 0
    tow.rotation_euler.y=math.radians(cfg['tow']['deployed_degrees']) if tow_open else 0
    apply_tow_paint(tow_open)
    bpy.context.view_layer.update()


def screen_bbox(objects):
    coords=[world_to_camera_view(scene,scene.camera,o.matrix_world@Vector(corner))
            for o in objects for corner in o.bound_box]
    if not coords:return None
    return [min(p.x for p in coords)*1024, min(1-p.y for p in coords)*1024,
            max(p.x for p in coords)*1024, max(1-p.y for p in coords)*1024]


def overlap(a,b):
    return a[0]<b[2]+3 and a[2]>b[0]-3 and a[1]<b[3]+3 and a[3]>b[1]-3


def pivot_pixels(obj):
    p=world_to_camera_view(scene,scene.camera,obj.matrix_world.translation)
    return [p.x*1024,(1-p.y)*1024]


def render(relative,visible,occluders=(),allow_empty=False):
    path=OUT/relative
    path.parent.mkdir(parents=True,exist_ok=True)
    visible=set(visible);occluders=set(occluders)-visible
    for obj in meshes:
        mode='color' if obj in visible else 'holdout' if obj in occluders else 'hidden'
        if current_modes.get(obj.name)==mode:continue
        obj.hide_render=mode=='hidden'
        if mode!='hidden':
            for i,mat in enumerate(saved_slots[obj.name]):
                obj.data.materials[i]=mat if mode=='color' else hold
        current_modes[obj.name]=mode
    scene.render.filepath=str(path)
    bpy.ops.render.render(write_still=True)
    im=bpy.data.images.load(str(path),check_existing=False)
    rgba=np.asarray(im.pixels[:],dtype=np.float32).reshape(1024,1024,4)
    ys,xs=np.where(rgba[:,:,3]>.01)
    bounds=[int(xs.min()),int(1023-ys.max()),int(xs.max()+1),int(1024-ys.min())] if len(xs) else None
    assert bounds is not None or allow_empty, relative
    if bounds:
        assert min(bounds[0],bounds[1],1024-bounds[2],1024-bounds[3])>=8, (relative,bounds)
    assert rgba[0,0,3]==0
    digest=hashlib.sha256(np.round(rgba*255).astype(np.uint8).tobytes()).hexdigest()
    bpy.data.images.remove(im)
    files[relative]={'bounds':bounds,'pixel_hash':digest,'visible_objects':[o.name for o in visible],
                     'holdout_objects':[o.name for o in occluders]}
    print('EXPORT_PROGRESS',variant,len(files),relative,flush=True)
    return relative


def rendered_or_default(relative,obj,occluders,default):
    name=render(relative,[obj],occluders,allow_empty=True)
    if files[name]['pixel_hash']==files[default]['pixel_hash']:
        (OUT/name).unlink()
        del files[name]
        return default
    return name


for direction in cfg['directions']:
    pose()
    if tow_only:
        info=catalog[direction]
        data=bpy.data.cameras.new('TOW repaint top '+direction)
        data.type='ORTHO';data.ortho_scale=info['ortho_scale']
        cam=bpy.data.objects.new('TOW repaint top '+direction,data);scene.collection.objects.link(cam)
        cam.location=turret.matrix_world.translation+Vector((0,0,16))
        cam.rotation_euler=(0,0,math.radians(cfg['turret_top_rotation_z_degrees'][direction]))
        scene.camera=cam
        pose(tow_open=True)
        render(info['parts']['tow_deployed'],tow_parts,fixed_top+gun_parts)
        render(info['turret_states']['deployed']['file'],top)
        top_era=[o for o in era if under(o,turret)]
        if top_era:
            render(info['turret_states']['deployed']['installed_reference'],top+top_era)
        print('TOW_REPAINT_COMPLETE',variant,direction,flush=True)
        continue
    scene.camera=bpy.data.objects[cfg['hull_camera'][direction]]
    scale=scene.camera.data.ortho_scale
    p=world_to_camera_view(scene,scene.camera,turret.matrix_world.translation)
    direction_info={'hull_turret_anchor':[p.x*1024,(1-p.y)*1024],
                    'turret_pivot':[512,512],'hull_elevation':64,'turret_elevation':90,
                    'ortho_scale':scale,'front':{'east':'screen-right','west':'screen-left','north':'screen-up','south':'screen-down'}[direction],
                    'hull_states':{},'turret_states':{},'era':{},'parts':{}}
    direction_info['hull_control_pivots']={'roof_hatch':pivot_pixels(roof_axis),'rear_ramp':pivot_pixels(ramp)}
    catalog[direction]=direction_info
    path=lambda folder,name:f'{folder}/{name}_{direction}.png'
    direction_info['parts']['hull_static']=render(path('Hull','hull_static'),fixed_hull)
    for opened in [False,True]:
        state='open' if opened else 'closed'
        pose(roof_open=opened)
        direction_info['parts']['roof_hatch_'+state]=render(path('Parts','roof_hatch_'+state),roof_parts,fixed_hull)
        pose(ramp_open=opened)
        direction_info['parts']['rear_ramp_'+state]=render(path('Parts','rear_ramp_'+state),ramp_parts,fixed_hull)
    hull_era=[o for o in era if not under(o,turret)]
    # Each tile is clipped only by the body in this state. Neighboring ERA is
    # absent, so removing a neighbor never permanently erases this tile's paint.
    for roof_open,ramp_open in itertools.product([False,True],repeat=2):
        state=f'roof_{"open" if roof_open else "closed"}_ramp_{"open" if ramp_open else "closed"}'
        pose(roof_open,ramp_open)
        full=render(path('Hull','hull_'+state),hull)
        info={'file':full,'roof_open':roof_open,'ramp_open':ramp_open,'era_files':[]}
        direction_info['hull_states'][state]=info
        if hull_era:
            info['installed_reference']=render(path('ReferenceInstalled','hull_'+state),hull+hull_era)
    pose()
    changed_bounds=[]
    for parts,kwargs in [(roof_parts,{'roof_open':True}),(ramp_parts,{'ramp_open':True})]:
        pose();closed=screen_bbox(parts)
        pose(**kwargs);opened=screen_bbox(parts)
        changed_bounds.append([min(closed[0],opened[0]),min(closed[1],opened[1]),max(closed[2],opened[2]),max(closed[3],opened[3])])
    pose()
    camera_inverse=scene.camera.matrix_world.inverted()
    hull_era.sort(key=lambda o:(camera_inverse@o.matrix_world.translation).z)
    for obj in hull_era:
        default=render(path('ERA/Hull',obj.name),[obj],hull,allow_empty=True)
        entry={'name':obj.name,'family':obj['visual_family'],'parent':'hull','default_file':default,
               'pivot_pixel':list(world_to_camera_view(scene,scene.camera,obj.matrix_world.translation))[:2],
               'visible':files[default]['bounds'] is not None,'states':{}}
        entry['pivot_pixel']=[entry['pivot_pixel'][0]*1024,(1-entry['pivot_pixel'][1])*1024]
        bbox=screen_bbox([obj]);maybe_changed=any(overlap(bbox,b) for b in changed_bounds)
        for state,info in direction_info['hull_states'].items():
            use=default
            if maybe_changed and (info['roof_open'] or info['ramp_open']):
                pose(info['roof_open'],info['ramp_open'])
                use=rendered_or_default(path('ERA/Hull/'+state,obj.name),obj,hull,default)
            info['era_files'].append(use)
            entry['states'][state]=use
        pose()
        direction_info['era'][obj.name]=entry
    pose()
    data=bpy.data.cameras.new('Export turret top '+direction)
    data.type='ORTHO';data.ortho_scale=scale
    cam=bpy.data.objects.new('Export turret top '+direction,data)
    scene.collection.objects.link(cam)
    cam.location=turret.matrix_world.translation+Vector((0,0,16))
    cam.rotation_euler=(0,0,math.radians(cfg['turret_top_rotation_z_degrees'][direction]))
    scene.camera=cam
    bpy.context.view_layer.update()
    direction_info['turret_control_pivots']={'tow':pivot_pixels(tow),'gun':pivot_pixels(gun)}
    direction_info['parts']['turret_base']=render(path('Turret','turret_base'),fixed_top)
    direction_info['parts']['gun']=render(path('Parts','gun'),gun_parts,fixed_top)
    top_era=[o for o in era if under(o,turret)]
    for opened in [False,True]:
        state='deployed' if opened else 'stowed'
        pose(tow_open=opened)
        layer=render(path('Parts','tow_'+state),tow_parts,fixed_top+gun_parts)
        direction_info['parts']['tow_'+state]=layer
        complete=render(path('Turret','turret_'+state),top)
        info={'file':complete,'tow_open':opened,'era_files':[]}
        direction_info['turret_states'][state]=info
        if top_era:
            info['installed_reference']=render(path('ReferenceInstalled','turret_'+state),top+top_era)
    pose()
    closed=screen_bbox(tow_parts)
    pose(tow_open=True);opened=screen_bbox(tow_parts)
    changed=[min(closed[0],opened[0]),min(closed[1],opened[1]),max(closed[2],opened[2]),max(closed[3],opened[3])]
    pose()
    inverse=scene.camera.matrix_world.inverted()
    top_era.sort(key=lambda o:(inverse@o.matrix_world.translation).z)
    for obj in top_era:
        default=render(path('ERA/Turret',obj.name),[obj],top,allow_empty=True)
        p=world_to_camera_view(scene,scene.camera,obj.matrix_world.translation)
        entry={'name':obj.name,'family':obj['visual_family'],'parent':'turret','default_file':default,
               'pivot_pixel':[p.x*1024,(1-p.y)*1024],'visible':files[default]['bounds'] is not None,'states':{}}
        for state,info in direction_info['turret_states'].items():
            use=default
            if info['tow_open'] and overlap(screen_bbox([obj]),changed):
                pose(tow_open=True)
                use=rendered_or_default(path('ERA/Turret/deployed',obj.name),obj,top,default)
                pose()
            info['era_files'].append(use)
            entry['states'][state]=use
        direction_info['era'][obj.name]=entry
    pose()
    print('DIRECTION_COMPLETE',variant,direction,flush=True)

assert hashlib.sha256(source.read_bytes()).hexdigest()==spec['sha256']
report={'variant':variant,'source':str(source),'source_sha256':spec['sha256'],'source_unchanged':True,
        'source_faces':source_faces,'master':1024,'outline':False,'shadows':False,
        'camera_directions_rendered_independently':True,'empty_occluded_tiles_kept':True,
        'directions':catalog,'files':files,'settings':cfg,
        'tow_deployed_paint':tow_paint_report,
        'BUSK_body_includes_ERA':False,
        'roof_hatch_motion':'New render-only hinge; source geometry and face colors unchanged',
        'rear_door_scope':'large ramp with small personnel door closed',
        'compositing':'body-state image, then listed ERA files far to near; validate against installed references',
        'game_validation_performed':False}
(OUT/'render_manifest.json').write_text(json.dumps(report,indent=2)+'\n')
print('TEXTURE_RENDER_COMPLETE',variant,len(files),'PNGs',flush=True)
