"""Authored support levels and surface roles baked to fixed display RGB.

No lighting, absolute height ranking, camera-dependent tint or cylindrical
normal gradients. Asset builders own the actual component/face selections.
"""
from pathlib import Path
import json

SETTINGS=json.loads(Path(__file__).with_name('weapon_paint.json').read_text())
WEIGHTS=(.2126,.7152,.0722)


def luminance(rgb):return sum(c*w for c,w in zip(rgb,WEIGHTS))


def levels():
    result={}
    def solve(name):
        if name not in result:
            node=SETTINGS['nodes'][name]
            result[name]=solve(node['support'])+SETTINGS['minimum_contact_step'] if 'support' in node else node['base']
        return result[name]
    for name in SETTINGS['nodes']:solve(name)
    return result


def fde(value):
    # Keep the authored FDE hue at the main painted surface, darken supports
    # and neutralize only their darkest faces instead of washing tops to TAN.
    value=max(SETTINGS['minimum_paint_luminance'],value)
    saturation=max(SETTINGS['minimum_paint_saturation'],min(1,(value-SETTINGS['saturation_zero_luminance'])/(SETTINGS['saturation_full_luminance']-SETTINGS['saturation_zero_luminance'])))
    tint=SETTINGS['fde_tint_srgb'];mean=luminance(tint)
    return tuple(round(value*(1+(c/mean-1)*saturation)) for c in tint)


def face(normal):
    """Only call for authored planar housings, never rounded/cylindrical parts."""
    x,y,z=normal
    if z>.65:return 'top'
    if z<-.65:return 'bottom'
    if abs(x)>abs(y):return 'side'
    return 'front' if y>0 else 'rear'


def palette():
    result={}
    for role,value in levels().items():
        for surface,drop in SETTINGS['face_drop'].items():result[role+'_'+surface]=fde(value-drop)
    result.update(SETTINGS['exceptions'])
    return result


def report():
    colors=palette();contacts=[]
    for name,node in SETTINGS['nodes'].items():
        if 'support' not in node:continue
        top=name+'_top';bottom=node['support']+'_top';gap=luminance(colors[top])-luminance(colors[bottom])
        assert gap>=SETTINGS['minimum_contact_step']-.6,(name,gap)
        contacts.append(dict(above=top,below=bottom,display_luminance_gap=gap))
    return dict(reference=SETTINGS['reference'],palette=colors,authored_contacts=contacts,
                cylindrical_sides='single fixed color; caps may differ',world_height_ranking=False,
                lights=False,geometry_edits=False)
