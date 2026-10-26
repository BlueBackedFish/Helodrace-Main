"""Labeled petroleum artwork sheets and true game-tile-scale comparison."""
from pathlib import Path
import os,json
from PIL import Image,ImageDraw,ImageFont
OUT=Path(__file__).resolve().parent;ROOT=OUT.parent.parent
fontpath=Path(os.environ.get('WINDIR','C:/Windows'))/'Fonts/segoeui.ttf'
font=ImageFont.truetype(str(fontpath),20);small=ImageFont.truetype(str(fontpath),15)
assets=[('HD_BasicPumpJack','BasicPumpJack','Timber basic pumpjack','Existing 6x3'),
        ('HD_MetalPumpJack','MetalPumpJack','Steel pumpjack','New artwork, proposed 6x3'),
        ('HD_DistillationTower','DistillationTower','Fractionation tower','Existing 3x3'),
        ('HD_RotaryDrill','RotaryDrill','1950s rotary drilling rig','New artwork, proposed 4x4')]
native=Image.new('RGBA',(1024,1024),(70,73,72,255))
world=Image.new('RGBA',(1280,960),(70,73,72,255))
views=Image.new('RGBA',(1536,2208),(70,73,72,255))
for i,(stem,folder,title,subtitle) in enumerate(assets):
    v=json.loads((ROOT/'ArtSource'/folder/'validation.json').read_text())
    color=Image.open(ROOT/'Textures/Buildings'/f'{stem}_north.png').convert('RGBA')
    x,y=(i%2)*512,(i//2)*512
    ImageDraw.Draw(native).text((x+14,y+12),title,font=font,fill='white')
    ImageDraw.Draw(native).text((x+14,y+39),subtitle,font=small,fill=(190,205,198))
    copy=color.crop(color.getbbox());copy.thumbnail((472,420),Image.Resampling.LANCZOS)
    native.alpha_composite(copy,(x+(512-copy.width)//2,y+70+(420-copy.height)//2))
    x,y=(i%2)*640,(i//2)*480;draw=ImageDraw.Draw(world);cx,cy=x+320,y+280
    draw.text((x+14,y+12),title,font=font,fill='white');draw.text((x+14,y+40),subtitle+' / 64px per tile',font=small,fill=(190,205,198))
    fw,fh=v['footprint'];left=cx-fw*32;top=cy-fh*32
    for col in range(fw+1):draw.line((left+col*64,top,left+col*64,top+fh*64),fill=(98,104,100))
    for row in range(fh+1):draw.line((left,top+row*64,left+fw*64,top+row*64),fill=(98,104,100))
    size=round(v['orthographic_scale']*64);copy=color.resize((size,size),Image.Resampling.LANCZOS)
    world.alpha_composite(copy,(cx-size//2,round(cy-size/2-v['expected_draw_offset'][2]*64)))
    for col,direction in enumerate(['north','east','south']):
        x,y=col*512,i*552
        ImageDraw.Draw(views).text((x+14,y+10),title+' / '+direction.title(),font=font,fill='white')
        views.alpha_composite(Image.open(ROOT/'Textures/Buildings'/f'{stem}_{direction}.png').convert('RGBA').resize((512,512),Image.Resampling.LANCZOS),(x,y+34))
native.convert('RGB').save(OUT/'overview_north.png')
world.convert('RGB').save(OUT/'tile_scale_comparison.png')
views.convert('RGB').save(OUT/'north_east_south.png')
print('Saved four-asset North sheet, 12-view sheet and actual tile-scale comparison.')
