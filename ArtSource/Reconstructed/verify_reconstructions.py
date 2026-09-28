"""Verify saved cameras/output and make outline-free visual comparison sheets."""
import bpy
import os
import sys
import json
import math
import numpy as np
from mathutils import Vector

ROOT=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,ROOT)
from analyze_references import pixels, bounds, REF

def normalized(data, stroke=False, size=280):
    data=data.copy()
    if stroke:
        data[data[:,:,:3].max(axis=2)<=24,3]=0
    region=bounds(data[:,:,3]>127)
    x0,y0,x1,y1=region
    data=data[y0:y1,x0:x1]
    scale=(size-24)/max(data.shape[:2])
    h,w=[max(1,round(v*scale)) for v in data.shape[:2]]
    # Nearest sampling keeps source palette intact for these flat-color sprites.
    y=np.minimum((np.arange(h)/scale).astype(int),data.shape[0]-1)
    x=np.minimum((np.arange(w)/scale).astype(int),data.shape[1]-1)
    sample=data[y[:,None],x[None,:]]
    result=np.zeros((size,size,4),dtype=np.uint8)
    left=(size-w)//2;top=(size-h)//2
    result[top:top+h,left:left+w]=sample
    return result

def save(path, data):
    image=bpy.data.images.new('QA_sheet',width=data.shape[1],height=data.shape[0],alpha=True)
    image.colorspace_settings.name='Non-Color'
    image.pixels.foreach_set((data[::-1].astype(np.float32)/255).ravel())
    image.filepath_raw=path;image.file_format='PNG';image.save()
    bpy.data.images.remove(image)

def composite(data,bg=38):
    a=data[:,:,3:4].astype(float)/255
    out=np.empty_like(data)
    out[:,:,:3]=np.rint(data[:,:,:3]*a+bg*(1-a))
    out[:,:,3]=255
    return out

results={}
all_thumbs=[]
for asset in sorted(os.listdir(ROOT)):
    path=os.path.join(ROOT,asset,'HD_'+asset+'.blend')
    if not os.path.isfile(path):continue
    bpy.ops.wm.open_mainfile(filepath=path)
    scene=bpy.context.scene
    rows=[];scales=[];targets=[];checks={};ious={}
    for direction in ['north','south','east']:
        camera=bpy.data.objects['Camera_'+direction.title()]
        forward=camera.rotation_euler.to_quaternion()@Vector((0,0,-1))
        angle=math.degrees(math.asin(-forward.z))
        assert abs(angle-64)<.001,(asset,direction,angle)
        assert camera.data.type=='ORTHO'
        scales.append(camera.data.ortho_scale)
        targets.append(tuple(camera['target_center']))
        rendered=pixels(os.path.join(ROOT,asset,'HD_'+asset+'_'+direction+'.png'))
        assert rendered.shape==(512,512,4)
        box=bounds(rendered[:,:,3]>127)
        assert min(box[0],box[1],512-box[2],512-box[3])>=20,(asset,direction,box)
        checks[direction]={'actual_downward_degrees':angle,'alpha_bounds':box,
                           'transparent_pixels':int((rendered[:,:,3]==0).sum())}
        right=normalized(rendered)
        reference=os.path.join(REF,asset,'HD_'+asset+'_'+direction+'.png')
        if os.path.isfile(reference):
            left=normalized(pixels(reference),stroke=True)
            maskpath=reference[:-4]+'m.png'
            assert os.path.isfile(maskpath)
            # Reading all corresponding masks is mandatory, including E/S.
            selected=pixels(maskpath)
            checks[direction]['mask_red_pixels']=int(((selected[:,:,0]>180)&(selected[:,:,1]<80)&(selected[:,:,3]>127)).sum())
            aa=left[:,:,3]>127;bb=right[:,:,3]>127
            ious[direction]=round(float((aa&bb).sum()/(aa|bb).sum()),4)
        else:
            left=np.zeros_like(right)
        rows.append(np.concatenate([composite(left),composite(right)],axis=1))
        all_thumbs.append((asset,direction,composite(right)))
    assert max(scales)-min(scales)<1e-6
    assert len(set(targets))==1
    assert all(node.type!='TEX_IMAGE' for m in bpy.data.materials if m.use_nodes for node in m.node_tree.nodes)
    sheet=np.concatenate(rows,axis=0)
    save(os.path.join(ROOT,asset,'comparison.png'),sheet)
    results[asset]={'views':checks,'same_scale_and_target':True,
                    'silhouette_iou_after_uniform_crop_fit':ious,
                    'comparison_sheet':'Left: source with black stroke removed; right: render. Rows North, South, East. Empty source cells mean unavailable views.',
                    'packed_reference_images':len([i for i in bpy.data.images if i.packed_file])}
with open(os.path.join(ROOT,'verification.json'),'w') as f:json.dump(results,f,indent=2)
overview=np.concatenate([np.concatenate([t[2] for t in all_thumbs[i:i+3]],axis=1) for i in range(0,len(all_thumbs),3)],axis=0)
save(os.path.join(ROOT,'all_assets.png'),overview)
print(json.dumps({a:r['silhouette_iou_after_uniform_crop_fit'] for a,r in results.items()},indent=2))
