"""Publish grille before/after at inspection and game sizes."""
from pathlib import Path
import sys
from PIL import Image,ImageDraw,ImageFont
HERE=Path(__file__).resolve().parent;TRIAL=HERE.parent;ROOT=TRIAL.parent
sys.path.insert(0,str(ROOT))
from export_bradley_lowres import resize_linear
font=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',22);small=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',17)
board=Image.new('RGB',(1040,870),(58,63,68));draw=ImageDraw.Draw(board)
for col,(label,front,game) in enumerate([
    ('수정 전 · 줄무늬',HERE/'before_front.png',HERE/'before_east_256.png'),
    ('수정 후 · 사각 격자',TRIAL/'HullRepair/after_front.png',TRIAL/'VF_East/256/preview_east.png')]):
    x=12+520*col;draw.text((x,8),label,font=font,fill=(241,237,220))
    im=resize_linear(Image.open(front),512);board.paste(im,(x,44),im)
    draw.text((x+80,562),'EAST 합성 · 256px 실제 크기',font=small,fill='white')
    im=Image.open(game).convert('RGBA');board.paste(im,(x+128,590),im)
board.save(HERE/'before_after.png')
for size in (256,512):
    im=Image.open(TRIAL/'VF_East'/str(size)/'preview_east.png');assert im.size==(size,size) and im.getbbox()
print('SQUARE GRILLE COMPARISON PUBLISHED')
