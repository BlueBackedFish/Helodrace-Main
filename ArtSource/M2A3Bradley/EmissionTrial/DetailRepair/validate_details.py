"""Compare untouched objects to the pre-repair model and restorations to the OBJ."""
import bpy, hashlib, json, sys
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent;TRIAL=HERE.parent
sys.path.insert(0,str(HERE))
from source_details import read_parts
bpy.ops.wm.open_mainfile(filepath=str(TRIAL/'bradley_emission_trial.blend'));s=bpy.context.scene;s.frame_set(1)
def snapshot():
    result={}
    for o in s.objects:
        if o.type!='MESH':continue
        record={'parent':o.parent.name,'world':[[round(v,6) for v in row] for row in o.matrix_world],
                'vertices':[tuple(round(v,6) for v in p.co) for p in o.data.vertices],
                'faces':[list(p.vertices) for p in o.data.polygons],
                'colors':[tuple(round(c,6) for c in o.data.materials[p.material_index].diffuse_color) for p in o.data.polygons]}
        result[o.name]=hashlib.sha256(json.dumps(record,sort_keys=True).encode()).hexdigest()
    return result
current=snapshot();baseline=HERE/'before_objects.json'
if '--capture' in sys.argv:
    assert not baseline.exists(),'Do not overwrite the pre-repair baseline'
    baseline.write_text(json.dumps({'commit':'38a4fd44','objects':current},indent=2)+'\n')
    print('DETAIL REPAIR BASELINE CAPTURED');sys.exit(0)
before=json.loads(baseline.read_text())['objects']
report=json.loads((TRIAL/'manifest.json').read_text())['source_detail_repairs']
hull=json.loads((TRIAL/'manifest.json').read_text())['hull_detail_repairs']
assert set(current)==set(before)|set(report['added_objects'])|set(hull['added_objects'])
for name,digest in before.items():
    if name not in report['replaced_objects']+hull['replaced_objects']:assert current[name]==digest,('Unrelated object changed',name)
parts,manifest=read_parts();records={}
for name,record in report['parts'].items():
    obj=bpy.data.objects[name];expected=[parts[record['source_group']][i] for i in record['source_polygon_indices']]
    assert obj.parent.name=='TURRET_YAW'
    assert len(obj.data.polygons)==len(expected)
    for p,(coordinates,material) in zip(obj.data.polygons,expected):
        actual=[obj.matrix_world@obj.data.vertices[i].co for i in p.vertices]
        assert len(actual)==len(coordinates) and all((a-Vector(b)).length<2e-6 for a,b in zip(actual,coordinates)),(name,p.index)
        assert p.area>1e-12,(name,'degenerate face',p.index)
        mat=obj.data.materials[p.material_index]
        if 'optic' in material or 'dynamic_glass' in material:assert mat.name=='Restored Optics'
        else:
            e=next(n for n in mat.node_tree.nodes if n.type=='EMISSION')
            assert not e.inputs['Color'].links and e.inputs['Strength'].default_value==1
    # Source triangles must not already occur anywhere else in the output.
    def key(points):return tuple(sorted(tuple(round(float(c),4) for c in p) for p in points))
    own={key(coords) for coords,_ in expected}
    for other in s.objects:
        if other.type!='MESH' or other==obj:continue
        for p in other.data.polygons:
            assert key([other.matrix_world@other.data.vertices[v].co for v in p.vertices]) not in own,('Duplicate face',name,other.name,p.index)
    records[name]={'exact_source_faces':len(expected),'source_group':record['source_group'],'parent':obj.parent.name}
validation={'source_sha256':manifest['source_sha256'],'untouched_objects_identical_to_38a4fd44_except_hull_repair':len(before)-len(report['replaced_objects'])-len(hull['replaced_objects']),
            'hull_changes_checked_by':'HullRepair/validate_hull.py',
            'restored_parts':records,'no_duplicate_source_faces':True,'constant_emission_and_original_optics':True,
            'faces':sum(len(o.data.polygons) for o in s.objects if o.type=='MESH'),
            'meshes':len(current),'in_game_validation':'not_performed'}
(HERE/'validation.json').write_text(json.dumps(validation,indent=2)+'\n')
print('SOURCE DETAILS VALIDATED',json.dumps(validation))
