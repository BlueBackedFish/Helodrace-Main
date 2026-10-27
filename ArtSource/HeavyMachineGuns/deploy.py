"""Deploy the seven validated FDE textures and their exact definition references."""
from pathlib import Path
from collections import Counter
import argparse,datetime,hashlib,json,shutil,tempfile,os,copy
from lxml import etree as E
from PIL import Image
ART=Path(__file__).resolve().parent;ROOT=ART.parent.parent
parser=argparse.ArgumentParser();parser.add_argument('--installed',default='C:/Program Files (x86)/Steam/steamapps/common/RimWorld/Mods/HelodRace-Main');args=parser.parse_args()
INSTALLED=Path(args.installed).resolve();assert (INSTALLED/'About/About.xml').is_file()
manifest=json.loads((ART/'output/manifest.json').read_text());style=json.loads((ART/'art_style.json').read_text())
validation=json.loads((ART/'output/registration_validation.json').read_text());assert validation['passed'] and validation['texture_directory']==manifest['texture_directory']==style['texture_directory']
assert len(manifest['files'])==7 and all(Path(n).name==n and n.endswith('.png') for n in manifest['files'])
backup=ROOT/'.codex-staging/HMG-InstalledBackup'/datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%SZ');backup.mkdir(parents=True)
files={}

def sha(data):return hashlib.sha256(data).hexdigest()

def preserve(path):
    if path.exists():
        old=backup/path.relative_to(INSTALLED);old.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(path,old)

def replace(path,data):
    preserve(path);path.parent.mkdir(parents=True,exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=path.parent,prefix=path.name+'.',suffix='.hmg-temp',delete=False) as output:output.write(data);temporary=output.name
    os.replace(temporary,path);assert path.read_bytes()==data;files[path.relative_to(INSTALLED).as_posix()]=sha(data)

for name,digest in manifest['files'].items():
    relative=Path('Textures')/manifest['texture_directory']/name;data=(ROOT/relative).read_bytes();assert sha(data)==digest;replace(INSTALLED/relative,data)

definition_fields={
    'Defs/GreatWar/Buildings/Defense_GreatWar.xml':{
        'HD_Building_M2HB':['graphicData/texPath','graphicData/drawSize','uiIconPath',*["comps/li[@Class='Helodrace.CompProperties_ReciprocatingTurretBarrel']/"+key for key in ['barrelTexPath','bodyTexPath','drawSize','drawOffset']]],
        'HD_Building_M1917HMG':['graphicData/texPath','graphicData/drawSize','uiIconPath','building/turretTopDrawSize']},
    'Defs/GreatWar/Items/Weapons_GreatWar.xml':{'HD_Gun_M1917HMG_TurretGun':['graphicData/texPath']}}
registered={}
for relative,definitions in definition_fields.items():
    source=E.parse(str(ROOT/relative));target=E.parse(str(INSTALLED/relative));original=copy.deepcopy(target)
    for defname,paths in definitions.items():
        a=source.xpath("ThingDef[defName='%s']"%defname)[0];b=target.xpath("ThingDef[defName='%s']"%defname)[0]
        for path in paths:
            desired=a.find(path);node=b.find(path);assert desired is not None and node is not None,(defname,path)
            node.text=desired.text
            if path.endswith(('texPath','TexPath')) or path=='uiIconPath':registered[defname+'/'+path]=node.text
    # Prove that the targeted art leaves are the only installed-definition edits.
    normalized=copy.deepcopy(target)
    for defname,paths in definitions.items():
        old=original.xpath("ThingDef[defName='%s']"%defname)[0];new=normalized.xpath("ThingDef[defName='%s']"%defname)[0]
        for path in paths:new.find(path).text=old.findtext(path)
    assert E.tostring(original,method='c14n')==E.tostring(normalized,method='c14n')
    content=(INSTALLED/relative).read_bytes()
    if relative.endswith('Weapons_GreatWar.xml'):
        old_path=original.xpath("ThingDef[defName='HD_Gun_M1917HMG_TurretGun']/graphicData/texPath")[0].text
        new_path=target.xpath("ThingDef[defName='HD_Gun_M1917HMG_TurretGun']/graphicData/texPath")[0].text
        needle=('<texPath>'+old_path+'</texPath>').encode('utf-8');assert content.count(needle)==1
        content=content.replace(needle,('<texPath>'+new_path+'</texPath>').encode('utf-8'),1)
    else:
        prefix=b'<?xml version="1.0" encoding="utf-8"?>\n' if content.lstrip().startswith(b'<?xml') else b''
        content=prefix+E.tostring(target.getroot(),encoding='utf-8').rstrip()+b'\n'
    replace(INSTALLED/relative,content)

assert len(set(registered.values()))==7 and all(p.startswith(manifest['texture_directory']+'/') for p in registered.values())
for path in registered.values():assert (INSTALLED/'Textures'/(path+'.png')).is_file(),path
paint={}
for name in ['HD_M2HB_Tripod.png','HD_M1917_Tripod.png','HD_m1917HMG_Top.png','HD_M2HB_Body.png']:
    with Image.open(INSTALLED/'Textures'/manifest['texture_directory']/name) as image:
        counts=Counter(tuple(p[:3]) for p in image.convert('RGBA').getdata() if p[3]>250)
    expected=style['mount_fde_rgb'] if name.endswith('_Tripod.png') else style['fde_rgb']
    assert counts[tuple(expected)]>100,(name,expected)
    paint[name]=dict(dominant_rgb=list(counts.most_common(1)[0][0]),required_rgb=expected,required_rgb_pixels=counts[tuple(expected)])

# Remove only the seven superseded loose PNGs after the FDE paths are verified.
legacy=INSTALLED/'Textures/Buildings/Security'
removed=[]
for name in manifest['files']:
    path=legacy/name
    if path.exists():preserve(path);path.unlink();removed.append(path.relative_to(INSTALLED).as_posix())
report=dict(passed=True,runtime_files=len(files),backup=str(backup),files=files,registered_paths=registered,installed_paint=paint,removed_legacy_textures=removed,unrelated_installed_definitions_unchanged=True,in_game_render_test=False)
(ART/'output/deployment_validation.json').write_text(json.dumps(report,indent=2)+'\n')
print('HMG_FDE_DEPLOYMENT_PASS: seven registered PNG paths + two targeted XML files; installed darker FDE paints verified; backups in '+str(backup))
