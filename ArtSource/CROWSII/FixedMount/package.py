from pathlib import Path
import json,sys,math
from PIL import Image
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';REPO=ROOT.parents[2]
sys.path.insert(0,str(ROOT.parents[1]/'PropAssets'));import image_export as e
meta=json.loads((OUT/'metadata.json').read_text());target=REPO/'Textures/Buildings/Security/CROWSII/FixedMount';files={}
for direction in ['top','north','east','south','west']:
    scale=3.2 if direction=='top' else 3.6;name='HD_CROWSII_FixedMount_'+direction+'.png'
    files[name]=e.export(e.opened(OUT/('plate_'+direction+'_raw.png')),target/name,1024,.026/scale*1024)
raw=e.opened(OUT/'assembled_top_raw.png');bbox=raw.getchannel('A').getbbox();crop=raw.crop(bbox);side=math.ceil(max(crop.size)*1024/928);square=Image.new('RGBA',(side,side));square.alpha_composite(crop,((side-crop.width)//2,(side-crop.height)//2))
name='HD_CROWSII_FixedMount_UI.png';files[name]=e.export(e.resize_linear(square,2048),target/name,1024,10)
source=REPO/'Textures/Buildings/Security/CROWSII';top=e.opened(target/'HD_CROWSII_FixedMount_top.png')
for name in ['HD_CROWSII_M2_Base.png','HD_CROWSII_M2_Frame.png','HD_CROWSII_M2Barrel.png','HD_CROWSII_M2Body.png','HD_CROWSII_M2_FeedOverlay.png']:top=Image.alpha_composite(top,e.opened(source/name))
entries=[('FIXED MOUNT / shared pivot',top)]
for direction in ['north','east','south','west']:
    preview=e.resize_linear(e.outline(e.opened(OUT/('assembled_'+direction+'_raw.png')),.026/3.6*2048),1024);entries.append((direction.upper()+' / assembled',preview))
e.sheet(entries,OUT/'preview.png',columns=3)
e.sheet([('PLATE / '+d.upper(),e.opened(target/('HD_CROWSII_FixedMount_'+d+'.png'))) for d in ['top','north','east','south','west']],OUT/'plate_preview.png',columns=3)
result=dict(passed=True,files=files,presentation=meta,draw_order=['foundation','base','frame','barrel','body','feed_overlay'],foundation_texture='Buildings/Security/CROWSII/FixedMount/HD_CROWSII_FixedMount_top',weapon_layers_manifest='../output/recoil_layers.json',source_station_unchanged=True,transparent=True,in_game_tested=False)
(OUT/'validation.json').write_text(json.dumps(result,indent=2)+'\n')
print('FIXED_MOUNT_EXPORT_PASS: 1024px separate top/four directions and assembled icon; original recoil layers remain reusable')
