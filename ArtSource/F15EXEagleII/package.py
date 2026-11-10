"""Export clean F-15EX art at the shared C-17/F-22 physical display scale."""
from pathlib import Path
import json,sys,hashlib
import numpy as np
from PIL import Image,ImageDraw

ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';REPO=ROOT.parents[1]
sys.path.insert(0,str(ROOT.parent/'VehiclePipeline'));from images import resize_linear,sheet,font
sys.path.insert(0,str(ROOT.parent/'PropAssets'));from image_export import outline
style=json.loads((ROOT/'style.json').read_text(encoding='utf-8'));model=json.loads((OUT/'model_validation.json').read_text(encoding='utf-8'))
size=model['delivery_resolution'];master=Image.open(OUT/'top_plain_2048.png').convert('RGBA')
target=REPO/'Textures/Effects/Aircraft/F15EX';target.mkdir(parents=True,exist_ok=True)
validation={};images={}
for label,im in [('plain',master),('outlined',outline(master,style['outline_pixels']*master.width/size))]:
    im=resize_linear(im,size);a=np.asarray(im);bbox=im.getbbox();margin=min(bbox[0],bbox[1],size-bbox[2],size-bbox[3])
    assert im.mode=='RGBA' and im.size==(size,size) and margin>=12
    assert not a[a[:,:,3]==0,:3].any()
    path=OUT/f'HD_F15EX_{label}.png';im.save(path);images[label]=im
    validation[label]=dict(sha256=hashlib.sha256(path.read_bytes()).hexdigest(),bounds=list(bbox),margin=margin)
images['outlined'].save(OUT/'HD_F15EX.png');images['outlined'].save(target/'HD_F15EX.png')
assert (OUT/'HD_F15EX.png').read_bytes()==(target/'HD_F15EX.png').read_bytes()
backgrounds=[]
for label,rgb in [('LIGHT',(220,222,225)),('GRAY',(91,97,104)),('DARK',(31,37,44))]:
    bg=Image.new('RGBA',(size,size),rgb+(255,));bg.alpha_composite(images['outlined']);backgrounds.append((label,bg))
sheet(backgrounds,OUT/'preview.png',columns=3,cell_size=(400,440))
sheet([('TOP / CLEAN CONFIGURATION',images['outlined']),('OBLIQUE / NO EXTERNAL STORES',Image.open(OUT/'oblique.png'))],OUT/'model_review.png',columns=2,cell_size=(520,560))
sheet([('BEFORE',Image.open(ROOT/'analysis/panel_colors_before.png')),
    ('PANEL TONES',images['outlined']),('EMPTY MOUNTS / UNDERSIDE',Image.open(OUT/'mounts_underside.png'))],
    OUT/'panel_color_review.png',columns=3,cell_size=(460,510))
review=Image.new('RGB',(840,590),(91,97,104));draw=ImageDraw.Draw(review)
for n,x,y in [(256,20,150),(512,300,52)]:
    small=resize_linear(images['outlined'],n);review.paste(small,(x,y),small);draw.text((x,y-32),f'{n}px / actual pixels',font=font(16),fill='white')
review.save(OUT/'scale_review.png')
comparison=Image.new('RGBA',(1140,700),(64,71,78,255));draw=ImageDraw.Draw(comparison)
draw.text((40,20),'F-22 / F-15EX — SAME PHYSICAL SCALE',font=font(25),fill='white')
for title,image,tiles,center in [('F-22 Raptor',Image.open(ROOT.parent/'F22Raptor/output/HD_F22.png'),json.loads((ROOT.parent/'F22Raptor/style.json').read_text())['draw_size_tiles'][0],300),('F-15EX Eagle II / clean',images['outlined'],model['draw_size_tiles'][0],850)]:
    side=round(tiles*70);small=resize_linear(image,side);comparison.alpha_composite(small,(round(center-side/2),110))
    draw.text((center-150,75),title,font=font(22),fill='white')
draw.text((40,650),'C-17 = 19 tiles; F-22 = 18.9 m; F-15EX = 19.4 m. Uniform scaling; original model proportions retained.',font=font(17),fill=(205,212,217))
comparison.convert('RGB').save(OUT/'F22_F15EX_real_scale.png')
if (OUT/'nozzles_neutral.png').exists():
    sheet([(title,Image.open(OUT/f'nozzles_{name}.png')) for title,name in
        [('CONTRACTED','contracted'),('NEUTRAL / MATCHED PAIR','neutral'),('OPEN','open')]],
        OUT/'nozzle_review.png',columns=3,cell_size=(400,440))
    sheet([('NEUTRAL',Image.open(OUT/'controls_neutral.png')),
        ('8 CONTROL SURFACES / OPEN NOZZLES',Image.open(OUT/'controls_pose_b.png'))],
        OUT/'articulation_review.png',columns=2,cell_size=(520,560))
    before=Image.open(ROOT/'analysis/engine_before.png').convert('RGBA')
    crop=(330,640,892,1200)
    sheet([('BEFORE',before.crop(crop)),('LOWER SHOULDER / TAIL SHADE',images['outlined'].crop(crop))],
        OUT/'engine_paint_review.png',columns=2,cell_size=(450,500))
    nose=(500,30,722,350)
    sheet([('BEFORE',Image.open(ROOT/'analysis/panel_colors_before.png').crop(nose)),('STRAIGHT RADOME PAINT',images['outlined'].crop(nose))],
        OUT/'radome_paint_review.png',columns=2,cell_size=(300,480))
    frames=[]
    for i in range(7):
        path=OUT/f'motion_{i:02}.png'
        if not path.exists():break
        bg=Image.new('RGBA',(640,640),(64,71,78,255));bg.alpha_composite(Image.open(path).convert('RGBA'))
        ImageDraw.Draw(bg).text((18,18),'F-15EX / control surfaces + variable exhaust',font=font(20),fill='white')
        frames.append(bg.convert('RGB').convert('P',palette=Image.Palette.ADAPTIVE,colors=256))
    if len(frames)==7:
        frames[0].save(OUT/'articulation.gif',save_all=True,append_images=frames[1:],duration=[350,180,350,180,350,180,350],loop=0,disposal=2)
        for i in range(7):(OUT/f'motion_{i:02}.png').unlink()
(OUT/'texture_validation.json').write_text(json.dumps(dict(passed=True,resolution=[size,size],outline_pixels=12,
    external_weapons=0,external_drop_tanks=0,targeting_pods=0,draw_size_tiles=model['draw_size_tiles'],
    reference='C-17 19 tiles',transparent_background=True,pivot_normalized=[.5,.5],images=validation),indent=2)+'\n',encoding='utf-8')
print('F15EX_TEXTURE_PASS:',size,'px RGBA; 12px outline; clean external-store configuration')
