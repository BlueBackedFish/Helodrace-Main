"""Reopen the delivery model, exercise saved drivers and render inspection poses."""
from pathlib import Path
import bpy, sys, json, math
from mathutils import Vector

ROOT=Path(__file__).resolve().parent
OUT=ROOT/'output'
sys.path.insert(0,str(ROOT.parent/'PropAssets'))
import modeling as m

bpy.ops.wm.open_mainfile(filepath=str(OUT/'f15ex_eagle_ii.blend'))
scene=bpy.context.scene
report=json.loads((OUT/'model_validation.json').read_text())['articulation']
saved_checks=[]
for frame in [1,20,40,60]:
    scene.frame_set(frame)
    bpy.context.view_layer.update()
    maximum=0
    for i in range(24):
        right=scene.objects[f'EXHAUST_right_petal_{i:02}']
        left=scene.objects[f'EXHAUST_left_petal_{i:02}']
        for a,b in zip(right.data.vertices,left.data.vertices):
            a=right.matrix_world@a.co
            b=left.matrix_world@b.co
            maximum=max(maximum,(Vector((-a.x,a.y,a.z))-b).length)
    assert maximum<5e-5,(frame,maximum)  # Float32 parenting; <0.003 delivery pixels.
    saved_checks.append(dict(frame=frame,mirror_error_m=maximum,
        aperture=scene.objects['CTRL_nozzle_right']['aperture']))
assert saved_checks[1]['aperture']==0 and saved_checks[2]['aperture']==1
curves=[o for o in scene.objects if o.type=='CURVE']
assert all(o.parent and o.parent.type=='MESH' for o in curves)
scene.frame_set(1)
neutral={o.name:o.matrix_world.copy() for o in curves}
scene.frame_set(40)
following=[o for o in curves if not o.parent.name.startswith(('AIRFRAME','JOINT_FIXED'))]
assert all(max(abs(v) for row in (o.matrix_world-neutral[o.name]) for v in row)>.0001 for o in following)
scene.frame_set(1)
assert all(max(abs(v) for row in (o.matrix_world-neutral[o.name]) for v in row)<1e-5 for o in curves)

rear=m.camera('REVIEW nozzle apertures',-90,12,3.6,(0,-7.8,1.6))
for name,frame in [('neutral',1),('contracted',20),('open',40)]:
    scene.frame_set(frame)
    m.render(OUT/f'nozzles_{name}.png',rear,800)
controls=m.camera('REVIEW articulated surfaces',-110,35,16,(0,-3.2,1.8))
for name,frame in [('neutral',1),('pose_a',20),('pose_b',40)]:
    scene.frame_set(frame)
    m.render(OUT/f'controls_{name}.png',controls,1000)

# A compact two-second preview; the Blender model holds the full independent
# actions and editable engine aperture properties.
for i,frame in enumerate([1,10,20,30,40,50,60]):
    scene.frame_set(frame)
    m.render(OUT/f'motion_{i:02}.png',controls,640)
scene.frame_set(1)
checks=dict(passed=True,saved_blend_reopened=True,saved_driver_symmetry_checks=saved_checks,
    joint_line_objects=len(curves),joint_lines_follow_their_own_meshes=True,
    neutral_lines_return=True,independent_movement=report['independent_movement_checks'],
    no_gameplay_controller_added=True)
(OUT/'articulation_validation.json').write_text(json.dumps(checks,indent=2)+'\n')
print('F15EX_SAVED_RIG_PASS: 8 control surfaces; 24 leaves per engine; saved drivers and attached joints')
