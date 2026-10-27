"""Common game-scale review sheets; game textures remain transparent and unchanged."""
from pathlib import Path
import json
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[2];OUT=Path(__file__).resolve().parent
font=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',22)
small=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',17)
assets=[('1970년대 머시닝 센터','HD_MachiningCenter1970','MachiningCenter1970'),
 ('2000년대 복합 가공기','HD_MultiTaskMachine2000','MultiTaskMachine2000'),
 ('1970년대 유압 압연기','HD_RollingMill1970','RollingMill1970'),
 ('1970년대 유압 프레스','HD_HydraulicPress1970','HydraulicPress1970')]
def preview(stem,folder,direction,unit_pixels):
    meta=json.loads((ROOT/'ArtSource'/folder/'validation.json').read_text(encoding='utf8'))
    im=Image.open(ROOT/'Textures/Buildings'/f'{stem}_{direction}.png').convert('RGBA')
    edge=round(meta['orthographic_scale']*unit_pixels)
    im=im.resize((edge,edge),Image.Resampling.LANCZOS)
    return im.crop(im.getbbox()),meta
sheet=Image.new('RGB',(1000,920),(186,186,182));d=ImageDraw.Draw(sheet)
d.text((15,10),'발전형 공작·성형 설비 / 64° / 동일 게임 배율',font=font,fill=(30,30,28))
for i,(label,stem,folder) in enumerate(assets):
    x=(i%2)*500;y=48+(i//2)*430
    im,meta=preview(stem,folder,'north',100)
    assert im.width<=470 and im.height<=360
    sheet.paste(im,(x+(500-im.width)//2,y+(360-im.height)//2),im)
    d.text((x+20,y+363),label,font=font,fill=(30,30,28))
    d.text((x+20,y+397),'x'.join(map(str,meta['footprint']))+' / '+str(meta['texture_resolution'][0])+'px / 12px outline',font=small,fill=(45,45,41))
sheet.save(OUT/'overview_north.png')
sheet=Image.new('RGB',(1360,1600),(186,186,182));d=ImageDraw.Draw(sheet)
for row,(label,stem,folder) in enumerate(assets):
    y=row*400;d.text((15,y+4),label,font=font,fill=(30,30,28))
    for col,direction in enumerate(['north','east','south','west']):
        im,_=preview(stem,folder,direction,72)
        assert im.width<=325 and im.height<=336,(label,direction,im.size)
        sheet.paste(im,(col*340+(340-im.width)//2,y+36+(336-im.height)//2),im)
        d.text((col*340+15,y+376),direction.title(),font=small,fill=(30,30,28))
sheet.save(OUT/'four_views.png')
print('Saved common-scale North and four-view reviews for four later industrial machines.')
