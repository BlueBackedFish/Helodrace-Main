"""Validate deliverable art without rewriting any source or review decision."""
from pathlib import Path
import hashlib,json
import numpy as np
from PIL import Image
HERE=Path(__file__).resolve().parent
cfg=json.loads((HERE/'settings.json').read_text());manifest=json.loads((HERE/'output/manifest.json').read_text())
assert manifest['native_size']==3072 and manifest['draw_size']==18 and manifest['outline_pixels']==12
assert len(manifest['layers'])==48 and len({l['key'] for l in manifest['layers']})==48
reports={}
for variant,spec in cfg['variants'].items():
    source=HERE.parent/spec['path'];digest=hashlib.sha256(source.read_bytes()).hexdigest()
    report=json.loads((HERE/f'output/{variant}/render.json').read_text())
    assert digest==spec['sha256']==report['source_sha256']
    assert report['source_geometry_unchanged'] and report['master']==4096 and report['draw_size']==18
    assert len(report['directions'])==4 and report['canopy_count']==4
    assert (HERE/f'output/{variant}/vehicle_drop_rig.blend').is_file()
    reports[variant]={'source_unchanged':True,'source_faces':report['source_faces_preserved'],'source_mesh_uv_signature':report['source_mesh_uv_signature']}
for layer in manifest['layers']:
    path=HERE/layer['file'];assert hashlib.sha256(path.read_bytes()).hexdigest()==layer['sha256']
    image=Image.open(path).convert('RGBA');a=np.asarray(image)
    assert a[:,:,3].any() and not a[a[:,:,3]==0,:3].any(),path
    bounds=image.getchannel('A').getbbox()
    assert bounds[0]>=3 and bounds[1]>=3 and bounds[2]<=image.width-3 and bounds[3]<=image.height-3
    assert all(v>0 for v in layer['size']) and all(abs(v)<18 for v in layer['offset'])
result=dict(passed=True,variants=reports,runtime_layers=48,absolute_pixels_per_tile=cfg['pixels_per_tile'],
    native_canvas=3072,world_canvas=18,outline_pixels=12,source_uv_and_geometry_preserved=True,
    all_layers_nonempty_unclipped=True,transparent_rgb_zero=True,art_only=True)
(HERE/'output/delivery_validation.json').write_text(json.dumps(result,indent=2)+'\n')
print('HEAVY_DROP_ART_PASS: three original models preserved, 48 registered layers, alpha/margins/hashes/absolute scale')
