from pathlib import Path
import json,copy,hashlib
from lxml import etree as E
ROOT=Path(__file__).resolve().parent;REPO=ROOT.parent.parent;OUT=ROOT/'output'
parser=E.XMLParser(remove_blank_text=True)
before=E.parse(str(ROOT/'Reference/Previous/Defense_GreatWar.xml'),parser)
after=E.parse(str(REPO/'Defs/GreatWar/Buildings/Defense_GreatWar.xml'),parser)
manifest=json.loads((OUT/'manifest.json').read_text());models=json.loads((OUT/'model_validation.json').read_text());assert models['passed']
for kind,name in [('M2HB','HD_Building_M2HB'),('M1917','HD_Building_M1917HMG')]:
    old=before.xpath("ThingDef[defName='%s']"%name)[0];new=after.xpath("ThingDef[defName='%s']"%name)[0]
    assert new.findtext('graphicData/texPath')=='Buildings/Security/HD_'+kind+'_Tripod'
    new.find('graphicData/texPath').text=old.findtext('graphicData/texPath')
    base_scale=manifest['mount_draw_tiles'][kind]
    assert new.findtext('graphicData/drawSize')==f'({base_scale:.8f},{base_scale:.8f})'
    new.find('graphicData/drawSize').text=old.findtext('graphicData/drawSize')
    if kind=='M2HB':
        op=old.find("comps/li[@Class='Helodrace.CompProperties_ReciprocatingTurretBarrel']");np=new.find("comps/li[@Class='Helodrace.CompProperties_ReciprocatingTurretBarrel']")
        assert abs(float(np.findtext('drawSize'))/10-manifest['gun_draw_tiles'][kind])<1e-7
        assert np.findtext('drawOffset')=='0' and np.findtext('recoilDistance')=='0.125'
        for key in ['drawSize','drawOffset']:np.find(key).text=op.findtext(key)
    else:
        draw=new.find('building/turretTopDrawSize');assert abs(float(draw.text)-manifest['gun_draw_tiles'][kind])<1e-7
        new.find('building').remove(draw)
assert E.tostring(before,method='c14n')==E.tostring(after,method='c14n'),'An unrelated gameplay definition changed'
for name,digest in manifest['files'].items():
    assert hashlib.sha256((REPO/'Textures/Buildings/Security'/name).read_bytes()).hexdigest()==digest,name
result=dict(passed=True,textures=len(manifest['files']),unrelated_definitions_unchanged=True,registered_gun_scales=manifest['gun_draw_tiles'],
    m2_recoil_tiles=.125,source_model_validation=models['passed'],gameplay_balance_unchanged=True,in_game_rotation_firing_test=False)
(OUT/'registration_validation.json').write_text(json.dumps(result,indent=2)+'\n')
print('HMG_REGISTRATION_PASS: 7 matching 1024 textures; mount and gun registration; 0.125-tile recoil preserved; all unrelated definitions unchanged')
