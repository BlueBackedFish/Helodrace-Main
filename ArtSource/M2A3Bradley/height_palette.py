"""Final display luminance from physical height, with explicit pigment exceptions."""
from math import log1p
WEIGHTS=(.2126,.7152,.0722)
COLOR_ATTRIBUTE='Vehicle final paint'
LUMINANCE_ATTRIBUTE='Final paint luminance'
SATURATION_ATTRIBUTE='Paint saturation'

def height_luminance(z,settings):
    points=settings['height_luminance_ramp']
    if z<=points[0][0]:return points[0][1]
    for (za,va),(zb,vb) in zip(points,points[1:]):
        if z<=zb:return va+(vb-va)*(z-za)/(zb-za)
    return points[-1][1]

def linear(v):
    v=v/255;return v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4

def display(v):return 255*(12.92*v if v<=.0031308 else 1.055*max(0,v)**(1/2.4)-.055)

def contrast_levels(settings):
    c=settings['step_contrast']
    levels=[height_luminance(z,settings) for z in c['reference_heights']]
    steps=[c['minimum_step']+c['log_gain']*log1p((b-a)/c['soft_gap']) for a,b in zip(levels,levels[1:])]
    value=c['top_level_luminance']-sum(steps);mapped=[value]
    for step in steps:value+=step;mapped.append(value)
    return [(settings['minimum_luminance'],settings['minimum_luminance'])]+list(zip(levels,mapped))+[(settings['maximum_luminance'],settings['maximum_luminance'])]

def contrast_luminance(value,settings):
    points=contrast_levels(settings)
    for (a,va),(b,vb) in zip(points,points[1:]):
        if value<=b:return va+(vb-va)*(value-a)/(b-a)
    return points[-1][1]

def final_paint(z,normal,role,recess,settings,final_value=None):
    """Return linear RGB plus *final* display-space luminance and saturation.

    Upward TAN plates have no per-object baseline, camera factor, or recess
    multiplier: their height order cannot be undone later in the shader.
    """
    value=height_luminance(z,settings);nz=max(0,min(1,normal.z))
    form=max(0,1-nz/settings['upward_normal_threshold'])
    direction=abs(normal.x)/(abs(normal.x)+abs(normal.y)+1e-6)
    value-=form**settings['form_exponent']*(settings['wall_darkening']+settings['side_darkening']*direction)
    if role=='main_barrel':
        value=settings['barrel_luminance'][0]+nz*(settings['barrel_luminance'][1]-settings['barrel_luminance'][0])
    elif role=='hatch_lid_handle':value=min(settings['handle_highlight_ceiling'],value+settings['handle_highlight'])
    if nz<settings['upward_normal_threshold']:value*=recess
    value=contrast_luminance(value,settings)
    if role=='rear_basket_floor':value-=settings['rear_floor_darkening']
    # Cel patches supply an already contrasted, authored two-color value.
    if final_value is not None:value=final_value
    value=max(settings['minimum_luminance'],min(settings['maximum_luminance'],value))
    sat=max(settings['minimum_saturation'],min(1,(value-65)/165))
    if role=='main_barrel':sat=.08
    tint=settings['tan_tint_srgb'];y=sum(c*w for c,w in zip(tint,WEIGHTS));ratio=[c/y for c in tint]
    # Preserve luminance and highlight headroom; no clipped red channel or
    # blanket exposure lift that collapses the upper equipment into white.
    deviation=max(ratio)-1
    if deviation>0:sat=min(sat,(255-value)/(value*deviation+1e-12))
    rgb=[value*(1+(c-1)*sat) for c in ratio]
    return tuple(linear(c) for c in rgb),value,sat
