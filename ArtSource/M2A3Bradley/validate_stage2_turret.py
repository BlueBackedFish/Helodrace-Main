"""Check stage-two geometry against its frozen inputs and animation hierarchy."""
import bpy,json,hashlib,math
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'RimWorld'
cfg=json.loads((ROOT/'turret_stage2.json').read_text());roles=json.loads((ROOT/cfg['role_registry']).read_text(encoding='utf-8'))
report=json.loads((OUT/'stage2_manifest.json').read_text())
def under(o,name):
    while o:
        if o.name==name:return True
        o=o.parent
    return False
def snapshot_hull():
    return {o.name:([tuple(v.co) for v in o.data.vertices],[list(p.vertices) for p in o.data.polygons],o.parent.name)
        for o in bpy.context.scene.objects if o.type=='MESH' and not under(o,'TURRET_YAW')}
bpy.ops.wm.open_mainfile(filepath=str(ROOT/cfg['baseline']));bpy.context.scene.frame_set(1);original_hull=snapshot_hull()
bpy.ops.wm.open_mainfile(filepath=str(OUT/'bradley_rimworld.blend'));s=bpy.context.scene;s.frame_set(1)
assert snapshot_hull()==original_hull
turret=[o for o in s.objects if o.type=='MESH' and under(o,'TURRET_YAW')]
assert all(o.get('build_stage')==2 and len(o.data.polygons)>0 for o in turret)
assert sum(len(o.data.polygons) for o in turret)==report['turret_faces']<10000
for entry in roles['entries']:
    if entry['parent'] not in {'TURRET_YAW','GUN_ELEVATION'} or entry['importance']!='essential':continue
    o=bpy.data.objects.get(entry['object']);assert o and len(o.data.polygons)>0,entry['object']
    assert o.parent.name==entry['parent']
assert len([o for o in turret if o.get('art_role')=='smoke_launcher'])==8
assert len([o for o in turret if o.get('art_role')=='smoke_mouth'])==8
assert len([o for o in turret if o.get('art_role')=='rear_stowage_bin'])==25
assert len([o for o in turret if o.get('art_role')=='hatch_handle'])==6
for anchor in report['smoke_anchors']:
    delta=Vector(anchor['base'])-Vector(anchor['armor_hit']);assert abs(delta.length-.012)<1e-5
    assert Vector(anchor['normal']).dot(delta)>0
for o in turret:
    assert all(p.area>1e-12 for p in o.data.polygons),o.name
    assert o.data.attributes.get('Face depth tone')
assert not any(o.type=='LIGHT' for o in s.objects)
assert all(c.data.type=='ORTHO' for c in s.objects if c.type=='CAMERA')
assert s.render.film_transparent
result={'hull_geometry_and_parenting_unchanged':True,'all_essential_turret_roles_present':True,
    'empty_turret_meshes':0,'faces':report['turret_faces'],'smoke_tubes':8,'ray_attached_smoke_mounts':8,
    'rear_bins':5,'hatch_handles':2,'source_geometry_placement':'source armor and equipment bounds retained; new smoke mounting is an art fit',
    'final_palette':'pending_stage4','in_game_validation':'not_performed'}
(OUT/'stage2_validation.json').write_text(json.dumps(result,indent=2)+'\n')
print('STAGE2 GEOMETRY VALIDATION PASSED: exact hull preservation, essential turret roles, attached smoke mounts, no empty or degenerate turret meshes')
