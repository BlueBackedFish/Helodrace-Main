"""Downsample the rendered transparent layers with premultiplied-alpha filtering."""
from pathlib import Path
from PIL import Image
import json,numpy as np,tempfile,os

def resize_linear(source,size):
    rgba=np.asarray(source.convert('RGBA'),dtype=np.float32)/255
    alpha=rgba[:,:,3:4]
    rgb=rgba[:,:,:3]
    rgb=np.where(rgb<=.04045,rgb/12.92,((rgb+.055)/1.055)**2.4)
    premul=np.concatenate((rgb*alpha,alpha),axis=2)
    filtered=np.stack([np.asarray(Image.fromarray(premul[:,:,i]).resize((size,size),Image.Resampling.LANCZOS)) for i in range(4)],axis=2)
    alpha=np.clip(filtered[:,:,3:4],0,1)
    rgb=np.clip(filtered[:,:,:3]/np.maximum(alpha,1e-8),0,1)
    rgb=np.where(rgb<=.0031308,rgb*12.92,1.055*rgb**(1/2.4)-.055)
    rgba=np.concatenate((rgb,alpha),axis=2)
    rgba[alpha[:,:,0]<1/510]=0
    return Image.fromarray(np.round(np.clip(rgba,0,1)*255).astype(np.uint8))

# Known color-energy and transparency cases catch gamma-space darkening and
# hidden RGB bleeding into the outline before publishing the game-sized PNGs.
energy=Image.new('RGBA',(2,1));energy.putdata([(0,0,0,255),(255,255,255,255)])
assert abs(resize_linear(energy,1).getpixel((0,0))[0]-188)<=1
edge=Image.new('RGBA',(2,1));edge.putdata([(0,0,0,0),(255,255,255,255)])
pixel=resize_linear(edge,1).getpixel((0,0))
assert pixel[:3]==(255,255,255) and abs(pixel[3]-128)<=1
def main():
    root=Path(__file__).resolve().parent/'RimWorld'/'VF_East'
    names=['vehicle_east','turret_top_east','barrel_top_east','turret_top_full_east','preview_east']
    for size in (256,512):
        target=root/str(size);target.mkdir(exist_ok=True)
        for name in names:
            source=Image.open(root/(name+'.png')).convert('RGBA')
            result=resize_linear(source,size)
            # Publish a fresh file: Windows preview tabs can retain an open handle
            # that prevents overwriting an existing PNG in place.
            with tempfile.TemporaryDirectory(prefix='.resize-',dir=target) as staging:
                path=Path(staging)/(name+'.png');result.save(path)
                os.replace(path,target/(name+'.png'))
            assert result.getbbox() and result.getbbox()[0]>0 and result.getbbox()[2]<size
        report=json.loads((root/'render_report.json').read_text())
        settings=report['settings'];factor=size/1024
        (target/'pivots.json').write_text(json.dumps({'size':[size,size],'top_pivot':[size/2,size/2],
            'turret_pivot_pixels_top_origin':[round(v*factor,3) for v in settings['turret_pivot_pixels_top_origin']],
            'hull_elevation':64,'turret_elevation':90,
            'resize':'linear-light float premultiplied Lanczos'},indent=2))
    print('256px and 512px transparent exports passed')

if __name__=='__main__':main()
