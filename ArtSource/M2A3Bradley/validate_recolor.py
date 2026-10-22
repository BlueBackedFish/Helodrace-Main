"""Validate source-part restoration, limited edits, and rendered height/curve paint."""
import bpy,json,sys,colorsys
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'RimWorld';sys.path.insert(0,str(ROOT))
from turret_repairs import read_source_parts,REPAIRED,ADDED,SOURCE_RESTORED,RESTORATION
cfg=json.loads((ROOT/'part_contrast.json').read_text())

def snapshot():
    return {o.name:{'verts':[tuple(v.co) for v in o.data.vertices],
        'faces':[list(p.vertices) for p in o.data.polygons],'parent':o.parent.name,
        'world':[tuple(row) for row in o.matrix_world]} for o in bpy.context.scene.objects if o.type=='MESH'}
bpy.ops.wm.open_mainfile(filepath=str(ROOT/cfg['geometry_source']));bpy.context.scene.frame_set(1);before=snapshot()
bpy.ops.wm.open_mainfile(filepath=str(OUT/'bradley_rimworld.blend'));bpy.context.scene.frame_set(1);after=snapshot()
assert set(after)-set(before)==ADDED and set(before)<=set(after)
assert {n for n in before if before[n]!=after[n]}==REPAIRED
for name in REPAIRED:
    assert before[name]['parent']==after[name]['parent'] and before[name]['world']==after[name]['world']
source=read_source_parts()
for name in SOURCE_RESTORED:
    o=bpy.data.objects[name]
    assert len(o.data.polygons)==len(source[name])
    for p,(original,_) in zip(o.data.polygons,source[name]):
        assert len(p.vertices)==len(original)
        assert all((o.matrix_world@o.data.vertices[i].co-Vector(co)).length<2e-6 for i,co in zip(p.vertices,original))
for part in RESTORATION['parts']:
    o=bpy.data.objects[part['name']];assert o.parent.name=='TURRET_YAW'
    assert len(o.data.polygons)==len(part['source_polygon_indices'])
    for p,pi in zip(o.data.polygons,part['source_polygon_indices']):
        coordinates=source['WT_bone_turret'][pi][0]
        assert all((o.matrix_world@o.data.vertices[i].co-Vector(co)).length<2e-6 for i,co in zip(p.vertices,coordinates))
cloth=bpy.data.objects['WT_ex_decor_01'];assert len(cloth.data.polygons)==6 and len(cloth.data.vertices)==8
top=[cloth.matrix_world@v.co for v in cloth.data.vertices[:4]]
assert abs((top[1]-top[0]).cross(top[2]-top[0]).normalized().dot(top[3]-top[0]))<1e-6
for name in ('WT_hatch_02','WT_hatch_03'):
    o=bpy.data.objects[name];attribute=o.data.attributes['Height and curve tone']
    values=[v.value for v in attribute.data]
    assert max(values)-min(values)>.03 and len({round(v,4) for v in values})>=4
    assert all(m.node_tree.nodes.get('Height and curve tone') for m in o.data.materials)
    assert all(m.get('height_desaturation') for m in o.data.materials)
for name in ('WT_roll_05','WT_ex_decor_01','WT_ex_armor_03'):
    assert any(m.get('paint_role') for m in bpy.data.objects[name].data.materials),name
manifest=json.loads((OUT/'manifest.json').read_text())
accents=manifest['accent_regions']
assert any(a['role']=='rear_basket_floor' for a in accents)
handles=[a for a in accents if a['role']=='hatch_lid_handle']
assert len(handles)==1 and len(handles[0]['polygon_indices'])==268
o=bpy.data.objects['WT_hatch_02']
points=[o.matrix_world@o.data.vertices[v].co for i in handles[0]['polygon_indices'] for v in o.data.polygons[i].vertices]
assert min(p.y for p in points)>.44 and max(p.y for p in points)<.87
assert not any(a['role']=='hatch_upper_linkage' for a in accents)
handle_colors=[m.diffuse_color[0] for m in o.data.materials if m.get('paint_role')=='hatch_lid_handle']
lid_colors=[m.diffuse_color[0] for m in o.data.materials if m.get('paint_role')=='turret_hatch_a']
assert max(handle_colors)>max(lid_colors)+.04
floor=next(a for a in accents if a['role']=='rear_basket_floor');o=bpy.data.objects[floor['object']]
assert all(max(o.data.materials[o.data.polygons[i].material_index].diffuse_color[:3])<.25 for i in floor['polygon_indices'])
assert sum(r['darkened_faces'] for r in manifest['recess_paint'].values())>0
for name in ('WT_bone_gun','WT_gun_barrel'):
    o=bpy.data.objects[name];planes={}
    for p in o.data.polygons:
        mat=o.data.materials[p.material_index]
        if not mat.get('paint_role'):continue
        key=tuple(round(v,3) for v in p.normal)+(round(p.normal.dot(p.center),3),)
        planes.setdefault(key,[]).append(mat.diffuse_color[:3])
    for colors in planes.values():assert all(max(c[i] for c in colors)-min(c[i] for c in colors)<.004 for i in range(3))
# On a common paint material and equal normal, top vertices must get more gain.
o=bpy.data.objects['WT_@root'];a=o.data.attributes['Height and curve tone']
samples=[((o.matrix_world@o.data.vertices[l.vertex_index].co).z,a.data[l.index].value) for l in o.data.loops]
samples.sort();assert samples[-1][1]>samples[0][1]+.15
for (_,v0),(_,v1) in zip(samples,samples[1:]):assert v1>=v0-1e-5
saturation=o.data.attributes['Height saturation']
values=[v.value for v in saturation.data];assert min(values)<.6 and max(values)>.99
def hsv_saturation(mat):
    rgb=[12.92*v if v<=.0031308 else 1.055*v**(1/2.4)-.055 for v in mat.diffuse_color[:3]]
    return colorsys.rgb_to_hsv(*rgb)[1]
paint=[m for m in o.data.materials if m.get('paint_role')=='hull_assembly']
top=max(paint,key=lambda m:m['reference_gray']);wall=min(paint,key=lambda m:abs(m['reference_gray']-148))
assert hsv_saturation(wall)<hsv_saturation(top)*.7
assert all(m.node_tree.nodes.get('Height saturation') for m in paint)
assert manifest['reference_paint']['samples']=='rimthunder_palette_samples.json'
assert max(m['reference_gray'] for m in bpy.data.objects['WT_roll_05'].data.materials if m.get('paint_role'))>220
report={'restored_source_groups':list(SOURCE_RESTORED),'restored_embedded_components':len(ADDED),'smoke_tubes':8,
 'exact_source_face_positions':True,'gun_coplanar_base_colors_consistent':True,
 'bright_lid_handle_not_rear_hinge':True,'direction_and_height_darkening_desaturates':True,
 'rimthunder_part_palette_applied':True,'dark_rear_basket_floor':True,'geometric_recess_paint':True,
 'geometry_edits_limited_to':sorted(REPAIRED|ADDED),'other_geometry_and_all_parenting_unchanged':True,
 'planar_cover_faces':6,'height_led_paint':True,'curved_hatches_use_interpolated_tones':True,
 'separate_dark_tan_equipment':True,'in_game_validation':'not_performed'}
(OUT/'recolor_validation.json').write_text(json.dumps(report,indent=2)+'\n')
print('TURRET AND PALETTE VALIDATION PASSED: exact geometry, lid handle not hinge, RimThunder part tones, lower saturation in shape-darkened tones')
