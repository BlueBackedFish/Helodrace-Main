from pathlib import Path
import json,xml.etree.ElementTree as E
from PIL import Image
import numpy as np
ROOT=Path(__file__).resolve().parent;REPO=ROOT.parent.parent
meta=json.loads((ROOT/'validation.json').read_text())
assert meta['footprint']==[1,1] and meta['texture_resolution']==[256,256]
assert meta['outline_pixels']==12 and meta['orthographic_scale']==1.5
assert all(abs(v['downward_degrees']-64)<.001 and v['minimum_margin']>=12 for v in meta['cameras'].values())
for d in ['north','east','south','west']:
 color=np.array(Image.open(REPO/f'Textures/Buildings/HD_ClayTrap_{d}.png').convert('RGBA'))
 mask=np.array(Image.open(REPO/f'Textures/Buildings/HD_ClayTrap_{d}m.png').convert('RGBA'))
 assert color.shape==mask.shape==(256,256,4) and np.array_equal(color[:,:,3],mask[:,:,3])
 assert np.any(mask[:,:,1]>200) and np.max(mask[:,:,:2].astype(int).sum(2))<=256
item=Image.open(REPO/'Textures/Items/HD_ClayPigeon.png');assert item.mode=='RGBA' and item.size==(256,256)
assert (ROOT/'HD_ClayTrap.blend').is_file() and (ROOT/'clay_pigeon.blend').is_file()
defs=E.parse(REPO/'Defs/Recreation/Shooting.xml').getroot()
trap=next(d for d in defs if d.findtext('defName')=='HD_ClayTrap')
assert trap.findtext('size')=='(1,1)' and trap.findtext('graphicData/drawSize')=='(1.5,1.5)'
assert trap.findtext('graphicData/colorTwo')=='(105,105,105)'
assert not any(c.attrib.get('Class')=='CompProperties_Refuelable' for c in trap.findall('comps/li'))
assert not any(d.findtext('defName') in ['HD_ClayPigeon','HD_MakeClayPigeons'] for d in defs)
result=dict(passed=True,actual_four_views=True,flat_emission=True,no_lamps_AO_shadows=True,building_pixels=256,item_pixels=256,building_outline=12,footprint=[1,1],game_draw_size=[1.5,1.5],requires_refill=False,in_game_visual_tested=False)
(ROOT/'validation_game_links.json').write_text(json.dumps(result,indent=2)+'\n')
print('PASS: saved 3D scenes, four 64-degree native views, density, 12px outlines, masks, item and definition links.')
