"""Camera-relative flat tone, shared by every coplanar face in a surface."""
from mathutils import Vector
import bpy
import collections
ATTRIBUTE='Face depth tone'
def apply_face_depth_tone(scene):
    bpy.context.view_layer.update()
    toward=scene.camera.matrix_world.to_quaternion()@Vector((0,0,1))
    for o in scene.objects:
        if o.type!='MESH':continue
        me=o.data;groups=collections.defaultdict(list)
        for p in me.polygons:
            # A plane key prevents triangles of a broad surface acquiring seams.
            key=tuple(round(v,3) for v in p.normal)+(round(p.normal.dot(p.center),3),)
            groups[key].append(p)
        attr=me.attributes.get(ATTRIBUTE) or me.attributes.new(ATTRIBUTE,'FLOAT','FACE')
        for polygons in groups.values():
            area=sum(p.area for p in polygons)
            center=sum((p.center*p.area for p in polygons),Vector())/max(area,1e-12)
            world=o.matrix_world@center
            # Linear-light gain: restrained, flat across the complete plane.
            gain=max(.88,min(1.08,.965+.022*(world.z-1.6)+.018*toward.dot(world-Vector((0,0,1.6)))))
            for p in polygons:attr.data[p.index].value=gain
        for m in me.materials:
            if not m or not m.use_nodes:continue
            nodes=m.node_tree.nodes;links=m.node_tree.links
            if nodes.get(ATTRIBUTE):continue
            emission=next((n for n in nodes if n.type=='EMISSION'),None)
            if not emission:continue
            a=nodes.new('ShaderNodeAttribute');a.name=ATTRIBUTE;a.attribute_name=ATTRIBUTE
            multiply=nodes.new('ShaderNodeMixRGB');multiply.blend_type='MULTIPLY';multiply.inputs[0].default_value=1
            if emission.inputs['Color'].links:links.new(emission.inputs['Color'].links[0].from_socket,multiply.inputs[1])
            else:multiply.inputs[1].default_value=emission.inputs['Color'].default_value
            links.new(a.outputs['Fac'],multiply.inputs[2]);links.new(multiply.outputs[0],emission.inputs['Color'])
