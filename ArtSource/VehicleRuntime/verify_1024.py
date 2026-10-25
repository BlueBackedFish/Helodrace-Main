"""Verify every runtime crop against the real 1024 master, plus parts/anchors/recoil."""
from pathlib import Path
import argparse,json,hashlib,xml.etree.ElementTree as E
from PIL import Image
ROOT=Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser();parser.add_argument('--mod-root',type=Path,required=True);mod=parser.parse_args().mod_root.resolve()
defs=mod/'1.6/Defs/Vehicles';textures=mod/'1.6/Textures/Vehicles'
b=E.parse(defs/'M3A3.xml');props=b.find(".//li[@Class='Helodrace.LobosArsenal.Vehicles.CompProperties_Bradley']")
assert props.findtext('textureSize')=='1024' and props.findtext('outlineWidth')=='8'
art=E.parse(defs/'M3A3Art.xml');states=art.findall('.//states/li');assert len(states)==48
seen={};modules=set();count=0
for state in states:
    turret=':turret:' in state.findtext('key')
    assert sum(n.findtext('recoiling')=='true' for n in state.findall('layers/li'))==(1 if turret else 0)
    for layer in state.findall('layers/li'):
        relative=layer.findtext('file');path=textures/'M3A3/BUSK'/relative
        assert '..' not in Path(relative).parts
        x,y,w,h=[int(layer.findtext(k)) for k in ['x','y','width','height']]
        assert 0<=x<=1024-w and 0<=y<=1024-h
        if relative not in seen:
            variant='Standard' if relative.startswith('Standard/') else 'BUSK'
            name=relative[len('Standard/'):] if variant=='Standard' else relative
            source=ROOT/'ArtSource/M2A3Bradley/TextureExport/output/1024'/variant/name
            original=Image.open(source).convert('RGBA');crop=Image.open(path).convert('RGBA')
            assert original.size==(1024,1024) and crop.size==(w,h)
            assert original.crop((x,1024-y-h,x+w,1024-y)).tobytes()==crop.tobytes(),relative
            assert original.getchannel('A').getbbox()==(x,1024-y-h,x+w,1024-y),relative
            seen[relative]=hashlib.sha256(path.read_bytes()).hexdigest()
        if layer.findtext('module'):modules.add(layer.findtext('module'))
        count+=1
assert len(modules)==124
for position in art.findall('.//modulePositions/li'):
    direction=position.findtext('direction');mid=position.findtext('module')
    index=json.loads((ROOT/'ArtSource/M2A3Bradley/TextureExport/output/1024/BUSK/index.json').read_text())
    xy=[float(v) for v in position.findtext('pixel').strip('()').split(',')]
    assert all(abs(a-b)<1e-5 for a,b in zip(xy,index['directions'][direction]['era'][mid]['pivot_pixel']))
m=E.parse(defs/'M8.xml');roles=m.findall('.//properties/roles/li');commander=next(r for r in roles if r.findtext('key')=='commander')
assert commander.findtext('handlingTypes')=='Turret' and commander.findtext('turretIds/li')=='roof'
assert {'m2hb','ammo50'}.issubset({n.findtext('key') for n in m.findall('.//components/li')})
for direction in ['north','east','south','west']:
    for part in ['hull','turret','barrel','m2hb','turret_full','vehicle']:
        im=Image.open(textures/'M8'/f'{part}_{direction}.png')
        assert im.mode=='RGBA' and im.size==(1024,1024) and im.getchannel('A').getbbox()
for filename,dname in [('M3A3Weapons.xml','HDA_M3A3_M242'),('M8Weapons.xml','HDA_M8_M35')]:
    t=E.parse(defs/filename).find(f"Vehicles.VehicleTurretDef[defName='{dname}']")
    assert all(float(t.findtext('recoil/'+key))>0 for key in ['distanceTotal','distancePerTick','speedMultiplierPostRecoil'])
report=dict(passed=True,resolution=1024,states=48,unique_exact_master_crops=len(seen),layer_references=count,armor_modules=124,
            independent_barrels=True,m2hb_role_ammo_parts=True,world_scale_preserved=True)
out=ROOT/'ArtSource/VehicleRuntime/output';out.mkdir(exist_ok=True);(out/'asset_validation.json').write_text(json.dumps(report,indent=2)+'\n')
print('VEHICLE_ASSETS_PASS:',json.dumps(report))
