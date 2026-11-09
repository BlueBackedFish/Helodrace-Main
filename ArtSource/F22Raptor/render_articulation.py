"""Render model-only articulation and TVC inspection views, not game animation."""
from pathlib import Path
import bpy,sys,json
from mathutils import Matrix,Vector
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';REVIEW=OUT/'articulation_review';REVIEW.mkdir(exist_ok=True)
sys.path.insert(0,str(ROOT.parent/'PropAssets'));import modeling as m
bpy.ops.wm.open_mainfile(filepath=str(OUT/'f22_raptor.blend'));scene=bpy.context.scene
meta=json.loads((OUT/'model_validation.json').read_text());T=Matrix(meta['root_transform'])
overview=m.camera('Control inspection',-55,37,4.6,(0,-.02,.5))
nozzles=m.camera('TVC inspection',-65,20,1.0,T@Vector((-1.66,0,.415)))
for label,frame in [('neutral',1),('negative',20),('positive',40),('return',60)]:
    scene.frame_set(frame);bpy.context.view_layer.update();m.render(REVIEW/('controls_'+label+'.png'),overview,800)
    if label!='return':m.render(REVIEW/('tvc_'+label+'.png'),nozzles,600)
scene.frame_set(1)
canopy=scene.objects['Cockpit.001'];dg=bpy.context.evaluated_depsgraph_get()
evaluated=canopy.evaluated_get(dg)
points=[evaluated.matrix_world@v.co for v in evaluated.data.vertices]
target=sum(points,Vector())/len(points)
for label,azimuth,elevation in [('top',0,90),('oblique',-30,30)]:
    camera=m.camera('Canopy '+label,azimuth,elevation,1.0,target)
    m.render(REVIEW/('canopy_'+label+'.png'),camera,700)
print('F22_ARTICULATION_REVIEW_PASS: neutral / negative / positive / return; independent controls and TVC close-ups')
