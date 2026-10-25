"""Publish registered, cropped equipment sprites at the project's absolute pixel scale."""
from pathlib import Path
import json,sys,hashlib
import numpy as np
from PIL import Image,ImageDraw
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent/'VehiclePipeline'))
from images import resize_linear
from outline_review import black_outline
cfg=json.loads((HERE/'settings.json').read_text())
size=round(cfg['draw_size']*cfg['pixels_per_tile'])
manifest={'native_size':size,'draw_size':cfg['draw_size'],'outline_pixels':12,'layers':[],'variants':{}}
review=Image.new('RGB',(2400,1320),(69,73,76));draw=ImageDraw.Draw(review)
for vi,variant in enumerate(cfg['variants']):
    root=HERE/'output'/variant;meta=json.loads((root/'render.json').read_text())
    variant_report={}
    for di,direction in enumerate(cfg['directions']):
        pivot=np.array(meta['directions'][direction]['vehicle_pivot'])*size/cfg['master']
        combined=Image.new('RGBA',(size,size))
        for layer in ['pallet','restraints','lines','canopy']:
            source=Image.open(root/f'{layer}_{direction}.png').convert('RGBA')
            if layer in ['pallet','canopy']:source=black_outline(source,12*cfg['master']/size)
            native=resize_linear(source,size)
            a=np.asarray(native);assert a[:,:,3].any(),f'Empty {variant}/{direction}/{layer}'
            box=native.getchannel('A').getbbox()
            assert min(box)>0 and box[2]<size and box[3]<size, f'Clipped {variant}/{layer}: {box}'
            box=(max(0,box[0]-4),max(0,box[1]-4),min(size,box[2]+4),min(size,box[3]+4))
            crop=native.crop(box);dest=root/'Textures'/f'{layer}_{direction}.png';dest.parent.mkdir(exist_ok=True);crop.save(dest)
            center=np.array([(box[0]+box[2])/2,(box[1]+box[3])/2]);offset=(center-pivot)*cfg['draw_size']/size
            manifest['layers'].append(dict(key=f'{variant}:{direction}:{layer}',file=str(dest.relative_to(HERE)).replace('\\','/'),
                size=[crop.width*cfg['draw_size']/size,crop.height*cfg['draw_size']/size],offset=[float(offset[0]),float(-offset[1])],
                sha256=hashlib.sha256(dest.read_bytes()).hexdigest()))
            combined.alpha_composite(native)
        # Source vehicle is a geometry reference in the review, never baked into runtime equipment.
        preview=Image.open(root/f'preview_{direction}.png').convert('RGBA')
        preview=resize_linear(preview,400)
        review.paste(preview,(di%2*400+vi*800,di//2*650+26),preview)
        draw.text((di%2*400+vi*800+8,di//2*650+6),f'{variant} / {direction}',fill='white')
        variant_report[direction]=dict(vehicle_pivot=pivot.tolist(),all_layers_nonempty=True,unclipped=True)
    manifest['variants'][variant]=variant_report
(HERE/'output/manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
review.save(HERE/'output/rig_review.png')
print(f'PASS: {len(manifest["layers"])} registered equipment textures, native {size}px, 12px pallet/canopy outline')

