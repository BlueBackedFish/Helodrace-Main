"""East-only Vehicle Framework art trial; no changes to the source blend."""
import bpy
import json
import os
import numpy as np
import math
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view

ROOT = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(ROOT, 'VF_East_Test')
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=os.path.join(ROOT, 'm2a3_bradley.blend'))
scene = bpy.context.scene
scene.frame_set(1)
# Game East faces screen-right. The source camera names describe camera position,
# so -X (Camera_west) gives the correct screen direction for this -Y-front model.
scene.camera = bpy.data.objects['Camera_west']
scene.render.resolution_x = scene.render.resolution_y = 1024
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
turret = bpy.data.objects['TURRET_YAW']
gun = bpy.data.objects['GUN_ELEVATION']
meshes = [o for o in scene.objects if o.type == 'MESH']

def under(obj, parent):
    while obj:
        if obj == parent:
            return True
        obj = obj.parent
    return False

groups = {
    'preview_geometry_east': meshes,
    'vehicle_east': [o for o in meshes if not under(o, turret)],
    'turret_east_test': [o for o in meshes if under(o, turret) and not under(o, gun)],
    'barrel_east_test': [o for o in meshes if under(o, gun)],
}
report = {}
for name, objects in groups.items():
    for obj in meshes:
        obj.hide_render = obj not in objects
    scene.render.filepath = os.path.join(OUT, name + '.png')
    bpy.ops.render.render(write_still=True)
    im = bpy.data.images.load(scene.render.filepath, check_existing=False)
    pixels = np.array(im.pixels[:]).reshape(1024, 1024, 4)
    ys, xs = np.where(pixels[:, :, 3] > .01)
    assert len(xs) and min(xs) > 8 and max(xs) < 1015 and min(ys) > 8 and max(ys) < 1015
    assert pixels[0, 0, 3] == 0
    report[name] = {'objects': len(objects), 'alpha_bounds_bottom_origin':
                    [int(min(xs)), int(min(ys)), int(max(xs)), int(max(ys))]}
    bpy.data.images.remove(im)
assert sum(len(groups[k]) for k in ('vehicle_east','turret_east_test','barrel_east_test')) == len(meshes)
assert not any(o.type == 'LIGHT' for o in scene.objects)
p = world_to_camera_view(scene, scene.camera, turret.matrix_world.translation)
anchor=(round(p.x*1024),round(p.y*1024))
# Dedicated orthographic 90-degree turret sprites, centered on the yaw pivot.
# Local -Y (the gun direction) is screen-right, matching game East.
data=bpy.data.cameras.new('VF turret top');data.type='ORTHO';data.ortho_scale=scene.camera.data.ortho_scale
top_camera=bpy.data.objects.new('VF turret top',data);scene.collection.objects.link(top_camera)
top_camera.location=turret.matrix_world.translation+Vector((0,0,16))
top_camera.rotation_euler=(0,0,-math.pi/2)
scene.camera=top_camera
top_groups={'turret_top_east':groups['turret_east_test'],
            'barrel_top_east':groups['barrel_east_test'],
            'turret_top_full_east':groups['turret_east_test']+groups['barrel_east_test']}
for name,objects in top_groups.items():
    for obj in meshes:obj.hide_render=obj not in objects
    scene.render.filepath=os.path.join(OUT,name+'.png')
    bpy.ops.render.render(write_still=True)
    im=bpy.data.images.load(scene.render.filepath,check_existing=False)
    pixels=np.array(im.pixels[:]).reshape(1024,1024,4)
    ys,xs=np.where(pixels[:,:,3]>.01)
    assert min(xs)>8 and max(xs)<1015 and min(ys)>8 and max(ys)<1015
    report[name]={'pivot_pixels':[512,512],'elevation_degrees':90,'objects':len(objects)}
    bpy.data.images.remove(im)
# Assemble the two camera passes as a sprite preview using straight-alpha over.
# This is render-layer compositing; source textures keep their own pivots.
def read_rgba(name):
    im=bpy.data.images.load(os.path.join(OUT,name+'.png'),check_existing=False)
    a=np.array(im.pixels[:],dtype=np.float32).reshape(1024,1024,4)
    # Byte-buffer PNG pixels are sRGB; the float-buffer output below is linear.
    # Decode before alpha-over so save_render does not apply the display curve twice.
    c=a[:,:,:3]
    a[:,:,:3]=np.where(c<=.04045,c/12.92,((c+.055)/1.055)**2.4)
    bpy.data.images.remove(im);return a
body=read_rgba('vehicle_east');top=read_rgba('turret_top_full_east')
overlay=np.zeros_like(top);dx,dy=anchor[0]-512,anchor[1]-512
x0,x1=max(0,dx),min(1024,1024+dx);y0,y1=max(0,dy),min(1024,1024+dy)
overlay[y0:y1,x0:x1]=top[y0-dy:y1-dy,x0-dx:x1-dx]
assert abs(float(overlay[:,:,3].sum()-top[:,:,3].sum()))<.1
a=overlay[:,:,3:4];b=body[:,:,3:4]
alpha=a+b*(1-a)
rgb=np.divide(overlay[:,:,:3]*a+body[:,:,:3]*b*(1-a),alpha,out=np.zeros_like(body[:,:,:3]),where=alpha>0)
result=np.concatenate((rgb*alpha,alpha),axis=2)
im=bpy.data.images.new('VF mixed projection preview',width=1024,height=1024,alpha=True,float_buffer=True)
im.alpha_mode='PREMUL'
im.pixels.foreach_set(result.ravel());im.filepath_raw=os.path.join(OUT,'preview_east.png');im.file_format='PNG'
im.save_render(im.filepath_raw,scene=scene);bpy.data.images.remove(im)
report['settings'] = {'size': [1024,1024], 'front': 'screen-right', 'elevation_degrees': 64,
                      'ortho_scale': scene.camera.data.ortho_scale,
                      'turret_pivot_pixels_top_origin': [round(p.x*1024,2),round((1-p.y)*1024,2)],
                      'turret_top_elevation':90,'top_pivot':[512,512],
                      'note': 'preview_east composites hull 64 degrees and turret 90 degrees at projected yaw pivot; top sprites are pivot-centered, old *_test layers remain same-camera diagnostics.'}
with open(os.path.join(OUT, 'render_report.json'), 'w', encoding='utf-8') as f:
    json.dump(report, f, indent=2)
print('VF EAST TRIAL PASSED: 4 geometry passes, 3 top passes, mixed-projection preview; no cropping, complete mesh groups, no lights')
