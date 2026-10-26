from pathlib import Path
import bpy,json,math
from mathutils import Vector
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';results={}
source=ROOT.parent/'M8AGS/output/WeaponLayers/vehicle.blend'
bpy.ops.wm.open_mainfile(filepath=str(source))
source_objects=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.name.startswith('g0044')]
meta=json.loads((OUT/'M2HB/metadata.json').read_text());axis=meta['axis_x']
source_vertices=set();source_uv={};source_faces=0
for o in source_objects:
    source_faces+=len(o.data.polygons)
    for p in o.data.polygons:
        for i in p.vertices:
            v=o.matrix_world@o.data.vertices[i].co;source_vertices.add(tuple(round(c,5) for c in (-(v.x-axis),-v.y,v.z-2.82)))
    for uv in o.data.uv_layers:source_uv.setdefault(uv.name,[]).extend(tuple(round(c,6) for c in loop.uv) for loop in uv.data)
for kind in ['M2HB','M1917']:
    bpy.ops.wm.open_mainfile(filepath=str(OUT/kind/'model.blend'));scene=bpy.context.scene
    assert scene.camera.data.type=='ORTHO' and (scene.camera.rotation_euler.to_quaternion()@Vector((0,0,-1))-Vector((0,0,-1))).length<.00001
    assert scene.render.film_transparent and not any(o.type=='LIGHT' for o in scene.objects)
    meshes=[o for o in scene.objects if o.type=='MESH'];assert all('layer' in o for o in meshes)
    for o in meshes:
        for material in o.data.materials:
            assert material and material.use_nodes and any(n.type=='EMISSION' for n in material.node_tree.nodes),o.name
    result=dict(camera='90 degree vertical orthographic',transparent=True,lights=0,materials='fixed RGB emission',meshes=len(meshes),faces=sum(len(o.data.polygons) for o in meshes))
    if kind=='M2HB':
        copies=[o for o in meshes if o.name.startswith('g0044')]
        vertices={tuple(round(c,5) for c in o.matrix_world@v.co) for o in copies for v in o.data.vertices}
        assert vertices==source_vertices,'Source exterior mesh changed'
        assert sum(len(o.data.polygons) for o in copies)==source_faces,'Source faces lost during barrel split'
        uv={}
        for o in copies:
            for layer in o.data.uv_layers:uv.setdefault(layer.name,[]).extend(tuple(round(c,6) for c in loop.uv) for loop in layer.data)
        assert {k:sorted(v) for k,v in uv.items()}=={k:sorted(v) for k,v in source_uv.items()},'Source UV loops changed'
        result.update(original_exterior_vertices_preserved=True,original_faces=source_faces,original_uv_loops_preserved=True)
    else:
        bad=[(o.name,p.index,p.area) for o in meshes for p in o.data.polygons if p.area<=1e-10]
        assert not bad,('New degenerate face',bad)
        result['degenerate_faces']=0
    results[kind]=result
(OUT/'model_validation.json').write_text(json.dumps(dict(passed=True,models=results),indent=2)+'\n')
print('HMG_MODEL_VALIDATION_PASS: vertical cameras, emission, source M2 vertices/faces/UV retained, no new M1917 degenerate faces',flush=True)
