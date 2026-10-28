"""Export the empty station with the existing CROWS canvas, pivot and cutout."""
from pathlib import Path
import sys,json,math,hashlib
from PIL import Image
import numpy as np
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';REPO=ROOT.parents[2]
sys.path.insert(0,str(ROOT.parents[1]/'PropAssets'));import image_export as e
TARGET=REPO/'Textures/Buildings/Security/CROWSII/Unarmed';STATION=TARGET.parent
meta=json.loads((OUT/'metadata.json').read_text());files={}
frame_raw=e.opened(OUT/'frame_raw.png')
name='HD_CROWSII_Unarmed_Frame.png';files[name]=e.export(frame_raw,TARGET/name,1024,.026/3.2*1024)
frame=e.opened(TARGET/name);base=e.opened(STATION/'HD_CROWSII_M2_Base.png')
top=Image.alpha_composite(base,frame);name='HD_CROWSII_Unarmed_Top.png';e.save(top,TARGET/name)
files[name]=dict(size=list(top.size),sha256=hashlib.sha256((TARGET/name).read_bytes()).hexdigest(),registered_composite=True)
# Check the registered raw base/frame against the new full 3D render.
predicted=Image.alpha_composite(e.opened(ROOT.parent/'output/base_raw.png'),frame_raw)
a=np.asarray(predicted).astype(int);b=np.asarray(e.opened(OUT/'full_raw.png')).astype(int)
opaque=(a[:,:,3]==255)&(b[:,:,3]==255)
error=opaque&(np.abs(a[:,:,:3]-b[:,:,:3]).max(axis=2)>8)
fraction=float(error.sum()/max(1,opaque.sum()));assert fraction<.001,fraction
entries=[('UNARMED / TOP',top)];mounted=[]
plate=STATION/'FixedMount'
mounted.append(('UNARMED / TOP',Image.alpha_composite(e.opened(plate/'HD_CROWSII_FixedMount_top.png'),top)))
for direction in ['north','east','south','west']:
    name='HD_CROWSII_Unarmed_'+direction+'.png'
    files[name]=e.export(e.opened(OUT/(direction+'_raw.png')),TARGET/name,1024,.026/3.6*1024)
    image=e.opened(TARGET/name);entries.append((direction.upper(),image))
    mounted.append((direction.upper(),Image.alpha_composite(e.opened(plate/('HD_CROWSII_FixedMount_'+direction+'.png')),image)))
bbox=top.getbbox();crop=top.crop(bbox);side=math.ceil(max(crop.size)*1024/928)
icon=Image.new('RGBA',(side,side));icon.alpha_composite(crop,((side-crop.width)//2,(side-crop.height)//2))
name='HD_CROWSII_Unarmed_UI.png';files[name]=e.export(e.resize_linear(icon,1024),TARGET/name,1024,0)
for name in files:
    image=e.opened(TARGET/name);assert image.size==(1024,1024)
    assert image.getchannel('A').getextrema()==(0,255)
e.sheet(entries,OUT/'station_preview.png',columns=3,cell=320)
e.sheet(mounted,OUT/'preview.png',columns=3,cell=320)
layers=dict(base='../HD_CROWSII_M2_Base',frame='HD_CROWSII_Unarmed_Frame',draw_order=['base','frame'],
    yaw_pivot=[0,0,0],top_draw_size=[3.2,3.2],directional_draw_size=[3.6,3.6],
    optional_foundation='../FixedMount/HD_CROWSII_FixedMount_top',weapon=None,feed=None)
(OUT/'layers.json').write_text(json.dumps(layers,indent=2)+'\n')
(OUT/'validation.json').write_text(json.dumps(dict(passed=True,files=files,
    model=meta,layer_registration_opaque_difference_fraction=fraction,
    existing_base_reused=True,transparent=True,foundation_is_separate=True,in_game_tested=False),indent=2)+'\n')
print('CROWS_UNARMED_EXPORT_PASS: 7 registered 1024px sprites; empty frame/base composite agrees with 3D render')
