"""Run with Blender 5.1 --background --python build_wall_blender.py.
Creates real wall geometry, UV-mapped PNG textures, blend/glb and review renders.
"""
import bpy
import math
import os
import json
from mathutils import Vector

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'Blender')
TEX = os.path.join(ROOT, 'Textures')
os.makedirs(TEX, exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for block in list(bpy.data.materials):
    bpy.data.materials.remove(block)

SIZE = 512

def noise(x, y):
    v = (x * 374761393 + y * 668265263 + 1729) & 0xffffffff
    v = ((v ^ (v >> 13)) * 1274126177) & 0xffffffff
    return ((v ^ (v >> 16)) & 65535) / 65535.0

def image(name, fn, noncolor=False):
    im = bpy.data.images.new(name, width=SIZE, height=SIZE, alpha=True)
    im.colorspace_settings.name = 'Non-Color' if noncolor else 'sRGB'
    pixels = [0.0] * (SIZE * SIZE * 4)
    for y in range(SIZE):
        for x in range(SIZE):
            rgb = fn(x, y)
            idx = (y * SIZE + x) * 4
            pixels[idx:idx+4] = (*rgb, 1.0)
    im.pixels.foreach_set(pixels)
    im.filepath_raw = os.path.join(TEX, name + '.png')
    im.file_format = 'PNG'
    im.save()
    return im

def face_color(x, y):
    # Periodic ribs and a seam at the repeat boundary. No baked lighting.
    u = x / SIZE
    phase = (u * 8) % 1
    groove = phase < .055 or phase > .945
    seam = x < 3 or x >= SIZE-3
    base = .57 if seam else (.70 if groove else .79)
    grain = (noise(x, y)-.5) * .012
    return (base+grain, base+.019+grain, base+.022+grain)

base = image('HD_SandwichWall_Metal_BaseColor', face_color)
rough = image('HD_SandwichWall_Metal_Roughness', lambda x,y: (.67,)*3, True)

def normal_color(x, y):
    # Tangent-space normal; rib geometry also supplies actual silhouette depth.
    p = ((x / SIZE * 8) % 1)
    slope = .42 if .055 < p < .10 else (-.42 if .90 < p < .945 else 0)
    n = Vector((slope, 0, 1)).normalized()
    return tuple(v * .5 + .5 for v in n)

normal = image('HD_SandwichWall_Metal_Normal', normal_color, True)
foam = image('HD_SandwichWall_EPS_BaseColor', lambda x,y: (
    .89 + .065*noise(x//3,y//3),
    .885 + .065*noise(x//3,y//3),
    .865 + .065*noise(x//3,y//3)))

def solid(name, color, metallic=0, roughness=.7):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    bs = mat.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Base Color'].default_value = (*color,1)
    bs.inputs['Metallic'].default_value = metallic
    bs.inputs['Roughness'].default_value = roughness
    return mat

metal = solid('Silver coated aluminium | UV textured', (.79,.809,.812), .18)
nodes, links = metal.node_tree.nodes, metal.node_tree.links
bs = nodes.get('Principled BSDF')
for im, socket in [(base,'Base Color'), (rough,'Roughness')]:
    node = nodes.new('ShaderNodeTexImage')
    node.image = im
    links.new(node.outputs['Color'], bs.inputs[socket])
nn = nodes.new('ShaderNodeTexImage')
nn.image = normal
nm = nodes.new('ShaderNodeNormalMap')
nm.inputs['Strength'].default_value = .25
links.new(nn.outputs['Color'], nm.inputs['Color'])
links.new(nm.outputs['Normal'], bs.inputs['Normal'])
coremat = solid('White expanded polystyrene core', (.93,.925,.905))
node = coremat.node_tree.nodes.new('ShaderNodeTexImage')
node.image = foam
coremat.node_tree.links.new(node.outputs['Color'], coremat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])
trim = solid('Folded metal trim', (.55,.59,.60), .22)
seam = solid('Dark gasket and cut edges', (.22,.25,.26))

collection = bpy.data.collections.new('Sandwich wall | one-cell module')
bpy.context.scene.collection.children.link(collection)

def move_collection(obj):
    for col in list(obj.users_collection):
        col.objects.unlink(obj)
    collection.objects.link(obj)

def box(name, loc, scale, mat, bevel=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(mat)
    move_collection(obj)
    # Explicit UV projection: wide faces use X/Z, cut ends use Y/Z.
    uv = obj.data.uv_layers.active
    for poly in obj.data.polygons:
        n = poly.normal
        for idx in poly.loop_indices:
            co = obj.data.vertices[obj.data.loops[idx].vertex_index].co
            if abs(n.y) > .5:
                u,v = co.x/scale[0]+.5, co.z/scale[2]+.5
            elif abs(n.x) > .5:
                u,v = co.y/scale[1]+.5, co.z/scale[2]+.5
            else:
                u,v = co.x/scale[0]+.5, co.y/scale[1]+.5
            uv.data[idx].uv = (u,v)
    if bevel:
        mod = obj.modifiers.new('Small folded edge bevel', 'BEVEL')
        mod.width = bevel
        mod.segments = 1
    return obj

# Coordinate contract: X = run, Y = thickness, Z = up; 1 Blender unit = one cell.
# Baseline assumed visual wall thickness .24; this module uses .18 = 75%.
W,H,D = 1.0,1.25,.18
box('EPS core | exposed only on cut ends and upper cut', (0,0,H/2), (W,.154,H), coremat)
for side in (-1,1):
    # A continuous corrugated sheet with explicit UVs, not separate floating ribs.
    xs = [-.5]
    for i in range(8):
        xs.extend([-.5+(i+.055)/8, -.5+(i+.10)/8,
                   -.5+(i+.90)/8, -.5+(i+.945)/8])
    xs.append(.5)
    verts=[]
    for x in xs:
        phase=((x+.5)*8)%1
        inset = .004 if phase < .05501 or phase > .94499 else 0
        y=side*(D/2-.005-inset)
        verts.extend([(x,y,0),(x,y,H)])
    faces=[]
    for i in range(len(xs)-1):
        a=2*i
        face=(a,a+2,a+3,a+1)
        faces.append(face if side == -1 else tuple(reversed(face)))
    mesh=bpy.data.meshes.new('Corrugated sheet UV mesh')
    mesh.from_pydata(verts,[],faces)
    mesh.materials.append(metal)
    obj=bpy.data.objects.new('Front aluminium skin' if side==-1 else 'Rear aluminium skin',mesh)
    collection.objects.link(obj)
    uv=mesh.uv_layers.new(name='Wall UV')
    for poly in mesh.polygons:
        for idx in poly.loop_indices:
            co=mesh.vertices[mesh.loops[idx].vertex_index].co
            uv.data[idx].uv=(co.x+.5,co.z/H)
    solidify=obj.modifiers.new('Sheet metal actual thickness','SOLIDIFY')
    solidify.thickness=.006
    solidify.offset=-1
    # Narrow edge flashings leave the white top core readable at game scale.
    for z in (.016,H-.016):
        box('Folded perimeter flashing', (0,side*.083,z), (W,.014,.032),trim,.002)
    for x in (-.492,.492):
        box('End seam flashing', (x,side*.084,H/2), (.016,.012,H-.064),trim,.001)

scene=bpy.context.scene
scene.unit_settings.system='METRIC'
scene['asset']='Styrofoam-core aluminium sandwich wall'
scene['baseline_wall_thickness_assumption']=.24
scene['wall_thickness']=D
scene['thickness_ratio']=.75
scene['status']='Art source and textures; game renderer hookup not included'
scene.render.engine='CYCLES'
scene.cycles.device='CPU'
scene.cycles.samples=24
scene.cycles.use_denoising=True
scene.render.image_settings.file_format='PNG'
scene.render.image_settings.color_mode='RGBA'
scene.render.film_transparent=True
scene.render.resolution_percentage=100
scene.view_settings.view_transform='Standard'
scene.world.color=(.45,.45,.45)

def area(name,loc,power,size):
    bpy.ops.object.light_add(type='AREA',location=loc)
    light=bpy.context.object
    light.name=name
    light.data.energy=power
    light.data.shape='DISK'
    light.data.size=size
    light.rotation_euler=(Vector((0,0,H/2))-light.location).to_track_quat('-Z','Y').to_euler()

area('Large soft key',(-3,-4,6),350,5)
area('Soft fill',(3,2,4),200,4)
bpy.ops.object.camera_add()
cam=bpy.context.object
cam.name='Orthographic asset review camera'
cam.data.type='ORTHO'
scene.camera=cam

def render(name,pos,target,scale,res=768):
    cam.location=pos
    cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.ortho_scale=scale
    scene.render.resolution_x=res
    scene.render.resolution_y=res
    scene.render.filepath=os.path.join(ROOT,name+'.png')
    bpy.ops.render.render(write_still=True)

# Export only the wall, excluding cameras and lights.
bpy.context.view_layer.update()
deps=bpy.context.evaluated_depsgraph_get()
bounds=[]
for obj in collection.objects:
    evaluated=obj.evaluated_get(deps)
    mesh=evaluated.to_mesh()
    bounds.extend([evaluated.matrix_world @ v.co for v in mesh.vertices])
    assert len(mesh.uv_layers)>0, obj.name+' has no UVs'
    assert all(p.area>0 for p in mesh.polygons), obj.name+' has degenerate faces'
    evaluated.to_mesh_clear()
extent=[max(v[i] for v in bounds)-min(v[i] for v in bounds) for i in range(3)]
assert abs(extent[1]-D)<1e-5, 'Wall thickness mismatch: '+str(extent)
manifest={
    'dimensions_xyz': extent,
    'baseline_thickness_assumption': .24,
    'thickness_ratio': extent[1]/.24,
    'objects': len(collection.objects),
    'textures': [{'name': im.name, 'size':list(im.size)} for im in (base,rough,normal,foam)],
    'checks': ['evaluated thickness is 0.18','all wall meshes UV-mapped','no zero-area mesh faces'],
    'game_integration': 'not performed; one straight module, no junction variants'
}
with open(os.path.join(ROOT,'validation.json'),'w',encoding='utf-8') as f:
    json.dump(manifest,f,indent=2)
bpy.ops.object.select_all(action='DESELECT')
for obj in collection.objects:
    obj.select_set(True)
bpy.context.view_layer.objects.active=next(iter(collection.objects))
bpy.ops.export_scene.gltf(filepath=os.path.join(ROOT,'HD_StyrofoamSandwichWall.glb'),use_selection=True,export_format='GLB',export_apply=True)
render('HD_StyrofoamSandwichWall_Isometric',(2.7,-4,2.5),(0,0,H/2),1.8)
render('HD_StyrofoamSandwichWall_Front',(0,-4,H/2),(0,0,H/2),1.48,512)
render('HD_StyrofoamSandwichWall_Top',(0,0,4),(0,0,0),1.15,512)
render('HD_StyrofoamSandwichWall_End',(4,-.35,1.65),(0,0,H/2),1.55,512)
# Save source with textures packed, then restore external PNG paths for portability.
cam.location=(2.7,-4,2.5)
cam.rotation_euler=(Vector((0,0,H/2))-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.ortho_scale=1.8
for im in (base,rough,normal,foam):
    im.pack()
    im.filepath='//Textures/'+os.path.basename(im.filepath)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT,'HD_StyrofoamSandwichWall.blend'))
print('WALL_BUILD_COMPLETE',ROOT, 'wall_objects=',len(collection.objects))
