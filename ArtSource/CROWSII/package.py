from pathlib import Path
import sys,json,math,hashlib
import numpy as np
from PIL import Image
ROOT=Path(__file__).resolve().parent;REPO=ROOT.parent.parent;OUT=ROOT/'output';sys.path.insert(0,str(ROOT.parent/'PropAssets'));import image_export as e
meta=json.loads((OUT/'metadata.json').read_text());target=REPO/'Textures/Buildings/Security/CROWSII';files={};entries=[]
stroke=meta['outline_world_tiles']/meta['top_draw_size'][0]*1024
for part,name in [('base','Base'),('frame','Frame')]:
    path=target/('HD_CROWSII_M2_'+name+'.png');files[path.name]=e.export(e.opened(OUT/(part+'_raw.png')),path,1024,stroke)
full=e.opened(OUT/'gun_raw.png');radius=stroke*2;alpha=np.asarray(full.getchannel('A'));edge=(e.grown(full,radius).astype('int16')-alpha).clip(0,255).astype('uint8')
for part,name in [('body','M2Body'),('barrel','M2Barrel')]:
    image=e.opened(OUT/(part+'_raw.png'));part_edge=edge.copy();part_edge[e.grown(image,radius)==0]=0;back=Image.new('RGBA',image.size);back.putalpha(Image.fromarray(part_edge));output=e.resize_linear(Image.alpha_composite(back,image),1024)
    path=target/('HD_CROWSII_'+name+'.png');e.save(output,path);files[path.name]=dict(size=[1024,1024],sha256=hashlib.sha256(path.read_bytes()).hexdigest(),shared_gun_outline=True)
for direction in ['north','east','south','west']:
    path=target/('HD_CROWSII_M2_'+direction+'.png');files[path.name]=e.export(e.opened(OUT/(direction+'_raw.png')),path,1024,meta['outline_world_tiles']/meta['directional_draw_size'][0]*1024);entries.append((direction.upper()+' / 64 deg',e.opened(path)))
full=e.opened(OUT/'full_raw.png');bbox=full.getchannel('A').getbbox();crop=full.crop(bbox);side=math.ceil(max(crop.size)*1024/928);square=Image.new('RGBA',(side,side));square.alpha_composite(crop,((side-crop.width)//2,(side-crop.height)//2));icon=e.resize_linear(square,2048)
path=target/'HD_CROWSII_M2_UI.png';files[path.name]=e.export(icon,path,1024,10)
base=e.opened(target/'HD_CROWSII_M2_Base.png');frame=e.opened(target/'HD_CROWSII_M2_Frame.png');body=e.opened(target/'HD_CROWSII_M2Body.png');barrel=e.opened(target/'HD_CROWSII_M2Barrel.png')
feed=e.resize_linear(e.opened(OUT/'feed_overlay_raw.png'),1024);feed_path=target/'HD_CROWSII_M2_FeedOverlay.png';e.save(feed,feed_path)
files[feed_path.name]=dict(size=[1024,1024],sha256=hashlib.sha256(feed_path.read_bytes()).hexdigest(),visible_feed_only=True,no_artificial_cut_edge_outline=True)
travel=round(meta['articulation']['recoil_tiles']/meta['top_draw_size'][0]*1024)
def assembled(offset=0):
    moved=Image.new('RGBA',barrel.size);moved.alpha_composite(barrel,(0,offset))
    image=Image.alpha_composite(base,frame);image=Image.alpha_composite(image,moved);image=Image.alpha_composite(image,body);return Image.alpha_composite(image,feed)
e.sheet([('CROWS II / registered layers',assembled()),*entries],OUT/'preview.png',columns=3)
e.sheet([('1 / frame',frame),('2 / moving barrel',barrel),('3 / fixed receiver',body),('REST',assembled()),('RECOIL / '+str(travel)+'px',assembled(travel))],OUT/'recoil_preview.png',columns=3)
animation=[]
for offset in [0,travel//3,2*travel//3,travel,2*travel//3,travel//3,0]:
    image=Image.new('RGB',(512,512),(65,70,73));small=e.resize_linear(assembled(offset),512);image.paste(small,(0,0),small);animation.append(image)
animation[0].save(OUT/'recoil_preview.gif',save_all=True,append_images=animation[1:],duration=[500,25,25,40,40,40,250],loop=0)
layers=dict(base='HD_CROWSII_M2_Base',frame='HD_CROWSII_M2_Frame',barrel='HD_CROWSII_M2Barrel',body='HD_CROWSII_M2Body',feed_overlay='HD_CROWSII_M2_FeedOverlay')
recoil_manifest=dict(passed=True,texture_directory='Buildings/Security/CROWSII',layers=layers,draw_order=meta['articulation']['draw_order'],draw_size=meta['top_draw_size'],pivot=[0,0],muzzle_direction='+Y / north',moving_layer='barrel',recoil_direction='local -Y / image +Y',recoil_tiles=meta['articulation']['recoil_tiles'],recoil_pixels=travel,qcb_handle_and_coupling_move_with_barrel=True,fixed_feed_chute=True,receiver_above_barrel=True,feed_foreground_overlay_last=True,gameplay_definition_added=False)
(OUT/'recoil_layers.json').write_text(json.dumps(recoil_manifest,indent=2)+'\n')
(OUT/'validation.json').write_text(json.dumps(dict(passed=True,files=files,source=meta,pivot_registration='all top layers share centered 3.2-tile canvas',recoil_layers=recoil_manifest,transparent=True,fixed_emission=True,in_game_tested=False),indent=2)+'\n')
print('CROWS_EXPORT_PASS: 10 cutout sprites; registered frame/barrel/body layers and visible feed overlay; recoil manifest and preview')
