"""Add/update the dual-paddle key alone in the existing desk scene."""
import bpy, os, math, json
import numpy as np
from mathutils import Vector, Matrix
OUT=os.path.dirname(os.path.abspath(__file__))
TEX=os.path.abspath(os.path.join(OUT,'../../Textures/Buildings'))
bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT,'collins_radio_desk.blend'))
scene=bpy.context.scene
for o in list(scene.objects):
    if o.name.startswith('TelegraphKey_'):bpy.data.objects.remove(o,do_unlink=True)
with open(os.path.join(OUT,'build_radio_desk.py'),encoding='utf-8') as f:source=f.read()
exec(compile(source[source.index('def mat('):source.index('# Square footprint')],__file__,'exec'))
exec(compile(source[source.index('# Twin paddle'):source.index('# Cameras retain')],__file__,'exec'))
assert len([o for o in scene.objects if o.name.startswith('TelegraphKey_paddle')])==2
exec(compile(source[source.index('scene.render.engine='):],__file__,'exec'))
