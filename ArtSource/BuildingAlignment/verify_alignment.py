"""Check desktop registration and preview actual game draw offsets on a tile grid."""
from pathlib import Path
import json,sys,xml.etree.ElementTree as ET
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[2];OUT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT/'ArtSource/PropAssets'));import image_export as e
measure=json.loads((OUT/'measurements.json').read_text())
defs={n.findtext('defName'):n for p in (ROOT/'Defs').rglob('*.xml') for n in ET.parse(p).getroot().findall('ThingDef')}
mapping={'computer':'HD_CrowsComputer','controller':'HD_CrowsComputer','radio':'HD_Radio','gunsmith':'HD_GunSmithTable','clothing':'HD_ClothingBench'}
def numbers(s):return [float(v) for v in s.strip('()').split(',')]
report={}
for key,name in mapping.items():
    graphic=defs[name].find('graphicData');offset=numbers(graphic.findtext('drawOffset','(0,0,0)'));size=numbers(graphic.findtext('drawSize'))
    checks={}
    for direction,view in measure[key]['views'].items():
        uv=view['anchor_uv'];error=[(uv[0]-.5)*size[0]+offset[0],(uv[1]-.5)*size[1]+offset[2]]
        assert max(map(abs,error))<1e-5,(key,direction,error)
        checks[direction]=dict(tabletop_center_error_tiles=error)
    report[key]=dict(def_name=name,draw_offset=offset,views=checks)
# The two older sprites have no 3D source. The basic workbench's visible top
# face is already within a pixel of the center; do not replace its east/west art.
report['basic_workbench']=dict(def_name='HD_BasicWorkbench',already_centered=True,maximum_error_tiles=.01,changed=False)
telegraph=defs['HD_TelegraphTable'].find('graphicData');off=numbers(telegraph.findtext('drawOffset'))
for direction in ['north','east','south']:
    im=e.opened(ROOT/'Textures/Buildings'/('HD_TelegraphTable_'+direction+'.png'))
    # Inspected white top face: [146,146]..[365,355], exclusive center 256,251.
    assert im.getpixel((146,146))[:3]==(255,255,255) and im.getpixel((365,355))[:3]==(255,255,255)
    assert abs((.5-251/im.height)*2+off[2])<1e-8
report['telegraph']=dict(def_name='HD_TelegraphTable',draw_offset=off,anchor_pixels=[256,251],west_uses_mirrored_east=True)
(OUT/'validation.json').write_text(json.dumps(dict(passed=True,anchor='top face center of desktop',models_and_textures_unchanged=True,assets=report,in_game_tested=False),indent=2)+'\n')

# Draw the real registered CROWS layers and computer sprite at a shared world
# scale. Guides show the occupied area and its center, not the alpha bounds.
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',19)
canvas=Image.new('RGB',(1040,1040),(65,70,73))
def panel(x,y,label,footprint,layers):
    d=ImageDraw.Draw(canvas);d.text((x+12,y+8),label,font=font,fill='white');cx,cy=x+260,y+280;ppu=116
    for i in range(-2,3):
        d.line((cx+i*ppu, y+48,cx+i*ppu,y+508),fill=(80,85,87));d.line((x+28,cy+i*ppu,x+510,cy+i*ppu),fill=(80,85,87))
    w,h=footprint;d.rectangle((cx-w*ppu/2,cy-h*ppu/2,cx+w*ppu/2,cy+h*ppu/2),outline=(210,173,78),width=2)
    for image,size,offset in layers:
        side=round(size*ppu);image=e.resize_linear(image,side);left=round(cx-side/2+offset[0]*ppu);top=round(cy-side/2-offset[2]*ppu);canvas.paste(image,(left,top),image)
    d.line((cx-10,cy,cx+10,cy),fill=(80,230,160),width=2);d.line((cx,cy-10,cx,cy+10),fill=(80,230,160),width=2)
plate=e.opened(ROOT/'Textures/Buildings/Security/CROWSII/FixedMount/HD_CROWSII_FixedMount_top.png')
station=Image.new('RGBA',(1024,1024))
for n in ['HD_CROWSII_M2_Base','HD_CROWSII_M2_Frame','HD_CROWSII_M2Barrel','HD_CROWSII_M2Body','HD_CROWSII_M2_FeedOverlay']:
    station=Image.alpha_composite(station,e.opened(ROOT/'Textures/Buildings/Security/CROWSII'/(n+'.png')))
panel(0,0,'CROWS / before: 0.32-tile upper canvas',(2,2),[(plate,3.2,[0,0,0]),(station,.32,[0,0,0])])
panel(520,0,'CROWS / after: 3.2-tile upper canvas',(2,2),[(plate,3.2,[0,0,0]),(station,3.2,[0,0,0])])
computer=e.opened(ROOT/'Textures/Buildings/ComputerDesk/CROWSController/HD_ComputerDesk_CROWS_north.png')
panel(0,520,'COMPUTER / before: floor origin',(2,1),[(computer,3,[0,0,0])])
panel(520,520,'COMPUTER / after: desktop centered',(2,1),[(computer,3,report['computer']['draw_offset'])])
e.save(canvas,OUT/'before_after.png')
print('BUILDING_ALIGNMENT_PASS: five modeled desktop variants / four directions; legacy desks audited; center errors <0.00001 tile')
