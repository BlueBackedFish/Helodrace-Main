"""Publish HD_Radio; North faces controls, South faces backs, with a real West view.

Default preserves the already-corrected N/E/S PNG bytes. Use --all after rebuilding
the model to publish all four active 256px views. The historical Collins prefix is
not restored and no color mask is installed.
"""
import bpy,os,sys,json,math
import numpy as np
from mathutils import Vector
OUT=os.path.dirname(os.path.abspath(__file__));ROOT=os.path.abspath(os.path.join(OUT,'../..'))
sys.path.insert(0,os.path.join(OUT,'../IndustrialMachines'))
import sprite_common as s
from sprite_common import geometry_hash
blend=os.path.join(OUT,'collins_radio_desk.blend');bpy.ops.wm.open_mainfile(filepath=blend)
scene=bpy.context.scene;before=geometry_hash();tex=os.path.join(ROOT,'Textures/Buildings')
n=bpy.data.objects['Camera_North'];south=bpy.data.objects['Camera_South']
if n.location.y>south.location.y:
    n.name='Old_camera_north';south.name='Camera_North';n.name='Camera_South'
    n.data.name='South_camera_data';south.data.name='North_camera_data'
old=bpy.data.objects.get('Camera_West')
if old:bpy.data.objects.remove(old,do_unlink=True)
east=bpy.data.objects['Camera_East'];target=Vector((0,0,.42))
data=east.data.copy();data.name='Camera_West'
west=bpy.data.objects.new('Camera_West',data);scene.collection.objects.link(west)
west.location=target+Vector((-abs(east.location.x-target.x),0,east.location.z-target.z))
west.rotation_euler=(target-west.location).to_track_quat('-Z','Y').to_euler()
scene.render.resolution_x=scene.render.resolution_y=256;scene.render.resolution_percentage=100
scene.render.film_transparent=True;scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA'
checks={};images=[]
for name in ['North','East','South','West']:
    camera=bpy.data.objects['Camera_'+name];forward=camera.rotation_euler.to_quaternion()@Vector((0,0,-1))
    angle=math.degrees(math.asin(-forward.z));assert abs(angle-64)<.001
    color=os.path.join(tex,'HD_Radio_'+name.lower()+'.png')
    if '--all' in sys.argv or name=='West':
        scene.camera=camera;scene.render.filepath=color;bpy.ops.render.render(write_still=True)
        im=bpy.data.images.load(color,check_existing=False);im.colorspace_settings.name='Non-Color'
        p=np.empty(256*256*4,np.float32);im.pixels.foreach_get(p);p=p.reshape(256,256,4)
        p[:,:,:3]=0;mask=bpy.data.images.new('Temporary_outline_alpha',256,256,alpha=True)
        mask.colorspace_settings.name='Non-Color';mask.pixels.foreach_set(p.ravel());mask.file_format='PNG'
        temporary=os.path.join(OUT,'temporary_outline_mask.png');mask.filepath_raw=temporary;mask.save()
        s.outline_file(color,temporary,6);os.remove(temporary)
    im=bpy.data.images.load(color,check_existing=False);im.colorspace_settings.name='Non-Color'
    assert tuple(im.size)==(256,256)
    p=np.empty(256*256*4,np.float32);im.pixels.foreach_get(p);p=p.reshape(256,256,4)
    yy,xx=np.where(p[:,:,3]>.5);margin=int(min(xx.min(),yy.min(),255-xx.max(),255-yy.max()));assert margin>=12
    checks[name]={'downward_degrees':angle,'margin_px':margin,'camera_location':list(camera.location)};images.append(p)
scene.camera=bpy.data.objects['Camera_North'];bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=blend);bpy.ops.wm.open_mainfile(filepath=blend)
assert geometry_hash()==before
im=bpy.data.images.new('Active_radio_N_E_S_W',1024,256,alpha=True);im.colorspace_settings.name='Non-Color'
im.pixels.foreach_set(np.concatenate(images,axis=1).ravel());im.file_format='PNG'
im.filepath_raw=os.path.join(OUT,'preview_active_four_views.png');im.save()
with open(os.path.join(OUT,'active_validation.json'),'w') as f:
    json.dump({'texture_prefix':'Buildings/HD_Radio','resolution':256,'color_mask':False,
               'north':'operating faces','south':'cabinet backs','west_is_actual_render':True,
               'geometry_and_palette_preserved':True,'geometry_palette_sha256':before,
               'cameras':checks,'in_game_tested':False},f,indent=2)
print('Active HD_Radio: N/S corrected, actual West, geometry/colors preserved',flush=True)
