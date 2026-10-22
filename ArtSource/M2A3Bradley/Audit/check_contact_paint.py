"""Validate local support contrast, independent siblings and single-color cylinder sides."""
import bpy,sys,json
from pathlib import Path
ROOT=Path(__file__).resolve().parent.parent;OUT=ROOT/'Audit/ContactPaint';sys.path.insert(0,str(ROOT))
from height_palette import COLOR_ATTRIBUTE,display,WEIGHTS
from cel_patches import CYLINDER_ATTRIBUTE,BROAD_ATTRIBUTE
from local_panel_paint import panel_faces
from mesh_regions import regions
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'RimWorld/bradley_rimworld.blend'))
bpy.context.scene.frame_set(1)
cfg=json.loads((ROOT/'part_contrast.json').read_text())['height_first_paint']
manifest=json.loads((ROOT/'RimWorld/manifest.json').read_text())
before=json.loads((OUT/'before_manifest.json').read_text())
assert manifest['geometry_sha256']==before['geometry_sha256'],'Paint edit changed geometry'
assert manifest['faces']==before['faces']==52878 and manifest['meshes']==before['meshes']==103
def y(obj,p):
    return sum(sum(display(v)*w for v,w in zip(obj.data.color_attributes[COLOR_ATTRIBUTE].data[i].color[:3],WEIGHTS)) for i in p.loop_indices)/len(p.loop_indices)
def cap(name,role,lo,hi,main=False):
    obj=bpy.data.objects[name];selected=set(regions(obj.data)[0]) if main else None
    polygons=[p for p in obj.data.polygons if p.normal.z>=.96 and lo<=(obj.matrix_world@p.center).z<=hi
        and obj.data.materials[p.material_index].get('paint_role')==role and (selected is None or p.index in selected)]
    assert polygons,name
    values=[y(obj,p) for p in polygons]
    return {'object':name,'faces':len(polygons),'minimum':min(values),'maximum':max(values),
        'area_mean':sum(p.area*v for p,v in zip(polygons,values))/sum(p.area for p in polygons)}
samples={
'hull':cap('WT_@root','hull_assembly',1.98,2.16),
'turret':cap('WT_bone_turret','turret_assembly',2.715,2.745),
'driver_hatch':cap('WT_hatch_01','driver_hatch',0,5,True),
'rear_roof_hatch':cap('WT_hatch_05','rear_roof_hatch',0,5,True),
'hatch_a':cap('WT_hatch_02','turret_hatch_a',2.85,3.0,True),
'hatch_b':cap('WT_hatch_03','turret_hatch_b',2.77,2.90,True),
'sight':cap('WT_bone_commander_sight_v','commander_sight',3.28,3.4),
'handle':cap('WT_hatch_02','hatch_lid_handle',2.77,2.95)}
for target in cfg['local_raised_panels']['targets']:
    obj=bpy.data.objects[target['object']];ids=panel_faces(obj,target)
    values=[y(obj,obj.data.polygons[i]) for i in ids]
    samples[target['name']]={'object':obj.name,'faces':len(ids),'minimum':min(values),'maximum':max(values),'area_mean':sum(values)/len(values)}
pairs=[('hull','turret'),('hull','driver_hatch'),('hull','rear_roof_hatch'),('turret','hatch_a'),('turret','hatch_b'),('turret','sight'),('turret','coax_port_cover'),('turret','gun_upper_shroud'),('hatch_a','handle')]
gaps={}
for parent,child in pairs:
    gap=samples[child]['minimum']-samples[parent]['maximum']
    assert gap>=cfg['contact_palette']['minimum_step']-.01,(parent,child,gap)
    gaps[parent+' -> '+child]=gap
# Different world Z does not require a difference between independent tops.
assert abs(samples['hatch_b']['area_mean']-samples['sight']['area_mean'])<.01
assert abs(samples['coax_port_cover']['area_mean']-samples['gun_upper_shroud']['area_mean'])<.01
cylinders={};broad={}
for obj in bpy.context.scene.objects:
    if obj.type!='MESH':continue
    groups={}
    for p in obj.data.polygons:
        group=obj.data.attributes[CYLINDER_ATTRIBUTE].data[p.index].value
        if group<0:continue
        groups.setdefault(group,set()).update(tuple(round(c,6) for c in obj.data.color_attributes[COLOR_ATTRIBUTE].data[i].color[:3]) for i in p.loop_indices)
    assert all(len(colors)==1 for colors in groups.values()),('Multi-color cylinder side',obj.name)
    if groups:cylinders[obj.name]=len(groups)
    values=[y(obj,p) for p in obj.data.polygons if obj.data.attributes[BROAD_ATTRIBUTE].data[p.index].value]
    if values:broad[obj.name]={'range':[min(values),max(values)],'retains_gradient':len({round(v,3) for v in values})>2}
assert sum(cylinders.values())>15,cylinders
assert all(broad[n]['retains_gradient'] for n in ('WT_hatch_02','WT_hatch_03'))
# A virtual height change of an independent sight must not recolor either that
# part or the rest of the vehicle. Nothing from this experiment is saved.
import numpy as np
from face_depth_tone import apply_face_depth_tone
objects=[o for o in bpy.context.scene.objects if o.type=='MESH' and len(o.data.loops)>0]
baseline={o.name:np.array([v.color[:] for v in o.data.color_attributes[COLOR_ATTRIBUTE].data]) for o in objects}
sight=bpy.data.objects['WT_bone_commander_sight_v'];location=sight.location.copy()
try:
    sight.location.z+=.7;apply_face_depth_tone(bpy.context.scene)
    shift_error=max(float(np.max(abs(np.array([v.color[:] for v in o.data.color_attributes[COLOR_ATTRIBUTE].data])-baseline[o.name]))) for o in objects)
    assert shift_error<.000001,('Independent equipment height changed paint',shift_error)
finally:
    sight.location=location;apply_face_depth_tone(bpy.context.scene)
report={'geometry_unchanged_from':'04b091e3','geometry_sha256':manifest['geometry_sha256'],'actual_final_rgb_samples':samples,
'contact_gaps':gaps,'minimum_contact_gap':min(gaps.values()),'independent_hatch_b_and_sight_share_color':True,
'independent_coax_and_gun_cover_share_color':True,'single_color_cylinder_side_groups':cylinders,'broad_hatch_paint':broad,
'world_height_not_a_global_palette_input':True,'independent_sight_virtual_height_shift':.7,
'all_vehicle_colors_max_error_after_virtual_height_shift':shift_error,'in_game_validation':'not_performed'}
(OUT/'validation.json').write_text(json.dumps(report,indent=2)+'\n')
print('LOCAL CONTACT PAINT VALIDATED',len(pairs),'support relations,',sum(cylinders.values()),'single-color cylindrical sides')
