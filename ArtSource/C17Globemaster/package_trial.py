"""Export a 19-tile aircraft at the radio/machine absolute pixel density."""
from pathlib import Path
import hashlib,json,sys
import numpy as np
from PIL import Image,ImageDraw,ImageFilter,ImageChops

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent/'VehiclePipeline'))
from images import resize_linear,composite_linear,font,check_margin
from outline_review import black_outline
from surface_details import paint_details
from absolute_scale import reference_scale

style=json.loads((HERE/'style.json').read_text(encoding='utf-8'))
out=HERE/'output';size=style['game_resolution'];tiles=style['game_draw_size_tiles'][0]
model=json.loads((out/'model_validation.json').read_text())
assert model['game_resolution']==size and model['master_size']==style['master_size']
assert style['master_size']>=size,'Render at native delivery density or above; do not upscale the old trial'
references=reference_scale(HERE.parents[1])
assert abs(size-tiles*style['reference_pixels_per_tile'])<=.5
source=out/'top_plain_4096.png';source_hash=hashlib.sha256(source.read_bytes()).hexdigest()
master=Image.open(source).convert('RGBA');assert master.size==(style['master_size'],)*2
factor=master.width/size
mask=Image.open(out/'panel_mask_4096.png').getchannel('R')
radius=round(style['internal_panel_game_pixels']*factor)
edge=ImageChops.subtract(mask,mask.filter(ImageFilter.MinFilter(2*radius+1)))
edge=ImageChops.multiply(edge,master.getchannel('A'))
panel=Image.new('RGBA',master.size,tuple(style['internal_panel_rgb'])+(0,));panel.putalpha(edge)
plain=composite_linear(master,panel,(0,0))
details,detail_report=paint_details(out,style,model)
plain=composite_linear(plain,details,(0,0))
# Painting changes colors, not the source silhouette's fractional coverage.
plain.putalpha(master.getchannel('A'))
# One whole-aircraft outline, fixed at 12 delivery pixels, independent of span.
outlined=black_outline(plain,style['silhouette_outline_game_pixels']*factor)
images={};validation={}
for kind,image in [('Plain',plain),('Outlined',outlined)]:
    directory=out/'Textures'/kind;directory.mkdir(parents=True,exist_ok=True)
    delivered=resize_linear(image,size);path=directory/'HD_C17.png';delivered.save(path)
    pixels=np.asarray(delivered)
    assert delivered.mode=='RGBA' and delivered.size==(size,size)
    assert not pixels[pixels[:,:,3]==0,:3].any()
    bounds=check_margin(delivered,kind)
    assert bounds and min(bounds[0],bounds[1],size-bounds[2],size-bounds[3])>=8
    images[kind]=delivered
    validation[kind]=dict(size=[size,size],bounds=bounds,
        outline_pixels=style['silhouette_outline_game_pixels'] if kind=='Outlined' else 0,
        sha256=hashlib.sha256(path.read_bytes()).hexdigest())
images['Outlined'].save(out/'HD_C17.png')
scale=dict(draw_size_tiles=[tiles,tiles],resolution=[size,size],
    reference_pixels_per_tile=style['reference_pixels_per_tile'],actual_pixels_per_tile=size/tiles,
    rounding_error_pixels=size-tiles*style['reference_pixels_per_tile'],
    outline_pixels=12,outline_world_tiles=12/(size/tiles),
    internal_panel_pixels=style['internal_panel_game_pixels'],reference_assets=references,
    pivot_pixels=[size/2,size/2],pivot_normalized=[.5,.5],
    alpha_bounds_pixels=validation['Outlined']['bounds'],
    transparent_margin_note='The 19x19 world canvas includes a small transparent AA margin; source aspect ratio is preserved.',
    integration='Use drawSize (19,19), Graphic_Single, scale 1. This is a texture specification, not a new aircraft definition.')
(out/'scale.json').write_text(json.dumps(scale,indent=2)+'\n',encoding='utf-8')

# Compare full canvases at a common world scale, never normalize visible width.
pixels_per_tile=64;origin=(48,92);extent=round(tiles*pixels_per_tile)
board=Image.new('RGB',(1728,1408),(83,88,94));draw=ImageDraw.Draw(board)
draw.text((48,15),'C-17: 19 x 19 tiles / 3243px / fixed 12px outline',font=font(24),fill='white')
draw.text((48,48),'All objects below share 64 screen pixels per world tile',font=font(18),fill='white')
for n in range(20):
    x=origin[0]+n*pixels_per_tile;y=origin[1]+n*pixels_per_tile
    draw.line((x,origin[1],x,origin[1]+extent),fill=(104,109,115))
    draw.line((origin[0],y,origin[0]+extent,y),fill=(104,109,115))
