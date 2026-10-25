"""Aircraft-wide silhouette plus muted internal panel lines; native-size review."""
from pathlib import Path
import hashlib,json,sys
import numpy as np
from PIL import Image,ImageDraw,ImageFilter,ImageChops

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent/'VehiclePipeline'))
from images import resize_linear,composite_linear,font,check_margin
from outline_review import black_outline

style=json.loads((HERE/'style.json').read_text(encoding='utf-8'))
out=HERE/'output'
source=out/'top_plain_2048.png'
source_hash=hashlib.sha256(source.read_bytes()).hexdigest()
master=Image.open(source).convert('RGBA')
mask=Image.open(out/'panel_mask_2048.png').convert('RGBA').getchannel('R')
radius=round(style['internal_panel_pixels_at_1024']*master.width/1024)
eroded=mask.filter(ImageFilter.MinFilter(2*radius+1))
edge=ImageChops.subtract(mask,eroded)
edge=ImageChops.multiply(edge,master.getchannel('A'))
panel=Image.new('RGBA',master.size,tuple(style['internal_panel_rgb'])+(0,))
panel.putalpha(edge)
plain=composite_linear(master,panel,(0,0))
outlined=black_outline(plain,style['silhouette_outline_pixels_at_1024']*master.width/1024)
images={}
validation={}
for kind,image in [('Plain',plain),('Outlined',outlined)]:
    for size in style['sizes']:
        root=out/'Textures'/kind/str(size);root.mkdir(parents=True,exist_ok=True)
        scaled=resize_linear(image,size)
        file=root/'HD_C17.png'
        scaled.save(file)
        pixels=np.asarray(scaled)
        assert scaled.mode=='RGBA' and scaled.size==(size,size)
        assert not pixels[pixels[:,:,3]==0,:3].any()
        bounds=check_margin(scaled,str(file))
        assert bounds and min(bounds[0],bounds[1],size-bounds[2],size-bounds[3])>=2
        validation[f'{kind}/{size}']=dict(bounds=bounds,visible_width=bounds[2]-bounds[0],
            outline_pixels=style['silhouette_outline_pixels_at_1024']*size/1024 if kind=='Outlined' else 0,
            sha256=hashlib.sha256(file.read_bytes()).hexdigest())
        images[kind,size]=scaled
# Default deliverable follows the existing 1024px aircraft texture convention.
images['Outlined',1024].save(out/'HD_C17.png')
board=Image.new('RGB',(980,640),(72,76,81));draw=ImageDraw.Draw(board)
for x,size in [(12,128),(168,256),(454,512)]:
    im=images['Outlined',size]
    board.paste(im,(x,55),im)
    draw.text((x,10),f'{size}px',font=font(16),fill='white')
    draw.text((x,31),f'Outline {style["silhouette_outline_pixels_at_1024"]*size/1024:g}px',font=font(12),fill='white')
draw.text((14,595),'C-17 / exact top view / flat fixed colors / one aircraft silhouette',font=font(19),fill='white')
draw.text((14,620),'No lights, AO, cast shadow or photographic source albedo. Trial texture only.',font=font(15),fill='white')
board.save(out/'native_sizes.png')
board=Image.new('RGB',(1536,570));draw=ImageDraw.Draw(board)
for i,color in enumerate([(230,230,225),(96,100,105),(29,32,36)]):
    draw.rectangle((512*i,0,512*(i+1),570),fill=color)
    im=images['Outlined',512];board.paste(im,(512*i,32),im)
    draw.text((512*i+12,12),str(color),font=font(14),fill='black' if i==0 else 'white')
board.save(out/'alpha_backgrounds.png')
references=json.loads((HERE/'analysis/aircraft_references.json').read_text(encoding='utf-8'))
refs={entry['label']:entry for entry in references}
board=Image.new('RGB',(1440,610),(82,86,91));draw=ImageDraw.Draw(board)
for i,(label,path) in enumerate([
    ('Main A-10C',Path(refs['Main A-10C']['path'])),
    ('C-17 trial',out/'HD_C17.png'),
    ('Arsenal DH-4',Path(refs['Arsenal DH-4']['path']))]):
    im=Image.open(path).convert('RGBA');im=im.crop(im.getchannel('A').getbbox())
    # Same visible wingspan, not matching canvas widths.
    factor=448/im.width;im=im.resize((448,round(im.height*factor)),Image.Resampling.LANCZOS)
    board.paste(im,(i*480+16,55+(505-im.height)//2),im)
    draw.text((i*480+14,14),label,font=font(23),fill='white')
draw.text((14,580),'448px visible wingspan each / style comparison only; different airframes',font=font(20),fill='white')
board.save(out/'style_comparison.png')
assert hashlib.sha256(source.read_bytes()).hexdigest()==source_hash
report=dict(status='passed',projection='Exactly top-down orthographic; nose points up',
    raw_render_sha256=source_hash,raw_render_unchanged=True,files=validation,
    silhouette='Black circular alpha dilation beneath the entire aircraft, before downsampling',
    internal_lines='Muted gray inside selected control-surface/wing-detail/engine-lip/glass boundaries',
    filter='Linear-light premultiplied-alpha Lanczos',
    pivot='Canvas center at each resolution; no trim or per-size recentering',
    native_size_visual_review=False,reference_style_visual_review=False,
    game_validation_performed=False,blueprint_validation_performed=False)
(out/'texture_validation.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print('C17_TEXTURE_PACKAGE_PASS: 8 transparent PNGs, native sizes, silhouette and alpha checks')
