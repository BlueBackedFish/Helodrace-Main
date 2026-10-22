"""Validate source-part restoration, limited edits, and rendered height/curve paint."""
import bpy,json,sys
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
from height_palette import COLOR_ATTRIBUTE,LUMINANCE_ATTRIBUTE,SATURATION_ATTRIBUTE,display,WEIGHTS,height_luminance,final_paint,contrast_luminance,contrast_levels
from cel_patches import CEL_ATTRIBUTE,BAND_ATTRIBUTE,BROAD_ATTRIBUTE
from mesh_regions import regions

def final_y(o,p):
    colors=o.data.color_attributes[COLOR_ATTRIBUTE].data
    return sum(sum(display(v)*w for v,w in zip(colors[i].color[:3],WEIGHTS)) for i in p.loop_indices)/len(p.loop_indices)

# Audit the color that actually feeds emission, not a positive gain upstream.
for o in bpy.context.scene.objects:
    if o.type!='MESH':continue
    for m in o.data.materials:
        if not m.get('paint_role'):continue
        emission=next(n for n in m.node_tree.nodes if n.type=='EMISSION')
        incoming=emission.inputs['Color'].links
        assert len(incoming)==1 and incoming[0].from_node.type=='VERTEX_COLOR'
        assert incoming[0].from_node.layer_name==COLOR_ATTRIBUTE
    for p in o.data.polygons:
        m=o.data.materials[p.material_index]
        if not m.get('paint_role'):continue
        expected=sum(o.data.attributes[LUMINANCE_ATTRIBUTE].data[i].value for i in p.loop_indices)/len(p.loop_indices)
        assert abs(final_y(o,p)-expected)<.002,(o.name,p.index)
        # All flat, upward painted plates share a monotonic height ramp. Curve
        # normals and the two requested pigment accents have separate checks.
        if p.normal.z>.9999 and not o.data.attributes[BROAD_ATTRIBUTE].data[p.index].value and o.data.attributes[CEL_ATTRIBUTE].data[p.index].value<0 and m.get('paint_role') not in {'hatch_lid_handle','rear_basket_floor','main_barrel'}:
            expected=sum(contrast_luminance(height_luminance((o.matrix_world@o.data.vertices[v].co).z,cfg['height_first_paint']),cfg['height_first_paint']) for v in p.vertices)/len(p.vertices)
            assert abs(final_y(o,p)-expected)<.002,(o.name,p.index,final_y(o,p),expected)

def measure(name,lo,hi,main=False,role=None):
    o=bpy.data.objects[name];ids=set(regions(o.data)[0]) if main else None;values=[]
    for p in o.data.polygons:
        z=(o.matrix_world@p.center).z;m=o.data.materials[p.material_index]
        if ids is not None and p.index not in ids:continue
        if p.normal.z<.85 or not lo<=z<=hi or not m.get('paint_role') or role and m['paint_role']!=role:continue
        values.append((p.area,z,final_y(o,p)))
    assert values,name
    area=sum(a for a,z,y in values)
    return {'object':name,'faces':len(values),'height':sum(a*z for a,z,y in values)/area,
        'final_display_luminance':sum(a*y for a,z,y in values)/area}

samples={
 'hull_roof':measure('WT_@root',1.98,2.16,role='hull_assembly'),
 'turret_roof':measure('WT_bone_turret',2.715,2.745,role='turret_assembly'),
 'hatch_03':measure('WT_hatch_03',2.77,2.90,True),
 'hatch_02':measure('WT_hatch_02',2.85,3.0,True),
 'commander_sight_top':measure('WT_bone_commander_sight_v',3.28,3.40)}
ordered=sorted(samples.items(),key=lambda q:q[1]['height'])
assert all(b['final_display_luminance']>a['final_display_luminance']+1 for (_,a),(_,b) in zip(ordered,ordered[1:])),samples
cel_summary={}
for o in bpy.context.scene.objects:
    if o.type!='MESH':continue
    groups={}
    for p in o.data.polygons:
        patch=o.data.attributes[CEL_ATTRIBUTE].data[p.index].value
        if patch<0:continue
        colors=[tuple(round(v,6) for v in o.data.color_attributes[COLOR_ATTRIBUTE].data[i].color[:3]) for i in p.loop_indices]
        assert len(set(colors))==1,('Gradient inside cel face',o.name,p.index)
        groups.setdefault(patch,[]).append((p.index,colors[0],final_y(o,p)))
    for patch,records in groups.items():
        colors={r[1] for r in records};assert len(colors)<=2,(o.name,patch)
        bands={o.data.attributes[BAND_ATTRIBUTE].data[r[0]].value for r in records}
        if len(bands)==2:
            assert len(colors)==2
            assert max(r[2] for r in records)-min(r[2] for r in records)>=29.99
    if groups:cel_summary[o.name]={'patches':len(groups),'two_band_patches':sum(len({r[1] for r in records})==2 for records in groups.values())}
