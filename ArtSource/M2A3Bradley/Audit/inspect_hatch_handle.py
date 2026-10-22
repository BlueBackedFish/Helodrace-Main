"""Identify the lid-mounted handle separately from the rear hinge."""
import bpy,sys,json
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parent.parent;OUT=ROOT/'Audit/PaletteMatch';OUT.mkdir(exist_ok=True)
sys.path.insert(0,str(ROOT));from mesh_regions import regions
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'RimWorld/bradley_rimworld.blend'));s=bpy.context.scene;s.frame_set(1)
o=bpy.data.objects['WT_hatch_02'];groups=regions(o.data);report=[]
for q in s.objects:
    if q.type=='MESH':q.hide_render=q!=o
o.data.materials.clear()
for i,indices in enumerate(groups):
    palette=(.7,.55,.3) if i==0 else (1,.15,.04) if i==1 else (.1,.1,.1) if i==2 else (.03,.8,1) if i==3 else (.4,.4,.4)
    m=bpy.data.materials.new('Region '+str(i));m.use_nodes=True;nodes=m.node_tree.nodes;nodes.clear()
    e=nodes.new('ShaderNodeEmission');e.inputs['Color'].default_value=(*palette,1)
    out=nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(e.outputs[0],out.inputs[0]);o.data.materials.append(m)
    # Finish material allocation before assigning polygon slots below.
    coords=[o.matrix_world@o.data.vertices[v].co for pi in indices for v in o.data.polygons[pi].vertices]
    report.append({'id':i,'faces':len(indices),'bounds':[[min(p[k] for p in coords) for k in range(3)],[max(p[k] for p in coords) for k in range(3)]]})
for i,indices in enumerate(groups):
    for pi in indices:o.data.polygons[pi].material_index=i
cam=bpy.data.objects['Camera_iso_front'];s.camera=cam;center=Vector((-.53,.59,2.83));cam.location=center+Vector((-3,-4,6))
cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=1.1
s.render.resolution_x=s.render.resolution_y=768;s.render.resolution_percentage=100;s.eevee.taa_render_samples=16
s.render.filepath=str(OUT/'hatch_regions.png');bpy.ops.render.render(write_still=True)
(OUT/'hatch_regions.json').write_text(json.dumps(report,indent=2)+'\n')
print('HATCH REGION INSPECTION COMPLETE')
