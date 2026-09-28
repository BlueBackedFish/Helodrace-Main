"""Edit the existing tailoring scene in place; safe to rerun without compounding.
Blender --background --python revise_clothing_bench.py
"""
import bpy
import bmesh
import math
import os
import json
import numpy as np
from mathutils import Vector

OUT=os.path.dirname(os.path.abspath(__file__))

def linear(c):
    c/=255
    return c/12.92 if c<=.04045 else ((c+.055)/1.055)**2.4

def flat(name,rgb):
    material=bpy.data.materials.get(name) or bpy.data.materials.new(name)
    material.use_nodes=True
    material.node_tree.nodes.clear()
    emission=material.node_tree.nodes.new('ShaderNodeEmission')
    material.diffuse_color=tuple(linear(c) for c in rgb)+(1,)
    emission.inputs['Color'].default_value=material.diffuse_color
    output=material.node_tree.nodes.new('ShaderNodeOutputMaterial')
    material.node_tree.links.new(emission.outputs[0],output.inputs['Surface'])
    return material

def assign(obj,material):
    obj.data.materials.clear();obj.data.materials.append(material)
    for face in obj.data.polygons:face.material_index=0

def box(name,position,size,material):
    bpy.ops.mesh.primitive_cube_add(size=1,location=position)
    obj=bpy.context.object;obj.name=name;obj.dimensions=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    assign(obj,material)
    return obj

def change_world_vertices(obj,transform):
    inverse=obj.matrix_world.inverted()
    for vertex in obj.data.vertices:vertex.co=inverse@transform(obj.matrix_world@vertex.co)

def revise_scene():
    scene=bpy.context.scene
    if scene.get('sprite_revision_v1'):return
    # Small face-value differences only; fabric and secondary props are flat.
    base_colors={'Cast_gray':(143,148,148),'Worktop_gray':(178,182,181),
                 'Charcoal':(63,69,70),'Dark_cyan':(44,90,101),
                 'Muted_canvas':(174,155,123),'Muted_fabric':(113,130,116)}
    for material in bpy.data.materials:
        for prefix,rgb in base_colors.items():
            if material.name.startswith(prefix+'_'):
                factor=1 if prefix.startswith('Muted') else (1 if material.name.endswith('_top') else .94)
                rgba=tuple(linear(c*factor) for c in rgb)+(1,)
                material.diffuse_color=rgba
                for node in material.node_tree.nodes:
                    if node.type=='EMISSION':node.inputs['Color'].default_value=rgba

    # Reuse the machine's existing bed/C-profile/wheel geometry. Split only the
    # C-profile faces at the upper-housing boundary, assigning actual flat colors.
    machine=bpy.data.objects['05_Sewing_machine']
    assert len(machine.data.vertices)==42
    machine.data.materials.clear()
    for material in [flat('Machine_bed_flat',(76,81,82)),flat('Machine_lower_flat',(99,104,104)),
                     flat('Handwheel_flat',(172,177,176)),flat('Machine_upper_flat',(211,215,212))]:
        machine.data.materials.append(material)
    bm=bmesh.new();bm.from_mesh(machine.data);bm.verts.ensure_lookup_table()
    body_verts=set(list(bm.verts)[8:26])
    body_edges=[edge for edge in bm.edges if all(v in body_verts for v in edge.verts)]
    body_faces=[face for face in bm.faces if all(v in body_verts for v in face.verts)]
    for face in bm.faces:
        face.material_index=1 if face in body_faces else (0 if all(v.index<8 for v in face.verts) else 2)
    bmesh.ops.bisect_plane(bm,geom=list(body_verts)+body_edges+body_faces,
                          plane_co=(0,0,.40),plane_no=(0,0,1),dist=.00001,
                          clear_inner=False,clear_outer=False)
    for face in bm.faces:
        if face.material_index==1 and face.calc_center_median().z>=.399:
            face.material_index=3
    for vertex in bm.verts:vertex.co.z=-.035+(vertex.co.z+.035)*.82
    bm.to_mesh(machine.data);bm.free();machine.data.update()

    # Reduce prop depth. Remove tiny roll-core disks that no longer aid the sprite.
    folded=bpy.data.objects['10_Folded_fabric']
    change_world_vertices(folded,lambda p:Vector((p.x,p.y,.955+(p.z-.955)*.55)))
    roll=bpy.data.objects['09_Fabric_roll']
    bm=bmesh.new();bm.from_mesh(roll.data);bm.verts.ensure_lookup_table()
    assert len(bm.verts)==36
    bmesh.ops.delete(bm,geom=list(bm.verts)[20:],context='VERTS')
    bm.to_mesh(roll.data);bm.free()
    change_world_vertices(roll,lambda p:Vector((p.x,p.y,.954+(p.z-.954)*.65)))
    scissors=bpy.data.objects['11_Large_scissors']
    assign(scissors,flat('Scissors_single_flat',(73,78,78)))
    scissors.location+=Vector((-.18,-.07,0))

    # A shallower cabinet reveals the new hanging fabric in the East view.
    cabinet=bpy.data.objects['03_Storage_cabinet']
    cabinet.scale.y*=.66/.83
    bpy.data.objects['04_Drawer_fronts'].location.y+=.085
    # A gently angled board stays within the original one-tile depth. The cloth
    # straddles it as a single broad draped slab, readable from either side.
    panel=box('12_Front_hanging_panel',(-.15,-.37,.43),(1.04,.035,.66),flat('Panel_flat',(105,112,111)))
    panel.rotation_euler.z=math.radians(12)
    cloth=box('13_Hanging_canvas',(-.15,-.37,.35),(.50,.058,.58),flat('Hanging_canvas_flat',(174,155,123)))
    cloth.rotation_euler.z=math.radians(12)

    # Two generic six-corner inserts, thin and subordinate to the tailoring kit.
    def plate(name,center,angle,rgb):
        w=.30;h=.35;cut=.065
        outline=[(-w/2,-h/2),(w/2,-h/2),(w/2,h/2-cut),
                 (w/2-cut,h/2),(-w/2+cut,h/2),(-w/2,h/2-cut)]
        verts=[(x,y,z) for z in [-.0045,.0045] for x,y in outline]
        faces=[tuple(reversed(range(6))),tuple(range(6,12))]
        faces += [(i,(i+1)%6,(i+1)%6+6,i+6) for i in range(6)]
        data=bpy.data.meshes.new(name);data.from_pydata(verts,[],faces)
        obj=bpy.data.objects.new(name,data);scene.collection.objects.link(obj)
        obj.location=center;obj.rotation_euler.z=math.radians(angle)
        assign(obj,flat(name+'_flat',rgb))
    plate('14_Armor_insert_A',(.27,.12,.979),-10,(83,91,94))
    plate('15_Armor_insert_B',(.51,.19,.991),8,(112,119,120))
    scene['sprite_revision_v1']=True
    scene['revision_notes']='Existing bench retained: split gray machine housing, flatter props, single-color scissors, draped front board, two thin inserts.'

