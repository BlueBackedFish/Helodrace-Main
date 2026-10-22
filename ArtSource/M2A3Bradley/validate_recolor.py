"""Check exact pre-stage-two geometry restoration and material-only changes."""
import bpy,json,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'RimWorld'
cfg=json.loads((ROOT/'part_contrast.json').read_text())
def snapshot():
    return {o.name:{'verts':[tuple(v.co) for v in o.data.vertices],'faces':[list(p.vertices) for p in o.data.polygons],
        'parent':o.parent.name,'world':[tuple(row) for row in o.matrix_world]}
        for o in bpy.context.scene.objects if o.type=='MESH'}
bpy.ops.wm.open_mainfile(filepath=str(ROOT/cfg['geometry_source']));bpy.context.scene.frame_set(1);before=snapshot()
bpy.ops.wm.open_mainfile(filepath=str(OUT/'bradley_rimworld.blend'));bpy.context.scene.frame_set(1)
assert snapshot()==before
assert len(before)==83 and sum(len(r['faces']) for r in before.values())==43344
assert not any(o.name.startswith(('Smoke_','RearBin','TurretHatch')) for o in bpy.context.scene.objects)
report=json.loads((OUT/'manifest.json').read_text())
assert report['part_difference_multiplier']==3
changed=[r for r in report['part_adjustments'] if r['object'] in {'WT_hatch_02','WT_hatch_03'}]
assert len(changed)==2
for r in changed:
    # Existing hatch-to-roof gaps exceed the configured minimum, so these
    # important part differences must scale by exactly three before clipping.
    assert abs(r['target_delta']/r['old_top_delta']-3)<.01
assert any(r['object']=='WT_hatch_05' and r['target_delta']>0 for r in report['part_adjustments'])
(OUT/'recolor_validation.json').write_text(json.dumps({'exact_pre_stage2_geometry_and_parenting':True,
    'mesh_count':83,'faces':43344,'hatch_difference_multiplier':3,'stage2_new_geometry_removed':True,
    'rear_roof_hatch_kept_brighter':True,'in_game_validation':'not_performed'},indent=2)+'\n')
print('RECOLOR VALIDATION PASSED: exact baseline geometry, no stage2 additions, hatch-to-roof differences x3')
