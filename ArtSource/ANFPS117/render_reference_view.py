"""Low-angle shape review only; preserves the saved 64-degree game scene."""
from pathlib import Path
import math
import bpy
from mathutils import Vector

OUT = Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(OUT/'HD_ANFPS117.blend'))
scene = bpy.context.scene
target = Vector((0,0,2.45))
azimuth, elevation = math.radians(-65), math.radians(20)
data = bpy.data.cameras.new('Reference_review_20deg')
data.type = 'ORTHO'
data.ortho_scale = 6.0
camera = bpy.data.objects.new('Reference_review_20deg',data)
scene.collection.objects.link(camera)
camera.location = target+12*Vector((math.cos(azimuth)*math.cos(elevation),
                                   math.sin(azimuth)*math.cos(elevation),math.sin(elevation)))
camera.rotation_euler = (target-camera.location).to_track_quat('-Z','Y').to_euler()
scene.camera = camera
scene.render.resolution_x = scene.render.resolution_y = 768
scene.render.resolution_percentage = 100
scene.render.filepath = str(OUT/'reference_view.png')
bpy.ops.render.render(write_still=True)
print('REFERENCE_REVIEW_PASS: 20-degree inspection view; source scene unchanged')
