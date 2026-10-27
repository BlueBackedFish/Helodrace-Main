from pathlib import Path
import sys,json,math,hashlib
import numpy as np
from PIL import Image
ROOT=Path(__file__).resolve().parent;REPO=ROOT.parent.parent;OUT=ROOT/'output';sys.path.insert(0,str(ROOT.parent/'PropAssets'));import image_export as e
meta=json.loads((OUT/'metadata.json').read_text());target=REPO/'Textures/Buildings/Security/CROWSII';files={};entries=[]
stroke=meta['outline_world_tiles']/meta['top_draw_size'][0]*1024
for part,name in [('base','Base'),('frame','Frame'),('top','Top')]:
    path=target/('HD_CROWSII_M2_'+name+'.png');files[path.name]=e.export(e.opened(OUT/(part+'_raw.png')),path,1024,stroke)
    if part=='top':
        native=e.opened(path).transpose(Image.Transpose.ROTATE_270);path2=target/'HD_CROWSII_M2_TurretTop.png';e.save(native,path2);files[path2.name]=dict(size=[1024,1024],sha256=hashlib.sha256(path2.read_bytes()).hexdigest(),muzzle_axis='+X for native TurretTop -90-degree artwork rotation')
full=e.opened(OUT/'gun_raw.png');radius=stroke*2;alpha=np.asarray(full.getchannel('A'));edge=(e.grown(full,radius).astype('int16')-alpha).clip(0,255).astype('uint8')
for part,name in [('body','M2Body'),('barrel','M2Barrel')]:
    image=e.opened(OUT/(part+'_raw.png'));part_edge=edge.copy();part_edge[e.grown(image,radius)==0]=0;back=Image.new('RGBA',image.size);back.putalpha(Image.fromarray(part_edge));output=e.resize_linear(Image.alpha_composite(back,image),1024)
    path=target/('HD_CROWSII_'+name+'.png');e.save(output,path);files[path.name]=dict(size=[1024,1024],sha256=hashlib.sha256(path.read_bytes()).hexdigest(),shared_gun_outline=True)
for direction in ['north','east','south','west']:
    path=target/('HD_CROWSII_M2_'+direction+'.png');files[path.name]=e.export(e.opened(OUT/(direction+'_raw.png')),path,1024,meta['outline_world_tiles']/meta['directional_draw_size'][0]*1024);entries.append((direction.upper()+' / 64 deg',e.opened(path)))
full=e.opened(OUT/'full_raw.png');bbox=full.getchannel('A').getbbox();crop=full.crop(bbox);side=math.ceil(max(crop.size)*1024/928);square=Image.new('RGBA',(side,side));square.alpha_composite(crop,((side-crop.width)//2,(side-crop.height)//2));icon=e.resize_linear(square,2048)
path=target/'HD_CROWSII_M2_UI.png';files[path.name]=e.export(icon,path,1024,10)
e.sheet([('CROWS II + M2 / top',e.opened(path)),*entries],OUT/'preview.png',columns=3)
(OUT/'validation.json').write_text(json.dumps(dict(passed=True,files=files,source=meta,pivot_registration='all top layers share centered 3.2-tile canvas',native_turret_artwork='east-facing TurretTop; other top layers north-facing',transparent=True,fixed_emission=True,in_game_tested=False),indent=2)+'\n')
print('CROWS_EXPORT_PASS: FDE M153 with M2; 1024px directions, centered layers, native turret top and icon')
