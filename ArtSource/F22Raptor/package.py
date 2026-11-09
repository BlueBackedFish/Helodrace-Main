"""Outline the aircraft; internal joints come exclusively from the 3D model."""
from pathlib import Path
import json,sys,hashlib,os,tempfile
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
before=ROOT/'analysis/paint_before.png'
if before.exists():
    sheet([('BEFORE / SOURCE PANELS AND MARKINGS',Image.open(ROOT/'analysis/cleanup_before.png')),
        ('CLEAN CAMOUFLAGE / MODELED DETAILS',images['Outlined'])],
        OUT/'paint_review.png',columns=2,cell_size=(540,580))
motion=OUT/'articulation_review'
if (motion/'controls_neutral.png').exists():
    frames=[]
    for label in ['neutral','negative','positive','return']:
        image=Image.open(motion/('controls_'+label+'.png')).convert('RGBA')
        bg=Image.new('RGBA',image.size,(91,97,104,255));bg.alpha_composite(image);frames.append(bg.convert('RGB'))
    # The desktop preview can still be reading the previous GIF. Publish a
    # complete replacement instead of truncating the displayed file in place.
    with tempfile.NamedTemporaryFile(dir=OUT,suffix='.gif',delete=False) as f:temporary=f.name
    frames[0].save(temporary,format='GIF',save_all=True,append_images=frames[1:],duration=[800,650,650,800],loop=0)
    os.replace(temporary,OUT/'articulation_preview.gif')
    sheet([(label.upper(),Image.open(motion/('tvc_'+label+'.png'))) for label in ['negative','neutral','positive']],
        OUT/'tvc_review.png',columns=3,cell_size=(400,440))
if (motion/'canopy_top.png').exists():
    sheet([('BEFORE / TOP',Image.open(ROOT/'analysis/canopy_before.png')),
        ('REFINED / TOP',Image.open(motion/'canopy_top.png')),
        ('REFINED / OBLIQUE',Image.open(motion/'canopy_oblique.png'))],
        OUT/'canopy_review.png',columns=3,cell_size=(400,440))
if (motion/'cannon_open.png').exists():
    sheet([(label,Image.open(motion/(name+'.png'))) for label,name in [
        ('CANNON / SHUTTER CLOSED','cannon_closed'),('CANNON / SHUTTER HIDDEN','cannon_open'),
        ('CURVED RADOME','radome_oblique'),('RECESSED DORSAL DETAILS','dorsal_details')]],
        OUT/'airframe_review.png',columns=2,cell_size=(500,540))
(OUT/'texture_validation.json').write_text(json.dumps(dict(passed=True,images=validation,resolution=[size,size],
    outline_pixels=style['outline_pixels'],internal_lines_from_model_geometry=True,
    one_outline_for_complete_aircraft=True,pivot_normalized=[.5,.5],nose_up=True,draw_size_tiles=None,
    moving_controls=12,source_subsystem_pose=650,model_neutral_frame=1,
    color_treatment='Original camouflage with source-painted panels/markings removed; crisp colors and closest sampling',
    detail_border_pixels=style['detail_border_pixels'],
    physical_recesses=len(report['airframe']['details']),cannon_shutter='closed',
    texture='Textures/Effects/Aircraft/F22/HD_F22.png',game_definition_added=False,in_game_tested=False),indent=2)+'\n')
(OUT/'canopy_validation.json').write_text(json.dumps(dict(passed=True,canopy=report['canopy']),indent=2)+'\n')
if before.exists():
    old_alpha=np.asarray(Image.open(before).convert('RGBA'))[:,:,3].astype(int)
    new_alpha=np.asarray(images['Outlined'])[:,:,3].astype(int)
    difference=np.abs(old_alpha-new_alpha)
    assert not difference[160:,:].any(),'Airframe silhouette changed outside the radome'
    ratio=float(new_alpha.sum()/old_alpha.sum());assert .97<ratio<1.03
    y,x=np.where(difference>0)
    (OUT/'silhouette_validation.json').write_text(json.dumps(dict(passed=True,baseline='9ebb9fa4',
        neutral_projected_alpha_area_ratio=ratio,
        alpha_change_bounds=[int(x.min()),int(y.min()),int(x.max()+1),int(y.max()+1)] if len(x) else None,
        alpha_unchanged_below_row=160,
        checks='Only the refined nose changes the outer silhouette; wing, tail and lower airframe alpha unchanged'),indent=2)+'\n')
print('F22_TEXTURE_PASS: 1024px RGBA; exact top; 12px outside outline; physical model joints without 2D seam painting')
