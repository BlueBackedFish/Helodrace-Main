"""Part-matched grayscale measurements translated to the editable TAN model."""
import json
from functools import lru_cache
from pathlib import Path
ROOT=Path(__file__).resolve().parent
@lru_cache(maxsize=4)
def reference_samples(path):return json.loads((ROOT/path).read_text())['samples']

def reference_tone(role,normal,settings):
    samples=reference_samples(settings['samples'])
    def sample(name):return samples[name]['median_gray']
    hull={'roof':sample('hull_roof'),'front':sample('hull_front_slope'),'side':sample('hull_side_slope'),
          'rear':sample('hull_front_slope'),'wall':sample('hull_wall')}
    turret={'roof':sample('turret_roof'),'front':sample('turret_front_slope'),'side':sample('turret_side_slope'),
            'rear':sample('turret_rear_slope'),'wall':sample('turret_wall')}
    if role=='turret_assembly':levels=turret.copy()
    elif role=='hull_assembly':levels=hull.copy()
    else:
        mapping={
          'turret_hatch_a':'hatch_dome','turret_hatch_b':'hull_roof','driver_hatch':'hull_roof',
          'rear_roof_hatch':'hull_roof','tow_launcher':'tow_roof','tow_mount':'tow_body','tow_end_cover':'tow_roof',
          'gun_mount':'gun_mantlet','main_barrel':'barrel','gun_upper_shroud':'gun_shroud',
          'turret_front_upper_cover':'gun_shroud','turret_front_lower_cover':'gun_shroud',
          'rear_stowage_bin':'rear_bin_wall','side_skirt':'hull_skirt',
          'commander_sight':'tow_roof','commander_sight_guard':'tow_roof',
          'turret_linkage_support':'gun_shroud','turret_front_linkage':'tow_body','turret_front_mechanism':'tow_body',
          'turret_roof_equipment':'turret_roof','front_service_panel':'hull_front_slope',
          'rear_ramp':'hull_wall','personnel_door':'hull_wall','road_wheel':'hull_skirt',
          'drive_sprocket':'hull_skirt','rear_idler':'hull_skirt'}
        top=sample(mapping.get(role,'hull_roof'))
        if role=='rear_basket_floor':top=min(sample('rear_basket_floor'),sample('turret_rear_slope')-25)
        if role in {'smoke_launcher','smoke_mount'}:top=sample('gun_shroud')
        if role=='hatch_lid_handle':top=255
        levels={'roof':top,'front':top*.78,'side':top*.69,'rear':top*.72,'wall':top*.60}
    nz=max(0,min(1,float(normal.z)))
    slope=levels['front' if normal.y<-.25 and abs(normal.y)>=abs(normal.x) else 'rear' if normal.y>.25 and abs(normal.y)>=abs(normal.x) else 'side']
    # Lid curvature gets its own smooth normal modulation in the render shader.
    if role in {'hull_assembly','turret_assembly'}:
        if nz>.88:gray=levels['roof']
        elif nz>.15:gray=slope
        else:gray=levels['wall']
    elif role in {'turret_hatch_a','turret_hatch_b','driver_hatch'} and nz>.4:gray=levels['roof']
    else:gray=levels['wall']+(levels['roof']-levels['wall'])*min(1,nz/.35)
    if normal.z<-.25:gray=levels['wall']*.7
    return gray/255,gray/max(levels['roof'],1)

def desaturate_shade(rgb,ratio,settings):
    saturation=max(settings['minimum_saturation'],min(1,1-settings['strength']*(1-ratio)))
    y=sum(c*w for c,w in zip(rgb,(.2126,.7152,.0722)))
    return tuple(y+(c-y)*saturation for c in rgb)
