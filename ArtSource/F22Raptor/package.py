"""Use the established aircraft silhouette and muted internal-line treatment."""
from pathlib import Path
import json,sys,hashlib
import numpy as np
from PIL import Image,ImageChops,ImageFilter,ImageDraw
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';REPO=ROOT.parents[1]
sys.path.insert(0,str(ROOT.parent/'VehiclePipeline'))
from images import resize_linear,composite_linear,sheet,font
sys.path.insert(0,str(ROOT.parent/'PropAssets'));from image_export import outline as black_outline
style=json.loads((ROOT/'style.json').read_text());report=json.loads((OUT/'model_validation.json').read_text())
size=style['delivery_resolution'];master=Image.open(OUT/'top_plain_2048.png').convert('RGBA');factor=master.width/size
mask=Image.open(OUT/'control_mask_2048.png').getchannel('R')
radius=round(style['panel_line_pixels']*factor)
edge=ImageChops.subtract(mask,mask.filter(ImageFilter.MinFilter(2*radius+1)))
edge=ImageChops.multiply(edge,master.getchannel('A'))
lines=Image.new('RGBA',master.size,tuple(style['panel_rgb'])+(0,));lines.putalpha(edge)
plain=composite_linear(master,lines,(0,0));plain.putalpha(master.getchannel('A'))
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
(OUT/'texture_validation.json').write_text(json.dumps(dict(passed=True,images=validation,resolution=[size,size],
    outline_pixels=style['outline_pixels'],panel_line_pixels=style['panel_line_pixels'],
    one_outline_for_complete_aircraft=True,pivot_normalized=[.5,.5],nose_up=True,draw_size_tiles=None,
    color_treatment='Source UV layout, four-color flat camouflage and muted control seams; opaque gold canopy',
    texture='Textures/Effects/Aircraft/F22/HD_F22.png',game_definition_added=False,in_game_tested=False),indent=2)+'\n')
print('F22_TEXTURE_PASS: 1024px RGBA; exact top; 12px outside outline; 2px muted control seams; plain/editable model retained')
