from pathlib import Path
import bpy,json
from mathutils import Vector
ROOT=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(ROOT.parent/'M8AGS/output/WeaponLayers/vehicle.blend'))
result={}
for obj in bpy.context.scene.objects:
    if not obj.name.startswith('g0044') or obj.type!='MESH':continue
    vertices=[obj.matrix_world@v.co for v in obj.data.vertices]
    result[obj.name]=dict(min=[min(v[i] for v in vertices) for i in range(3)],max=[max(v[i] for v in vertices) for i in range(3)],materials=[m.name for m in obj.data.materials],faces=len(obj.data.polygons))
(ROOT/'source_inspection.json').write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps(result),flush=True)
