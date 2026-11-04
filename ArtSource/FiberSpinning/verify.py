"""Validate the saved export contract and game texture links without Blender."""
from pathlib import Path
import json,xml.etree.ElementTree as E
from PIL import Image
ROOT=Path(__file__).resolve().parent;REPO=ROOT.parent.parent
meta=json.loads((ROOT/'validation.json').read_text())
d=E.parse(REPO/'Defs/GreatWar/Buildings/FiberSpinningModule.xml').getroot().find('ThingDef')
assert d.findtext('size')=='(1,1)' and d.findtext('graphicData/drawSize')=='(1.5,1.5)'
offset=[float(x) for x in d.findtext('graphicData/drawOffset').strip('()').split(',')]
assert max(abs(a-b) for a,b in zip(offset,meta['expected_draw_offset']))<1e-7
assert meta['outline_pixels']==12 and meta['mesh_objects']>=25
hashes=set()
for direction in ['north','east','south','west']:
    p=REPO/'Textures/Buildings'/('HD_FiberSpinningModule_'+direction+'.png');m=p.with_stem(p.stem+'m')
    with Image.open(p) as a,Image.open(m) as b:
        assert a.size==b.size==(256,256) and a.getchannel('A').tobytes()==b.getchannel('A').tobytes()
        box=a.getchannel('A').point(lambda v:255 if v>127 else 0).getbbox()
        assert min(box[0],box[1],256-box[2],256-box[3])>=12
        hashes.add(a.tobytes())
    assert abs(meta['cameras'][direction.title()]['downward_degrees']-64)<.001
assert len(hashes)==4
for stem in ['HD_Nylon','HD_Aramid']:
    for suffix in ['a','b']:
        with Image.open(REPO/'Textures/Items'/stem/(stem+'_'+suffix+'.png')) as im:
            assert im.size==(256,256) and im.getchannel('A').getextrema()==(0,255)
for file in ['HD_FiberSpinningModule.blend','HD_Nylon.blend','HD_Aramid.blend']:
    assert (ROOT/file).stat().st_size>10000
print('PASS: 12 game PNGs, four distinct 64-degree views, matching masks, compensated anchor, 1x1 density, 12px outline and three saved Blender sources.')
