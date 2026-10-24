"""Render the saved scene's North/East/South views without rebuilding the model."""
import bpy, os, math, json
import numpy as np
from mathutils import Vector
OUT=os.path.dirname(os.path.abspath(__file__))
TEX=os.path.abspath(os.path.join(OUT,'../../Textures/Buildings'))
blend=os.path.join(OUT,'collins_radio_desk.blend')
bpy.ops.wm.open_mainfile(filepath=blend)
scene=bpy.context.scene
scene.render.resolution_x=scene.render.resolution_y=512
scene.render.resolution_percentage=100
scene.render.film_transparent=True
scene.render.image_settings.file_format='PNG'
scene.render.image_settings.color_mode='RGBA'
for name in ['North','East','South']:
    camera=bpy.data.objects['Camera_'+name]
    forward=camera.rotation_euler.to_quaternion()@Vector((0,0,-1))
    assert camera.data.type=='ORTHO' and abs(math.degrees(math.asin(-forward.z))-64)<.001
    scene.camera=camera
    scene.render.filepath=os.path.join(TEX,'HD_CollinsRadioDesk_'+name.lower()+'.png')
    bpy.ops.render.render(write_still=True)
with open(os.path.join(OUT,'build_radio_desk.py'),encoding='utf-8') as f:source=f.read()
exec(compile(source[source.index('bpy.ops.wm.open_mainfile(filepath=blend)'):],__file__,'exec'))
