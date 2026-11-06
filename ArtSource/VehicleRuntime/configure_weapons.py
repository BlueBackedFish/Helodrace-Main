"""Configure native VF main-barrel recoil and the independently crewed M8 M2HB."""
from pathlib import Path
import argparse,copy,xml.etree.ElementTree as E
parser=argparse.ArgumentParser();parser.add_argument('--mod-root',type=Path,required=True);mod=parser.parse_args().mod_root.resolve()
defs=mod/'1.6/Defs/Vehicles';NS='Helodrace.LobosArsenal.Vehicles.'
def setv(p,tag,value):
    n=p.find(tag)
    if n is None:n=E.SubElement(p,tag)
    n.text=str(value);return n
def write(root,path):
    E.indent(root,space='  ');E.ElementTree(root).write(path,encoding='utf-8',xml_declaration=True)
for filename,name,distance,step,return_speed in [('M3A3Weapons.xml','HDA_M3A3_M242',.09,.045,.40),('M8Weapons.xml','HDA_M8_M35',.32,.08,.18)]:
    root=E.parse(defs/filename).getroot();t=next(n for n in root if n.findtext('defName')==name)
    recoil=t.find('recoil')
    if recoil is None:recoil=E.SubElement(t,'recoil')
    for k,v in dict(distanceTotal=distance,distancePerTick=step,speedMultiplierPostRecoil=return_speed).items():setv(recoil,k,v)
    write(root,defs/filename)
m8=E.parse(defs/'M8.xml').getroot();tank=m8[0]
role=next(n for n in tank.find('properties/roles') if n.findtext('key')=='commander')
setv(role,'handlingTypes','Turret');setv(role,'slotsToOperate',1);ids=role.find('turretIds')
if ids is None:ids=E.SubElement(role,'turretIds')
ids.clear();E.SubElement(ids,'li').text='roof'
components=tank.find('components')
for key,label,health,depth in [('m2hb','M2HB roof machine gun',140,'External'),('ammo50','.50 caliber ammunition box',100,'External')]:
    if any(n.findtext('key')==key for n in components):continue
    part=E.SubElement(components,'li')
    for k,v in dict(key=key,label=label,health=health,depth=depth).items():setv(part,k,v)
    hit=E.SubElement(part,'hitbox');setv(hit,'from','(1,0)');setv(hit,'to','(1,0)')
turrets=tank.find("comps/li[@Class='Vehicles.CompProperties_VehicleTurrets']/turrets")
if not any(n.findtext('key')=='roof' for n in turrets):
    t=E.SubElement(turrets,'li',{'Class':NS+'M8Turret'})
    for k,v in dict(key='roof',def_='HDA_M8_M2HB',gizmoLabel='M2HB .50').items():setv(t,k.rstrip('_'),v)
    setv(E.SubElement(t,'component'),'key','m2hb')
setv(tank,'description','A LEVEL 3 light tank with an autoloaded M35 105 mm gun, M240 coaxial machine gun and a separately aimed commander-operated M2HB. It can be rigged for HELLVADS heavy airdrop.')
write(m8,defs/'M8.xml')
root=E.parse(defs/'M8Weapons.xml').getroot()
for n in list(root):
    if n.findtext('defName') in ['HDA_M8_M2HB','HDA_50BMG_Round','HDA_Make50BMG']:root.remove(n)
