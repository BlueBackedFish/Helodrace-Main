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
fibers=E.parse(REPO/'Defs/GreatWar/Items/SyntheticFibers.xml').getroot()
for d in fibers:
    assert d.findtext('graphicData/texPath')=='Things/Item/Resource/Cloth'
    assert d.findtext('graphicData/shaderType')=='CutoutComplex'
    assert d.findtext('graphicData/shaderParameters/_MaskTex')=='/Items/SyntheticFibers/ClothTintMask'
    assert d.findtext('graphicData/color')==d.findtext('stuffProps/color')
with Image.open(REPO/'Textures/Items/SyntheticFibers/ClothTintMask.png') as mask:
    assert mask.size==(2,2) and mask.convert('RGBA').tobytes()==bytes([255,0,0,255])*4
assert (ROOT/'HD_FiberSpinningModule.blend').stat().st_size>10000
print('PASS: 8 module PNGs, four distinct 64-degree views, matching masks, compensated 1x1 anchor, 12px outline, vanilla Cloth references and shared primary-color mask.')
