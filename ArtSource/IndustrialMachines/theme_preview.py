"""Preview native game albedo through its CutoutComplex white/Steel color mask."""
from pathlib import Path
from functools import lru_cache
import json,os,tempfile
import numpy as np
from PIL import Image
ROOT=Path(__file__).resolve().parents[2]

def save_review(im,path):
    """Publish PNGs atomically; viewers may hold the previous file open on Windows."""
    path=Path(path)
    descriptor,temp=tempfile.mkstemp(prefix='.review-',suffix='.png',dir=path.parent)
    os.close(descriptor)
    try:
        im.save(temp,format='PNG');os.replace(temp,path)
    finally:
        if os.path.exists(temp):os.remove(temp)

@lru_cache(None)
def themed_stems():
    result={}
    for path in (ROOT/'ArtSource').rglob('validation.json'):
        try:meta=json.loads(path.read_text(encoding='utf8'))
        except (ValueError,UnicodeError):continue
        if isinstance(meta,dict) and meta.get('building_theme_version'):result[meta['asset']]=meta
    return result

def game_image(path):
    path=Path(path);im=Image.open(path).convert('RGBA')
    if path.resolve().parent != (ROOT/'Textures/Buildings').resolve():return im
    stem=path.stem.rsplit('_',1)[0];meta=themed_stems().get(stem)
    if not meta:return im
    mask=Image.open(path.with_name(path.stem+'m.png')).convert('RGBA')
    color=np.array(im,dtype=np.float32);weights=np.array(mask,dtype=np.float32)[:,:,:2]/255
    factor=(1-weights.sum(axis=2))[:,:,None]+weights[:,:,0,None]+weights[:,:,1,None]*np.array(meta['theme_secondary_color'])/255
    color[:,:,:3]*=np.clip(factor,0,1)
    return Image.fromarray(np.rint(color).clip(0,255).astype('uint8'))
