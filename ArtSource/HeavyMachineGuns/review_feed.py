"""Render the actual M2 feed path from above and from the front for visual review."""
from pathlib import Path
import bpy
from mathutils import Vector
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'M2HB/model.blend'))
scene=bpy.context.scene;camera=scene.camera
scene.render.resolution_x=scene.render.resolution_y=1024
camera.data.ortho_scale=.62;target=Vector((-.205,-.043,.015))
for name,direction in [('top',(0,0,5)),('front',(0,5,0))]:
    camera.location=target+Vector(direction);camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(OUT/('m2_feed_'+name+'.png'));bpy.ops.render.render(write_still=True)
print('M2_FEED_REVIEW_RENDERED')
