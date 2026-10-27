"""Compare true XML sprite sizes at 64 pixels per game tile, without bbox fitting."""
from pathlib import Path
import os,json,xml.etree.ElementTree as ET
from theme_preview import game_image,save_review
from PIL import Image,ImageDraw,ImageFont
OUT=Path(__file__).resolve().parent;ROOT=OUT.parent.parent;REVIEW=OUT/'Review'
REVIEW.mkdir(exist_ok=True);PPT=64
font_path=Path(os.environ.get('WINDIR','C:/Windows'))/'Fonts/segoeui.ttf'
font=ImageFont.truetype(str(font_path),17);small=ImageFont.truetype(str(font_path),14)
defs={}
for file in [ROOT/'Defs/WildWest/Buildings/Industry_WildWest.xml',ROOT/'Defs/WildWest/Buildings/Radio.xml',ROOT/'Defs/GreatWar/Buildings/Industry_GreatWar.xml']:
    defs.update({n.findtext('defName'):n for n in ET.parse(file).getroot().findall('ThingDef')})
items=[('HD_Radio','Radio','1x1 reference'),('HD_LineShaftMillingMachine','Milling machine','2x1 reference'),
       ('HD_LineShaftTurretLathe','Turret lathe','2x1 reference'),('HD_SmallSteamEngine','Small steam plant','2x2 updated'),
       ('HD_LargeSteamEngine','Large steam engine','4x3 updated'),('HD_IndustrialDieselEngine','Stationary diesel','4x3 updated'),
       ('HD_EarlyIndustrialGenerator','DC dynamo','3x3 updated'),('HD_EarlyIndustrialLargeMotor','Electric motor','3x3 updated'),
       ('HD_HaberBoschHighPressureReactor','High-pressure reactor','3x3 new, circa 1950'),('HD_StirredTankReactor','Stirred reaction kettle','3x3 new, circa 1950')]
def values(text):return [float(v) for v in text.strip('()').split(',')]
report={'pixels_per_game_tile':PPT,'bbox_normalization':False,'assets':{}}
def tile(stem,title,subtitle,before=False):
    node=defs[stem];size=values(node.findtext('size'));draw=values(node.findtext('graphicData/drawSize'))
    offset=values(node.findtext('graphicData/drawOffset','(0,0,0)'))
    if before:
        validation=json.loads((ROOT/'ArtSource'/folders[stem]/'validation.json').read_text())
        draw=values(validation['previous_draw_size']);offset=values(validation['previous_draw_offset'])
    path=REVIEW/'BeforeScale'/f'{stem}_north.png' if before else ROOT/'Textures/Buildings'/f'{stem}_north.png'
    im=game_image(path);box=im.getbbox();width=box[2]-box[0]
    board=Image.new('RGBA',(416,430),(70,73,72,255));d=ImageDraw.Draw(board)
    d.text((12,8),title,font=font,fill=(244,244,240));d.text((12,32),subtitle,font=small,fill=(188,202,194))
    d.text((12,52),f'drawSize {draw[0]:g}, {draw[1]:g} / {PPT}px per tile',font=small,fill=(165,180,171))
    cx,cy=208,253
    left=cx-size[0]*PPT/2;top=cy-size[1]*PPT/2
    for i in range(int(size[0])+1):
        x=round(left+i*PPT);d.line((x,round(top),x,round(top+size[1]*PPT)),fill=(98,103,99))
    for i in range(int(size[1])+1):
        y=round(top+i*PPT);d.line((round(left),y,round(left+size[0]*PPT),y),fill=(98,103,99))
    im=im.resize((round(draw[0]*PPT),round(draw[1]*PPT)),Image.Resampling.LANCZOS)
    board.alpha_composite(im,(round(cx-im.width/2+offset[0]*PPT),round(cy-im.height/2-offset[2]*PPT)))
    if not before:
        report['assets'][stem]={'footprint':size,'draw_size':draw,'draw_offset':offset,'image_size':Image.open(path).size,
                              'north_opaque_width_in_tiles':width*draw[0]/Image.open(path).width}
    return board
context=Image.new('RGBA',(1664,1290),(70,73,72,255))
for i,(stem,title,subtitle) in enumerate(items):context.alpha_composite(tile(stem,title,subtitle),(i%4*416,i//4*430))
ImageDraw.Draw(context).text((860,968),'Same game scale. Grid = one tile.\nTransparent sprites; gray is review background.\n12px stroke; canvas grows 256px per footprint tile.',font=font,fill=(215,224,219),spacing=12)
save_review(context.convert('RGB'),REVIEW/'tile_scale_comparison.png')
folders={'HD_SmallSteamEngine':'SmallSteamEngine','HD_LargeSteamEngine':'LargeSteamEngine',
         'HD_IndustrialDieselEngine':'IndustrialDieselEngine','HD_EarlyIndustrialGenerator':'EarlyIndustrialGenerator',
         'HD_EarlyIndustrialLargeMotor':'EarlyIndustrialMotor'}
compare=Image.new('RGBA',(832,2150),(70,73,72,255))
for i,(stem,title,subtitle) in enumerate(items[3:8]):
    compare.alpha_composite(tile(stem,title,'Before size correction',True),(0,i*430))
    compare.alpha_composite(tile(stem,title,'After size correction'),(416,i*430))
save_review(compare.convert('RGB'),REVIEW/'power_scale_before_after.png')
reactors=Image.new('RGBA',(1536,1120),(70,73,72,255))
for row,(stem,title,subtitle) in enumerate(items[8:]):
    for col,direction in enumerate(['north','east','south']):
        x,y=col*512,row*560
        ImageDraw.Draw(reactors).text((x+15,y+10),title+' / '+direction.title(),font=font,fill=(244,244,240))
        im=game_image(ROOT/'Textures/Buildings'/f'{stem}_{direction}.png').resize((512,512),Image.Resampling.LANCZOS)
        reactors.alpha_composite(im,(x,y+36))
save_review(reactors.convert('RGB'),REVIEW/'reactors_north_east_south.png')
fronts=[game_image(ROOT/'Textures/Buildings'/f'{stem}_north.png').resize((512,512),Image.Resampling.LANCZOS) for stem,title,subtitle in items[8:]]
top=min(im.getbbox()[1] for im in fronts)-8;bottom=max(im.getbbox()[3] for im in fronts)+8
north=Image.new('RGBA',(1024,bottom-top+52),(70,73,72,255))
for col,(stem,title,subtitle) in enumerate(items[8:]):
    ImageDraw.Draw(north).text((col*512+15,10),title+' / North',font=font,fill=(244,244,240))
    north.alpha_composite(fronts[col].crop((0,top,512,bottom)),(col*512,36))
save_review(north.convert('RGB'),REVIEW/'reactors_north.png')
(OUT/'presentation_calibration.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('Saved reference-anchored tile-scale comparisons and reactor direction sheet.')
