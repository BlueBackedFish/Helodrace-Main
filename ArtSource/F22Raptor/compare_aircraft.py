"""Compose the restored F-22 and existing C-17 at one physical scale."""
from pathlib import Path
import json,sys,hashlib
import numpy as np
from PIL import Image,ImageDraw

ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';C17=ROOT.parent/'C17Globemaster'
sys.path.insert(0,str(ROOT.parent/'VehiclePipeline'));from images import resize_linear,font
calibration=json.loads((OUT/'physical_scale.json').read_text(encoding='utf-8'))
f22_model=json.loads((OUT/'model_validation.json').read_text(encoding='utf-8'))
c17_model=json.loads((C17/'output/model_validation.json').read_text(encoding='utf-8'))
pixels_per_meter=20;pixels_per_tile=pixels_per_meter/calibration['tiles_per_meter']
board=Image.new('RGBA',(1800,1420),(64,71,78,255));draw=ImageDraw.Draw(board)
for x in range(140,1750,100):draw.line((x,160,x,1280),fill=(75,82,89),width=1)
for y in range(180,1281,100):draw.line((125,y,1740,y),fill=(75,82,89),width=1)
draw.text((140,30),'AIRCRAFT SCALE COMPARISON',font=font(32),fill='white')
draw.text((140,78),'Same top view, same meters per pixel; original aircraft proportions retained.',font=font(21),fill=(203,210,216))
draw.line((1230,130,1230,1290),fill=(109,118,126),width=2)

records=[]
for name,path,plain_path,tiles,center,length,span,model in [
    ('C-17 Globemaster III',C17/'output/HD_C17.png',C17/'output/Textures/Plain/HD_C17.png',19,660,53,51.75,c17_model),
    ('F-22 Raptor',OUT/'HD_F22.png',OUT/'HD_F22_plain.png',calibration['f22_draw_size_tiles'][0],1470,18.9,13.6,f22_model)]:
    image=Image.open(path).convert('RGBA');side=round(tiles*pixels_per_tile)
    sprite=resize_linear(image,side);plain=resize_linear(Image.open(plain_path).convert('RGBA'),side)
    alpha=np.asarray(plain)[:,:,3];yy,xx=np.where(alpha>=128)
    # Align actual noses, excluding transparent margins and outside outlines.
    left=round(center-side/2);top=180-int(yy.min());board.alpha_composite(sprite,(left,top))
    draw.text((left+20,125),name,font=font(26),fill='white')
    if name.startswith('C-17'):
        lo,hi=model['used_bounds'];native_length=hi[1]-lo[1];native_span=hi[0]-lo[0]
    else:
        lo,hi=model['source_pose_bounds'];native_length=hi[0]-lo[0];native_span=hi[1]-lo[1]
    inferred_span=native_span/native_length*length
    assert abs(inferred_span/span-1)<.005
    raster_length=(int(yy.max())-int(yy.min())+1)/pixels_per_meter
    assert abs(raster_length/length-1)<.01
    label_y=180+int(yy.max()-yy.min())+38
    draw.text((center-130,label_y),f'{length:g} m long / {span:g} m span',font=font(21),fill=(225,230,234))
    records.append(dict(aircraft=name,source=str(path.relative_to(ROOT.parents[1])),
        texture_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
        canvas_tiles=tiles,comparison_canvas_pixels=side,length_m=length,wingspan_m=span,
        model_inferred_wingspan_m=inferred_span,model_span_relative_error=inferred_span/span-1,
        raster_length_m=raster_length,native_model_proportions_preserved=True))

for meters in range(0,56,5):
    y=180+meters*pixels_per_meter;draw.line((105,y,124,y),fill=(205,212,217),width=2)
    draw.text((35,y-10),f'{meters} m',font=font(18),fill=(205,212,217))
draw.line((120,180,120,1280),fill=(205,212,217),width=2)
lines=[('REAL DIMENSIONS',26),('F-22 length / C-17: 35.66%',21),('F-22 span / C-17: 26.28%',21),
    ('',10),('ARTWORK DISPLAY SIZE',24),('C-17: 19 x 19 tiles',21),
    (f'F-22: {calibration["f22_draw_size_tiles"][0]:.3f} x {calibration["f22_draw_size_tiles"][0]:.3f} tiles',21),
    (f'F-22 texture: {f22_model["delivery_resolution"]} x {f22_model["delivery_resolution"]} px',21),('12 px outside outline on both assets',19)]
y=735
for text,size in lines:
    draw.text((1280,y),text,font=font(size),fill=(229,233,237));y+=size+17
draw.rectangle((1280,1195,1380,1207),fill=(225,230,234))
draw.rectangle((1380,1195,1480,1207),fill=(121,132,142),outline=(225,230,234),width=1)
for x,label in [(1280,'0'),(1380,'5'),(1480,'10 m')]:draw.text((x-7,1220),label,font=font(18),fill='white')
draw.text((140,1360),'Dimensions: USAF fact sheets. C-17 kept at 19 tiles; F-22 calibrated by body length, excluding padding/outline.',font=font(19),fill=(205,212,217))
board.convert('RGB').save(OUT/'F22_C17_real_scale.png')
(OUT/'comparison_validation.json').write_text(json.dumps(dict(passed=True,pixels_per_meter=pixels_per_meter,
    uniform_scale=True,noses_aligned=True,aircraft=records,calibration='physical_scale.json',
    comparison_image='F22_C17_real_scale.png'),indent=2)+'\n',encoding='utf-8')
print('AIRCRAFT_COMPARISON_PASS: one physical scale; both model spans within 0.5% of USAF dimensions')
