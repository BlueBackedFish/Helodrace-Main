"""East-only Vehicle Framework art trial; no changes to the source blend."""
import bpy
import json
import os
import numpy as np
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
    'preview_east': meshes,
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
report['settings'] = {'size': [1024,1024], 'front': 'screen-right', 'elevation_degrees': 64,
                      'ortho_scale': scene.camera.data.ortho_scale,
                      'turret_pivot_pixels_top_origin': [round(p.x*1024,2),round((1-p.y)*1024,2)],
                      'note': 'East-only trial; turret and barrel layers share hull canvas, not rotation-centered production turret textures.'}
with open(os.path.join(OUT, 'render_report.json'), 'w', encoding='utf-8') as f:
    json.dump(report, f, indent=2)
print('VF EAST TRIAL PASSED: 4 RGBA outputs, matching canvas, complete disjoint mesh groups, no lights')
