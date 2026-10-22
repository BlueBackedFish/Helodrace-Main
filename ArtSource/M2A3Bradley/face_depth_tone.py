"""Bake final unlit paint from height, with continuous curved forms.

The public function name is retained for the render callbacks. No camera- or
part-baseline multiplier follows the final color attribute in the shader.
"""
from mathutils import Vector
from collections import defaultdict
from math import cos, radians
from pathlib import Path
import bpy,json
from height_palette import COLOR_ATTRIBUTE,LUMINANCE_ATTRIBUTE,SATURATION_ATTRIBUTE,final_paint
ROOT=Path(__file__).resolve().parent
RECESS_ATTRIBUTE='Recess paint'

def apply_face_depth_tone(scene):
    cfg=json.loads((ROOT/'part_contrast.json').read_text());settings=cfg['height_first_paint']
    registry=json.loads((ROOT/'bradley_roles.json').read_text(encoding='utf-8'))
    roles={e['object']:e['role'] for e in registry['entries']}
    bpy.context.view_layer.update()
    threshold=cos(radians(settings['normal_smoothing_degrees']))
    for o in scene.objects:
        if o.type!='MESH':continue
        me=o.data;role=o.get('art_role',roles.get(o.name,''))
        for name in ('Face depth tone','Height and curve tone','Height saturation'):
            if me.attributes.get(name):me.attributes.remove(me.attributes[name])
        if not me.color_attributes.get(COLOR_ATTRIBUTE):
            me.color_attributes.new(name=COLOR_ATTRIBUTE,type='FLOAT_COLOR',domain='CORNER')
        me.color_attributes.active_color_name=COLOR_ATTRIBUTE
        for name in (LUMINANCE_ATTRIBUTE,SATURATION_ATTRIBUTE):
            if not me.attributes.get(name):me.attributes.new(name,'FLOAT','CORNER')
        colors=me.color_attributes[COLOR_ATTRIBUTE]
        luminance=me.attributes[LUMINANCE_ATTRIBUTE];saturation=me.attributes[SATURATION_ATTRIBUTE]
        adjacent=defaultdict(list);curved=role in settings['smooth_roles']
        if curved:
            for p in me.polygons:
                for vi in p.vertices:
                    adjacent[tuple(round(c,5) for c in me.vertices[vi].co)].append((p.normal.copy(),p.area))
        normal_matrix=o.matrix_world.to_3x3().inverted().transposed()
        recess=me.attributes.get(RECESS_ATTRIBUTE)
        for p in me.polygons:
            mat=me.materials[p.material_index];paint_role=mat.get('paint_role') if mat else None
            for li in p.loop_indices:
                co=me.vertices[me.loops[li].vertex_index].co;normal=p.normal
                if curved:
                    normal=sum((n*a for n,a in adjacent[tuple(round(c,5) for c in co)] if n.dot(p.normal)>=threshold),Vector()).normalized()
                normal=(normal_matrix@normal).normalized()
                color,y,sat=final_paint((o.matrix_world@co).z,normal,paint_role or role,
                    recess.data[p.index].value if recess else 1,settings)
                if not paint_role:
                    color=tuple(mat.diffuse_color[:3]) if mat else (0,0,0)
                colors.data[li].color=(*color,1)
                luminance.data[li].value=y;saturation.data[li].value=sat
        for m in me.materials:
            if not m or not m.use_nodes:continue
            nodes=m.node_tree.nodes;links=m.node_tree.links
            emission=next((n for n in nodes if n.type=='EMISSION'),None)
            if not emission:continue
            if m.get('paint_role'):
                if not nodes.get(COLOR_ATTRIBUTE):
                    a=nodes.new('ShaderNodeVertexColor');a.name=COLOR_ATTRIBUTE;a.layer_name=COLOR_ATTRIBUTE
                    links.new(a.outputs['Color'],emission.inputs['Color'])
            elif 'grille' in m.name.lower():
                ramp=next((n for n in nodes if n.type=='VALTORGB'),None)
                assert ramp,'Missing grille pattern'
                links.new(ramp.outputs['Color'],emission.inputs['Color'])
            else:
                for link in list(emission.inputs['Color'].links):links.remove(link)
                emission.inputs['Color'].default_value=m.diffuse_color
