"""Validate restored hull faces, preserved turret, material scope and door parenting."""
import bpy,json,hashlib,sys,collections
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent;TRIAL=HERE.parent;ROOT=TRIAL.parent
sys.path[:0]=[str(ROOT),str(TRIAL/'DetailRepair')]
from source_details import read_parts
from height_palette import display,WEIGHTS
manifest=json.loads((TRIAL/'manifest.json').read_text());report=manifest['hull_detail_repairs']
before=json.loads((HERE/'before_objects.json').read_text())['objects']
bpy.ops.wm.open_mainfile(filepath=str(TRIAL/'bradley_emission_trial.blend'));s=bpy.context.scene;s.frame_set(1)
def digest(o):
    record={'parent':o.parent.name,'world':[[round(v,6) for v in row] for row in o.matrix_world],
        'vertices':[tuple(round(c,6) for c in v.co) for v in o.data.vertices],
        'faces':[list(p.vertices) for p in o.data.polygons],
        'colors':[tuple(round(c,6) for c in o.data.materials[p.material_index].diffuse_color) for p in o.data.polygons]}
    return hashlib.sha256(json.dumps(record,sort_keys=True).encode()).hexdigest()
objects={o.name:o for o in s.objects if o.type=='MESH'}
assert set(objects)==set(before)|set(report['added_objects'])
unchanged=[]
for name,old in before.items():
    if name=='WT_@root':continue # Geometry and exact local paint scope: check_side_slopes.py
    if name not in report['replaced_objects']:
        assert digest(objects[name])==old,('Unrelated geometry/paint/parent changed',name)
        unchanged.append(name)
parts,source=read_parts({r['source_group'] for r in report['parts'].values()})
assert source['source_sha256']==report['source_sha256']
def key(points):return tuple(sorted(tuple(round(float(c),4) for c in p) for p in points))
occurrences=collections.Counter(key([o.matrix_world@o.data.vertices[v].co for v in p.vertices]) for o in objects.values() for p in o.data.polygons)
records={};role_values=collections.defaultdict(set)
for name,record in report['parts'].items():
    obj=objects[name];assert obj.parent.name==record['parent']
    ids=record['source_polygon_indices'];expected=[parts[record['source_group']][i] for i in ids]
    assert len(expected)==len(obj.data.polygons)
    for p,i,(coords,source_mat) in zip(obj.data.polygons,ids,expected):
        actual=[obj.matrix_world@obj.data.vertices[v].co for v in p.vertices]
        assert len(actual)==len(coords) and all((a-Vector(b)).length<2e-6 for a,b in zip(actual,coords)),(name,p.index)
        assert p.area>1e-12,(name,'Degenerate face',p.index)
        assert occurrences[key(coords)]==1,(name,'Duplicate restored face',p.index)
        m=obj.data.materials[p.material_index];e=next(n for n in m.node_tree.nodes if n.type=='EMISSION')
        assert not e.inputs['Color'].links and e.inputs['Strength'].default_value==1
        if 'headlight_glass' in source_mat:assert m.get('fixed_accent')=='headlamp glass'
        if m.get('fixed_emission'):
            role=record['source_roles'][str(i)];y=sum(display(c)*w for c,w in zip(m.diffuse_color,WEIGHTS))
            role_values[role].add(round(y,3))
    records[name]={'source_faces':len(ids),'parent':obj.parent.name,'source_group':record['source_group']}
assert len([n for n in report['added_objects'] if 'SkirtHandle_' in n])==14
assert 'WT_@root' not in report['replaced_objects'],'Hull shell must remain intact'
assert all(digest(o)==before[o.name] for o in objects.values() if o.name in before and o.name.startswith(('WT_GunnerSight_','WT_Antenna02_','WT_roll_')))
# Doors retain their original pivots; recovered fixtures follow the corresponding door.
motion={}
for control,frame,names in [('REAR_RAMP_HINGE',80,['WT_rear_ramp','WT_Hull_RampLatchUpper','WT_Hull_RampLatchLower']),
                          ('PERSONNEL_DOOR_HINGE',25,['WT_hatch_04'])]:
    for o in s.objects:
        if o.animation_data and o.name!=control:o.animation_data.action=None
    s.frame_set(1);bpy.context.view_layer.update();start={n:objects[n].matrix_world.copy() for n in names}
    s.frame_set(frame);bpy.context.view_layer.update()
    for n in names:
        assert max(abs(a-b) for ra,rb in zip(start[n],objects[n].matrix_world) for a,b in zip(ra,rb))>.05,(control,n)
    motion[control]=names
    # Reload so the next control's independent demo is active again.
    bpy.ops.wm.open_mainfile(filepath=str(TRIAL/'bradley_emission_trial.blend'));s=bpy.context.scene;objects={o.name:o for o in s.objects if o.type=='MESH'}
assert 192 in role_values['hatch'] and 216 in role_values['raised_lid'] and 216 in role_values['fitting']
assert role_values['skirt_handle']=={168},'No cylindrical side-shading bands on skirt handles'
validation={'baseline':'cbf17877','source_sha256':source['source_sha256'],
 'untouched_objects_identical':len(unchanged),'all_turret_geometry_and_colors_preserved':True,
 'hull_shell_geometry_tracks_wheels_and_grilles_preserved':True,'local_side_slope_check':'HullRepair/check_side_slopes.py','restored_parts':records,
 'no_duplicate_restored_faces':True,'all_restored_faces_positive_area':True,
 'constant_emission':True,'roles_display_y':{k:sorted(v) for k,v in role_values.items()},
 'door_and_ramp_motion':motion,'skirt_handles':14,'ERA':'not added',
 'faces':manifest['faces'],'meshes':manifest['meshes'],'in_game_validation':'not_performed'}
(HERE/'validation.json').write_text(json.dumps(validation,indent=2)+'\n')
print('HULL RESTORATION VALIDATED',len(records),'parts;',len(unchanged),'untouched objects')
