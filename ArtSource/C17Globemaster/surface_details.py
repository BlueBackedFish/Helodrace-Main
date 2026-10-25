"""Flat painted details in supplied OBJ coordinates, clipped by visible 3D masks."""
from pathlib import Path
import json,math
from PIL import Image,ImageDraw,ImageChops


def paint_details(root,style,model):
    size=style['master_size'];ratio=size/style['game_resolution']
    scale=model['ortho_scale'];cx,cy,_=model['camera_center']
    colors=style['detail_colors'];center=203.949
    layers={name:Image.new('RGBA',(size,size)) for name in ['body','wing','engine','tail']}
    counts={name:0 for name in layers}
    def point(x,z):
        return (round((.5-(x-cx)/scale)*size),round((.5-(z+cy)/scale)*size))
    def width(value):return max(1,round(value*ratio))
    def line(kind,points,color='seam',w=3):
        ImageDraw.Draw(layers[kind]).line([point(x,z) for x,z in points],fill=tuple(colors.get(color,color))+(255,),width=width(w),joint='curve')
        counts[kind]+=1
    def rectangle(kind,x0,x1,z0,z1,fill=None,outline='hatch_border',w=3,radius=0):
        a,b=point(x0,z0),point(x1,z1)
        box=(min(a[0],b[0]),min(a[1],b[1]),max(a[0],b[0]),max(a[1],b[1]))
        draw=ImageDraw.Draw(layers[kind])
        options=dict(fill=tuple(colors[fill])+(255,) if fill else None,
            outline=tuple(colors[outline])+(255,) if outline else None,width=width(w))
        if radius:draw.rounded_rectangle(box,radius=width(radius),**options)
        else:draw.rectangle(box,**options)
        counts[kind]+=1
    def dot(kind,x,z,rgb,r=2):
        px,py=point(x,z);r=width(r)
        ImageDraw.Draw(layers[kind]).ellipse((px-r,py-r,px+r,py+r),fill=tuple(rgb)+(255,))
        counts[kind]+=1
    def hatch(kind,x,z,half_x,half_z):
        rectangle(kind,x-half_x,x+half_x,z-half_z,z+half_z,'hatch','hatch_border',3,5)
        rectangle(kind,x-half_x+.4,x+half_x-.4,z-half_z+.4,z+half_z-.4,None,'seam',1.5,3)
        line(kind,[(x-.65,z),(x+.65,z)],'latch',4)
        for side in [-1,1]:
            line(kind,[(x+side*half_x,z-.55),(x+side*half_x,z+.55)],'latch',5)
        for dx in [-half_x+.7,half_x-.7]:
            for dz in [-half_z+.7,half_z-.7]:dot(kind,x+dx,z+dz,colors['fastener'],1.6)

    # Two continuous, restrained crown-panel seams; these are painted regions,
    # with no light gradient or change to the supplied shell geometry.
    stations=[(-52,3.4),(-24,7.4),(14,9.7),(49,10.0),(84,9.7),(115,7.4)]
    for sign in [-1,1]:
        line('body',[(center+sign*half,z) for z,half in stations],'seam',3)
    for z,half in [(-36,5.0),(-7,9.0),(23,10.6),(52,10.6),(81,10.1),(108,8.0)]:
        line('body',[(center-half,z),(center+half,z)],'seam',2.5)
    for z,half_x,half_z in [(120,3.1,2.2),(96,3.7,3.0),(65,3.4,3.3),(33,3.4,3.0),(-16,2.8,2.6)]:
        hatch('body',center,z,half_x,half_z)
    for z in [104,74,43,-5]:
        for side in [-1,1]:
            for i in range(5):
                zz=z+i*.45
                line('body',[(center+side*5.2,zz),(center+side*7.4,zz)],'vent',2)

    # Spanwise inspection/control-surface divisions follow the supplied swept
    # wing envelope. Actual visible masks clip every stroke at overlaps.
    for sign in [-1,1]:
        for distance in [35,50,65,80,95,110]:
            leading=79.84-(distance-18.38)*.57
            trailing=49.5-(distance-18.38)*.27
            x=center+sign*distance
            line('wing',[(x,trailing+1),(x,leading-1)],'seam',2.5)
            for z in [leading-2.5,trailing+2.5]:dot('wing',x,z,colors['fastener'],1.6)
        line('wing',[(center+sign*d,z) for d,z in [(22,65),(52,49),(82,32),(109,20)]],'seam',2.5)
        line('wing',[(center+sign*d,z) for d,z in [(22,41),(50,34),(80,26),(109,18)]],'seam',2.5)
    # Non-glowing identification lamps; no actual lighting/shader effects.
    dot('wing',320.7,20.2,[166,65,57],4.5)
    dot('wing',87.2,20.2,[61,134,104],4.5)

    for sign in [-1,1]:
        for offset,front,rear in [(33.7,97.8,62.4),(64.5,82.8,47.4)]:
            x=center+sign*offset
            line('engine',[(x-6.1,front-1.4),(x+6.1,front-1.4)],'lip',5)
            line('engine',[(x-5.8,front-2.3),(x+5.8,front-2.3)],'seam',2)
            line('engine',[(x-5.9,rear+8),(x+5.9,rear+8)],'seam',3)
            for side in [-1,1]:
                line('engine',[(x+side*4.8,rear+8),(x+side*4.8,front-4)],'seam',2.5)
            hatch('engine',x,(front+rear)/2,2.1,3.3)
            for i in range(6):
                z=rear+3+i*.45
                line('engine',[(x-2,z),(x+2,z)],'vent',2)
    for sign in [-1,1]:
        for distance in [14,28,40]:
            line('tail',[(center+sign*distance,-92),(center+sign*distance,-77)],'seam',2.5)

    from images import composite_linear
    result=Image.new('RGBA',(size,size))
    visible={}
    for kind,layer in layers.items():
        surface=Image.open(root/(kind+'_mask_4096.png')).convert('RGBA')
        mask=ImageChops.multiply(surface.getchannel('R'),surface.getchannel('A'))
        alpha=ImageChops.multiply(layer.getchannel('A'),mask)
        assert alpha.getbbox(),kind+' detail strokes missed visible source surfaces'
        layer.putalpha(alpha)
        visible[kind]=alpha.getbbox()
        result=composite_linear(result,layer,(0,0))
    result.save(root/'surface_details_4096.png')
    return result,dict(operations=counts,visible_bounds=visible,
        scope='Stylized painted skin/inspection panels, wing control divisions, nacelle covers/lips/vents and position lamps; not a certified C-17 maintenance diagram',
        source_geometry_modified=False,source_projection_preserved=True)
