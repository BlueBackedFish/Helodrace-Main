"""Compare original and current sprites at one actual game scale; review only."""
from pathlib import Path
import io,json,subprocess,xml.etree.ElementTree as ET
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[2];OUT=Path(__file__).resolve().parent
font=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',17)
small=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',14)
def old_bytes(path):return subprocess.check_output(['git','show','36dae884:'+path],cwd=ROOT)
paths=['Defs/WildWest/Buildings/Industry_WildWest.xml','Defs/GreatWar/Buildings/Industry_GreatWar.xml',
       'Defs/ColdWar/Buildings/Industry_ColdWar.xml','Defs/WildWest/Buildings/Radio.xml',
       'Defs/WildWest/Buildings/ClothingBench.xml']
old={};current={}
for path in paths:
    for nodes,dest in [(ET.fromstring(old_bytes(path)),old),(ET.parse(ROOT/path).getroot(),current)]:
        for n in nodes.findall('ThingDef'):dest[n.findtext('defName')]=n
def values(t):return [float(v) for v in t.strip('()').split(',')]
def cell(stem,title,before):
    node=(old if before else current)[stem];draw=values(node.findtext('graphicData/drawSize'))
    offset=values(node.findtext('graphicData/drawOffset','(0,0,0)'));footprint=values(node.findtext('size'))
    path='Textures/Buildings/'+node.findtext('graphicData/texPath').split('/')[-1]+'_north.png'
    im=Image.open(io.BytesIO(old_bytes(path)) if before else ROOT/path).convert('RGBA');resolution=im.width
    board=Image.new('RGBA',(448,400),(190,190,187,255));d=ImageDraw.Draw(board);cx,cy=224,255
    d.text((12,9),title+(' / BEFORE' if before else ' / AFTER'),font=font,fill=(30,30,28))
    d.text((12,34),f'{resolution}px / drawSize {draw[0]:g} / grid = 1 tile',font=small,fill=(50,50,46))
    w,h=footprint
    for x in range(int(w)+1):d.line((cx+(x-w/2)*64,cy-h*32,cx+(x-w/2)*64,cy+h*32),fill=(163,165,159))
    for y in range(int(h)+1):d.line((cx-w*32,cy+(y-h/2)*64,cx+w*32,cy+(y-h/2)*64),fill=(163,165,159))
    im=im.resize((round(draw[0]*64),round(draw[1]*64)),Image.Resampling.LANCZOS)
    board.alpha_composite(im,(round(cx-im.width/2+offset[0]*64),round(cy-im.height/2-offset[2]*64)))
    return board
items=[('HD_Radio','Radio'),('HD_SAFurnace','Arc furnace'),('HD_BasicPumpJack','Timber pumpjack')]
sheet=Image.new('RGBA',(896,1240),(190,190,187,255))
ImageDraw.Draw(sheet).text((12,8),'Same game scale: 64px per tile. Canvas density and outline comparison.',font=font,fill=(30,30,28))
for i,(stem,title) in enumerate(items):
    for col,before in enumerate([True,False]):sheet.alpha_composite(cell(stem,title,before),(col*448,38+i*400))
sheet.convert('RGB').save(OUT/'density_before_after.png')
catalog=[]
for path in (ROOT/'ArtSource').rglob('validation.json'):
    try:m=json.loads(path.read_text())
    except (ValueError,UnicodeError):continue
    if not isinstance(m,dict) or not m.get('density_export_preserves_geometry_and_palette'):continue
    catalog.append({'asset':m['asset'],'source_folder':str(path.parent.relative_to(ROOT)).replace('\\','/'),
                    'footprint':m['footprint'],'resolution':m['texture_resolution'],'draw_size':m['expected_draw_size'],
                    'draw_offset':m['expected_draw_offset'],'outline_pixels':m['outline_pixels'],
                    'pixels_per_game_unit':m['pixels_per_game_unit'],'outline_width_in_game_units':m['outline_width_in_game_units']})
catalog.sort(key=lambda a:a['asset']);assert len(catalog)==25
(OUT/'density_catalog.json').write_text(json.dumps(catalog,indent=2))
print('Saved fixed-game-scale before/after comparison and 25-building density catalog.')
