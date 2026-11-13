import bpy,os,sys,argparse
from mathutils import Vector
root=os.path.dirname(os.path.abspath(__file__))
parser=argparse.ArgumentParser()
parser.add_argument('--output',default=os.path.abspath(os.path.join(root,'..','..','Textures','Buildings','WallPanels')))
opts=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
out=opts.output
os.makedirs(out,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=os.path.join(root,'Blender','HD_StyrofoamSandwichWall.blend'))
scene=bpy.context.scene
scene.cycles.samples=24
scene.render.resolution_x=256;scene.render.resolution_y=256
scene.render.filepath=os.path.join(out,'HD_SandwichWall_MenuIcon.png')
bpy.ops.render.render(write_still=True)
col=bpy.data.collections.get('Sandwich wall | one-cell module')
for ob in list(col.objects):bpy.data.objects.remove(ob,do_unlink=True)

def cube(name,size,loc,mat):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc)
    o=bpy.context.object;o.name=name;o.dimensions=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    o.data.materials.append(mat)
    mod=o.modifiers.new('Soft cut edges','BEVEL');mod.width=.008;mod.segments=1
    return o

for kind,color in [('Styrofoam',(.91,.91,.88,1)),('Aramid',(.72,.52,.16,1))]:
    mat=bpy.data.materials.new(kind+' panel material');mat.use_nodes=True
    bs=mat.node_tree.nodes.get('Principled BSDF');bs.inputs['Base Color'].default_value=color
    bs.inputs['Roughness'].default_value=.95
    tex=mat.node_tree.nodes.new('ShaderNodeTexNoise');tex.inputs['Scale'].default_value=160
    bump=mat.node_tree.nodes.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.18;bump.inputs['Distance'].default_value=.01
    mat.node_tree.links.new(tex.outputs['Fac'],bump.inputs['Height']);mat.node_tree.links.new(bump.outputs['Normal'],bs.inputs['Normal'])
    panel=cube(kind+' slab',(1,.13,.68),(0,0,.34),mat)
    scene.camera.location=(1.7,-3,2.0);scene.camera.rotation_euler=(Vector((0,0,.34))-scene.camera.location).to_track_quat('-Z','Y').to_euler();scene.camera.data.ortho_scale=1.35
    scene.render.filepath=os.path.join(out,'HD_Panel_'+kind+'.png');bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(panel,do_unlink=True)
print('PANEL_ICONS_COMPLETE')
