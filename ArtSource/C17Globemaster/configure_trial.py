"""Configure the supplied C-17 as a separate flat aircraft art trial."""
from pathlib import Path
import json, sys

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent/'VehiclePipeline'))
from pipeline import read_obj, inventory, write_json, sha256

profile = json.loads((HERE/'vehicle.json').read_text(encoding='utf-8'))
profile['axes'] = dict(forward='z', up='y', scale=1, confirmed=True)
profile['variant_notes'] = 'Supplied Roblox-export C-17; flat top view on a 19x19-tile drawing canvas. Original units unverified; artwork only, no aircraft gameplay added.'
profile['draw_size_tiles'] = [19,19]
profile['render_pipeline'] = 'build_trial.py, then package_trial.py; aircraft-specific style, not the ground-vehicle renderer'
profile['review'] = dict(blueprint=False, style_reference=False, native_size=False, geometry=False, game=False)
groups = {
    'control': ['Rightaileron1','Leftaileron1','Meshpart11','Meshpart120','Meshpart10','Meshpart90'],
    'wing': ['Meshpart21','Meshpart122'],
    'wing_detail': ['Meshpart99','Meshpart110','Meshpart111'],
    'tail': ['Meshpart20'],
    'rudder': ['Meshpart94','Meshpart124'],
    'engine': ['Meshpart14','Meshpart19','Meshpart131','Meshpart16','Meshpart126'],
    'engine_aft': ['Meshpart18','Meshpart130','Meshpart17','Meshpart129'],
    'engine_lip': ['Meshpart12','Meshpart13','Meshpart93'],
    'engine_fan': ['Meshpart6','Meshpart7','Meshpart8','Meshpart9'],
    'glass': ['Meshpart82','Meshpart101'],
    'ramp': ['Meshpart1','Meshpart2','Meshpart3','Main1'],
    'fin_detail': ['Meshpart96'],
}
assignments = {name:role for role,names in groups.items() for name in names}
for part in profile['parts']:
    role = assignments.get(part['label'], 'body')
    if part['label'].startswith('Wheel') or part['label'] in ['Meshpart4','Meshpart5']:
        role = 'rubber'
    part.update(role=role, mode='flat', confirmed=True)
    part['paint_basis'] = 'Source group/part atlas; constant aircraft role color, no surface-normal or height shading'
profile['sources']['mtl'] = dict(path=str(Path(profile['sources']['obj']['path']).with_suffix('.mtl')))
profile['sources']['mtl']['sha256'] = sha256(profile['sources']['mtl']['path'])
write_json(HERE/'vehicle.json', profile)
write_json(HERE/'inventory.json', inventory(read_obj(profile['sources']['obj']['path']), profile['axes']))
write_json(HERE/'style.json', dict(
    schema=2, kind='flat-aircraft-absolute-scale', master_size=4096,
    game_draw_size_tiles=[19,19], reference_pixels_per_tile=512/3,
    game_resolution=round(19*512/3), transparent_margin_game_pixels=16,
    silhouette_outline_game_pixels=12, internal_panel_game_pixels=4,
    internal_panel_rgb=[117,123,128],
    roles=dict(body=[169,175,180],wing=[154,162,169],wing_detail=[161,169,175],
        control=[143,151,158],tail=[168,176,182],rudder=[145,154,162],
        engine=[183,190,195],engine_aft=[165,173,180],engine_lip=[204,210,214],
        engine_fan=[59,67,75],glass=[69,103,122],ramp=[153,161,168],
        fin_detail=[188,194,199],rubber=[45,49,53]),
    panel_roles=['control','wing_detail','engine_lip','glass'],
    surface_mask_roles=dict(body=['body','ramp'],wing=['wing','wing_detail','control'],
        engine=['engine','engine_aft','engine_lip'],tail=['tail','rudder','control']),
    detail_colors=dict(seam=[137,146,154],hatch=[177,184,190],hatch_border=[115,127,138],
        latch=[97,111,122],vent=[93,108,120],lip=[79,96,110],fastener=[121,134,145]),
    silhouette_rule='One black outside silhouette for the entire aircraft. Internal panels use muted gray; no separate black outlines around every mesh.',
    projection='Orthographic, exactly top-down, nose up. No lights, AO, shadows, normal shading or source photographic albedo.'
))
print('Configured C-17 flat aircraft trial; all 161 source groups retained')
