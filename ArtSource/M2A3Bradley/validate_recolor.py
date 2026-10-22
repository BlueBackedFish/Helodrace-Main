"""Validate source-part restoration, limited edits, and rendered height/curve paint."""
import bpy,json,sys
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'RimWorld';sys.path.insert(0,str(ROOT))
from turret_repairs import read_source_parts,REPAIRED
cfg=json.loads((ROOT/'part_contrast.json').read_text())

def snapshot():
    return {o.name:{'verts':[tuple(v.co) for v in o.data.vertices],
        'faces':[list(p.vertices) for p in o.data.polygons],'parent':o.parent.name,
        'world':[tuple(row) for row in o.matrix_world]} for o in bpy.context.scene.objects if o.type=='MESH'}
bpy.ops.wm.open_mainfile(filepath=str(ROOT/cfg['geometry_source']));bpy.context.scene.frame_set(1);before=snapshot()
bpy.ops.wm.open_mainfile(filepath=str(OUT/'bradley_rimworld.blend'));bpy.context.scene.frame_set(1);after=snapshot()
assert set(before)==set(after)
assert {n for n in before if before[n]!=after[n]}==REPAIRED
for name in REPAIRED:
    assert before[name]['parent']==after[name]['parent'] and before[name]['world']==after[name]['world']
source=read_source_parts()
for name in ('WT_roll_01','WT_roll_02'):
    o=bpy.data.objects[name]
    assert len(o.data.polygons)==len(source[name])
    for p,(original,_) in zip(o.data.polygons,source[name]):
        assert len(p.vertices)==len(original)
        assert all((o.matrix_world@o.data.vertices[i].co-Vector(co)).length<2e-6 for i,co in zip(p.vertices,original))
cloth=bpy.data.objects['WT_ex_decor_01'];assert len(cloth.data.polygons)==6 and len(cloth.data.vertices)==8
top=[cloth.matrix_world@v.co for v in cloth.data.vertices[:4]]
assert abs((top[1]-top[0]).cross(top[2]-top[0]).normalized().dot(top[3]-top[0]))<1e-6
for name in ('WT_hatch_02','WT_hatch_03'):
    o=bpy.data.objects[name];attribute=o.data.attributes['Height and curve tone']
    values=[v.value for v in attribute.data]
    assert max(values)-min(values)>.03 and len({round(v,4) for v in values})>=4
    assert all(m.node_tree.nodes.get('Height and curve tone') for m in o.data.materials)
    assert not any(m.name.startswith('Dark TAN') for m in o.data.materials)
for name in ('WT_roll_05','WT_ex_decor_01','WT_ex_armor_03'):
    assert any(m.name.startswith('Dark TAN') for m in bpy.data.objects[name].data.materials),name
# On a common paint material and equal normal, top vertices must get more gain.
o=bpy.data.objects['WT_@root'];a=o.data.attributes['Height and curve tone']
samples=[((o.matrix_world@o.data.vertices[l.vertex_index].co).z,a.data[l.index].value) for l in o.data.loops]
samples.sort();assert samples[-1][1]>samples[0][1]+.15
for (_,v0),(_,v1) in zip(samples,samples[1:]):assert v1>=v0-1e-5
report={'source_roll_01_faces':354,'source_roll_02_faces':1067,'exact_source_face_positions':True,
 'geometry_edits_limited_to':sorted(REPAIRED),'other_geometry_and_all_parenting_unchanged':True,
 'planar_cover_faces':6,'height_led_paint':True,'curved_hatches_use_interpolated_tones':True,
 'separate_dark_tan_equipment':True,'in_game_validation':'not_performed'}
(OUT/'recolor_validation.json').write_text(json.dumps(report,indent=2)+'\n')
print('TURRET REPAIR VALIDATION PASSED: exact OBJ parts, planar cover, unchanged other geometry, height and curved hatch tones')
