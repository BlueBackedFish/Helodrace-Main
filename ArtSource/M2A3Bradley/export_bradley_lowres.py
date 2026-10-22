"""Downsample the rendered transparent layers with premultiplied-alpha filtering."""
from pathlib import Path
from PIL import Image
import json
root=Path(__file__).resolve().parent/'RimWorld'/'VF_East'
names=['vehicle_east','turret_top_east','barrel_top_east','turret_top_full_east','preview_east']
for size in (256,512):
    target=root/str(size);target.mkdir(exist_ok=True)
    for name in names:
        source=Image.open(root/(name+'.png')).convert('RGBA')
        result=source.convert('RGBa').resize((size,size),Image.Resampling.LANCZOS).convert('RGBA')
        result.save(target/(name+'.png'))
        assert result.getbbox() and result.getbbox()[0]>0 and result.getbbox()[2]<size
    report=json.loads((root/'render_report.json').read_text())
    settings=report['settings'];factor=size/1024
    (target/'pivots.json').write_text(json.dumps({'size':[size,size],'top_pivot':[size/2,size/2],
        'turret_pivot_pixels_top_origin':[round(v*factor,3) for v in settings['turret_pivot_pixels_top_origin']],
        'hull_elevation':64,'turret_elevation':90},indent=2))
print('256px and 512px transparent exports passed')
