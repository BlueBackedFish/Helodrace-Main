"""Match the TelegraphTable edge/leg shade relationship without changing devices."""
import bpy, os, math, json
import numpy as np
from mathutils import Vector, Matrix
OUT=os.path.dirname(os.path.abspath(__file__))
TEX=os.path.abspath(os.path.join(OUT,'../../Textures/Buildings'))
bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT,'collins_radio_desk.blend'))
scene=bpy.context.scene
def is_desk(o):return o.name.startswith('Desk_') or o.name in ('Front_apron','Rear_apron')
def snapshot():
    result={}
    for o in scene.objects:
        item={'matrix':[list(row) for row in o.matrix_world]}
        if o.type=='MESH':
            item['verts']=[list(v.co) for v in o.data.vertices]
            item['faces']=[list(f.vertices) for f in o.data.polygons]
            if not is_desk(o):
                item['colors']=[list(o.data.materials[f.material_index].node_tree.nodes.get('Emission').inputs[0].default_value) for f in o.data.polygons]
        elif o.type=='CAMERA':item['camera']=[o.data.type,o.data.ortho_scale]
        result[o.name]=item
    return result
before=snapshot()
with open(os.path.join(OUT,'build_radio_desk.py'),encoding='utf-8') as f:source=f.read()
exec(compile(source[source.index('def mat('):source.index('# Square footprint')],__file__,'exec'))
for o in scene.objects:
    if o.type=='MESH' and is_desk(o):
        o.data.materials.clear()
        for material in wood:o.data.materials.append(material)
        for face in o.data.polygons:
            face.material_index=0 if face.normal.z>.5 else (1 if abs(face.normal.y)>.5 else 2)
assert snapshot()==before,'Unrelated geometry, device colors or camera changed'
with open(os.path.join(OUT,'desk_color_validation.json'),'w') as f:
    json.dump({'reference':'Textures/Buildings/HD_TelegraphTable_south.png',
               'reference_top_rgb':[255]*3,'reference_edge_and_leg_rgb':[217]*3,
               'wood_top_rgb':[133,97,67],'wood_edge_and_leg_rgb':[113,83,57],
               'all_geometry_cameras_and_device_colors_preserved':True},f,indent=2)
exec(compile(source[source.index('scene.render.engine='):],__file__,'exec'))
