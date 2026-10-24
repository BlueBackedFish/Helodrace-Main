"""Replace only radio assemblies in the saved scene, keeping desk and microphone edits."""
import bpy, os, math, json
import numpy as np
from mathutils import Vector, Matrix
OUT=os.path.dirname(os.path.abspath(__file__))
TEX=os.path.abspath(os.path.join(OUT,'../../Textures/Buildings'))
bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT,'collins_radio_desk.blend'))
scene=bpy.context.scene
for obj in list(scene.objects):
    if obj.type=='MESH' and obj.name.startswith(('Collins_', '32V3_', '75A3_')):
        bpy.data.objects.remove(obj,do_unlink=True)
with open(os.path.join(OUT,'build_radio_desk.py'),encoding='utf-8') as f:source=f.read()
# Shared primitive/material helpers and radio design, without recreating other objects.
exec(compile(source[source.index('def mat('):source.index('# Square footprint')],__file__,'exec'))
exec(compile(source[source.index('# Two separate'):source.index('# Single prominent')],__file__,'exec'))
exec(compile(source[source.index('scene.render.engine='):],__file__,'exec'))
