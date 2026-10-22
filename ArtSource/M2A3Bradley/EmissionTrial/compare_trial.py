"""Publish the trial at game sizes and compare it with the untouched baseline."""
from pathlib import Path
import sys,json,os,tempfile
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parent.parent;OUT=Path(__file__).resolve().parent;sys.path.insert(0,str(ROOT))
from export_bradley_lowres import resize_linear
validation=json.loads((OUT/'validation.json').read_text())
assert validation['max_actual_pixel_srgb_channel_difference_0_to_255']<1.5
names=['vehicle_east','turret_top_east','barrel_top_east','turret_top_full_east','preview_east']
render=json.loads((OUT/'VF_East/render_report.json').read_text())['settings']
for size in (256,512):
    target=OUT/'VF_East'/str(size);target.mkdir(exist_ok=True)
    for name in names:
        result=resize_linear(Image.open(OUT/'VF_East'/(name+'.png')),size)
        assert result.getbbox() and 0<result.getbbox()[0]<result.getbbox()[2]<size
        with tempfile.TemporaryDirectory(prefix='.resize-',dir=target) as staging:
            p=Path(staging)/(name+'.png');result.save(p);os.replace(p,target/(name+'.png'))
    (target/'pivots.json').write_text(json.dumps({'size':[size,size],'top_pivot':[size/2,size/2],
        'turret_pivot_pixels_top_origin':[round(v*size/1024,3) for v in render['turret_pivot_pixels_top_origin']],
        'hull_elevation':64,'turret_elevation':90,'resize':'linear-light float premultiplied Lanczos'},indent=2)+'\n')
font=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',20);small=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',15)
def publish(board,name):
    with tempfile.TemporaryDirectory(prefix='.board-',dir=OUT) as staging:
        p=Path(staging)/name;board.save(p);os.replace(p,OUT/name)
board=Image.new('RGB',(1040,870),(69,69,69));draw=ImageDraw.Draw(board)
for col,(label,front,game) in enumerate([
    ('기존 Emission + 곡률 명암','before_front.png',OUT/'before_east_256.png'),
    ('시험: 고정 색면 Emission','closeup_front.png',OUT/'VF_East/256/preview_east.png')]):
    x=16+520*col;draw.text((x,8),label,font=font,fill='white')
    image=Image.open(OUT/front).convert('RGBA').resize((488,488),Image.Resampling.LANCZOS)
    board.paste(image,(x,42),image);draw.text((x,544),'256px 실제 출력 크기',font=small,fill='white')
    image=Image.open(game).convert('RGBA');assert image.size==(256,256);board.paste(image,(x+116,578),image)
draw.text((16,842),'시험본: 기총 옆 상부 판 Y230 / 형상 유지 / 원본 모델 유지',font=small,fill='white')
publish(board,'before_after.png')
board=Image.new('RGB',(1040,570),(69,69,69));draw=ImageDraw.Draw(board)
for col,(name,label) in enumerate([('closeup_front','시점 A · 조명 없음'),('closeup_angle','시점 B · 강한 조명 추가')]):
    x=16+520*col;draw.text((x,8),label,font=font,fill='white')
    image=Image.open(OUT/(name+'.png')).convert('RGBA').resize((488,488),Image.Resampling.LANCZOS);board.paste(image,(x,42),image)
draw.text((16,544),'두 시점 공통으로 보이는 304개 면: 실제 픽셀 RGB 차이 0.0 / 255',font=small,fill='white')
publish(board,'view_invariance.png')
print('TRIAL 256/512 EXPORT AND COMPARISONS PUBLISHED')
