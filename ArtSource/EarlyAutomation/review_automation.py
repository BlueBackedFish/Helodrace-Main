"""Common-scale review sheets; trim transparent margins without fitting silhouettes."""
from pathlib import Path
import sys
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'IndustrialMachines'))
from theme_preview import game_image,save_review
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[2];OUT=Path(__file__).resolve().parent
font=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',21)
small=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',17)
assets=[('초기 CNC 선반','HD_CNCLathe','3x2 / 1800W'),('초기 CNC 밀링기','HD_CNCMillingMachine','3x2 / 2200W'),
        ('초기 반도체 팹','HD_SemiconductorFab','3x3 / 4000W')]
def preview(stem,direction,unit_pixels):
    im=game_image(ROOT/'Textures/Buildings'/f'{stem}_{direction}.png')
    edge=round(4.5*unit_pixels)
    im=im.resize((edge,edge),Image.Resampling.LANCZOS)
    return im.crop(im.getbbox())
sheet=Image.new('RGB',(1050,430),(186,186,182));draw=ImageDraw.Draw(sheet)
draw.text((15,12),'1950년대 초기 수치제어 · 반도체 설비 / 64°',fill=(30,30,28),font=font)
for col,(label,stem,subtitle) in enumerate(assets):
    im=preview(stem,'north',96)
    sheet.paste(im,(col*350+(350-im.width)//2,60+(285-im.height)//2),im)
    draw.text((col*350+18,355),label,fill=(30,30,28),font=font)
    draw.text((col*350+18,389),subtitle+' / 768px',fill=(45,45,41),font=small)
save_review(sheet,OUT/'overview_north.png')
sheet=Image.new('RGB',(1360,1020),(186,186,182));draw=ImageDraw.Draw(sheet)
for row,(label,stem,subtitle) in enumerate(assets):
    y=row*340;draw.text((15,y+5),label,fill=(30,30,28),font=font)
    for col,direction in enumerate(['north','east','south','west']):
        im=preview(stem,direction,80)
        sheet.paste(im,(col*340+(340-im.width)//2,y+36+(255-im.height)//2),im)
        draw.text((col*340+15,y+309),direction.title(),fill=(30,30,28),font=small)
save_review(sheet,OUT/'four_views.png')
print('Saved NC/fab North and four-view reviews at fixed common display scales.')
