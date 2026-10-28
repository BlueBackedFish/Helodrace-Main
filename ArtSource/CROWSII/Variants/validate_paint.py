"""Compare palette-only revision with the previous committed models and sprites."""
from pathlib import Path
import sys,json,hashlib,struct,subprocess,io
try:import bpy
except ImportError:bpy=None
ROOT=Path(__file__).resolve().parent;REPO=ROOT.parents[2];OUT=ROOT/'output'
sys.path.insert(0,str(ROOT));import paint
BASELINE=sys.argv[sys.argv.index('--')+1] if '--' in sys.argv else '103891d1'
CACHE=REPO/'.codex-staging/CrowsPaintReview';CACHE.mkdir(parents=True,exist_ok=True)

def old_bytes(path):return subprocess.run(['git','show',BASELINE+':'+str(path.relative_to(REPO)).replace('\\','/')],cwd=REPO,capture_output=True,check=True).stdout
def geometry(path):
    bpy.ops.wm.open_mainfile(filepath=str(path));bpy.context.view_layer.update();h=hashlib.sha256();cameras={}
    for o in sorted(bpy.context.scene.objects,key=lambda o:o.name):
        h.update(o.name.encode());h.update(struct.pack('<16f',*(v for row in o.matrix_world for v in row)))
        if o.type=='CAMERA':cameras[o.name]=o.data.ortho_scale
        if o.type!='MESH':continue
        for v in o.data.vertices:h.update(struct.pack('<3f',*v.co))
        for p in o.data.polygons:h.update(struct.pack('<I',len(p.vertices)));h.update(struct.pack('<'+'I'*len(p.vertices),*p.vertices))
        for layer in o.data.uv_layers:
            h.update(layer.name.encode())
            for uv in layer.data:h.update(struct.pack('<2f',*uv.uv))
    return h.hexdigest(),cameras

if bpy is not None:
    models={}
    for kind in ['MK19','M240B','M249']:
        path=OUT/kind/('crows_'+kind.lower()+'.blend');old=CACHE/(kind+'_baseline.blend');old.write_bytes(old_bytes(path))
        previous=geometry(old);current=geometry(path);assert previous==current,(kind,'geometry/UV/cameras changed during paint revision')
        models[kind]=dict(geometry_uv_transform_sha256=current[0],cameras_unchanged=True)
    (OUT/'paint_model_validation.json').write_text(json.dumps(dict(passed=True,baseline=BASELINE,models=models),indent=2)+'\n')
    print('CROWS_PAINT_MODEL_PASS: geometry/UVs/transforms/cameras identical to',BASELINE)
    sys.exit(0)
from PIL import Image
import numpy as np
sys.path.insert(0,str(ROOT.parents[1]/'PropAssets'));import image_export as e
model_report=json.loads((OUT/'paint_model_validation.json').read_text());assert model_report['passed'] and model_report['baseline']==BASELINE
models=model_report['models']
report=json.loads((OUT/'validation.json').read_text());target=REPO/'Textures/Buildings/Security/CROWSII/Variants';images={};comparison=[];palette=paint.PALETTE
for name in report['files']:
    path=target/name;before=Image.open(io.BytesIO(old_bytes(path))).convert('RGBA');after=e.opened(path)
    assert before.size==after.size==(1024,1024)
    assert before.getchannel('A').tobytes()==after.getchannel('A').tobytes(),(name,'silhouette/outline alpha changed')
    same=path.read_bytes()==old_bytes(path)
    if name.endswith(('_Frame.png','_Feed.png')):assert same,(name,'station/feed appearance changed')
    images[name]=dict(alpha_unchanged=True,full_image_unchanged=same)
    if name.endswith('_Top.png'):comparison.extend([(name.split('_')[2]+' / BEFORE',before),(name.split('_')[2]+' / AFTER',after)])
visible={}
for kind in models:
    a=np.asarray(e.opened(target/('HD_CROWSII_'+kind+'_Body.png')))
    counts={key:int(np.count_nonzero((a[:,:,:3]==rgb).all(axis=2)&(a[:,:,3]==255))) for key,rgb in palette.items()}
    assert counts['cover']>=3 and counts['receiver']>=3,(kind,counts)
    visible[kind]=counts
e.sheet(comparison,OUT/'paint_comparison.png',columns=2)
(OUT/'paint_revision_validation.json').write_text(json.dumps(dict(passed=True,baseline=BASELINE,models=models,images=images,visible_palette_pixels=visible,palette_rules=paint.report(),source_mesh_uvs_and_cameras_unchanged=True,all_30_sprite_alpha_unchanged=True,station_and_feed_pixels_unchanged=True,in_game_tested=False),indent=2)+'\n')
print('CROWS_PAINT_REVISION_PASS: model geometry/UVs/transforms/cameras identical; 30 sprite alpha channels identical; station/feed pixels unchanged; M2 receiver and cover colors visible')
