"""Check delivered world scale, original coverage and visible outline thickness."""
from pathlib import Path
import hashlib,json
import numpy as np
from PIL import Image

HERE=Path(__file__).resolve().parent;out=HERE/'output'
style=json.loads((HERE/'style.json').read_text())
model=json.loads((out/'model_validation.json').read_text())
report=json.loads((out/'texture_validation.json').read_text())
scale=json.loads((out/'scale.json').read_text())
profile=json.loads((HERE/'vehicle.json').read_text(encoding='utf-8'))
assert scale['draw_size_tiles']==profile['draw_size_tiles']==[19,19]
assert scale['resolution']==[3243,3243] and scale['outline_pixels']==12
assert abs(scale['actual_pixels_per_tile']-512/3)<=.5/19
assert all(ref['pixels_per_tile']==[512/3]*2 for ref in scale['reference_assets'].values())
source=Path(profile['sources']['obj']['path'])
assert hashlib.sha256(source.read_bytes()).hexdigest()==model['source_sha256']
assert model['source_faces']==model['preserved_faces']==486906 and model['omitted_faces']==0
assert hashlib.sha256((out/'aircraft.blend').read_bytes()).hexdigest()==model['model_sha256']
raw=Image.open(out/'top_plain_4096.png').convert('RGBA')
assert raw.size==(4096,4096) and raw.width>=scale['resolution'][0]
assert hashlib.sha256((out/'top_plain_4096.png').read_bytes()).hexdigest()==report['raw_render_sha256']
images={}
for kind in ['Plain','Outlined']:
    path=out/'Textures'/kind/'HD_C17.png';image=Image.open(path).convert('RGBA')
    assert image.size==(3243,3243)
    assert hashlib.sha256(path.read_bytes()).hexdigest()==report['files'][kind]['sha256']
    pixels=np.asarray(image);assert not pixels[pixels[:,:,3]==0,:3].any()
    images[kind]=pixels
    assert not any(p.is_dir() for p in path.parent.iterdir()),'An old low-density game candidate remains'
assert (out/'HD_C17.png').read_bytes()==(out/'Textures/Outlined/HD_C17.png').read_bytes()
# Refilter only the original alpha, independently of painted RGB or details.
alpha=np.asarray(raw.getchannel('A'),dtype=np.float32)/255
expected=np.asarray(Image.fromarray(alpha).resize((3243,3243),Image.Resampling.LANCZOS))
expected=np.round(np.clip(expected,0,1)*255).astype(np.uint8)
assert np.array_equal(expected,images['Plain'][:,:,3]),'Surface painting altered the source silhouette'
assert np.all(images['Outlined'][:,:,3][expected==255]==255)
detail=np.asarray(Image.open(out/'surface_details_4096.png').getchannel('A'))
source_alpha=np.asarray(raw.getchannel('A'))
assert not np.any((detail>2)&(source_alpha==0)),'Detail paint escaped the visible source surfaces'
assert np.count_nonzero(detail)>10000
measurements=[]
for fraction in [.14,.16,.18,.20,.67,.70,.73]:
    y=round(3243*fraction)
    inner=np.where(images['Plain'][y,:,3]>=128)[0]
    outer=np.where(images['Outlined'][y,:,3]>=128)[0]
    for side,run in [('left',int(inner.min()-outer.min())),('right',int(outer.max()-inner.max()))]:
        assert 11<=run<=14,(y,side,run)
        measurements.append(dict(row=y,side=side,width_pixels=run))
    assert max(images['Outlined'][y,outer.min()+2,:3])<4
result=dict(status='passed',draw_size_tiles=[19,19],game_resolution=[3243,3243],outline_requested_pixels=12,
    horizontal_boundary_samples=measurements,absolute_density_matches_radio_and_machines=True,
    original_alpha_exactly_preserved_after_resampling=True,detail_pixels=int(np.count_nonzero(detail)),
    source_faces_preserved=486906,source_geometry_changed=False,no_old_low_density_game_candidates=True,
    in_game_tested=False)
(out/'delivery_validation.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
print('C17_DELIVERY_PASS: absolute density, 19x19 world canvas, native PNGs, 12px boundaries, source alpha, painted details')
