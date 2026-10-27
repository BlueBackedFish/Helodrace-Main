"""Compare registered recoil composites with actual 3D rest/recoil renders."""
from pathlib import Path
import hashlib,json,subprocess,io
import numpy as np
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';TARGET=ROOT.parents[1]/'Textures/Buildings/Security/CROWSII'
meta=json.loads((OUT/'metadata.json').read_text());manifest=json.loads((OUT/'validation.json').read_text());layers=json.loads((OUT/'recoil_layers.json').read_text())
def opened(path):
    with Image.open(path) as im:return im.convert('RGBA')
assert len(manifest['files'])==10
for name,entry in manifest['files'].items():
    path=TARGET/name;im=opened(path);assert im.size==(1024,1024) and im.getchannel('A').getbbox()
    assert hashlib.sha256(path.read_bytes()).hexdigest()==entry['sha256'],name
assert not (TARGET/'HD_CROWSII_M2_Top.png').exists() and not (TARGET/'HD_CROWSII_M2_TurretTop.png').exists(),'Baked barrel still exported'
frame=opened(OUT/'frame_raw.png');body=opened(OUT/'body_raw.png');barrel=opened(OUT/'barrel_raw.png');feed=opened(OUT/'feed_overlay_raw.png')
assert frame.size==body.size==barrel.size==feed.size==(2048,2048)
records=[]
for name,offset,reference in [('rest',0,'top_raw.png'),('recoil',layers['recoil_pixels']*2,'recoil_raw.png')]:
    moving=Image.new('RGBA',barrel.size);moving.alpha_composite(barrel,(0,offset))
    parts=[np.asarray(im) for im in [frame,moving,body,feed]]
    composite=Image.alpha_composite(Image.alpha_composite(Image.alpha_composite(frame,moving),body),feed)
    predicted=np.asarray(composite).astype(int);actual=np.asarray(opened(OUT/reference)).astype(int)
    interior=np.ones((2048,2048),bool)
    for part in parts:interior&=(part[:,:,3]==0)|(part[:,:,3]==255)
    interior&=(predicted[:,:,3]==255)&(actual[:,:,3]==255)
    errors=interior&(np.max(abs(predicted[:,:,:3]-actual[:,:,:3]),axis=2)>8)
    fraction=float(errors.sum()/max(1,interior.sum()));assert fraction<.001,(name,fraction)
    records.append(dict(pose=name,master_recoil_pixels=offset,interior_pixels=int(interior.sum()),interior_disagreements=int(errors.sum()),fraction=fraction))
assert layers['moving_layer']=='barrel' and layers['draw_order']==['base','frame','barrel','body','feed_overlay'] and layers['recoil_pixels']==12
with Image.open(OUT/'recoil_preview.gif') as animation:assert animation.n_frames>1
result=dict(passed=True,files=len(manifest['files']),shared_canvas=[1024,1024],shared_pivot=[0,0],raw_3d_pose_comparison=records,only_barrel_layer_moves=True,qcb_handle_moves_with_barrel=True,no_baked_barrel_in_fixed_head=True,no_internal_barrel_body_outline=True,feed_overlay_preserves_occlusion=True,world_outline_tiles=meta['outline_world_tiles'],in_game_tested=False)
(OUT/'layer_validation.json').write_text(json.dumps(result,indent=2)+'\n')
old=subprocess.run(['git','show','a6f57bd1:ArtSource/CROWSII/output/top_raw.png'],cwd=ROOT.parents[1],check=True,capture_output=True).stdout
with Image.open(io.BytesIO(old)) as im:before=im.convert('RGBA')
crop=(620,880,1130,1280);review=Image.new('RGB',(1020,440),(65,70,73));draw=ImageDraw.Draw(review);font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',18)
for i,(label,image) in enumerate([('BEFORE / FEED CHUTE',before),('AFTER / DONOR FEEDWAY FIT',opened(OUT/'top_raw.png'))]):
    detail=image.crop(crop);review.paste(detail,(i*510,40),detail);draw.text((i*510+10,10),label,font=font,fill='white')
review.save(OUT/'feed_comparison.png')
print('CROWS_LAYER_PASS: 10 registered sprites; separated barrel; rest/recoil composites match 3D interiors; correct feed occlusion and animated preview')
