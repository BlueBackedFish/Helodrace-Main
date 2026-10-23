"""Packed, view-independent square grille texture on the existing faces."""
import bpy,json
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent
UV_NAME='Grille square grid'
PITCH=.12

def apply():
    targets=json.loads((HERE/'targets.json').read_text())
    image=bpy.data.images.load(str(HERE/'grille_square_tile.png'),check_existing=False)
    image.name='Grille fixed square lattice';image.colorspace_settings.name='sRGB';image.pack()
    materials={};groups_report=[]
    for name,ids in targets.items():
        obj=bpy.data.objects[name];me=obj.data
        assert [p.index for p in me.polygons if 'grille' in me.materials[p.material_index].name.lower()]==ids
        layer=me.uv_layers.new(name=UV_NAME)
        # Shared planar frames keep the pattern continuous across source triangles.
        groups=[]
        for i in ids:
            p=me.polygons[i];n=(obj.matrix_world.to_3x3()@p.normal).normalized()
            group=next((g for g in groups if g['normal'].dot(n)>.995),None)
            if group is None:group={'normal':n,'ids':[]};groups.append(group)
            group['ids'].append(i)
        for group in groups:
            n=sum((obj.matrix_world.to_3x3()@me.polygons[i].normal*me.polygons[i].area for i in group['ids']),Vector()).normalized()
            primary=Vector((1,0,0)) if abs(n.x)<.9 else Vector((0,1,0))
            u=(primary-n*primary.dot(n)).normalized();v=n.cross(u).normalized()
            for i in group['ids']:
                p=me.polygons[i]
                for li in p.loop_indices:
                    co=obj.matrix_world@me.vertices[me.loops[li].vertex_index].co
                    layer.data[li].uv=(co.dot(u)/PITCH,co.dot(v)/PITCH)
                m=me.materials[p.material_index];materials[m.name]=m
            groups_report.append({'object':name,'faces':group['ids'],'u':list(u),'v':list(v),'normal':list(n)})
    for m in materials.values():
        nodes=m.node_tree.nodes;nodes.clear()
        uv=nodes.new('ShaderNodeUVMap');uv.uv_map=UV_NAME
        tex=nodes.new('ShaderNodeTexImage');tex.image=image;tex.extension='REPEAT';tex.interpolation='Linear'
        e=nodes.new('ShaderNodeEmission');e.inputs['Strength'].default_value=1
        out=nodes.new('ShaderNodeOutputMaterial')
        m.node_tree.links.new(uv.outputs['UV'],tex.inputs['Vector']);m.node_tree.links.new(tex.outputs['Color'],e.inputs['Color']);m.node_tree.links.new(e.outputs[0],out.inputs['Surface'])
        m['grille_grid']=True;m['grid_pitch']=PITCH;m['grid_uv']=UV_NAME
    return {'targets':targets,'planar_groups':groups_report,'pitch':PITCH,'bar_fraction':.30,
            'tile':'GrilleGrid/grille_square_tile.png','packed':True,'export':'KHR_materials_unlit baseColorTexture',
            'original_geometry_and_diffuse_colors_preserved':True}

def prepare_export(material):
    if not material.get('grille_grid'):return False
    nodes=material.node_tree.nodes
    image=next(n for n in nodes if n.type=='TEX_IMAGE')
    output=next(n for n in nodes if n.type=='OUTPUT_MATERIAL')
    material.node_tree.links.new(image.outputs['Color'],output.inputs['Surface'])
    return True
