"""Authored component/face roles using the established M2 steel palette."""
from pathlib import Path
import json,math
ROOT=Path(__file__).resolve().parent
SETTINGS=json.loads((ROOT/'paint_roles.json').read_text())
PALETTE=json.loads((ROOT/SETTINGS['palette']).read_text())['palette']
PALETTE.update(polymer=(37,43,40),polymer_top=(61,68,63))

def role(kind,component,layer):
    for name,ids in SETTINGS['weapons'][kind].items():
        if component in ids:return name
    return SETTINGS['default_barrel' if layer=='barrel' else 'default_body']

def select(kind,component,layer,normal,center,lo,hi,source_face=None):
    # Keep the M240 tube the same steel color as its exposed barrel. Being
    # visible through an aperture does not turn a barrel surface into a recess.
    for name,faces in SETTINGS.get('face_roles',{}).get(kind,{}).items():
        if source_face in faces:return name
    part=role(kind,component,layer)
    profile=SETTINGS.get('surface_profiles',{}).get(kind,{}).get(part)
    if profile:
        if 'axis' in profile:
            axis='XYZ'.index(profile['axis']);cross=[i for i in range(3) if i!=axis]
            delta=[center[i]-(lo[i]+hi[i])/2 for i in cross]
            radial=sum(normal[i]*d for i,d in zip(cross,delta))/max(math.hypot(*delta),1e-9)
            if radial<-.4:return profile['bore']
            return profile['cap'] if abs(normal[axis])>.82 else profile['outer']
        # Only reviewed plate components use this palette. Broad cover slopes
        # get an intermediate steel color instead of abruptly becoming a wall.
        if normal.z>.9:return profile['top']
        if normal.z>.15:return profile.get('slope',profile['top'])
        return profile['bottom'] if normal.z<-.65 else profile['wall']
    if part=='receiver':
        return 'receiver' if normal.z>.65 else 'recess' if normal.z<-.65 else 'receiver_wall'
    if part=='cover':
        return 'cover' if normal.z>.65 else 'recess' if normal.z<-.65 else 'cover_wall'
    if part=='polymer':return 'polymer_top' if normal.z>.65 else 'polymer'
    if part in ['cover_wall','sight','handle','fastener']:return part
    if part=='mechanism':return 'receiver'
    # The short cross-section axes are X/Z for the normalized +Y weapon axis.
    # Distinguish inward bore/vent walls; every outward cylindrical wall keeps
    # one fixed color around its entire circumference, irrespective of height.
    axis_x=(lo.x+hi.x)/2;axis_z=(lo.z+hi.z)/2
    dx=center.x-axis_x;dz=center.z-axis_z;radius=math.hypot(dx,dz)
    radial=(normal.x*dx+normal.z*dz)/max(radius,1e-9)
    if abs(normal.y)>.82:
        return 'recess' if part=='muzzle' and normal.y>.82 else 'sleeve_rim' if part=='sleeve' else 'barrel_shoulder'
    # Aperture cut walls point across the opening, not toward the weapon axis.
    # Testing only negative radial normals left these walls as light rim paint.
    if kind=='M240B' and part=='sleeve':
        return 'barrel_taper' if radial<=.45 else 'sleeve_outer'
    if radial<-.4:return 'recess'
    if part=='sleeve':return 'sleeve_outer' if radial>.45 else 'sleeve_rim'
    return 'muzzle' if part=='muzzle' else 'barrel_root' if part=='barrel_root' else 'barrel_taper'

def report():
    def lum(rgb):return sum(c*w for c,w in zip(rgb,(.2126,.7152,.0722)))
    gap=lum(PALETTE['cover'])-lum(PALETTE['receiver'])
    assert gap>=24
    return dict(palette_source=SETTINGS['palette'],palette=PALETTE,component_roles=SETTINGS['weapons'],
        receiver_cover_luminance_gap=gap,curved_walls='single fixed color; recesses and axial caps are separate roles',
        source_face_roles=SETTINGS.get('face_roles',{}),
        surface_profiles=SETTINGS.get('surface_profiles',{}),
        global_height_ranking=False,lights=False,geometry_edits=False)
