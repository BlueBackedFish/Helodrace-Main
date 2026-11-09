"""Render the unchanged local OBJ as exterior shape reference, not final art."""

import bpy
from pathlib import Path
from mathutils import Vector, Matrix

root=Path(__file__).resolve().parents[2]
source=root.parent/'.texture/ref/claymore-mine-m18-m18a1'
out=root/'ArtSource/M18A1/Reference'
out.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.obj_import(filepath=str(source/'source/Clay.obj'),forward_axis='NEGATIVE_Z',up_axis='Y')
objects=[o for o in bpy.context.scene.objects if o.type=='MESH']
for o in objects:
 o.data.transform(o.matrix_world)
 o.matrix_world=Matrix.Identity(4)
points=[o.matrix_world@v.co for o in objects for v in o.data.vertices]
low=Vector([min(p[i] for p in points) for i in range(3)])
high=Vector([max(p[i] for p in points) for i in range(3)])
center=(low+high)*.5
scale=2.5/max(high-low)
material=bpy.data.materials.new('Reference color only')
material.use_nodes=True
nodes=material.node_tree.nodes;nodes.clear()
image=nodes.new('ShaderNodeTexImage');image.image=bpy.data.images.load(str(source/'textures/ClayMore_Base_Color.png'))
emission=nodes.new('ShaderNodeEmission');output=nodes.new('ShaderNodeOutputMaterial')
material.node_tree.links.new(image.outputs['Color'],emission.inputs['Color'])
material.node_tree.links.new(emission.outputs[0],output.inputs['Surface'])
for o in objects:
 for v in o.data.vertices:v.co=(v.co-center)*scale
 o.data.materials.clear();o.data.materials.append(material)
scene=bpy.context.scene
scene.render.engine='CYCLES';scene.cycles.samples=8
scene.render.resolution_x=700;scene.render.resolution_y=700;scene.render.resolution_percentage=100
scene.render.film_transparent=True
scene.view_settings.view_transform='Standard'
bpy.ops.object.camera_add()
camera=bpy.context.object;camera.data.type='ORTHO';camera.data.ortho_scale=2.85;scene.camera=camera
for name,pos in [('front',(0,6,0)),('back',(0,-6,0)),('top',(0,0,6)),('front_above',(0,5,5))]:
 camera.location=pos;camera.rotation_euler=(-camera.location).to_track_quat('-Z','Y').to_euler()
 scene.render.filepath=str(out/(name+'.png'));bpy.ops.render.render(write_still=True)
