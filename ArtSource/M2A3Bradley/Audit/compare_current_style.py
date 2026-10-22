"""Diagnostic reference board only; reference pixels are not game assets."""
from pathlib import Path
from PIL import Image,ImageOps,ImageDraw
import json
HERE=Path(__file__).resolve().parent;ROOT=HERE.parent
WORKSHOP=Path('C:/Program Files (x86)/Steam/steamapps/workshop/content/294100')
legion=WORKSHOP/'3786789710/Textures/Things/Vehicles/LGIFV'
rim=WORKSHOP/'3724793908/Textures/Vehicles/M2A3'
ours=ROOT/'RimWorld/VF_East'
items=[('Legion hull',legion/'LGIFV_east.png',False,400),('Current hull',ours/'vehicle_east.png',False,400),('RimThunder hull',rim/'vehicle_east.png',False,400),
       ('Legion turret',legion/'LGIFVturret_autocannon.png',True,300),('Current turret',ours/'turret_top_full_east.png',False,300),('RimThunder turret',rim/'vehicle_turret_full.png',True,300)]
board=Image.new('RGB',(1320,720),(92,92,92));d=ImageDraw.Draw(board);metrics={}
for i,(label,path,rotate,width) in enumerate(items):
    im=Image.open(path).convert('RGBA');mask=im.getchannel('A').point(lambda a:255 if a>20 else 0);box=mask.getbbox()
    im=im.crop(box)
    if rotate:im=im.transpose(Image.Transpose.ROTATE_270)
    original_size=im.size;im=im.convert('RGBa').resize((width,round(im.height*width/im.width)),Image.Resampling.LANCZOS).convert('RGBA')
    gray=ImageOps.grayscale(im);gray=Image.merge('RGBA',(gray,gray,gray,im.getchannel('A')))
    x=(i%3)*440+20;y=(i//3)*350+40
    board.paste(gray,(x,y),gray);d.text((x,y-25),label,fill='white')
    metrics[label]={'source_alpha_bbox':box,'cropped_size_before_normalization':original_size,'comparison_width':width}
d.text((20,690),'Grayscale, equal hull width / equal turret+barrel width. Diagnostic comparison, not an asset.',fill='white')
board.save(HERE/'style_comparison.png')
for size in (256,512):
    im=Image.open(ours/str(size)/'preview_east.png').convert('RGBA');box=im.getchannel('A').point(lambda a:255 if a>20 else 0).getbbox()
    metrics[str(size)]={'alpha_bbox':box,'visible_width':box[2]-box[0]}
(HERE/'style_comparison_metrics.json').write_text(json.dumps(metrics,indent=2))