def improve_front_visibility():
    scene=bpy.context.scene
    if scene.get('panel_visibility_v1'):return
    # A slight outward lean puts the hanging hem just past the front edge,
    # visible end-on without changing the 3x1 tabletop or any camera framing.
    panel=bpy.data.objects['12_Front_hanging_panel']
    panel.location=(.10,-.505,.43)
    panel.rotation_euler=(math.radians(-12),0,0)
    cloth=bpy.data.objects['13_Hanging_canvas']
    cloth.location=(.10,-.522,.35)
    cloth.rotation_euler=(math.radians(-12),0,0)
    frame=bpy.data.objects['02_Support_frame']
    bm=bmesh.new();bm.from_mesh(frame.data);bm.verts.ensure_lookup_table()
    assert len(bm.verts)==16
    bmesh.ops.delete(bm,geom=list(bm.verts)[8:],context='VERTS')
    bm.to_mesh(frame.data);bm.free()
    scene['panel_visibility_v1']=True

if __name__=='__main__':
    bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT,'clothing_bench.blend'))
    revise_scene()
    improve_front_visibility()
    import runpy
    runpy.run_path(os.path.join(OUT,'correct_pegboard.py'),run_name='pegboard_library')['correct_pegboard']()
    scene=bpy.context.scene
    target=Vector((0,0,.79));scale=3.65
    for name,azimuth in [('North',90),('South',270),('East',0)]:
        camera=bpy.data.objects['Camera_'+name]
        a=math.radians(azimuth);e=math.radians(64)
        camera.location=target+8*Vector((math.cos(a)*math.cos(e),math.sin(a)*math.cos(e),math.sin(e)))
        camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
        camera.data.type='ORTHO';camera.data.ortho_scale=scale
        camera['downward_degrees']=64;camera['target_center']=list(target)
    # Reuse the existing export/verification section without rebuilding geometry.
    with open(os.path.join(OUT,'build_clothing_bench.py'),encoding='utf-8') as f:source=f.read()
    export=source[source.index('# Rendering and export:'):]
    exec(compile(export,'clothing_bench_export','exec'))
