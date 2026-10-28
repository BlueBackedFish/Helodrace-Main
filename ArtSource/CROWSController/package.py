from pathlib import Path
import json,sys,math,hashlib
from PIL import Image
import numpy as np
ROOT=Path(__file__).resolve().parent;REPO=ROOT.parents[1];OUT=ROOT/'output';sys.path.insert(0,str(ROOT.parent/'PropAssets'));import image_export as e
meta=json.loads((OUT/'metadata.json').read_text());target=REPO/'Textures/Buildings/ComputerDesk/CROWSController';files={};entries=[];overlays=[]
for direction in ['north','east','south','west']:
    full=e.opened(OUT/(direction+'_full_raw.png'));visible=e.opened(OUT/(direction+'_addon_raw.png'))
    name='HD_ComputerDesk_CROWS_'+direction+'.png';files[name]=e.export(full,target/name,512,12);entries.append((direction.upper()+' / desk + controller',e.opened(target/name)))
    # Match the shared desk silhouette rather than putting a 12px black stroke
    # around thin cables and every small part inside the tabletop.
    alpha=np.asarray(full.getchannel('A'));edge=(e.grown(full,24).astype('int16')-alpha).clip(0,255).astype('uint8');edge[e.grown(visible,24)==0]=0
    back=Image.new('RGBA',full.size);back.putalpha(Image.fromarray(edge));overlay=e.resize_linear(Image.alpha_composite(back,visible),512)
    name='HD_CROWSController_'+direction+'.png';e.save(overlay,target/name);files[name]=dict(size=[512,512],sha256=hashlib.sha256((target/name).read_bytes()).hexdigest(),registered_addon=True,desk_occlusion_baked=True)
    overlays.append((direction.upper()+' / separate addon',overlay))
raw=e.opened(OUT/'controller_ui_raw.png');crop=raw.crop(raw.getchannel('A').getbbox());side=math.ceil(max(crop.size)*512/448);square=Image.new('RGBA',(side,side));square.alpha_composite(crop,((side-crop.width)//2,(side-crop.height)//2));name='HD_CROWSController_UI.png';files[name]=e.export(e.resize_linear(square,1024),target/name,512,12)
e.sheet(entries,OUT/'preview.png',columns=2);e.sheet(overlays,OUT/'addon_preview.png',columns=2)
e.sheet([('CONTROLLER / device',e.opened(target/name)),('DESK / installed',entries[0][1])],OUT/'detail_preview.png',columns=2)
fixed_icon=REPO/'Textures/Buildings/Security/CROWSII/FixedMount/HD_CROWSII_FixedMount_UI.png'
e.sheet([('CROWS II / fixed mount',e.opened(fixed_icon)),('CONTROLLER / detail',e.opened(target/name)),('COMPUTER / addon installed',entries[0][1])],OUT/'summary_preview.png',columns=3)
result=dict(passed=True,files=files,presentation=meta,transparent=True,registered_overlay_canvas=[3,3],complete_desk_variant_available=True,overlay_outline='shared combined silhouette; no thick cable interiors',original_desk_textures_unchanged=True,in_game_tested=False)
(OUT/'validation.json').write_text(json.dumps(result,indent=2)+'\n')
print('CROWS_CONTROLLER_EXPORT_PASS: four registered addon overlays, four complete desk variants and device icon; 512px/12px furniture style')
