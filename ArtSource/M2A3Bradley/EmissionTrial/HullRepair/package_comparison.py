"""Publish hull before/after and native-size EAST comparisons."""
from pathlib import Path
import subprocess,sys,json
from PIL import Image,ImageDraw,ImageFont
HERE=Path(__file__).resolve().parent;TRIAL=HERE.parent;ROOT=TRIAL.parent
sys.path.insert(0,str(ROOT))
from export_bradley_lowres import resize_linear
for name in ['preview_east','vehicle_east']:
    before=HERE/('before_'+name+'_256.png')
    if not before.exists():
        before.write_bytes(subprocess.check_output(['git','show','cbf17877:ArtSource/M2A3Bradley/EmissionTrial/VF_East/256/'+name+'.png']))
font=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',22);small=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',17)
board=Image.new('RGB',(1040,1410),(58,63,68));draw=ImageDraw.Draw(board)
for col,(version,label) in enumerate([('before','수정 전'),('after','차체 복구 후')]):
    x=12+520*col;draw.text((x,8),label,font=font,fill=(241,237,220))
    for row,view in enumerate(['front','rear']):
        im=resize_linear(Image.open(HERE/(version+'_'+view+'.png')),512)
        board.paste(im,(x,44+row*520),im)
    draw.text((x+80,1094),'EAST 합성 · 256px 실제 크기',font=small,fill='white')
    path=HERE/'before_preview_east_256.png' if version=='before' else TRIAL/'VF_East/256/preview_east.png'
    im=Image.open(path).convert('RGBA');assert im.size==(256,256);board.paste(im,(x+128,1122),im)
draw.text((14,1384),'포탑 숨김 차체 비교 / 해치·견인 고리·손잡이·조명 복구 / 원본 좌표 보존',font=small,fill='white')
board.save(HERE/'before_after.png')
side=HERE/'SideSlopes'
if (side/'before_front.png').exists():
    board=Image.new('RGB',(1040,870),(58,63,68));draw=ImageDraw.Draw(board)
    for col,(label,front,game) in enumerate([
        ('측면 경사 수정 전 · 108',side/'before_front.png',side/'before_east_256.png'),
        ('측면 경사 수정 후 · 132',HERE/'after_front.png',TRIAL/'VF_East/256/preview_east.png')]):
        x=12+520*col;draw.text((x,8),label,font=font,fill=(241,237,220))
        im=resize_linear(Image.open(front),512);board.paste(im,(x,44),im)
        draw.text((x+80,562),'EAST 합성 · 256px 실제 크기',font=small,fill='white')
        im=Image.open(game).convert('RGBA');board.paste(im,(x+128,590),im)
    board.save(side/'before_after.png')
for size in (256,512):
    out=HERE/str(size);out.mkdir(exist_ok=True)
    for name in ['after_front','after_rear','after_top']:
        resize_linear(Image.open(HERE/(name+'.png')),size).save(out/(name+'.png'))
report={'display_sizes':[256,512],'hull_only_views':['front','rear','top'],
        'reference_commit':'cbf17877','resize':'linear-light premultiplied Lanczos',
        'native_EAST_vehicle_bounds':{}}
for size in (256,512):
    im=Image.open(TRIAL/'VF_East'/str(size)/'preview_east.png');bounds=im.getbbox()
    assert bounds and bounds[0]>0 and bounds[1]>0 and bounds[2]<size and bounds[3]<size
    report['native_EAST_vehicle_bounds'][str(size)]=list(bounds)
(HERE/'render_validation.json').write_text(json.dumps(report,indent=2)+'\n')
print('HULL COMPARISON AND 256/512 INSPECTION VIEWS PUBLISHED')
