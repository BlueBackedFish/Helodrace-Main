from pathlib import Path
import json,sys,math
from PIL import Image
import numpy as np
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';REPO=ROOT.parents[2]
sys.path.insert(0,str(ROOT.parents[1]/'PropAssets'));import image_export as e
meta=json.loads((OUT/'metadata.json').read_text());target=REPO/'Textures/Buildings/Security/CROWSII/FixedMount';files={};registration={}
for direction in ['top','north','east','south','west']:
    raw=Image.alpha_composite(e.opened(OUT/('plate_'+direction+'_raw.png')),e.opened(OUT/('station_'+direction+'_raw.png')))
    actual=np.asarray(raw);reference=np.asarray(e.opened(OUT/('assembled_'+direction+'_raw.png')))
    opaque=(actual[:,:,3]==255)&(reference[:,:,3]==255)
    differences=opaque&(np.abs(actual[:,:,:3].astype('int16')-reference[:,:,:3].astype('int16')).max(axis=2)>20)
    ratio=float(np.count_nonzero(differences)/np.count_nonzero(opaque));assert ratio<.001,(direction,ratio)
    registration[direction]=dict(opaque_color_difference_fraction=ratio,shared_camera_verified=True)
for direction in ['top','north','east','south','west']:
    scale=3.2 if direction=='top' else 3.6;name='HD_CROWSII_FixedMount_'+direction+'.png'
    files[name]=e.export(e.opened(OUT/('plate_'+direction+'_raw.png')),target/name,1024,.026/scale*1024)
plate=e.opened(OUT/'plate_top_raw.png');station=e.opened(OUT/'station_top_raw.png')
bbox=Image.alpha_composite(plate,station).getchannel('A').getbbox();side=math.ceil(max(bbox[2]-bbox[0],bbox[3]-bbox[1])*1024/928)
def icon_layer(raw):
    crop=raw.crop(bbox);square=Image.new('RGBA',(side,side));square.alpha_composite(crop,((side-crop.width)//2,(side-crop.height)//2))
    return e.resize_linear(square,2048)
plate_icon=icon_layer(plate);station_icon=icon_layer(station)
# Outline both silhouettes before compositing, so the station retains its
# black boundary where it sits over the opaque foundation.
outlined_icon=Image.alpha_composite(e.outline(plate_icon,20),e.outline(station_icon,20))
name='HD_CROWSII_FixedMount_UI.png';files[name]=e.export(outlined_icon,target/name,1024,0)
single_outline=e.resize_linear(e.outline(Image.alpha_composite(plate_icon,station_icon),20),1024)
actual=e.opened(target/name);actual_rgb=np.asarray(actual)[:,:,:3];single_rgb=np.asarray(single_outline)[:,:,:3]
internal_black=(actual_rgb.max(axis=2)<5)&(single_rgb.max(axis=2)>30)&(np.asarray(actual.getchannel('A'))==255)
assert np.count_nonzero(internal_black)>100,'Missing station outline over the foundation'
source=REPO/'Textures/Buildings/Security/CROWSII';top=e.opened(target/'HD_CROWSII_FixedMount_top.png')
for name in ['HD_CROWSII_M2_Base.png','HD_CROWSII_M2_Frame.png','HD_CROWSII_M2Barrel.png','HD_CROWSII_M2Body.png','HD_CROWSII_M2_FeedOverlay.png']:top=Image.alpha_composite(top,e.opened(source/name))
entries=[('FIXED MOUNT / shared pivot',top)]
for direction in ['north','east','south','west']:
    name='HD_CROWSII_FixedMount_Station_'+direction+'.png'
    files[name]=e.export(e.opened(OUT/('station_'+direction+'_raw.png')),target/name,1024,.026/3.6*1024)
    preview=Image.alpha_composite(e.opened(target/('HD_CROWSII_FixedMount_'+direction+'.png')),e.opened(target/name))
    e.save(preview,OUT/('assembled_'+direction+'_outlined.png'));entries.append((direction.upper()+' / separate outlines',preview))
e.sheet(entries,OUT/'preview.png',columns=3)
e.sheet([('PLATE / '+d.upper(),e.opened(target/('HD_CROWSII_FixedMount_'+d+'.png'))) for d in ['top','north','east','south','west']],OUT/'plate_preview.png',columns=3)
result=dict(passed=True,files=files,presentation=meta,draw_order=['foundation','base','frame','barrel','body','feed_overlay'],foundation_texture='Buildings/Security/CROWSII/FixedMount/HD_CROWSII_FixedMount_top',weapon_layers_manifest='../output/recoil_layers.json',source_station_unchanged=True,separate_foundation_and_station_outlines=True,station_outline_pixels_over_foundation=int(np.count_nonzero(internal_black)),directional_station_layers='HD_CROWSII_FixedMount_Station_{north,east,south,west}.png',transparent=True,in_game_tested=False)
result['layer_registration_against_original_assembly']=registration
(OUT/'validation.json').write_text(json.dumps(result,indent=2)+'\n')
print('FIXED_MOUNT_EXPORT_PASS: foundation/station outlined separately in all directions and UI; original recoil layers remain reusable')
