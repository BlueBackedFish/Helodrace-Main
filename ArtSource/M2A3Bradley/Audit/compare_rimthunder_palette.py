"""Equal-width color study: measured reference with TAN tint and actual renders."""
from pathlib import Path
import sys,json,hashlib,os,tempfile
import numpy as np
from PIL import Image,ImageDraw
ROOT=Path(__file__).resolve().parent.parent;OUT=ROOT/'Audit/ContactPaint';sys.path.insert(0,str(ROOT))
cfg=json.loads((ROOT/'part_contrast.json').read_text());ref=Path(json.loads((ROOT/'rimthunder_palette_samples.json').read_text())['reference_root'])
board=Image.new('RGB',(1100,1000),(79,79,79));d=ImageDraw.Draw(board)
d.text((25,15),'RimThunder | same TAN tint study (not a game capture)',fill='white')
d.text((575,15),'Current | rendered TAN + desaturated lower surfaces',fill='white')
metrics={}
for row,(name,ours,rotate) in enumerate([('vehicle_east.png','vehicle_east.png',False),('vehicle_turret.png','turret_top_east.png',True)]):
    original=Image.open(ref/name).convert('RGBA');rgba=np.array(original).astype(np.float32)
    gray=rgba[:,:,:3].mean(axis=2)/255;rgb=gray[:,:,None]*np.array(cfg['height_first_paint']['tan_tint_srgb'])
    y=np.sum(rgb*np.array([.2126,.7152,.0722]),axis=2,keepdims=True)
    sat=np.clip((gray*214-65)/165,cfg['height_first_paint']['minimum_saturation'],1)[:,:,None]
    rgba[:,:,:3]=y+(rgb-y)*sat;study=Image.fromarray(np.clip(rgba,0,255).astype('uint8'))
    if rotate:study=study.transpose(Image.Transpose.ROTATE_270)
    current=Image.open(ROOT/'RimWorld/VF_East'/ours).convert('RGBA')
    for col,im in enumerate([study,current]):
        box=im.getchannel('A').point(lambda a:255 if a>20 else 0).getbbox();im=im.crop(box)
        width=490;im=im.resize((width,round(im.height*width/im.width)),Image.Resampling.LANCZOS)
        x=25+col*550;y=75+row*400
        assert y+im.height<970
        board.paste(im,(x,y),im)
        metrics[f'{row}_{col}']={'file':name if col==0 else ours,'reference_sha256':hashlib.sha256((ref/name).read_bytes()).hexdigest() if col==0 else None,'source_alpha_bbox':box,'comparison_visible_width':width,'scale_basis':'body without long barrel'}
    d.text((25,55+row*400),'Hull' if row==0 else 'Turret body, long barrel excluded',fill='white')
d.text((25,980),'Local contact contrast; reference is a TAN study, not sampled game colors. Geometry/details differ.',fill='white')
with tempfile.TemporaryDirectory(prefix='.comparison-',dir=OUT) as staging:
    target=Path(staging)/'comparison.png';board.save(target)
    os.replace(target,OUT/'rimthunder_color_comparison.png')
(OUT/'comparison_metrics.json').write_text(json.dumps(metrics,indent=2)+'\n')
print('COLOR COMPARISON PUBLISHED: equal 490px visible widths')