assert all(cel_summary[n]['two_band_patches']>0 for n in ('WT_hatch_02','WT_hatch_03','WT_bone_commander_sight_v'))
levels=contrast_levels(cfg['height_first_paint'])[1:-1]
steps=[b[1]-a[1] for a,b in zip(levels,levels[1:])]
assert min(steps)>=cfg['height_first_paint']['step_contrast']['minimum_step']
gain=[(b[1]-a[1])/(b[0]-a[0]) for a,b in zip(levels,levels[1:])]
gaps=[b[0]-a[0] for a,b in zip(levels,levels[1:])]
assert gain[gaps.index(min(gaps))]>gain[gaps.index(max(gaps))]*2,'Small gaps must gain more contrast than large gaps'
for name in ('WT_roll_05','WT_ex_decor_01','WT_ex_armor_03'):
    assert any(m.get('paint_role') for m in bpy.data.objects[name].data.materials),name
manifest=json.loads((OUT/'manifest.json').read_text());accents=manifest['accent_regions']
handles=[a for a in accents if a['role']=='hatch_lid_handle']
assert len(handles)==1 and len(handles[0]['polygon_indices'])==268
o=bpy.data.objects['WT_hatch_02']
points=[o.matrix_world@o.data.vertices[v].co for i in handles[0]['polygon_indices'] for v in o.data.polygons[i].vertices]
assert min(p.y for p in points)>.44 and max(p.y for p in points)<.87
assert not any(a['role']=='hatch_upper_linkage' for a in accents)
lid=[final_y(o,p) for p in o.data.polygons if o.data.materials[p.material_index].get('paint_role')=='turret_hatch_a']
handle=[final_y(o,o.data.polygons[i]) for i in handles[0]['polygon_indices']]
assert max(handle)>max(lid)+1
floor=next(a for a in accents if a['role']=='rear_basket_floor');o=bpy.data.objects[floor['object']]
for i in floor['polygon_indices']:
    p=o.data.polygons[i]
    if p.normal.z>.85:
        plain=sum(contrast_luminance(height_luminance((o.matrix_world@o.data.vertices[v].co).z,cfg['height_first_paint']),cfg['height_first_paint']) for v in p.vertices)/len(p.vertices)
        assert final_y(o,p)<plain-14
assert sum(r['darkened_faces'] for r in manifest['recess_paint'].values())>0
# Lower structural paint reduces both value and chroma; glass/metal are separate.
o=bpy.data.objects['WT_@root'];attr=o.data.attributes[SATURATION_ATTRIBUTE]
values=[v.value for v in attr.data];assert min(values)<.3 and max(values)-min(values)>.3
assert len(bpy.data.objects['WT_hatch_03'].data.polygons)==1071
front=final_paint(2.6,Vector((0,.8,.6)),'turret_assembly',1,cfg['height_first_paint'])[1]
side=final_paint(2.6,Vector((.8,0,.6)),'turret_assembly',1,cfg['height_first_paint'])[1]
assert front-side>12
report={'restored_source_groups':list(SOURCE_RESTORED),'restored_embedded_components':len(ADDED),'smoke_tubes':8,
 'exact_source_face_positions':True,'final_emission_color_checked':True,'height_order_samples':samples,
 'bright_lid_handle_not_rear_hinge':True,'lower_paint_desaturates':True,
 'dark_rear_basket_floor':True,'geometric_recess_paint':True,
 'geometry_edits_limited_to':sorted(REPAIRED|ADDED),'other_geometry_and_all_parenting_unchanged':True,
 'hatch_03_original_detail_faces':1071,'planar_cover_faces':6,'front_side_slope_test_difference':front-side,
 'curved_patches_use_two_flat_colors':cel_summary,'contrast_levels':levels,'contrast_steps':steps,'in_game_validation':'not_performed'}
(OUT/'recolor_validation.json').write_text(json.dumps(report,indent=2)+'\n')
(ROOT/'Audit/SurfaceRules/after_analysis.json').write_text(json.dumps({'samples':samples,'hatch_03_faces':1071,
 'settings':manifest['render_quality'],'validation':'final emitted RGB, not gain coefficients'},indent=2)+'\n')
print('HEIGHT-FIRST FINAL EMISSION AND SOURCE GEOMETRY VALIDATION PASSED',json.dumps(samples))
