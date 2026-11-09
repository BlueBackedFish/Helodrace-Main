"""Discrete UV-atlas retouching; never blur or repaint the broad camouflage."""
import numpy as np
from PIL import Image,ImageFilter


def components(mask):
    """Run-length connected regions avoid a dependency on scipy/OpenCV."""
    parents=[];runs=[];previous=[]
    def find(index):
        while parents[index]!=index:
            parents[index]=parents[parents[index]];index=parents[index]
        return index
    for y,row in enumerate(mask):
        limits=np.flatnonzero(np.diff(np.r_[False,row,False]));current=[]
        for left,right in zip(limits[::2],limits[1::2]):
            index=len(parents);parents.append(index);runs.append((y,int(left),int(right)));current.append(index)
            for other in previous:
                _,a,b=runs[other]
                if b<=left:continue
                if a>=right:break
                parents[find(index)]=find(other)
        previous=current
    groups={}
    for index,(y,left,right) in enumerate(runs):
        region=groups.setdefault(find(index),dict(area=0,bounds=[left,y,right,y+1],runs=[]))
        region['area']+=right-left;region['runs'].append((y,left,right))
        a,b,c,d=region['bounds'];region['bounds']=[min(a,left),min(b,y),max(c,right),max(d,y+1)]
    return groups.values()


def clean(labels,roles,settings):
    source=labels.copy();height,width=labels.shape
    assert width==height==settings['atlas_resolution']
    selected=[];mask=np.zeros_like(labels,dtype=bool)
    explicit=settings['remove_components_by_bounds'];matched=set()
    for index,role in enumerate(roles):
        for region in components(source==index):
            bounds=region['bounds']
            named=bounds in explicit.get(role,[])
            if named:matched.add((role,tuple(bounds)))
            if role in settings['remove_all_roles'] or region['area']<=settings['remove_small_components'].get(role,0) or named:
                region['role']=role;selected.append(region)
                for y,left,right in region['runs']:mask[y,left:right]=True
    assert matched=={(role,tuple(b)) for role,boxes in explicit.items() for b in boxes},'Source panel regions changed'
    # Read the dominant surviving paint just outside each selected graphic.
    # The fill uses one existing palette label, with no color interpolation.
    for region in selected:
        left,top,right,bottom=region['bounds'];pad=12
        x0=max(0,left-pad);y0=max(0,top-pad);x1=min(width,right+pad);y1=min(height,bottom+pad)
        local=np.zeros((y1-y0,x1-x0),dtype=np.uint8)
        for y,a,b in region['runs']:local[y-y0,a-x0:b-x0]=255
        ring=np.asarray(Image.fromarray(local).filter(ImageFilter.MaxFilter(17)))>0
        ring&=~mask[y0:y1,x0:x1]
        values=source[y0:y1,x0:x1][ring]
        replacement=int(np.bincount(values,minlength=len(roles)).argmax()) if len(values) else roles.index('body')
        for y,a,b in region['runs']:labels[y,a:b]=replacement
    for region in settings['solid_retouch_regions']:
        left,top,right,bottom=region['bounds'];labels[top:bottom,left:right]=roles.index(region['role'])
        mask[top:bottom,left:right]=True
    assert np.array_equal(source[~mask],labels[~mask]),'Paint outside retouch regions changed'
    assert not np.any(labels==roles.index('vent'))
    report=dict(passed=True,removed_source_graphic_components=len(selected),
        explicit_panel_regions=len(matched),solid_retouch_regions=[r['name'] for r in settings['solid_retouch_regions']],
        removed_logos=['wing insignia'],
        edited_pixels=int(np.count_nonzero(source!=labels)),
        unchanged_outside_retouch_mask=True,blur_used=False,model_geometry_changed=False)
    return labels,mask,report
