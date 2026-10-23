"""No-outline review of the four-direction base/module separation and TOW paint."""
from pathlib import Path
import json,sys
import numpy as np
from PIL import Image,ImageDraw

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parents[1]/'VehiclePipeline'))
from images import composite_linear,font
OUT=HERE/'output'
root=OUT/'256/BUSK'
index=json.loads((root/'index.json').read_text())


def overlays(files):
    # Review only; source sprites and full-size parent canvases stay untouched.
    a=np.zeros((256,256,4),dtype=np.float32)
    for file in files:
        image=Image.open(root/file).convert('RGBA')
        bbox=image.getchannel('A').getbbox()
        if not bbox:continue
        b=np.asarray(image.crop(bbox),dtype=np.float32)/255
        x0,y0,x1,y1=bbox
        rgb=np.where(b[:,:,:3]<=.04045,b[:,:,:3]/12.92,((b[:,:,:3]+.055)/1.055)**2.4)
        alpha=b[:,:,3:4]
        a[y0:y1,x0:x1,:3]=rgb*alpha+a[y0:y1,x0:x1,:3]*(1-alpha)
        a[y0:y1,x0:x1,3:4]=alpha+a[y0:y1,x0:x1,3:4]*(1-alpha)
    alpha=a[:,:,3:4]
    rgb=a[:,:,:3]/np.maximum(alpha,1e-8)
    rgb=np.where(rgb<=.0031308,rgb*12.92,1.055*np.maximum(0,rgb)**(1/2.4)-.055)
    return Image.fromarray(np.round(np.clip(np.concatenate((rgb,alpha),2),0,1)*255).astype('uint8'))


board=Image.new('RGB',(4*280,3*302+42),(68,71,74))
draw=ImageDraw.Draw(board)
draw.text((12,10),'BUSK / ERA 없는 본체 → 개별 ERA 합성 / 256px 실제 크기 · 윤곽선 없음',font=font(17),fill='white')
for col,(direction,info) in enumerate(index['directions'].items()):
    hs=info['hull_states']['roof_closed_ramp_closed']
    ts=info['turret_states']['stowed']
    offset=tuple(round(v)-128 for v in info['rounded_master_hull_turret_anchor'])
    base=composite_linear(Image.open(root/hs['file']),Image.open(root/ts['file']),offset)
    armor=composite_linear(overlays(hs['era_files']),overlays(ts['era_files']),offset)
    complete=Image.open(root/info['previews']['roof_closed_ramp_closed_tow_stowed'])
    for row,(label,image) in enumerate([('본체 / ERA 없음',base),('ERA 레이어만',armor),('합성 확인',complete)]):
        x,y=col*280,row*302+42
        board.paste(image,(x+12,y+34),image)
        draw.text((x+12,y+8),direction.upper()+' / '+label,font=font(15),fill='white')
board.save(OUT/'era_four_directions.png')

# Optional previous render supplied by the caller, to document this paint pass.
if len(sys.argv)>1:
    before=Image.open(sys.argv[1]).convert('RGBA')
    after=Image.open(OUT/'512/Standard/Turret/turret_deployed_east.png').convert('RGBA')
    part=Image.open(OUT/'512/Standard/Parts/tow_deployed_east.png').convert('RGBA')
    box=part.getchannel('A').getbbox()
    box=(box[0]-10,box[1]-10,box[2]+10,box[3]+24)
    w,h=(box[2]-box[0])*3,(box[3]-box[1])*3
    page=Image.new('RGB',(w*2+48,h+78),(68,71,74))
    draw=ImageDraw.Draw(page)
    for col,(label,image) in enumerate([('이전 전개 채색',before),('전개 전용 채색',after)]):
        crop=image.crop(box).resize((w,h),Image.Resampling.NEAREST)
        page.paste(crop,(16+col*(w+16),44),crop)
        draw.text((16+col*(w+16),12),label,font=font(16),fill='white')
    draw.text((16,h+51),'512px 렌더 3배 확대 / 접힌 상태 색은 유지',font=font(14),fill='white')
    page.save(OUT/'tow_deployed_before_after.png')
print('ERA four-direction base/overlay review saved; no outlines')
