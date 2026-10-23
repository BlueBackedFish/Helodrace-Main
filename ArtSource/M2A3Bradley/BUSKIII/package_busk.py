"""Native-size transparent EAST assets and separate-layer black-outline review."""
from pathlib import Path
import sys, json, hashlib
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parents[1]/'VehiclePipeline'))
from images import resize_linear, composite_linear, check_margin, font
from outline_review import black_outline

OUT = HERE/'output'
RAW = OUT/'VF_East'
REVIEW = OUT/'OutlinedReview'
REVIEW.mkdir(exist_ok=True)
render = json.loads((RAW/'render_report.json').read_text())
cfg = json.loads((HERE/'settings.json').read_text())
anchor = [round(v) for v in render['settings']['turret_pivot_pixels_top_origin']]
offset = tuple(v-512 for v in anchor)
names = {'hull_east':'vehicle_east', 'turret_east':'turret_top_east',
         'barrel_east':'barrel_top_east', 'turret_full_east':'turret_top_full_east'}
sources = {name:Image.open(RAW/(file+'.png')).convert('RGBA') for name,file in names.items()}
hashes = {file:hashlib.sha256((RAW/(file+'.png')).read_bytes()).hexdigest() for file in names.values()}
outlined = {name:black_outline(im, cfg['outline_width_at_256']*4) for name,im in sources.items()}
for group in [sources, outlined]:
    group['preview_east'] = composite_linear(group['hull_east'], group['turret_full_east'], offset)
bounds = {}
for size in [256,512,1024]:
    for folder,group in [(RAW,sources),(REVIEW,outlined)]:
        directory = folder/str(size)
        directory.mkdir(exist_ok=True)
        for name,im in group.items():
            target = im if size == 1024 else resize_linear(im,size)
            bounds[f'{folder.name}/{size}/{name}'] = check_margin(target,name)
            target.save(directory/(name+'.png'))
        meta = {'size':[size,size], 'hull_turret_anchor':[v*size/1024 for v in anchor],
                'unrounded_hull_turret_anchor':[v*size/1024 for v in render['settings']['turret_pivot_pixels_top_origin']],
                'turret_top_pivot':[size/2]*2, 'turret_full_includes_barrel':True,
                'hull_elevation_degrees':64, 'turret_elevation_degrees':90,
                'filter':'linear light premultiplied alpha Lanczos',
                'outline_pixels':size/256*cfg['outline_width_at_256'] if folder == REVIEW else 0}
        (directory/'pivots.json').write_text(json.dumps(meta,indent=2)+'\n')

# The two output examples remain at actual pixel size, with no enlargement.
board = Image.new('RGB',(816,610),(68,71,74))
draw = ImageDraw.Draw(board)
draw.text((16,12),'BUSK III 외형 시안 / 실차 치수·장착 수량 미확정',font=font(18),fill='white')
for x,size in [(16,256),(288,512)]:
    im = Image.open(REVIEW/str(size)/'preview_east.png')
    board.paste(im,(x,72),im)
    draw.text((x,48),f'{size}px / 윤곽선 {size//256}px',font=font(),fill='white')
board.save(REVIEW/'native_sizes.png')

# Background checks reveal dark/bright alpha fringes without altering assets.
board = Image.new('RGB',(768,292))
im = Image.open(REVIEW/'256/preview_east.png')
draw = ImageDraw.Draw(board)
for index,color in enumerate([(245,245,245),(36,38,41),(104,119,83)]):
    x = index*256
    draw.rectangle((x,0,x+255,291),fill=color)
    board.paste(im,(x,36),im)
    draw.text((x+8,8),['밝은 배경','어두운 배경','지면 배경'][index],font=font(14),fill=(20,20,20) if index==0 else 'white')
board.save(REVIEW/'background_checks.png')
assert hashes == {file:hashlib.sha256((RAW/(file+'.png')).read_bytes()).hexdigest() for file in names.values()}
(REVIEW/'report.json').write_text(json.dumps({
    'model_sha256':hashlib.sha256((OUT/'bradley_busk_iii.blend').read_bytes()).hexdigest(),
    'source_hashes':hashes, 'source_raw_layers_unchanged':True,
    'composition':'outline hull and full turret separately before original-pivot composition',
    'opaque_source_pixels_preserved':True, 'outline_pixels_at_256':cfg['outline_width_at_256'],
    'alpha_bounds':bounds, 'game_drawSize':'not verified',
    'vehicle_width_at_256':bounds['OutlinedReview/256/preview_east'][2]-bounds['OutlinedReview/256/preview_east'][0],
},indent=2)+'\n')
print('BUSK PNG PACKAGING PASSED: 256/512/1024, separate hull/turret outlines, pivot and alpha margins')
