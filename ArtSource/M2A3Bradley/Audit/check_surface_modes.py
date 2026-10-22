"""Prove broad lid gradients return while non-lid paint remains unchanged."""
import bpy,sys,json,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parent.parent;OUT=ROOT/'Audit/SurfaceRules';sys.path.insert(0,str(ROOT))
from cel_patches import broad_hatch_faces,CEL_ATTRIBUTE,BROAD_ATTRIBUTE
from height_palette import COLOR_ATTRIBUTE
cfg=json.loads((ROOT/'part_contrast.json').read_text())['height_first_paint']
registry=json.loads((ROOT/'bradley_roles.json').read_text(encoding='utf-8'))
roles={r['object']:r['role'] for r in registry['runtime_bindings']}
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'RimWorld/bradley_rimworld.blend'));bpy.context.scene.frame_set(1)
def snapshot():
    result={}
    for o in bpy.context.scene.objects:
        if o.type!='MESH':continue
        broad=broad_hatch_faces(o,roles[o.name],cfg);colors=o.data.color_attributes[COLOR_ATTRIBUTE].data
        def color_hash(ids):
            values=[tuple(round(c,6) for c in colors[i].color[:3]) for p in o.data.polygons if p.index in ids for i in p.loop_indices]
            return hashlib.sha256(str(values).encode()).hexdigest()
        all_ids=set(range(len(o.data.polygons)))
        result[o.name]={'broad_faces':len(broad),'broad_hash':color_hash(broad),'other_hash':color_hash(all_ids-broad)}
    return result
current=snapshot()
if '--capture' in sys.argv:
    (OUT/'before_modes.json').write_text(json.dumps(current,indent=2)+'\n');print('BASELINE SURFACE MODES CAPTURED');sys.exit(0)
before=json.loads((OUT/'before_modes.json').read_text());assert before.keys()==current.keys()
for name,record in current.items():assert record['other_hash']==before[name]['other_hash'],('Non-lid paint changed',name)
wide={}
for name,record in current.items():
    if not record['broad_faces']:continue
    o=bpy.data.objects[name];broad=broad_hatch_faces(o,roles[name],cfg)
    assert all(o.data.attributes[CEL_ATTRIBUTE].data[i].value<0 and o.data.attributes[BROAD_ATTRIBUTE].data[i].value==1 for i in broad)
    colors=o.data.color_attributes[COLOR_ATTRIBUTE].data
    unique={tuple(round(c,5) for c in colors[li].color[:3]) for i in broad for li in o.data.polygons[i].loop_indices}
    varying=sum(len({tuple(round(c,5) for c in colors[li].color[:3]) for li in o.data.polygons[i].loop_indices})>1 for i in broad)
    assert len(unique)>8 and varying>0,(name,len(unique),varying)
    wide[name]={'faces':len(broad),'unique_corner_colors':len(unique),'interpolated_faces':varying,'changed_from_cel':record['broad_hash']!=before[name]['broad_hash']}
assert wide['WT_hatch_02']['changed_from_cel']
report={'broad_lids':wide,'all_non_lid_corner_colors_match_baseline':True,'method':'Compare every non-lid painted corner RGB hash, rounded to 6 decimals, against commit 7956c9a3 baseline.'}
(OUT/'mode_validation.json').write_text(json.dumps(report,indent=2)+'\n')
print('BROAD GRADIENT / SMALL CEL SURFACE VALIDATION PASSED',json.dumps(report))
