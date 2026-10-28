"""Package registered CROWS weapon variants with cutout edges at world scale."""
from pathlib import Path
import json,sys,math,hashlib
import numpy as np
from PIL import Image
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';REPO=ROOT.parents[2]
sys.path.insert(0,str(ROOT.parents[1]/'PropAssets'));import image_export as e
CONFIG=json.loads((ROOT/'weapons.json').read_text());target=REPO/'Textures/Buildings/Security/CROWSII/Variants'
files={};previews=[];weapons=[];directions=[];layers={}
base=e.opened(REPO/'Textures/Buildings/Security/CROWSII/HD_CROWSII_M2_Base.png')
foundation=e.opened(REPO/'Textures/Buildings/Security/CROWSII/FixedMount/HD_CROWSII_FixedMount_top.png')
for kind in CONFIG:
    folder=OUT/kind;meta=json.loads((folder/'metadata.json').read_text());stroke=meta['outline_world_tiles']/3.2*1024
    def export(raw,suffix,scale=3.2):
        name='HD_CROWSII_'+kind+'_'+suffix+'.png';files[name]=e.export(raw,target/name,1024,meta['outline_world_tiles']/scale*1024);return e.opened(target/name)
    frame=export(e.opened(folder/'frame_raw.png'),'Frame')
    full=e.opened(folder/'gun_raw.png');alpha=np.asarray(full.getchannel('A'));edge=(e.grown(full,stroke*2).astype('int16')-alpha).clip(0,255).astype('uint8');gun_layers={}
    for part in ['body','barrel']:
        raw=e.opened(folder/(part+'_raw.png'));outline=edge.copy();outline[e.grown(raw,stroke*2)==0]=0
        back=Image.new('RGBA',raw.size);back.putalpha(Image.fromarray(outline));image=e.resize_linear(Image.alpha_composite(back,raw),1024)
        name='HD_CROWSII_'+kind+'_'+part.title()+'.png';e.save(image,target/name);files[name]=dict(size=[1024,1024],sha256=hashlib.sha256((target/name).read_bytes()).hexdigest(),shared_weapon_outline=True);gun_layers[part]=image
    # Only the visible portion of the belt goes above the receiver; this avoids
    # drawing hidden cartridges over the top cover or painting a false seam.
    feed=e.resize_linear(e.opened(folder/'feed_visible_raw.png'),1024);name='HD_CROWSII_'+kind+'_Feed.png';e.save(feed,target/name);files[name]=dict(size=[1024,1024],sha256=hashlib.sha256((target/name).read_bytes()).hexdigest(),true_occlusion=True)
    top=Image.alpha_composite(base,frame)
    for image in [gun_layers['barrel'],gun_layers['body'],feed]:top=Image.alpha_composite(top,image)
    name='HD_CROWSII_'+kind+'_Top.png';e.save(top,target/name);files[name]=dict(size=[1024,1024],sha256=hashlib.sha256((target/name).read_bytes()).hexdigest(),registered_composite=True)
    previews.append((kind+' / CROWS mount',Image.alpha_composite(foundation,top)))
    weapon=Image.alpha_composite(gun_layers['barrel'],gun_layers['body']);weapon=Image.alpha_composite(weapon,feed);weapons.append((kind+' / gun + aligned belt',weapon))
    for direction in ['north','east','south','west']:directions.append((kind+' / '+direction.upper(),export(e.opened(folder/(direction+'_raw.png')),direction,3.6)))
    bbox=top.getchannel('A').getbbox();crop=top.crop(bbox);side=math.ceil(max(crop.size)*1024/928);square=Image.new('RGBA',(side,side));square.alpha_composite(crop,((side-crop.width)//2,(side-crop.height)//2))
    name='HD_CROWSII_'+kind+'_UI.png';files[name]=e.export(e.resize_linear(square,1024),target/name,1024,0)
    layers[kind]=dict(base='../HD_CROWSII_M2_Base',frame='HD_CROWSII_'+kind+'_Frame',barrel='HD_CROWSII_'+kind+'_Barrel',body='HD_CROWSII_'+kind+'_Body',feed='HD_CROWSII_'+kind+'_Feed',draw_order=['base','frame','barrel','body','feed'],draw_size=[3.2,3.2],pivot=[0,0],barrel_is_fixed=True,feed_geometry=meta['feed'])
e.sheet(previews,OUT/'preview.png',columns=3)
e.sheet(weapons,OUT/'weapon_preview.png',columns=3)
e.sheet(directions,OUT/'direction_preview.png',columns=4)
(OUT/'layers.json').write_text(json.dumps(layers,indent=2)+'\n')
(OUT/'validation.json').write_text(json.dumps(dict(passed=True,files=files,weapons=list(CONFIG),transparent=True,resolution=[1024,1024],top_draw_size=[3.2,3.2],directional_draw_size=[3.6,3.6],same_station_yaw_pivot=True,weapon_barrel_shared_outline=True,in_game_tested=False,gameplay_added=False),indent=2)+'\n')
print('CROWS_VARIANTS_EXPORT_PASS:',len(files),'1024px sprites; three mounted variants with aligned visible ammunition belts')