coax=next(n for n in root if n.findtext('defName')=='HDA_M8_Coax');mg=copy.deepcopy(coax);root.append(mg)
for k,v in dict(defName='HDA_M8_M2HB',label='M2HB .50 heavy machine gun',maxRange=40,warmUpTimer=.8,reloadTimer=5,magazineCapacity=100,projectile='HD_Bullet_12799M8_Proj').items():setv(mg,k,v)
recoil=E.SubElement(mg,'recoil')
for k,v in dict(distanceTotal=.055,distancePerTick=.0275,speedMultiplierPostRecoil=.5).items():setv(recoil,k,v)
setv(mg.find('graphicData'),'texPath','Vehicles/M8/m2hb_north')
mg.find('ammunition/thingDefs/li').text='HDA_50BMG_Round'
mode=mg.find('fireModes/li')
for k,v in dict(shotsPerBurst=10,ticksBetweenShots=8,ticksBetweenBursts=30).items():setv(mode,k,v)
source=E.parse(defs/'M3A3Weapons.xml').getroot()
ammo=copy.deepcopy(next(n for n in source if n.findtext('defName')=='HDA_25mmAP_Round'));root.append(ammo)
for k,v in dict(defName='HDA_50BMG_Round',label='.50 BMG ammunition',description='Ammunition for the M8 commander-operated M2HB.',stackLimit=500,projectileWhenLoaded='HD_Bullet_12799M8_Proj').items():setv(ammo,k,v)
setv(ammo.find('statBases'),'Mass',.115);setv(ammo.find('statBases'),'MarketValue',3)
setv(ammo.find('graphicData'),'texPath','Weapons/ColdWar/Ammo/20x102mm/HD_20X102MM_a')
recipe=copy.deepcopy(next(n for n in source if n.findtext('defName')=='HDA_Make25mmAP'));root.append(recipe)
for k,v in dict(defName='HDA_Make50BMG',label='make 100 .50 BMG rounds',description='Produce M2HB ammunition.',jobString='Making .50 BMG ammunition.',workAmount=1800).items():setv(recipe,k,v)
recipe.find('products').clear();E.SubElement(recipe.find('products'),'HDA_50BMG_Round').text='100'
for ingredient in recipe.find('ingredients'):
    ingredient.find('count').text={'Steel':'15','Plasteel':'2','Chemfuel':'4'}[ingredient.findtext('filter/thingDefs/li')]
write(root,defs/'M8Weapons.xml')
# Add only the new translated nodes; preserve all pre-existing translations.
folder=mod/'1.6/Languages/Korean (한국어)/DefInjected'
for kind,filename,values in [
 ('ThingDef','AirborneWeapons.xml',{'HDA_50BMG_Round.label':'.50 BMG 탄약','HDA_50BMG_Round.description':'M8 차장이 운용하는 M2HB 중기관총용 탄약입니다.'}),
 ('Vehicles.VehicleTurretDef','AirborneWeapons.xml',{'HDA_M8_M2HB.label':'M2HB .50 중기관총'}),
 ('RecipeDef','AirborneWeapons.xml',{'HDA_Make50BMG.label':'.50 BMG 탄약 100발 제작','HDA_Make50BMG.description':'M2HB 중기관총용 탄약을 제작합니다.','HDA_Make50BMG.jobString':'.50 BMG 탄약 제작 중.'}),
 ('Vehicles.VehicleDef','AirborneWeapons.xml',{
     f'HDA_M8AGS.components.{next(i for i,c in enumerate(components) if c.findtext("key")=="m2hb")}.label':'M2HB 상부 기관총',
     f'HDA_M8AGS.components.{next(i for i,c in enumerate(components) if c.findtext("key")=="ammo50")}.label':'.50구경 탄약통'})]:
    lang=E.Element('LanguageData')
    for k,v in values.items():E.SubElement(lang,k).text=v
    path=folder/kind/filename;path.parent.mkdir(parents=True,exist_ok=True);write(lang,path)
langpath=folder/'Vehicles.VehicleDef/Airborne.xml';lang=E.parse(langpath).getroot()
setv(lang,'HDA_M8AGS.description','LEVEL 3 장갑을 장착한 3인승 경전차입니다. 자동장전식 M35 105mm 주포와 M240 동축기관총, 차장이 독립 조준하는 M2HB 상부 중기관총을 갖춥니다. HELLVADS 강하 준비 후 C-17로 공수할 수 있습니다.')
write(lang,langpath)
print('NATIVE_RECOIL_AND_M2HB_PASS: M242/M35 barrel recoil; separate roof weapon/crew/ammo/parts')
