"""Review sheets only: composite delivered PNGs without editing game textures."""
from pathlib import Path
import json
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[2];OUT=Path(__file__).resolve().parent
font=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',18)
small=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',14)
new=[('Electric arc furnace','HD_SAFurnace',6),('Vacuum induction furnace','HD_VIMFurnace',6),('Cable-tool rig','HD_CableToolRig',6)]
dirs=['north','east','south','west']
sheet=Image.new('RGB',(1280,1050),(190,190,187));d=ImageDraw.Draw(sheet)
d.text((15,9),'NEW MACHINES / North - East - South - West / 64 degrees',fill=(30,30,28),font=font)
for row,(label,stem,scale) in enumerate(new):
    y=42+row*330;d.text((12,y),label,fill=(30,30,28),font=font)
    for col,direction in enumerate(dirs):
        im=Image.open(ROOT/'Textures/Buildings'/f'{stem}_{direction}.png').convert('RGBA')
        edge=round(scale*42);im=im.resize((edge,edge),Image.Resampling.LANCZOS)
        sheet.paste(im,(col*320+(320-edge)//2,y+26+(286-edge)//2),im)
        d.text((col*320+12,y+304),direction.title(),fill=(30,30,28),font=small)
sheet.save(OUT/'new_machines_four_views.png')
sheet=Image.new('RGB',(1152,410),(190,190,187));d=ImageDraw.Draw(sheet)
for col,(label,stem,scale) in enumerate(new):
    im=Image.open(ROOT/'Textures/Buildings'/f'{stem}_north.png').convert('RGBA')
    edge=round(scale*50);im=im.resize((edge,edge),Image.Resampling.LANCZOS)
    sheet.paste(im,(col*384+(384-edge)//2,30),im)
    d.text((col*384+16,370),label,fill=(30,30,28),font=font)
sheet.save(OUT/'new_machines_north.png')
items=[('Small steam','HD_SmallSteamEngine'),('Large steam','HD_LargeSteamEngine'),('Diesel','HD_IndustrialDieselEngine'),
 ('Generator','HD_EarlyIndustrialGenerator'),('Motor','HD_EarlyIndustrialLargeMotor'),('Pressure reactor','HD_HaberBoschHighPressureReactor'),
 ('Stirred tank','HD_StirredTankReactor'),('Timber pumpjack','HD_BasicPumpJack'),('Distillation','HD_DistillationTower'),
 ('Steel pumpjack (art)','HD_MetalPumpJack'),('Rotary drill (art)','HD_RotaryDrill')]
sheet=Image.new('RGB',(1040,880),(190,190,187));d=ImageDraw.Draw(sheet)
d.text((12,8),'ACTUAL WEST VIEWS / silhouettes fit to cells for review',fill=(30,30,28),font=font)
for i,(label,stem) in enumerate(items):
    x=(i%4)*260;y=45+(i//4)*275
    im=Image.open(ROOT/'Textures/Buildings'/f'{stem}_west.png').convert('RGBA');im=im.crop(im.getbbox())
    im.thumbnail((230,235),Image.Resampling.LANCZOS);sheet.paste(im,(x+(260-im.width)//2,y+(235-im.height)//2),im)
    d.text((x+12,y+242),label,fill=(30,30,28),font=small)
sheet.save(OUT/'existing_models_west.png')
print('Saved new-machine four-view/64px review sheets and actual-West contact sheet.')
