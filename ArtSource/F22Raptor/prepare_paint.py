"""Author a photo-referenced two-tone native-space aircraft paint map."""
from pathlib import Path
import json
import numpy as np
from PIL import Image,ImageDraw,ImageFilter
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';OUT.mkdir(parents=True,exist_ok=True)
style=json.loads((ROOT/'style.json').read_text());plan=json.loads((ROOT/'paint.json').read_text())
size=2048;lo,hi=plan['bounds_xy']
def pixel(point):
    x,y=point
    return ((y-lo[1])/(hi[1]-lo[1])*(size-1),(hi[0]-x)/(hi[0]-lo[0])*(size-1))

def curve(points):
    result=[];count=len(points)
    for i in range(count):
        a,b,c,d=[np.array(points[j%count],dtype=float) for j in [i-1,i,i+1,i+2]]
        for t in np.linspace(0,1,24,endpoint=False):
            result.append(.5*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t))
    return result

for profile in ['main','tail','fin']:
    mask=Image.new('L',(size,size));draw=ImageDraw.Draw(mask)
    for patch in plan['patches']:
        if patch.get('profile','main')!=('main' if profile=='fin' else profile):continue
        for sign in [1,-1] if patch['mirror'] else [1]:
            draw.polygon([pixel((p[0],p[1]*sign)) for p in curve(patch['points'])],fill=255)
    mask=mask.filter(ImageFilter.GaussianBlur(plan['soft_edge_native']*size/(hi[0]-lo[0])))
    base=Image.new('RGB',(size,size),tuple(style['palette']['body']))
    base.paste(tuple(style['palette']['camo_dark']),(0,0,size,size),mask)
    draw=ImageDraw.Draw(base)
    for strip in plan['ram_strips']:
        if profile=='fin' or strip.get('profile','main')!=profile:continue
        for sign in [1,-1] if strip['mirror'] else [1]:
            draw.line([pixel((x,y*sign)) for x,y in strip['points']],
                fill=tuple(style['palette']['edge']),width=round(strip['width']*size/(hi[1]-lo[1])),joint='curve')
    base.save(OUT/f'fuselage_{profile}_paint.png')
print('F22_NATIVE_PAINT_PASS: photo-referenced gray lobes and narrow RAM edge strips; no baked shading')
