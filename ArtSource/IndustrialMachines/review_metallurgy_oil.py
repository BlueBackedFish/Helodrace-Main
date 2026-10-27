"""Review-only contact sheets at a common display scale; game PNGs stay untouched."""
from pathlib import Path
from theme_preview import game_image,save_review
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parents[2];OUT=Path(__file__).resolve().parent
font=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',23)
small=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',17)
assets=[('전로','HD_Converter',4.5),('고로','HD_BlastFurnace',6),('배치식 증류기','HD_BatchStill',4.5),
        ('로터리 시추기','HD_RotaryDrill',6),('철제 펌프잭','HD_MetalPumpJack',9)]
sheet=Image.new('RGB',(1200,860),(186,186,182));d=ImageDraw.Draw(sheet)
d.text((18,12),'64° · 타일별 고정 해상도 · 12px 외곽선',font=font,fill=(30,30,28))
for i,(label,stem,scale) in enumerate(assets):
    x=i*400 if i<3 else 200+(i-3)*400;y=52 if i<3 else 450
    im=game_image(ROOT/'Textures/Buildings'/f'{stem}_north.png')
    edge=round(scale*42);im=im.resize((edge,edge),Image.Resampling.LANCZOS)
    sheet.paste(im,(x+(400-edge)//2,y+(350-edge)//2),im)
    d.text((x+20,y+350),label,font=font,fill=(30,30,28))
save_review(sheet,OUT/'metallurgy_oil_north.png')
sheet=Image.new('RGB',(1520,2000),(186,186,182));d=ImageDraw.Draw(sheet)
for row,(label,stem,scale) in enumerate(assets):
    y=row*400;d.text((15,y+6),label,font=font,fill=(30,30,28))
    for col,direction in enumerate(['north','east','south','west']):
        im=game_image(ROOT/'Textures/Buildings'/f'{stem}_{direction}.png')
        edge=round(scale*36);im=im.resize((edge,edge),Image.Resampling.LANCZOS)
        sheet.paste(im,(col*380+(380-edge)//2,y+36+(325-edge)//2),im)
        d.text((col*380+16,y+369),direction.title(),font=small,fill=(30,30,28))
save_review(sheet,OUT/'metallurgy_oil_four_views.png')
print('Saved five-building common-scale North and four-view review sheets.')
