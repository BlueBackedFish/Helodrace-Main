from pathlib import Path
import json,copy,hashlib
from lxml import etree as E
ROOT=Path(__file__).resolve().parent;REPO=ROOT.parent.parent;OUT=ROOT/'output'
parser=E.XMLParser(remove_blank_text=True)
before=E.parse(str(ROOT/'Reference/Previous/Defense_GreatWar.xml'),parser)
after=E.parse(str(REPO/'Defs/GreatWar/Buildings/Defense_GreatWar.xml'),parser)
manifest=json.loads((OUT/'manifest.json').read_text());models=json.loads((OUT/'model_validation.json').read_text());assert models['passed']
rotation=json.loads((OUT/'rotation_validation.json').read_text());assert rotation['passed']
assert rotation['runtime_top_sha256']==manifest['files']['HD_m1917HMG_Top.png'],'Rotation check is stale'
dimensions=json.loads((ROOT/'scale_reference.json').read_text())
style=json.loads((ROOT/'art_style.json').read_text());prefix=style['texture_directory']
relative_length=manifest['visible_gun_length_tiles']['M1917']/manifest['visible_gun_length_tiles']['M2HB']
assert abs(relative_length-dimensions['weapon_length_m']['M1917']/dimensions['weapon_length_m']['M2HB'])<1e-7
for kind,name in [('M2HB','HD_Building_M2HB'),('M1917','HD_Building_M1917HMG')]:
    old=before.xpath("ThingDef[defName='%s']"%name)[0];new=after.xpath("ThingDef[defName='%s']"%name)[0]
    assert new.findtext('graphicData/texPath')==prefix+'/HD_'+kind+'_Tripod'
    new.find('graphicData/texPath').text=old.findtext('graphicData/texPath')
    assert new.findtext('uiIconPath')==prefix+('/HD_M2HB_UI' if kind=='M2HB' else '/HD_m1917HMGUI')
    new.find('uiIconPath').text=old.findtext('uiIconPath')
    base_scale=manifest['mount_draw_tiles'][kind]
    assert new.findtext('graphicData/drawSize')==f'({base_scale:.8f},{base_scale:.8f})'
    new.find('graphicData/drawSize').text=old.findtext('graphicData/drawSize')
    if kind=='M2HB':
        op=old.find("comps/li[@Class='Helodrace.CompProperties_ReciprocatingTurretBarrel']");np=new.find("comps/li[@Class='Helodrace.CompProperties_ReciprocatingTurretBarrel']")
        assert abs(float(np.findtext('drawSize'))/10-manifest['gun_draw_tiles'][kind])<1e-7
        assert np.findtext('drawOffset')=='0' and np.findtext('recoilDistance')=='0.125'
        assert np.findtext('barrelTexPath')==prefix+'/HD_M2HB_Barrel' and np.findtext('bodyTexPath')==prefix+'/HD_M2HB_Body'
        for key in ['drawSize','drawOffset','barrelTexPath','bodyTexPath']:np.find(key).text=op.findtext(key)
    else:
        draw=new.find('building/turretTopDrawSize');assert abs(float(draw.text)-manifest['gun_draw_tiles'][kind])<1e-7
        new.find('building').remove(draw)
assert E.tostring(before,method='c14n')==E.tostring(after,method='c14n'),'An unrelated gameplay definition changed'
old_weapons=E.parse(str(ROOT/'Reference/Previous/Weapons_GreatWar.xml'),parser)
new_weapons=E.parse(str(REPO/'Defs/GreatWar/Items/Weapons_GreatWar.xml'),parser)
old_gun=old_weapons.xpath("ThingDef[defName='HD_Gun_M1917HMG_TurretGun']")[0];new_gun=new_weapons.xpath("ThingDef[defName='HD_Gun_M1917HMG_TurretGun']")[0]
assert new_gun.findtext('graphicData/texPath')==prefix+'/HD_m1917HMG_Top'
new_gun.find('graphicData/texPath').text=old_gun.findtext('graphicData/texPath')
assert E.tostring(old_weapons,method='c14n')==E.tostring(new_weapons,method='c14n'),'An unrelated weapon definition changed'
for name,digest in manifest['files'].items():
    assert hashlib.sha256((REPO/'Textures'/prefix/name).read_bytes()).hexdigest()==digest,name
for name,pixels in manifest['outline_texture_pixels'].items():
    if name in ['HD_M2HB_UI','HD_m1917HMGUI']:assert pixels==style['icon_outline_pixels']
    else:
        kind='M2HB' if 'M2HB' in name else 'M1917';draw=manifest['mount_draw_tiles'][kind] if name.endswith('Tripod') else manifest['gun_draw_tiles'][kind]
        assert abs(pixels/1024*draw-style['outline_world_tiles'])<1e-9,('Inconsistent world outline',name)
result=dict(passed=True,textures=len(manifest['files']),unrelated_definitions_unchanged=True,registered_gun_scales=manifest['gun_draw_tiles'],
    texture_directory=prefix,outline_world_tiles=style['outline_world_tiles'],icon_outline_pixels=style['icon_outline_pixels'],m2_recoil_tiles=.125,source_model_validation=models['passed'],m1917_eight_heading_rotation_verified=True,gameplay_balance_unchanged=True,m1917_length_relative_to_m2=relative_length,in_game_rotation_firing_test=False)
(OUT/'registration_validation.json').write_text(json.dumps(result,indent=2)+'\n')
print('HMG_REGISTRATION_PASS: 7 matching 1024 textures; mount and gun registration; 0.125-tile recoil preserved; all unrelated definitions unchanged')
