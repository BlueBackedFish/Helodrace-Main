"""One-time correction of the existing scene, preserving unrelated scene edits."""
import bpy, os, math, json
import numpy as np
from mathutils import Vector
OUT=os.path.dirname(os.path.abspath(__file__))
TEX=os.path.abspath(os.path.join(OUT,'../../Textures/Buildings'))
bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT,'collins_radio_desk.blend'))
scene=bpy.context.scene
if not scene.get('radios_flattened',False):
    for obj in list(scene.objects):
        if obj.type=='MESH' and obj.name.startswith(('Collins_', '32V3_', '75A3_')):
            obj.location.y=-.035+(obj.location.y+.035)*.72
            obj.location.z=.525+(obj.location.z-.525)*.75
            obj.scale.y*=.72
            obj.scale.z*=.75
    scene['radios_flattened']=True
# Share the established render/save/validation sequence without rebuilding geometry.
with open(os.path.join(OUT,'build_radio_desk.py'),encoding='utf-8') as f:
    source=f.read()
exec(compile(source[source.index("scene.render.engine="):],__file__,'exec'))
