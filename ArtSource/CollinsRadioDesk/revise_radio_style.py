"""Update radio and microphone details, remove note, preserve desk and cameras."""
import bpy, os, math, json
import numpy as np
from mathutils import Vector, Matrix
OUT=os.path.dirname(os.path.abspath(__file__))
TEX=os.path.abspath(os.path.join(OUT,'../../Textures/Buildings'))
bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT,'collins_radio_desk.blend'))
scene=bpy.context.scene
for obj in list(scene.objects):
    if obj.type=='MESH' and obj.name.startswith(('Collins_', '32V3_', '75A3_', 'Microphone_', 'Operating_card', 'Card_mark')):
        bpy.data.objects.remove(obj,do_unlink=True)
with open(os.path.join(OUT,'build_radio_desk.py'),encoding='utf-8') as f:source=f.read()
# Shared primitive/material helpers and radio design, without recreating other objects.
exec(compile(source[source.index('def mat('):source.index('# Square footprint')],__file__,'exec'))
exec(compile(source[source.index('# Two separate'):source.index('# Single prominent')],__file__,'exec'))
exec(compile(source[source.index('# Single prominent'):source.index('# Cameras retain')],__file__,'exec'))
# Keep the wood's top RGB and reduce only the face shading difference.
for obj in scene.objects:
    if obj.type=='MESH' and (obj.name.startswith('Desk_') or obj.name in ('Front_apron','Rear_apron')):
        obj.data.materials.clear()
        for material in wood:obj.data.materials.append(material)
        for face in obj.data.polygons:
            face.material_index=0 if face.normal.z>.5 else (1 if abs(face.normal.y)>.5 else 2)
assert not any(o.name.startswith(('Operating_card','Card_mark')) for o in scene.objects)
exec(compile(source[source.index('scene.render.engine='):],__file__,'exec'))
