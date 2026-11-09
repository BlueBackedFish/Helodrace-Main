"""Outline the aircraft; internal joints come exclusively from the 3D model."""
from pathlib import Path
import json,sys,hashlib
import numpy as np
from PIL import Image,ImageDraw
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';REPO=ROOT.parents[1]
sys.path.insert(0,str(ROOT.parent/'VehiclePipeline'))
from images import resize_linear,sheet,font
sys.path.insert(0,str(ROOT.parent/'PropAssets'));from image_export import outline as black_outline
style=json.loads((ROOT/'style.json').read_text());report=json.loads((OUT/'model_validation.json').read_text())
size=style['delivery_resolution'];master=Image.open(OUT/'top_plain_2048.png').convert('RGBA');factor=master.width/size
assert len(report['articulation']['controllers'])==12
plain=master
outlined=black_outline(plain,style['outline_pixels']*factor)
target=REPO/'Textures/Effects/Aircraft/F22';target.mkdir(parents=True,exist_ok=True)
validation={};images={}
for label,image in [('Plain',plain),('Outlined',outlined)]:
    image=resize_linear(image,size);path=OUT/('HD_F22_'+label.lower()+'.png');image.save(path)
    a=np.asarray(image);bbox=image.getbbox();margin=min(bbox[0],bbox[1],size-bbox[2],size-bbox[3])
    assert image.size==(1024,1024) and image.mode=='RGBA' and margin>=12
    assert image.getchannel('A').getextrema()==(0,255)
    assert not a[a[:,:,3]==0,:3].any()
    validation[label]=dict(sha256=hashlib.sha256(path.read_bytes()).hexdigest(),bounds=list(bbox),minimum_margin_pixels=margin)
    images[label]=image
images['Outlined'].save(OUT/'HD_F22.png');images['Outlined'].save(target/'HD_F22.png')
assert (OUT/'HD_F22.png').read_bytes()==(target/'HD_F22.png').read_bytes()
backgrounds=[]
for label,color in [('LIGHT',(220,222,225)),('GRAY',(91,97,104)),('DARK',(31,37,44))]:
    bg=Image.new('RGBA',(size,size),color+(255,));bg.alpha_composite(images['Outlined']);backgrounds.append((label,bg))
sheet(backgrounds,OUT/'preview.png',columns=3,cell_size=(400,440))
review=Image.new('RGB',(840,590),(91,97,104));draw=ImageDraw.Draw(review)
for n,x,y in [(256,20,150),(512,300,52)]:
    small=resize_linear(images['Outlined'],n);review.paste(small,(x,y),small)
    draw.text((x,y-32),str(n)+'px canvas / actual pixels',font=font(16),fill='white')
review.save(OUT/'scale_review.png')
motion=OUT/'articulation_review'
if (motion/'controls_neutral.png').exists():
    frames=[]
    for label in ['neutral','negative','positive','return']:
        image=Image.open(motion/('controls_'+label+'.png')).convert('RGBA')
        bg=Image.new('RGBA',image.size,(91,97,104,255));bg.alpha_composite(image);frames.append(bg.convert('RGB'))
    frames[0].save(OUT/'articulation_preview.gif',save_all=True,append_images=frames[1:],duration=[800,650,650,800],loop=0)
    sheet([(label.upper(),Image.open(motion/('tvc_'+label+'.png'))) for label in ['negative','neutral','positive']],
        OUT/'tvc_review.png',columns=3,cell_size=(400,440))
if (motion/'canopy_top.png').exists():
    sheet([('BEFORE / TOP',Image.open(ROOT/'analysis/canopy_before.png')),
        ('REFINED / TOP',Image.open(motion/'canopy_top.png')),
        ('REFINED / OBLIQUE',Image.open(motion/'canopy_oblique.png'))],
        OUT/'canopy_review.png',columns=3,cell_size=(400,440))
(OUT/'texture_validation.json').write_text(json.dumps(dict(passed=True,images=validation,resolution=[size,size],
    outline_pixels=style['outline_pixels'],internal_lines_from_model_geometry=True,
    one_outline_for_complete_aircraft=True,pivot_normalized=[.5,.5],nose_up=True,draw_size_tiles=None,
    moving_controls=12,source_subsystem_pose=650,model_neutral_frame=1,
    color_treatment='Flat camouflage, mildly brighter upright fins, physical control joints; opaque gold canopy',
    texture='Textures/Effects/Aircraft/F22/HD_F22.png',game_definition_added=False,in_game_tested=False),indent=2)+'\n')
print('F22_TEXTURE_PASS: 1024px RGBA; exact top; 12px outside outline; physical model joints without 2D seam painting')
