"""Review actual Unity captures; verify opaque aircraft pixels occlude the released load."""
from pathlib import Path
import argparse, json
from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT=Path(__file__).resolve().parent
parser=argparse.ArgumentParser();parser.add_argument('captures',type=Path);args=parser.parse_args()
source=args.captures;dest=ROOT/'output'
plane=Image.open(source/'plane_only.png').convert('RGB');overlap=Image.open(source/'0_000.png').convert('RGB')
background=Image.new('RGB',plane.size,plane.getpixel((0,0)))
mask=ImageChops.difference(plane,background).convert('L').point(lambda v:255 if v>12 else 0).filter(ImageFilter.MinFilter(9))
diff=ImageChops.difference(plane,overlap)
occluded=Image.composite(diff,Image.new('RGB',plane.size),mask)
assert occluded.getbbox() is None,'Load covers opaque aircraft interior pixels'

ticks=[0,54,108,198,360];labels=['M8 / East','M8 / Northeast','M3A3 / West']
sheet=Image.new('RGB',(5*384,3*420),(35,40,43));draw=ImageDraw.Draw(sheet)
for row,label in enumerate(labels):
    for col,tick in enumerate(ticks):
        image=Image.open(source/f'{row}_{tick:03}.png').convert('RGB');image.thumbnail((384,384),Image.Resampling.LANCZOS)
        sheet.paste(image,(384*col,420*row+34));draw.text((384*col+12,420*row+10),f'{label} / {tick/60:.1f}s',fill='white')
sheet.save(dest/'descent_review.png')
frames=[Image.open(source/f'1_{tick:03}.png').convert('RGB').resize((768,768),Image.Resampling.LANCZOS) for tick in [0,20,54,108,198,288,342,360]]
frames[0].save(dest/'descent_preview.gif',save_all=True,append_images=frames[1:],duration=[400,400,500,700,800,800,500,800],loop=0)
result=dict(passed=True,opaque_aircraft_occlusion=True,captures=len(list(source.glob('*.png'))),release_headings=[90,45,270],native_result=(source.parent/'vehicle-revision-result.txt').read_text())
(dest/'descent_validation.json').write_text(json.dumps(result,indent=2)+'\n')
print('DESCENT_REVIEW_PASS: opaque C-17 interior unchanged by load; east/northeast/west contact sheet and preview')
