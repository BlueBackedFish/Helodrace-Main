"""Render existing fighter models rolled +90 degrees about forward (+Y)."""
from pathlib import Path
import hashlib
import json
import math
import sys

import bpy
from mathutils import Matrix

ROOT = Path(__file__).resolve().parent
ART = ROOT.parent
OUT = ROOT / 'output'
OUT.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(ART / 'PropAssets'))
import modeling as m

records = []
for name, folder, filename in [
    ('F22', 'F22Raptor', 'f22_raptor.blend'),
    ('F15EX', 'F15EXEagleII', 'f15ex_eagle_ii.blend'),
]:
    path = ART / folder / 'output' / filename
    source_hash = hashlib.sha256(path.read_bytes()).hexdigest()
    model = json.loads((path.parent / 'model_validation.json').read_text(encoding='utf-8'))
    if name == 'F22':
        lo, hi = model['source_pose_bounds']
        meters_per_unit = 18.9 / (hi[0] - lo[0])
    else:
        meters_per_unit = 1.0
    bpy.ops.wm.open_mainfile(filepath=str(path))
    scene = bpy.context.scene
    scene.frame_set(1)
    bpy.context.view_layer.update()
    roots = [obj for obj in scene.objects if obj.parent is None and obj.type != 'CAMERA']

    def bounds():
        dg = bpy.context.evaluated_depsgraph_get()
        points = []
        for obj in scene.objects:
            if obj.type != 'MESH' or obj.hide_render:
                continue
            evaluated = obj.evaluated_get(dg)
            points.extend(evaluated.matrix_world @ v.co for v in evaluated.data.vertices)
        return [[min(p[i] for p in points) for i in range(3)],
                [max(p[i] for p in points) for i in range(3)]]

    before = bounds()
    rotation = Matrix.Rotation(math.pi / 2, 4, 'Y')
    for obj in roots:
        obj.matrix_world = rotation @ obj.matrix_world
    bpy.context.view_layer.update()
    after = bounds()
    # A roll preserves every longitudinal coordinate and all proportions.
    assert max(abs(before[j][1] - after[j][1]) for j in range(2)) < 1e-4
    for src, dst in [(0, 2), (1, 1), (2, 0)]:
        assert abs((before[1][src] - before[0][src]) -
                   (after[1][dst] - after[0][dst])) < 1e-4
    assert not any(obj.type == 'LIGHT' for obj in scene.objects)
    camera = scene.camera
    camera.location = (0, 0, 100 / meters_per_unit)
    camera.rotation_euler = (0, 0, 0)
    camera.data.type = 'ORTHO'
    camera.data.ortho_scale = 24 / meters_per_unit
    camera.data.clip_end = 1000 / meters_per_unit
    scene.render.film_transparent = True
    m.render(OUT / (name + '_roll90_plain.png'), camera, 1056)
    assert hashlib.sha256(path.read_bytes()).hexdigest() == source_hash
    records.append(dict(aircraft=name, model=str(path.relative_to(ART.parent)),
                        model_sha256=source_hash, roll_degrees=90, roll_axis='+Y / forward',
                        meters_per_native_unit=meters_per_unit,
                        viewport_m=24, resolution=1056, pixels_per_meter=44,
                        neutral_frame=1, bounds_before=before, bounds_after=after,
                        source_blend_unchanged=True))

(OUT / 'roll_validation.json').write_text(json.dumps(dict(passed=True, aircraft=records),
    indent=2) + '\n', encoding='utf-8')
print('AIRCRAFT_ROLL_PASS: actual +90 degree longitudinal roll; source models unchanged')
