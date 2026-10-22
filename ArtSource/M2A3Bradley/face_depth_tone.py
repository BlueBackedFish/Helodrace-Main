"""Height-led unlit paint, with curved hatch tones and a weak camera correction."""
from mathutils import Vector
import bpy
import collections
import json
from pathlib import Path
ROOT=Path(__file__).resolve().parent
ATTRIBUTE='Face depth tone'
HEIGHT_ATTRIBUTE='Height and curve tone'
RECESS_ATTRIBUTE='Recess paint'
SATURATION_ATTRIBUTE='Height saturation'

def height_gain(z,settings):
    return max(settings['minimum_gain'],min(settings['maximum_gain'],
        1+settings['linear_gain_per_unit']*(z-settings['reference_z'])))

def apply_face_depth_tone(scene):
    cfg=json.loads((ROOT/'part_contrast.json').read_text());settings=cfg['height_paint']
    registry=json.loads((ROOT/'bradley_roles.json').read_text(encoding='utf-8'))
    roles={e['object']:e['role'] for e in registry['entries']}
    bpy.context.view_layer.update()
    toward=scene.camera.matrix_world.to_quaternion()@Vector((0,0,1))
    for o in scene.objects:
        if o.type!='MESH':continue
        me=o.data;groups=collections.defaultdict(list)
        for p in me.polygons:
            key=tuple(round(v,3) for v in p.normal)+(round(p.normal.dot(p.center),3),)
            groups[key].append(p)
        attr=me.attributes.get(ATTRIBUTE) or me.attributes.new(ATTRIBUTE,'FLOAT','FACE')
        for polygons in groups.values():
            area=sum(p.area for p in polygons)
            center=sum((p.center*p.area for p in polygons),Vector())/max(area,1e-12)
            world=o.matrix_world@center
            gain=max(.985,min(1.015,1+settings['camera_gain_per_unit']*toward.dot(world-Vector((0,0,2.1)))))
            for p in polygons:attr.data[p.index].value=gain
        height=me.attributes.get(HEIGHT_ATTRIBUTE) or me.attributes.new(HEIGHT_ATTRIBUTE,'FLOAT','CORNER')
        saturation=me.attributes.get(SATURATION_ATTRIBUTE) or me.attributes.new(SATURATION_ATTRIBUTE,'FLOAT','CORNER')
        height=me.attributes[HEIGHT_ATTRIBUTE]
        curved=roles.get(o.name) in settings['curved_roles']
        smooth=collections.defaultdict(Vector)
        if curved:
            # Weld only for shading: source UV seams keep identical corner tone.
            for p in me.polygons:
                for vi in p.vertices:
                    key=tuple(round(c,5) for c in me.vertices[vi].co)
                    smooth[key]+=p.normal*p.area
        normal_matrix=o.matrix_world.to_3x3().inverted().transposed()
        for loop in me.loops:
            co=me.vertices[loop.vertex_index].co
            gain=height_gain((o.matrix_world@co).z,settings)
            if curved:
                normal=(normal_matrix@smooth[tuple(round(c,5) for c in co)]).normalized()
                gain*=1-settings['curve_normal_strength']*(1-max(0,normal.z))
            height.data[loop.index].value=gain
            sat=cfg['shade_desaturation']
            saturation.data[loop.index].value=max(sat['height_minimum_saturation'],min(1,
                1-sat['height_strength']*max(0,1-gain)/(1-settings['minimum_gain'])))
        for m in me.materials:
            if not m or not m.use_nodes:continue
            nodes=m.node_tree.nodes;links=m.node_tree.links
            emission=next((n for n in nodes if n.type=='EMISSION'),None)
            if not emission:continue
            for name in (ATTRIBUTE,HEIGHT_ATTRIBUTE,RECESS_ATTRIBUTE):
                if nodes.get(name):continue
                a=nodes.new('ShaderNodeAttribute');a.name=name;a.attribute_name=name
                multiply=nodes.new('ShaderNodeMixRGB');multiply.blend_type='MULTIPLY';multiply.inputs[0].default_value=1
                if emission.inputs['Color'].links:links.new(emission.inputs['Color'].links[0].from_socket,multiply.inputs[1])
                else:multiply.inputs[1].default_value=emission.inputs['Color'].default_value
                links.new(a.outputs['Fac'],multiply.inputs[2]);links.new(multiply.outputs[0],emission.inputs['Color'])
            if m.get('height_desaturation') and not nodes.get(SATURATION_ATTRIBUTE):
                a=nodes.new('ShaderNodeAttribute');a.name=SATURATION_ATTRIBUTE;a.attribute_name=SATURATION_ATTRIBUTE
                original=emission.inputs['Color'].links[0].from_socket
                gray=nodes.new('ShaderNodeRGBToBW');links.new(original,gray.inputs[0])
                mix=nodes.new('ShaderNodeMixRGB');links.new(a.outputs['Fac'],mix.inputs[0])
                links.new(gray.outputs[0],mix.inputs[1]);links.new(original,mix.inputs[2]);links.new(mix.outputs[0],emission.inputs['Color'])
