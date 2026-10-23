"""Bake an alternate launcher palette once in its deployed pose, independent of view."""
import hashlib
import math
import sys
from pathlib import Path
import bpy
from mathutils import Vector

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from height_palette import final_paint


def prepare_tow_palette(control,objects,settings,paint,deployed_degrees):
    old_rotation=control.rotation_euler.copy()
    control.rotation_euler.y=math.radians(deployed_degrees)
    bpy.context.view_layer.update()
    palettes={}
    records=[]
    mats={}
    for obj in objects:
        assert obj.data.users==1,('Shared launcher mesh',obj.name)
        original=[p.material_index for p in obj.data.polygons]
        rgb_before=[tuple(obj.data.materials[i].diffuse_color) for i in original]
        source_names=[obj.data.materials[i].name for i in original]
        deployed=[]
        values=[]
        for p,name,index in zip(obj.data.polygons,source_names,original):
            n=(obj.matrix_world.to_3x3()@p.normal).normalized()
            if name in settings['preserve_materials']:
                deployed.append(index);values.append(None);continue
            # The source slot is shared by small curved tube trim and broad
            # flat cover triangles. Keep trim uniform; repaint the broad cover.
            if name==settings['ring_material'] and p.area<=settings['ring_face_max_area']:
                y=settings['ring_luminance']
            else:
                y=(settings['top_luminance'] if n.z>.7 else settings['slope_luminance'] if n.z>.3
                   else settings['bottom_luminance'] if n.z<-.3 else settings['wall_luminance'])
            if y not in mats:
                rgb,_,_=final_paint(Vector((0,0,1)),'hull_assembly',1,paint,final_value=y)
                mat=bpy.data.materials.new(f'TOW deployed fixed TAN Y{y}')
                mat.use_nodes=True;mat.diffuse_color=(*rgb,1)
                nodes=mat.node_tree.nodes;nodes.clear()
                e=nodes.new('ShaderNodeEmission');e.inputs['Color'].default_value=(*rgb,1)
                e.inputs['Strength'].default_value=1
                out=nodes.new('ShaderNodeOutputMaterial');mat.node_tree.links.new(e.outputs[0],out.inputs['Surface'])
                mats[y]=mat
            mat=mats[y]
            slot=next((i for i,m in enumerate(obj.data.materials) if m==mat),None)
            if slot is None:
                slot=len(obj.data.materials);obj.data.materials.append(mat)
            deployed.append(slot);values.append(y)
        assert any(a!=b for a,b in zip(original,deployed))
        palettes[obj.name]=(original,deployed)
        top_values=[y for p,y in zip(obj.data.polygons,values)
                    if (obj.matrix_world.to_3x3()@p.normal).normalized().z>.7 and y is not None]
        assert settings['top_luminance'] in top_values
        records.append({'object':obj.name,'faces':len(original),'repainted_faces':sum(a!=b for a,b in zip(original,deployed)),
                        'preserved_recess_faces':sum(y is None for y in values),
                        'stowed_face_color_sha256':hashlib.sha256(repr(rgb_before).encode()).hexdigest(),
                        'deployed_palette_luminances':sorted({y for y in values if y is not None}),
                        'view_independent_emission':True})
    control.rotation_euler=old_rotation
    bpy.context.view_layer.update()
    current=None
    def apply(opened):
        nonlocal current
        if current==opened:return
        for obj in objects:
            indices=palettes[obj.name][int(opened)]
            for p,i in zip(obj.data.polygons,indices):p.material_index=i
        current=opened
    apply(False)
    return apply,records
