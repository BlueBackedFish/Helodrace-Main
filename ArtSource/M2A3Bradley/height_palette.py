"""Display-space paint from authored contact relations, never absolute height."""
WEIGHTS=(.2126,.7152,.0722)
COLOR_ATTRIBUTE='Vehicle final paint'
LUMINANCE_ATTRIBUTE='Final paint luminance'
SATURATION_ATTRIBUTE='Paint saturation'


def linear(v):
    v=v/255;return v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4


def display(v):return 255*(12.92*v if v<=.0031308 else 1.055*max(0,v)**(1/2.4)-.055)


def contact_levels(settings):
    """Only explicit support edges create a minimum luminance step.

    Sibling parts may share a value regardless of their world-space heights.
    A broad curved lid reserves headroom before its attached handle is solved.
    No object names or world coordinates belong in this common calculation.
    """
    c=settings['contact_palette'];result={};visiting=set()
    def solve(name):
        if name in result:return result[name]
        assert name not in visiting,('Cyclic paint support',name)
        visiting.add(name);node=c['nodes'][name]
        if 'support' in node:
            parent=c['nodes'][node['support']]
            value=solve(node['support'])+parent.get('curve_headroom',0)+c['minimum_step']
        else:value=node['base']
        assert settings['minimum_luminance']<=value<=settings['maximum_luminance'],(name,value)
        result[name]=value;visiting.remove(name);return value
    for name in c['nodes']:solve(name)
    return result


def top_luminance(role,settings):
    c=settings['contact_palette']
    return contact_levels(settings)[c['roles'].get(role,c['default_node'])]


def final_paint(normal,role,recess,settings,final_value=None,curve_offset=0):
    """Return linear RGB and final display Y/saturation for an unlit surface."""
    value=top_luminance(role,settings)+curve_offset;nz=max(0,min(1,normal.z))
    form=max(0,1-nz/settings['upward_normal_threshold'])
    direction=abs(normal.x)/(abs(normal.x)+abs(normal.y)+1e-6)
    value-=form**settings['form_exponent']*(settings['wall_darkening']+settings['side_darkening']*direction)
    armor=settings['armor_plane_contrast']
    if role in armor['roles'] and nz<settings['upward_normal_threshold']:
        level=(1 if nz>armor['wall_normal_threshold'] else 2)+int(direction>=.5)
        value=top_luminance(role,settings)-armor['step']*level
    if role=='main_barrel':
        value=settings['barrel_luminance'][0]+nz*(settings['barrel_luminance'][1]-settings['barrel_luminance'][0])
    if nz<settings['upward_normal_threshold']:value*=recess
    if role=='rear_basket_floor':value-=settings['rear_floor_darkening']
    if final_value is not None:value=final_value
    value=max(settings['minimum_luminance'],min(settings['maximum_luminance'],value))
    sat=max(settings['minimum_saturation'],min(1,(value-65)/165))
    if role=='main_barrel':sat=.08
    tint=settings['tan_tint_srgb'];y=sum(c*w for c,w in zip(tint,WEIGHTS));ratio=[c/y for c in tint]
    deviation=max(ratio)-1
    if deviation>0:sat=min(sat,(255-value)/(value*deviation+1e-12))
    rgb=[value*(1+(c-1)*sat) for c in ratio]
    return tuple(linear(c) for c in rgb),value,sat
