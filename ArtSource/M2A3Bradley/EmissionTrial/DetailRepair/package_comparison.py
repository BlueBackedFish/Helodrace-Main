"""Compare the repaired covers and antenna fittings from both sides."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
HERE=Path(__file__).resolve().parent
board=Image.new('RGB',(1040,1110),(58,63,68))
draw=ImageDraw.Draw(board)
font=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',22)
small=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',17)
for col,(version,label) in enumerate([('current','수정 전'),('after','복구 후')]):
    x=12+520*col;draw.text((x,10),label,font=font,fill=(241,237,220))
    for row,view in enumerate(['front','rear']):
        im=Image.open(HERE/(version+'_'+view+'.png')).convert('RGBA')
        im=im.resize((512,512),Image.Resampling.LANCZOS)
        board.paste(im,(x,46+row*520),im)
draw.text((16,1084),'관측장비 보호판·상부 덮개·외피 / 안테나 2 받침·스프링·연결부',font=small,fill='white')
board.save(HERE/'before_after.png')
print('SOURCE DETAIL COMPARISON PUBLISHED')