preview=resize_linear(images['Outlined'],extent)
board.paste(preview,origin,preview)
for label,key,y in [('Radio / drawSize 1.5','HD_Radio',180),('Treadle lathe / drawSize 3','HD_TreadleLathe',470),('Milling machine / drawSize 3','HD_LineShaftMillingMachine',820)]:
    reference=references[key];im=Image.open(reference['texture']).convert('RGBA')
    actual=resize_linear(im,round(reference['draw_size_tiles'][0]*pixels_per_tile))
    draw.text((1300,y-40),label,font=font(18),fill='white');board.paste(actual,(1300,y),actual)
draw.text((48,1338),'Reference density: 256 / 1.5 = 512 / 3 = 170.6667 px per tile',font=font(21),fill='white')
draw.text((48,1370),'Outline world width: approximately 0.0703 tile on all objects',font=font(18),fill='white')
board.save(out/'absolute_scale.png');board.save(out/'style_comparison.png')

board=Image.new('RGB',(1536,570));draw=ImageDraw.Draw(board)
for i,color in enumerate([(230,230,225),(96,100,105),(29,32,36)]):
    draw.rectangle((i*512,0,(i+1)*512,570),fill=color)
    im=resize_linear(images['Outlined'],512);board.paste(im,(i*512,30),im)
    draw.text((i*512+12,12),str(color),font=font(14),fill='black' if i==0 else 'white')
draw.text((15,548),'512px review only; game texture is 3243px for a 19-tile drawSize',font=font(16),fill='white')
board.save(out/'alpha_backgrounds.png')

# Native-detail crops, with no enlargement of delivery pixels.
board=Image.new('RGB',(1560,600),(73,78,84));draw=ImageDraw.Draw(board)
cx,cy,_=model['camera_center'];ortho=model['ortho_scale']
def point(x,z):return (round((.5-(x-cx)/ortho)*size),round((.5-(z+cy)/ortho)*size))
for i,(label,x,z) in enumerate([('Fuselage roof panels / 1:1',203.949,86),('Nacelle covers and vents / 1:1',237.65,81),('Wing panels and fasteners / 1:1',274,37)]):
    px,py=point(x,z);crop=images['Outlined'].crop((px-250,py-250,px+250,py+250))
    board.paste(crop,(i*520+10,55),crop);draw.text((i*520+10,15),label,font=font(17),fill='white')
draw.text((12,570),'Native 3243px delivery crops; flat painted details follow visible source surfaces',font=font(18),fill='white')
board.save(out/'detail_review.png')
before=Image.open(HERE/'review_history/01-initial-trial/HD_C17.png').convert('RGBA')
board=Image.new('RGB',(1280,710),(82,87,93));draw=ImageDraw.Draw(board)
for i,(label,im) in enumerate([('Before: 1024px trial, displayed at 19 tiles',before),('After: 3243px, fixed 12px outline',images['Outlined'])]):
    im=resize_linear(im,608);board.paste(im,(i*640+16,60),im)
    draw.text((i*640+16,15),label,font=font(19),fill='white')
draw.text((16,680),'Both full canvases represent 19 x 19 world tiles; no equal-wingspan normalization',font=font(18),fill='white')
board.save(out/'before_after_19tiles.png')
# The old small exports were trial game candidates; do not leave them available
# as current 19-tile textures with a mismatched absolute density.
for kind in ['Plain','Outlined']:
    for old_size in [1024,512,256,128]:
        old=out/'Textures'/kind/str(old_size)/'HD_C17.png'
        if old.exists():old.unlink();old.parent.rmdir()
for name in ['top_plain_2048.png','panel_mask_2048.png','native_sizes.png','visual_review.json']:
    old=out/name
    if old.exists():old.unlink()
assert hashlib.sha256(source.read_bytes()).hexdigest()==source_hash
report=dict(status='passed',projection='Exact top orthographic, nose up',raw_render_sha256=source_hash,
    source_render_unchanged=True,source_silhouette_alpha_preserved=True,files=validation,scale=scale,details=detail_report,
    filter='Linear-light premultiplied-alpha Lanczos',
    native_detail_visual_review=False,absolute_scale_visual_review=False,
    game_validation_performed=False,blueprint_validation_performed=False)
(out/'texture_validation.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print('C17_19_TILE_PACKAGE_PASS: 3243x3243, drawSize 19x19, fixed 12px outline, preserved source surfaces')
