"""Add an actual West view to saved industrial models, preserving their meshes/colors."""
import bpy,os,sys,json,hashlib,math
import numpy as np
from mathutils import Vector
OUT=os.path.dirname(os.path.abspath(__file__));ROOT=os.path.abspath(os.path.join(OUT,'../..'))
sys.path.insert(0,OUT)
import sprite_common as s
from sprite_common import geometry_hash
from integrate_asset import integrate
ASSETS=[('HD_SmallSteamEngine','SmallSteamEngine','small_steam_engine.blend',True),
 ('HD_LargeSteamEngine','LargeSteamEngine','HD_LargeSteamEngine.blend',True),
 ('HD_IndustrialDieselEngine','IndustrialDieselEngine','HD_IndustrialDieselEngine.blend',True),
 ('HD_EarlyIndustrialGenerator','EarlyIndustrialGenerator','HD_EarlyIndustrialGenerator.blend',True),
 ('HD_EarlyIndustrialLargeMotor','EarlyIndustrialMotor','HD_EarlyIndustrialLargeMotor.blend',True),
 ('HD_HaberBoschHighPressureReactor','HighPressureReactor','HD_HaberBoschHighPressureReactor.blend',True),
 ('HD_StirredTankReactor','StirredTankReactor','HD_StirredTankReactor.blend',True),
 ('HD_BasicPumpJack','BasicPumpJack','HD_BasicPumpJack.blend',True),
 ('HD_DistillationTower','DistillationTower','HD_DistillationTower.blend',True),
 ('HD_MetalPumpJack','MetalPumpJack','HD_MetalPumpJack.blend',False),
 ('HD_RotaryDrill','RotaryDrill','HD_RotaryDrill.blend',False)]

def read_pixels(path):
    im=bpy.data.images.load(path,check_existing=False);im.colorspace_settings.name='Non-Color'
    assert tuple(im.size)==(512,512)
    p=np.empty(512*512*4,np.float32);im.pixels.foreach_get(p);return p.reshape(512,512,4)

def export_one(stem,folder,blend,active):
    path=os.path.join(ROOT,'ArtSource',folder);tex=os.path.join(ROOT,'Textures/Buildings')
    validation=os.path.join(path,'validation.json')
    with open(validation) as f:report=json.load(f)
    bpy.ops.wm.open_mainfile(filepath=os.path.join(path,blend));s.scene=bpy.context.scene
    before=geometry_hash();target=Vector(report['ground_anchor']);scale=report['orthographic_scale']
    old=bpy.data.objects.get('Camera_West')
    if old:bpy.data.objects.remove(old,do_unlink=True)
    data=bpy.data.cameras.new('Camera_West');data.type='ORTHO';data.ortho_scale=scale
    c=bpy.data.objects.new('Camera_West',data);s.scene.collection.objects.link(c)
    e=math.radians(64);c.location=target+10*Vector((-math.cos(e),0,math.sin(e)))
    c.rotation_euler=(target-c.location).to_track_quat('-Z','Y').to_euler()
    meshes=[o for o in s.scene.objects if o.type=='MESH'];bpy.context.view_layer.update()
    q=c.rotation_euler.to_quaternion();right=q@Vector((1,0,0));up=q@Vector((0,1,0))
    span=max(max(abs((o.matrix_world@v.co-target).dot(right)),abs((o.matrix_world@v.co-target).dot(up))) for o in meshes for v in o.data.vertices)
    if (scale/2-span)*512/scale<24+report['outline_pixels']:
        for cam in [o for o in s.scene.objects if o.type=='CAMERA']:bpy.data.objects.remove(cam,do_unlink=True)
        s.export(path,stem,report['footprint'],report['features'],blend_name=blend)
        with open(validation) as f:new=json.load(f)
        report.update(new)
    else:
        s.scene.camera=c
        color=os.path.join(tex,stem+'_west.png');mask=os.path.join(tex,stem+'_westm.png')
        s.scene.render.filepath=color;bpy.ops.render.render(write_still=True)
        red=s.mat('MASK_West_selected',(255,0,0));black=s.mat('MASK_West_fixed',(0,0,0))
        originals={o.name:list(o.data.materials) for o in meshes}
        try:
            for o in meshes:
                for i,m in enumerate(originals[o.name]):o.data.materials[i]=red if m.get('tintable',False) else black
            s.scene.render.filepath=mask;bpy.ops.render.render(write_still=True)
        finally:
            for o in meshes:
                for i,m in enumerate(originals[o.name]):o.data.materials[i]=m
        s.outline_file(color,mask,report['outline_pixels'])
        a,b=read_pixels(color),read_pixels(mask);assert np.max(abs(a[:,:,3]-b[:,:,3]))<.001
        yy,xx=np.where(a[:,:,3]>.5);margin=int(min(xx.min(),yy.min(),511-xx.max(),511-yy.max()));assert margin>=24
        report['cameras']['West']={'downward_degrees':64.,'minimum_margin':margin,'mask_alpha_matches':True}
        images=[read_pixels(os.path.join(tex,stem+'_'+d+'.png')) for d in ['north','east','south','west']]
        im=bpy.data.images.new('Four_direction_review',2048,512,alpha=True);im.colorspace_settings.name='Non-Color'
        im.pixels.foreach_set(np.concatenate(images,axis=1).ravel());im.file_format='PNG'
        im.filepath_raw=os.path.join(path,'preview_four_views.png');im.save()
        s.scene.camera=bpy.data.objects['Camera_North'];bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(path,blend))
        bpy.ops.wm.open_mainfile(filepath=os.path.join(path,blend))
    assert geometry_hash()==before,'Direction export changed meshes or painted colors'
    report.update({'rotation_export_geometry_palette_sha256':before,'rotation_export_preserves_geometry_and_palette':True})
    with open(validation,'w') as f:json.dump(report,f,indent=2)
    if active:integrate(stem,folder)
    readme=os.path.join(path,'README.md')
    with open(readme,encoding='utf8') as f:doc=f.read()
    line='Four-direction export (2026-10-26): North / East / South / West are actual 64-degree orthographic renders of the same saved geometry and colors; West is not mirrored East. See preview_four_views.png and validation.json.'
    if line not in doc:
        with open(readme,'a',encoding='utf8') as f:f.write('\n'+line+'\n')
    print('WEST_COMPLETE '+stem,flush=True)

if __name__=='__main__':
    wanted=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    for entry in ASSETS:
        if not wanted or entry[0] in wanted:export_one(*entry)
