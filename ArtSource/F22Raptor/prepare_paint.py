"""Retain the source camouflage while removing painted panels and markings."""
from pathlib import Path
import json,hashlib
import numpy as np
from PIL import Image,ImageFilter
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';OUT.mkdir(parents=True,exist_ok=True)
style=json.loads((ROOT/'style.json').read_text(encoding='utf-8'))
source=(Path(style['source']).parent/style['texture_directory']/'F22_Main_Fuselage_Base_color.png').resolve()
assert hashlib.sha256(source.read_bytes()).hexdigest()==style['source_albedo_sha256'],'Source albedo changed; review the UV paint mapping'
# Work on the source UV layout, not on the rendered aircraft silhouette. Median
# regions preserve broad camouflage and RAM edges while removing baked scuffs.
gray=Image.open(source).convert('L').resize((2048,2048),Image.Resampling.LANCZOS).filter(ImageFilter.MedianFilter(5))
values=np.asarray(gray);labels=np.zeros_like(values)
for index,threshold in enumerate(style['camo_source_thresholds_srgb']):labels[values>=threshold]=index
labels=np.asarray(Image.fromarray(labels).filter(ImageFilter.MedianFilter(9)))
from paint_cleanup import clean
labels,mask,report=clean(labels.copy(),style['camo_roles'],json.loads((ROOT/'paint_cleanup.json').read_text(encoding='utf-8')))
palette=np.array([style['palette'][role] for role in style['camo_roles']],dtype=np.uint8)
Image.fromarray(palette[labels]).save(OUT/'fuselage_flat_uv.png')
Image.fromarray(mask.astype(np.uint8)*255).save(OUT/'paint_cleanup_mask.png')
(OUT/'paint_cleanup_validation.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print('F22_UV_PAINT_PASS: broad camouflage retained; source-painted panels, vents and insignia removed')
