"""Check the saved mount, seat contact and inherited barrel-only recoil."""
from pathlib import Path
import bpy,json,hashlib
from mathutils import Vector
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';meta=json.loads((OUT/'metadata.json').read_text())
source=Path(meta['source_crows']);assert hashlib.sha256(source.read_bytes()).hexdigest()==meta['source_crows_sha256']
bpy.ops.wm.open_mainfile(filepath=str(OUT/'fixed_mount.blend'));scene=bpy.context.scene;scene.frame_set(1);bpy.context.view_layer.update()
meshes=[o for o in scene.objects if o.type=='MESH'];plate=[o for o in meshes if o.get('layer')=='foundation'];station=[o for o in meshes if o not in plate]
assert len([o for o in plate if o.get('foundation_part')=='anchor_bolt'])==4
assert len([o for o in plate if o.get('foundation_part')=='flange_bolt'])==6
base=next(o for o in station if o.get('layer')=='base')
assert abs(min((base.matrix_world@v.co).z for v in base.data.vertices)-meta['plate_top_contact'])<1e-6
rest={o.name:[o.matrix_world@v.co for v in o.data.vertices] for o in meshes};scene.frame_set(2);bpy.context.view_layer.update()
moving=0
for o in meshes:
    delta=Vector((0,-meta['weapon_recoil_preserved']['recoil_tiles'],0)) if o.get('layer')=='barrel' else Vector()
    moving+=o.get('layer')=='barrel'
    for v,p in zip(o.data.vertices,rest[o.name]):assert (o.matrix_world@v.co-p-delta).length<2e-6,o.name
assert moving==7 and not any(o.type=='LIGHT' for o in scene.objects)
result=dict(passed=True,foundation_meshes=len(plate),station_meshes=len(station),ground_anchors=4,flange_bolts=6,seat_contact_height=meta['plate_top_contact'],foundation_and_fixed_parts_do_not_recoil=True,source_barrel_recoil_preserved=True,moving_barrel_meshes=moving,source_crows_sha256=meta['source_crows_sha256'],in_game_tested=False)
(OUT/'model_validation.json').write_text(json.dumps(result,indent=2)+'\n');print('FIXED_MOUNT_VALIDATION_PASS: seat contact, anchors and inherited barrel-only recoil verified in saved assembly')
