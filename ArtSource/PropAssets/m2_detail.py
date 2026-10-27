"""M2 steel surface roles in each asset's preserved, leveled coordinates.

The original apertures reveal the dark barrel underneath. Inward bore/cut
walls are a recess material; outward cylindrical walls keep a single color.
Actual axial diameter transitions receive separate fixed role colors.
"""
from pathlib import Path
import json,math

SETTINGS=json.loads(Path(__file__).with_suffix('.json').read_text())
PALETTE=SETTINGS['palette']


def radial(center,normal,profile):
    x=center[0]-profile['axis_xz'][0];z=center[2]-profile['axis_xz'][1]
    radius=math.hypot(x,z)
    return radius,(normal[0]*x+normal[2]*z)/max(radius,1e-9)


def sleeve(center,normal,kind):
    p=SETTINGS['profiles'][kind];radius,dot=radial(center,normal,p)
    if p['vent_range_y'][0]<center[1]<p['vent_range_y'][1]:
        if dot<.45:return 'recess'
        if dot<.82:return 'sleeve_rim'
    elif abs(normal[1])>.8:return 'sleeve_rim'
    return 'sleeve_outer'


def barrel(center,normal,kind):
    p=SETTINGS['profiles'][kind];radius,dot=radial(center,normal,p)
    if center[1]<p['sleeve_range_y'][1]:return 'recess'
    if dot<-.45:return 'recess'
    if 'carry_handle_range_y' in p and p['carry_handle_range_y'][0]<center[1]<p['carry_handle_range_y'][1] and radius>p['carry_handle_radius']:return 'handle'
    if abs(normal[1])>.7:return 'barrel_shoulder'
    if center[1]<p['root_end_y']:return 'barrel_root'
    if center[1]<p['shoulder_end_y']:return 'barrel_shoulder'
    return 'muzzle' if center[1]>=p['muzzle_start_y'] else 'barrel_taper'


def report(kind):
    return dict(palette=PALETTE,profile=SETTINGS['profiles'][kind],
                note='Fixed material roles; actual source apertures, inward cut walls and axial shoulders; no cylindrical light gradient or geometry change')
