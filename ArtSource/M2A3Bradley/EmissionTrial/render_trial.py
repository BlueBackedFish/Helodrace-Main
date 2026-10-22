"""Render frozen Emission trial with no recoloring callbacks."""
import bpy,sys,runpy,json,os,tempfile
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parent.parent;OUT=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(OUT/'bradley_emission_trial.blend'))
s=bpy.context.scene;s.frame_set(1)
cfg=json.loads((OUT/'settings.json').read_text(encoding='utf-8-sig'))
s.eevee.taa_render_samples=cfg['samples']
meshes=[o for o in s.objects if o.type=='MESH']
def under(obj,name):
    while obj:
        if obj.name==name:return True
        obj=obj.parent
    return False
views={}
with tempfile.TemporaryDirectory(prefix='.render-',dir=OUT) as staging:
    stage=Path(staging)
    s.camera=bpy.data.objects['Camera_iso_front'];s.render.filepath=str(stage/'iso_front.png')
    bpy.ops.render.render(write_still=True)
    for obj in meshes:obj.hide_render=not under(obj,'TURRET_YAW') or obj.name.startswith('WT_antenna')
    center=Vector((-.2,.45,2.75));camera=bpy.data.objects['Camera_iso_front'];camera.data.ortho_scale=3.5
    s.render.resolution_x=s.render.resolution_y=768
    for name,delta,sun in [('closeup_front',(-5,-8,7),False),('closeup_angle',(-8,-5,8),True)]:
        camera.location=center+Vector(delta);camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler()
        if sun:
            data=bpy.data.lights.new('Invariance stress light','SUN');data.energy=100
            light=bpy.data.objects.new('Invariance stress light',data);s.collection.objects.link(light);light.rotation_euler=(.6,.4,.7)
        s.render.filepath=str(stage/(name+'.png'));bpy.ops.render.render(write_still=True)
        views[name]={'location':list(camera.location),'rotation_euler':list(camera.rotation_euler),'ortho_scale':camera.data.ortho_scale,
            'size':768,'sun_energy':100 if sun else 0,'turret_only':True}
        if sun:bpy.data.objects.remove(light,do_unlink=True)
    (stage/'camera_views.json').write_text(json.dumps(views,indent=2)+'\n')
    runpy.run_path(str(ROOT/'render_vf_east.py'),run_name='__main__',
        init_globals={'SOURCE_BLEND':str(OUT/'bradley_emission_trial.blend'),'OUTPUT_DIR':str(stage/'VF_East')})
    for p in stage.rglob('*'):
        if not p.is_file():continue
        dst=OUT/p.relative_to(stage);dst.parent.mkdir(exist_ok=True,parents=True);os.replace(p,dst)
print('FROZEN EMISSION TRIAL RENDERS PUBLISHED; no PREPARE_RENDER or paint recalculation')
