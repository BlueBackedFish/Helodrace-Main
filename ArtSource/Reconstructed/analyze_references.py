"""Read every eligible color/mask pair; never visits the excluded GunBench."""
import bpy
import numpy as np
import os
import json
from collections import Counter

ROOT = os.path.dirname(os.path.abspath(__file__))
REF = os.path.join(os.path.dirname(ROOT), 'Reference')

def pixels(path):
    image = bpy.data.images.load(path, check_existing=False)
    image.colorspace_settings.name = 'Non-Color'
    width, height = image.size
    data = np.empty(width * height * 4, dtype=np.float32)
    image.pixels.foreach_get(data)
    bpy.data.images.remove(image)
    return np.rint(data.reshape(height, width, 4)[::-1] * 255).astype(np.uint8)

def bounds(binary):
    y, x = np.where(binary)
    return [int(x.min()), int(y.min()), int(x.max()+1), int(y.max()+1)] if len(x) else None

def analyze():
    report = {}
    for asset in sorted(os.listdir(REF)):
        if asset == 'GunBench' or not os.path.isdir(os.path.join(REF, asset)):
            continue
        rows = []
        for filename in sorted(os.listdir(os.path.join(REF, asset))):
            if not filename.endswith('.png') or filename.endswith('m.png'):
                continue
            path = os.path.join(REF, asset, filename)
            maskpath = path[:-4] + 'm.png'
            assert os.path.isfile(maskpath), path
            color, mask = pixels(path), pixels(maskpath)
            assert color.shape == mask.shape
            visible = color[:,:,3] > 127
            filled = visible & (color[:,:,:3].max(axis=2) > 24)
            red = (mask[:,:,0] > 180) & (mask[:,:,1] < 80) & (mask[:,:,3] > 127)
            # These are material-selection masks, not universal alpha cutouts.
            # Unmasked filled ochre, brown, sheet metal and controls remain real.
            interior = filled.copy()
            for dy, dx in [(1,0),(-1,0),(0,1),(0,-1)]:
                interior &= np.roll(filled, (dy,dx), axis=(0,1))
            palette = Counter(map(tuple, color[interior,:3].tolist())).most_common(12)
            rows.append({'color': filename, 'mask': os.path.basename(maskpath),
                         'dimensions': list(color.shape[:2][::-1]),
                         'alpha_bounds_including_stroke': bounds(visible),
                         'filled_bounds_without_black_stroke': bounds(filled), 'mask_red_bounds': bounds(red),
                         'red_mask_pixels': int(red.sum()),
                         'visible_unmasked_pixels': int((visible & ~red).sum()),
                         'interior_palette': [{'rgb': list(c), 'pixels': n} for c,n in palette]})
        report[asset] = rows
    with open(os.path.join(ROOT, 'reference_analysis.json'), 'w') as f:
        json.dump(report, f, indent=2)
    return report

if __name__ == '__main__':
    result = analyze()
    for asset, views in result.items():
        print(asset, [(v['color'], v['filled_bounds_without_black_stroke'], v['interior_palette'][:5]) for v in views])
