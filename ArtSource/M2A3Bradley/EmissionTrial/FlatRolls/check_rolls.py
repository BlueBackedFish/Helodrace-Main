import bpy,json,sys,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parent.parent;OUT=Path(__file__).resolve().parent;sys.path.insert(0,str(ROOT.parent))
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'bradley_emission_trial.blend'))
names={'WT_roll_01','WT_roll_02','WT_roll_08'}
def snapshot():
 result={}
 for o in bpy.context.scene.objects:
  if o.type!='MESH':continue
  values=[(p.index,tuple(round(c,6) for c in o.data.materials[p.material_index].diffuse_color[:3])) for p in o.data.polygons]
  protected=[r for r in values if not o.data.materials[o.data.polygons[r[0]].material_index].get('fixed_emission')]
  result[o.name]={'all_hash':hashlib.sha256(str(values).encode()).hexdigest(),'protected':protected}
 return result
current=snapshot()
if '--capture' in sys.argv:
 assert not (OUT/'before.json').exists()
 (OUT/'before.json').write_text(json.dumps({'commit':'ce465c14','objects':current},indent=2)+'\n');print('FLAT ROLL BASELINE CAPTURED');sys.exit(0)
before=json.loads((OUT/'before.json').read_text())['objects'];assert current.keys()==before.keys()
for name,record in current.items():
 if name not in names:assert record['all_hash']==before[name]['all_hash'],name
 assert json.loads(json.dumps(record['protected']))==before[name]['protected'],('Optics changed',name)
report={}
for target in json.loads((ROOT/'settings.json').read_text())['corrections']['uniform_rolls']:
 o=bpy.data.objects[target['object']];mats=[o.data.materials[p.material_index] for p in o.data.polygons if o.data.materials[p.material_index].get('fixed_emission')]
 assert len({tuple(m.diffuse_color[:3]) for m in mats})==1
 assert all(abs(m['display_luminance']-target['display_luminance'])<.01 for m in mats)
 report[o.name]={'painted_faces':len(mats),'display_y':target['display_luminance'],'caps_and_sides_identical':True}
(OUT/'validation.json').write_text(json.dumps({'compared_with':'ce465c14','other_objects_and_all_optics_unchanged':True,'rolls':report},indent=2)+'\n')
print('FLAT ROLL PAINT VALIDATED',report)
