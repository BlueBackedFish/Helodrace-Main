"""Render the repaired hull using the immutable audit cameras."""
import bpy
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent;TRIAL=HERE.parent
bpy.ops.wm.open_mainfile(filepath=str(TRIAL/'bradley_emission_trial.blend'));s=bpy.context.scene;s.frame_set(1)
def under(o):
    while o:
        if o.name=='TURRET_YAW':return True
        o=o.parent
    return False
for o in s.objects:
    if o.type=='MESH':o.hide_render=under(o)
s.render.resolution_x=s.render.resolution_y=1024;s.render.resolution_percentage=100;s.eevee.taa_render_samples=128;s.render.film_transparent=True
cam=bpy.data.objects['Camera_iso_front'];cam.data.type='ORTHO';cam.data.ortho_scale=7.8;s.camera=cam
center=Vector((0,-.15,1.25))
for name,delta in [('front',(7,-9,8)),('rear',(-7,9,8)),('top',(0,0,16))]:
    cam.location=center+Vector(delta);cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
    s.render.filepath=str(HERE/('after_'+name+'.png'));bpy.ops.render.render(write_still=True)
print('HULL REPAIR VIEWS RENDERED')
