"""Small fixed-emission Blender helpers for authored prop models."""
from pathlib import Path
import bpy,math,os,tempfile
from mathutils import Vector

def material(name,rgb):
    linear=lambda c:c/255/12.92 if c/255<=.04045 else ((c/255+.055)/1.055)**2.4
    m=bpy.data.materials.new(name);m.diffuse_color=tuple(linear(c) for c in rgb)+(1,);m.use_nodes=True;m.node_tree.nodes.clear()
    e=m.node_tree.nodes.new('ShaderNodeEmission');e.inputs[0].default_value=m.diffuse_color
    output=m.node_tree.nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(e.outputs[0],output.inputs[0]);return m

def start():
    bpy.ops.wm.read_factory_settings(use_empty=True);scene=bpy.context.scene
    scene.render.engine='CYCLES';scene.cycles.samples=8;scene.cycles.use_denoising=False
    scene.render.film_transparent=True;scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA';scene.render.image_settings.color_depth='8';scene.render.dither_intensity=0
    scene.view_settings.view_transform='Standard';scene.view_settings.look='None';scene.view_settings.exposure=0;scene.view_settings.gamma=1
    bpy.context.preferences.filepaths.save_version=0;return scene

def box(name,p,size,mat,bevel=0,side=None):
    bpy.ops.mesh.primitive_cube_add(size=1,location=p);o=bpy.context.object;o.name=name;o.scale=size;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);o.data.materials.append(mat)
    if side:
        o.data.materials.append(side)
        for f in o.data.polygons:f.material_index=0 if f.normal.z>.5 else 1
    if bevel:
        mod=o.modifiers.new('Small physical edge bevel','BEVEL');mod.width=min(bevel,min(size)*.25);mod.segments=2;bpy.ops.object.modifier_apply(modifier=mod.name)
    return o

def cylinder(name,a,b,r,mat,segments=32):
    a,b=Vector(a),Vector(b);v=b-a;bpy.ops.mesh.primitive_cylinder_add(vertices=segments,radius=r,depth=v.length,location=(a+b)/2)
    o=bpy.context.object;o.name=name;o.rotation_euler=v.to_track_quat('Z','Y').to_euler();o.data.materials.append(mat);return o

def panel(name,points,mat):
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(points,[],[tuple(range(len(points)))]);mesh.update();o=bpy.data.objects.new(name,mesh);bpy.context.scene.collection.objects.link(o);mesh.materials.append(mat);return o

def cable(name,points,r,mat):
    data=bpy.data.curves.new(name,'CURVE');data.dimensions='3D';data.resolution_u=12;data.bevel_depth=r;data.bevel_resolution=3;data.use_fill_caps=True
    spline=data.splines.new('BEZIER');spline.bezier_points.add(len(points)-1)
    for point,co in zip(spline.bezier_points,points):point.co=co;point.handle_left_type=point.handle_right_type='AUTO'
    o=bpy.data.objects.new(name,data);bpy.context.scene.collection.objects.link(o);data.materials.append(mat)
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o;bpy.ops.object.convert(target='MESH');return bpy.context.object

def camera(name,azimuth,elevation,scale,target=(0,0,0)):
    target=Vector(target);a=math.radians(azimuth);e=math.radians(elevation);data=bpy.data.cameras.new(name);data.type='ORTHO';data.ortho_scale=scale
    o=bpy.data.objects.new(name,data);bpy.context.scene.collection.objects.link(o);o.location=target+10*Vector((math.cos(a)*math.cos(e),math.sin(a)*math.cos(e),math.sin(e)));o.rotation_euler=(target-o.location).to_track_quat('-Z','Y').to_euler();return o

def render(path,cam,resolution):
    path=Path(path);path.parent.mkdir(parents=True,exist_ok=True);scene=bpy.context.scene;scene.camera=cam;scene.render.resolution_x=scene.render.resolution_y=resolution;scene.render.resolution_percentage=100
    with tempfile.NamedTemporaryFile(dir=path.parent,suffix='.png',delete=False) as f:temporary=f.name
    scene.render.filepath=temporary;bpy.ops.render.render(write_still=True);os.replace(temporary,path)

def save(path):
    bpy.ops.wm.save_as_mainfile(filepath=str(path),compress=True)
