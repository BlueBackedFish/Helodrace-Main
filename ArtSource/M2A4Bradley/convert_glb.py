"""Convert the WARNO M2A4 GLB to a pipeline OBJ: one 'o' per mesh node, world transforms baked,
Blender Z-up/+X-forward written as OBJ Y-up/+X-forward (pipeline axes forward=x, up=y)."""
import bpy, sys
args = sys.argv[sys.argv.index("--")+1:]
src, dst = args
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
for o in bpy.data.objects:
    o.select_set(o.type == 'MESH')
bpy.ops.wm.obj_export(filepath=dst, export_selected_objects=True, forward_axis='NEGATIVE_Z', up_axis='Y',
                      export_normals=False, export_uv=True, export_materials=True, export_triangulated_mesh=False,
                      apply_modifiers=True, export_object_groups=False, export_material_groups=False, path_mode='STRIP')
print("WROTE", dst)
