from extract import *

WHEAD=['이름','종류','피해량','관통력','사거리 (칸)','조준/준비 (초)','발사 수','발사 간격 (초)','재사용 (초)','근거리 명중률','단거리 명중률','중거리 명중률','장거리 명중률','무게 (kg)','가격','산탄 수/발','폭발 반경 (칸)','저지력','피해 기준','비교 소재','대체 모드','추가 성능']
AHEAD=['이름','종류','방어/착용 부위','날카로움','둔탁함','열','무게 (kg)','이동속도 변화 (칸/초)','추위 단열 (°C)','더위 단열 (°C)','가격','확정 방어 관통력','방탄판 내구도/장','내구도','인체공학','통풍','하중 분산','쾌적 최고온도 변화 (°C)','추가 방호','비교 소재','특수 효과']
WORDER=['권총','기관단총','소총/카빈','산탄총','지정사수/저격총','기관총','유탄발사기','대전차/중화기','수류탄','근접무기','근접 공구','특수무기','설치 폭약']
AORDER=['일반 의류','군복','전투장비','방탄복','방탄판','헬멧','방탄 모듈','헬멧 부착물','장비 모듈','특수 보호장비','착용형 무기']
WTYPE={}
for typ,ids in {
 '권총':['ColtSAA','ColtArmyModel1860','SWModelThree','M1911','Glock19Gen5'],
 '기관단총':['M1A1Harrington','M3A1'],
 '소총/카빈':['Henry','WinMNinetyFive','SprMOne','SprMThreeHigh','KragJorgensen','M1A1Carbine','M2Carbine','M1Garand','M1903Pedersen','M16A1','M16A2','M16A3','M16A4','M4A1','MK18MOD1'],
 '산탄총':['WinM1887','M1897'],
 '지정사수/저격총':['SprMThree','M1903A4','M1D'],
 '기관총':['M1918A2HAR','M1919A6LMG','M60E3'],
 '유탄발사기':['M79'],
 '대전차/중화기':['M9A1','M2FlameThrower'],
 '특수무기':['Nailgun','M8FlareGun','LANCE_LRA7']
}.items():
 for s in ids:WTYPE['HD_Gun_'+s+'_Weapon']=typ
WTYPE.update({'HD_Gun_M47_Dragon':'대전차/중화기','HD_Hardtack':'특수무기','HD_WoodenBaseballBat':'근접무기','HD_MetalBaseballBat':'근접무기','HD_M1902Saber':'근접무기','HD_mSixZeroSaber':'근접무기','HD_USMCFightingKnife':'근접무기','HD_PowerCutter':'근접 공구','HD_PowerforgeJackhammer':'근접 공구','HD_Apparel_ZaperX26_Device':'특수무기','HD_C4_Charge':'설치 폭약'})
GROUPS={'Torso':'몸통','Neck':'목','Shoulders':'어깨','Arms':'팔','Legs':'다리','Waist':'허리','UpperHead':'머리 상부','FullHead':'머리 전체','Eyes':'눈','Hands':'손','Feet':'발'}
BODY={'Eye':'눈','Nose':'코','Jaw':'턱','Head':'머리'}
DIR={'Front':'전방','Back':'후방','Left':'좌측','Right':'우측'}
MTAG='Helodrace.ModernWar.ModularArmorPartDef'
PTAG='Helodrace.ModernWar.ModularArmorPositionDef'
parts={d:n for (t,d),n in defs.items() if t==MTAG}
def cover(pos):
 if pos is None:return None
 groups=[GROUPS.get(x,lbl('BodyPartGroupDef',x)) for x in pos.xpath('bodyPartGroups/li/text()')]
 groups += [BODY.get(x,lbl('BodyPartDef',x)) for x in pos.xpath('bodyParts/li/text()')]
 ds=pos.xpath('protectedDirections/li/text()')
 result='·'.join(dict.fromkeys(groups))
 if ds:result+=' ('+'/'.join(DIR[x] for x in ds)+' '+f'{num(pos,"directionArcDegrees",90):g}°'+')'
 return result
def mod_cover(p):
 position=node(PTAG,txt(p,'fixedPosition'))
 if position is not None:return cover(position)
 covers=[]
 for panelid in p.xpath('allowedPalsPanels/li/text()'):
  panel=node('Helodrace.ModernWar.ModularArmorPalsPanelDef',panelid)
  pos=node(PTAG,txt(panel,'armorPosition'))
  if pos is not None:covers.append(cover(pos))
 return '/'.join(dict.fromkeys(covers)) or '장착 위치에 따름'
def protection(p):
 return '/'.join(f'{num(p,k,0)*100:g}%' for k in ['armorRatingSharp','armorRatingBlunt','armorRatingHeat'])
def ext(n,name):
 xs=n.xpath('modExtensions/li[contains(@Class,"'+name+'")]') if n is not None else []
 return xs[0] if xs else None
def f(x):return f'{x:.2f}'.rstrip('0').rstrip('.') if x!=int(x) else str(int(x))
def pct(x):return f'{x*100:g}%'
def meaningful(n,name,stuff):
 if n.find('statBases/'+name) is not None:return stat(n,name,stuff)
 sd=node('StatDef',name)
 if stuff:
  for p in sd.findall('parts/li'):
   if p.get('Class')=='StatPart_Stuff' and stat(n,txt(p,'multiplierStat'),stuff)>0:return stat(n,name,stuff)
 return None
weapon_rows=[];apparel_rows=[];audit={}
for dn,i in data['items'].items():
 n=node('ThingDef',dn);stuff=material(n);stoff=lbl('ThingDef',stuff) if stuff else None
 attacks=melee(n,stuff);mass=stat(n,'Mass',stuff);price=stat(n,'MarketValue',stuff)
 if i['weapon']:
  row={h:None for h in WHEAD};row.update({'이름':i['label'],'종류':'수류탄' if i['inventorygrenade'] else WTYPE[dn],'무게 (kg)':mass,'가격':price,'비교 소재':stoff})
  verbs=n.findall('verbs/li');verb=verbs[0] if verbs else None;notes=[];alt=[]
  pid=txt(verb,'defaultProjectile');pr=projectile(pid,n) if pid else {};pn=pr.get('n')
  if i['inventorygrenade']:
   pid=txt(n,'projectileWhenLoaded');pr=projectile(pid,n);pn=pr.get('n');verb=None
   row.update({'사거리 (칸)':13.9,'조준/준비 (초)':3,'발사 수':1})
   alt.append('근거리 투척: 5.9칸 / 준비 1.5초')
  elif verb is not None:
   burst=int(num(verb,'burstShotCount',1));row.update({'사거리 (칸)':num(verb,'range',0),'조준/준비 (초)':num(verb,'warmupTime',0)*stat(n,'RangedWeapon_WarmupMultiplier',stuff),'발사 수':burst,'발사 간격 (초)':num(verb,'ticksBetweenBurstShots',15)/60 if burst>1 else None,'재사용 (초)':stat(n,'RangedWeapon_Cooldown',stuff)})
   for key,s in zip(WHEAD[9:13],['AccuracyTouch','AccuracyShort','AccuracyMedium','AccuracyLong']):row[key]=stat(n,s,stuff)
   count=num(verb,'pelletCount',1)
   if count>1:row['산탄 수/발']=int(count)
   if num(verb,'minRange',0)>0:notes.append('최소 사거리 '+f(num(verb,'minRange'))+'칸')
  if pr:
   row.update({'피해량':pr['damage'],'관통력':pr['ap'],'폭발 반경 (칸)':pr['radius'],'저지력':pr['stopping'],'피해 기준':'산탄 1개' if row['산탄 수/발'] else '폭발' if pr['radius'] else '탄환 1발'})
   if pr['damageType']=='Smoke':row.update({'관통력':None,'폭발 반경 (칸)':None,'저지력':None,'피해 기준':'직접 피해 없음'})
   if i['inventorygrenade']:row['저지력']=None
  if not pr and attacks:
   a=attacks[0];row.update({'피해량':a['damage'],'관통력':a['ap'],'재사용 (초)':a['cooldown'],'피해 기준':'근접 최대 1회'})
   labels_dm={'cut':'베기','stab':'찌르기','blunt':'타격'}
   details=[]
   for a in attacks:
    s=labels_dm.get(a['type'],a['type'])+' '+f(a['damage'])+' / '+pct(a['ap'])+' / '+f(a['cooldown'])+'초'
    if s not in details:details.append(s)
   notes.append('공격: '+ '; '.join(details))
   if i['apparel']:notes.append('허리에 착용하여 근접 공격')
  elif pr and attacks:notes.append('근접 최대 '+f(attacks[0]['damage'])+' / '+pct(attacks[0]['ap']))
  sharp=comp(n,'SharpshooterWeapon')
  if sharp is not None and len(verbs)>int(num(sharp,'altVerbIndex',1)):
   v=verbs[int(num(sharp,'altVerbIndex',1))];count=int(num(v,'burstShotCount',1));bits=['사거리 '+f(num(v,'range',0))+'칸','조준 '+f(num(v,'warmupTime',0)*stat(n,'RangedWeapon_WarmupMultiplier',stuff))+'초',str(count)+'발']
   if count>1:bits.append('간격 '+f(num(v,'ticksBetweenBurstShots',15)/60)+'초')
   bits.append('재사용 '+f(stat(n,'RangedWeapon_Cooldown',stuff)*num(sharp,'altCooldownMultiplier',1))+'초')
   am=num(sharp,'altAccuracyMultiplier',1)
   if am!=1:bits.append('명중 배율 '+pct(am))
   mo=num(sharp,'moveSpeedOffset',0)
   if mo:bits.append('이동 '+f(mo)+'칸/초')
   alt.append('특등사수: '+' / '.join(bits))
  if pn is not None:
   frag=ext(pn,'FragmentationGrenadeExtension')
   if frag is not None:
    fp=projectile(txt(frag,'fragmentProjectile'))
    notes.append('파편 '+f(num(frag,'fragmentCount',24))+'개: 피해 '+f(fp['damage'])+' / '+pct(fp['ap'])+'; 반경 '+f(num(frag,'radius',8))+'칸, 원거리 최대 '+f(num(frag,'longRangeRadius',12))+'칸')
   flash=ext(pn,'FlashbangProjectileExtension')
   if flash is not None:
    radius=num(flash,'effectRadius',12);indoor=radius*num(flash,'indoorRadiusFactor',1.5)
    notes.append('억제 반경 '+f(radius)+'칸 (실내 '+f(indoor)+'칸); 기본 기절 '+f(num(flash,'stunTicksMin',120)/60)+'–'+f(num(flash,'stunTicksMax',180)/60)+'초'+(' (누적 시 최대 '+f(num(flash,'stunTicksAbsoluteMax',600)/60)+'초)' if num(flash,'stunSuppressionMultiplierMax',1)>1 else ''))
    if num(flash,'damageAtFullIntensity',0)>0:notes.append('고강도 추가 둔탁 피해 '+f(num(flash,'damageAtThreshold',0))+'–'+f(num(flash,'damageAtFullIntensity',0))+' / '+pct(num(flash,'damageArmorPenetration',0)))
   grenade=ext(pn,'ModernGrenadeProjectileExtension')
   if grenade is not None:
    fuse=num(grenade,'fuseTicks',270);var=num(grenade,'fuseVarianceTicks',30)
    notes.append('신관 '+f((fuse-var)/60)+'–'+f((fuse+var)/60)+'초')
    if txt(grenade,'gasDef'):
     gd=txt(grenade,'gasDef');gasname={'HD_CSGasGrid':'CS 최루 가스','HD_HCSmokeGrid':'HC 연막'}.get(gd,lbl('Helodrace.HelodGasDef',gd))
     notes.append(gasname+' 방출 반경 '+f(num(grenade,'gasEmissionRadius',1.7))+'칸 / '+f(num(grenade,'gasEmissionDurationTicksMin',1200)/60)+'–'+f(num(grenade,'gasEmissionDurationTicksMax',1800)/60)+'초')
   therm=ext(pn,'ThermiteGrenadeExtension')
   if therm is not None:
    row.update({'피해량':num(therm,'pawnDamage',12),'관통력':num(therm,'armorPenetration',.75),'피해 기준':'생물 화상 1회'})
    notes.append('생물: '+f(num(therm,'pawnDamage',12))+' 피해/'+f(num(therm,'pawnDamageIntervalTicks',30)/60)+'초; 사물: '+f(num(therm,'damage',18))+' 피해/'+f(num(therm,'damageIntervalTicks',12)/60)+'초; 십자 반경 '+f(num(therm,'crossRadius',1))+'칸 / '+f(num(therm,'reactionDurationTicks',420)/60)+'초')
   gel=ext(pn,'IncendiaryGelProjectileExtension')
   if gel is not None:notes.append('화재 반경 '+f(num(gel,'fireRadius',2.4))+'칸; 명중 시 15초 연소 젤 (1.5초마다 재발화)')
   if txt(pn,'projectile/postExplosionGasType')=='BlindSmoke':notes.append('시야 차단 연막 반경 '+f(num(pn,'projectile/explosionRadius',0))+'칸')
   if ext(pn,'HelodGasOnExplosionExtension') is not None:notes.append('백린 연막·착화')
  if dn=='HD_Gun_LANCE_LRA7_Weapon':
   row['피해 기준']='거리 0칸 기준';notes.append('1칸마다 피해 +0.36 / 관통 +0.8%p; 30칸부터 역류 확률 증가, 70칸에서 확정; 최대 사거리 54.9칸: 피해 30.76 / 관통 67.92%')
  if dn=='HD_Apparel_ZaperX26_Device':
   pr=projectile('HD_ZaperX26_Probe',n);row.update({'피해량':pr['damage'],'관통력':pr['ap'],'사거리 (칸)':7.9,'조준/준비 (초)':.35,'발사 수':2,'재사용 (초)':2.4,'피해 기준':'탐침 1개'})
   alt.append('접촉 전극: 피해 1 / 준비 0.3초 / 재사용 1.5초')
   notes.append('탐침 2개가 0.2초 내 상처를 내면 5초 전기충격; 연결 한계 11칸; 허리 착용')
  if dn=='HD_Gun_M79_Weapon':
   mc=comp(n,'M79Launcher');ammo=mc.xpath('allowedAmmoDefs/li/text()');notes.append('기본 M381 HE; 장전 '+f(num(mc,'reloadTicks',90)/60)+'초')
   texts=[]
   for aid in ammo[1:]:
    an=node('ThingDef',aid);ap=projectile(txt(an,'projectileWhenLoaded'),n)
    short=lbl('ThingDef',aid).replace('40×46mm ','')
    s=short+': '+f(ap['damage'])+' / '+pct(ap['ap'])
    if ap['radius']:s+=' / 폭발 '+f(ap['radius'])+'칸'
    if aid=='HD_40mmM576MP_Round':s+=' ×20 산탄 (명중 85/75/45/20%)'
    if aid=='HD_40mmM651CS_Round':s+='; CS 가스 방출 20–30초'
    if aid=='HD_40mmSponge_Round':s+='; 억제 +0.3'
    texts.append(s)
   notes.append('탄종 변경: '+'; '.join(texts))
   row['피해 기준']='M381 HE 폭발'
  if dn=='HD_Gun_M9A1_Weapon':
   mc=comp(n,'RecoillessWeapon');ammo=node('ThingDef','HD_Rocket_M10WP');wp=projectile(txt(ammo,'projectileWhenLoaded'),n)
   notes.append('M6A3 HEAT 기본; M10 WP: 피해 '+f(wp['damage'])+' / '+pct(wp['ap'])+' / 폭발 '+f(wp['radius'])+'칸; 장전 혼자 '+f(num(mc,'selfReloadTicks',1800)/60)+'초 / 조수 '+f(num(mc,'crewReloadTicks',90)/60)+'초')
  if dn=='HD_Gun_M47_Dragon':notes.append('유선 유도; 시야·유도 유지 필요; 비행 중 추가 발사 불가; 헬로드는 비행 중 표적 변경 가능')
  if dn=='HD_Gun_M8FlareGun_Weapon':
   flare=node('ThingDef','HD_M8FlareTarget');notes.append('신호탄 조명 반경 '+f(num(comp(flare,'Glower'),'glowRadius',0))+'칸 / 30초')
  if dn=='HD_Hardtack':notes.append('식량을 투척·근접 무기로 사용')
  if dn=='HD_PowerCutter':notes.append('벽·문 돌파 절단 기능')
  if dn=='HD_PowerforgeJackhammer':notes.append('근접 공구')
  if dn=='HD_C4_Charge':
   bc=comp(node('ThingDef','HD_InstalledBreachCharge'),'InstalledBreachCharge')
   row.update({'관통력':num(bc,'explosionArmorPenetration',.35),'폭발 반경 (칸)':num(bc,'explosionRadiusBase',1.5)+num(bc,'explosionRadiusPerC4',.18),'피해 기준':'1개 설치 기준'})
   notes.append('벽·문 돌파: 1개당 대상 500 HP까지; 폭발 피해는 대상 잔여 HP에 따름; 양에 따라 반경 증가; 기본 설치 1.5초·점화 1초; M60/M81 점화기 필요')
  row['대체 모드']='\n'.join(alt) or None;row['추가 성능']='\n'.join(notes) or None
  weapon_rows.append((dn,row));audit[dn]={'weapon':row,'projectile':pid,'material':stuff}
 if i['apparel'] or i['armorpart']:
  row={h:None for h in AHEAD};notes=[];additional=[]
  row.update({'이름':i['label'],'무게 (kg)':mass,'가격':price,'비교 소재':stoff,'내구도':stat(n,'MaxHitPoints',stuff)})
  groups=n.xpath('apparel/bodyPartGroups/li/text()');row['방어/착용 부위']='·'.join(dict.fromkeys(GROUPS.get(x,lbl('BodyPartGroupDef',x)) for x in groups)) or None
  for h,s in zip(['날카로움','둔탁함','열','추위 단열 (°C)','더위 단열 (°C)'],['ArmorRating_Sharp','ArmorRating_Blunt','ArmorRating_Heat','Insulation_Cold','Insulation_Heat']):row[h]=meaningful(n,s,stuff) if i['apparel'] else None
  row['이동속도 변화 (칸/초)']=num(n,'equippedStatOffsets/MoveSpeed')
  row['쾌적 최고온도 변화 (°C)']=num(n,'equippedStatOffsets/ComfyTemperatureMax')
  if i['armorpart']:
   mods=[p for p in parts.values() if (txt(p,'plateThingDef') or txt(p,'partThingDef'))==dn and txt(p,'playerSelectable','true')!='false']
   assert mods,('unlinked armor item',dn)
   for h,k in zip(['날카로움','둔탁함','열'],['armorRatingSharp','armorRatingBlunt','armorRatingHeat']):
    vs=[num(p,k) for p in mods if num(p,k) is not None]
    if vs:assert len(set(vs))==1,(dn,k,vs);row[h]=vs[0]
   row['방어/착용 부위']='/'.join(dict.fromkeys(mod_cover(p) for p in mods))
   if dn.startswith('HD_ArmorPlate_'):
    row['종류']='방탄판';row['방탄판 내구도/장']=row['내구도'];row['내구도']=None
    thresholds={num(p,'guaranteedBlockPenetration',0) for p in mods};assert len(thresholds)==1
    row['확정 방어 관통력']=thresholds.pop();notes.append('2장 세트; 해당 판 내구도 40% 이상일 때 원거리 날카로움 피해 확정 방어; IBTV 장착 필요')
   elif any(num(p,'armorRatingSharp',0)>0 for p in mods):row['종류']='방탄 모듈'
   elif dn in ['HD_ModularPart_GPNVG18']:row['종류']='헬멧 부착물'
   else:row['종류']='장비 모듈'
   for h,k in zip(['인체공학','통풍','하중 분산'],['ergonomics','ventilation','loadDistribution']):
    vals={num(p,k,0) for p in mods}
    if len(vals)==1:row[h]=vals.pop()
   if not dn.startswith('HD_ArmorPlate_'):notes.append('단독 착용 불가; 호환 장비에 장착')
   if dn=='HD_ModularPart_GPNVG18':notes.append('장착 시 조준 지연 −10%p, 사격 명중 배율 단/중/장거리 +5/+10/+15%; 바이저와 동시 장착 불가')
   if dn=='HD_ModularPart_MultiHitVisor':notes.append('FAST MT 장착; 눈·코·턱 전방 120° 보호; 야시경과 동시 장착 불가')
  else:
   if dn in ['HD_USMCFightingKnife','HD_Apparel_ZaperX26_Device']:row['종류']='착용형 무기';notes.append('무기 성능은 Weapons 시트 참조')
   elif 'Shield' in dn or 'GasMaskPouch' in dn or 'CBRNPouch' in dn:row['종류']='특수 보호장비'
   elif 'Helmet' in dn or 'FASTMT' in dn:row['종류']='헬멧'
   elif dn in ['HD_Apparel_IBTVAssault','HD_Apparel_M1952AFlakJacket','HD_WildWestMetalVest']:row['종류']='방탄복'
   elif any(x in dn for x in ['StormFrontInner','TrenchArmyInner','StormFrontOuter','TrenchArmyOuter','CarvalyUniform','UCPBlouse','UCPPants','ACSUpper']):row['종류']='군복'
   elif any(x in dn for x in ['Casual','FormalShirt','FrockCoat','TopHat','CowboyHat','CarvalyHat','LeatherVest']):row['종류']='일반 의류'
   else:row['종류']='전투장비'
   mc=comp(n,'CompProperties_ModularArmor')
   if mc is not None:
    defaults=[parts[x] for x in mc.xpath('defaultParts/li/part/text()')]
    metrics={k:num(mc,'base'+k,0)+sum(num(p,k[0].lower()+k[1:],0) for p in defaults) for k in ['Ergonomics','Ventilation','LoadDistribution']}
    for h,k in zip(['인체공학','통풍','하중 분산'],metrics):row[h]=metrics[k]
    for conv in mc.findall('statConversions/li'):
     value=(metrics[txt(conv,'source')]-num(conv,'referenceValue',0))*num(conv,'factor',1);target=txt(conv,'targetStat')
     if target=='MoveSpeed' and txt(conv,'mode','Offset')=='Offset':row['이동속도 변화 (칸/초)']=(row['이동속도 변화 (칸/초)'] or 0)+value
     elif target=='ComfyTemperatureMax' and txt(conv,'mode','Offset')=='Offset':row['쾌적 최고온도 변화 (°C)']=(row['쾌적 최고온도 변화 (°C)'] or 0)+value
     elif target=='CarryingCapacity':notes.append('운반 한도 '+f(value)+'kg')
    for p in defaults:
     if any(num(p,k,0)>0 for k in ['armorRatingSharp','armorRatingBlunt','armorRatingHeat']):additional.append(mod_cover(p)+': '+protection(p))
    if defaults:notes.append('기본 장착: '+', '.join(lbl('ThingDef',txt(p,'partThingDef')) if txt(p,'partThingDef') else lbl(MTAG,txt(p,'defName')) for p in defaults))
    if dn=='HD_Apparel_IBTVAssault':notes.append('방탄판 기본 미장착; 추가 방호는 별도 층 (날카로움/둔탁함/열)')
   shield=comp(n,'DirectionalBallisticShield')
   if shield is not None:
    row['날카로움']=num(shield,'sharpArmorRating',2);row['방어/착용 부위']='전방 '+f(num(shield,'frontalArcDegrees',90))+'° (방패)'
    notes.append('사격 명중 배율 '+pct(num(shield,'rangedAccuracyMultiplier',.75))+'; 옆걸음 '+pct(num(shield,'sidewaysMovementSpeedMultiplier',.65))+' / 뒷걸음 '+pct(num(shield,'backwardMovementSpeedMultiplier',.45))+'; 내구도 50% 미만에서 방호 감소')
    if shield.find('allowedWeaponDefs') is not None:notes.append('허용 권총·신호탄 총기와만 병용')
   gas=comp(n,'GasMaskPouch')
   if gas is not None:notes.append('보호구 전개 '+f(num(gas,'wearTicks',120)/60)+'초; 전개 시 독성 환경 저항 '+pct(num(gas,'toxicEnvironmentResistance',1))+('·스위트가스 방호' if txt(gas,'protectsSweetGas')=='true' else ''))
   mb=comp(n,'Medibag')
   if mb is not None:notes.append('응급처치 '+f(num(mb,'treatmentTicks',180)/60)+'초; 출혈 감소 '+pct(num(mb,'bloodLossReduction',.6))+'; 최대 '+f(num(mb,'hemostasisPartCount',4))+'부위 지혈')
   if dn=='HD_Apparel_SCR300_Radio':notes.append('SCR-300 무전 기능')
   if dn.startswith('HD_Apparel_M1Mortar'):notes.append('M1 81mm 박격포 운반·조립 부품')
   if dn=='HD_MilitaryTablet':notes.append('Switchblade 발사기 조작 권한')
   if dn in ['HD_Apparel_ANPEQ1C','HD_Apparel_IZLIDUltra','HD_Apparel_LA16uPEQ']:notes.append('레이저 표적 지시 장비')
   if dn=='HD_Bugle':
    bugle=comp(n,'Bugle');notes.append('나팔 강화 효과 반경 '+f(num(bugle,'radius',18))+'칸; 재사용 '+f(num(bugle,'cooldownTicks',30000)/60)+'초')
  row['추가 방호']='\n'.join(additional) or None;row['특수 효과']='\n'.join(notes) or None
  apparel_rows.append((dn,row));audit.setdefault(dn,{})['apparel']=row

weapon_rows.sort(key=lambda x:(WORDER.index(x[1]['종류']),x[1]['이름']))
apparel_rows.sort(key=lambda x:(AORDER.index(x[1]['종류']),x[1]['이름']))
for rows,heads in [(weapon_rows,WHEAD),(apparel_rows,AHEAD)]:
 assert len(set(d for d,r in rows))==len(rows)
 for dn,r in rows:
  for k,v in r.items():
   if isinstance(v,float):r[k]=round(v,8)
   if isinstance(v,str):assert not re.search(r'HD_|ThingDef|CompProperties|ParentName|\.xml',v),(dn,k,v)
 assert all(d not in data['excluded'] for d,r in rows)
assert set(d for d,r in weapon_rows)==set(d for d,i in data['items'].items() if i['weapon'])
assert set(d for d,r in apparel_rows)==set(d for d,i in data['items'].items() if i['apparel'] or i['armorpart'])
assert not warnings,warnings
payload={
 'Weapons':{'headers':WHEAD,'rows':[[r[h] for h in WHEAD] for dn,r in weapon_rows],'ids':[dn for dn,r in weapon_rows], 'notes':[
  '비교 기준: 기본 모드·보통 품질·최대 내구도. 명중률은 무기 자체의 거리별 수치이며 사수·엄폐·날씨 등의 영향은 제외합니다.',
  '피해는 탄환/산탄 1개 또는 1회 기준입니다. 산탄 수와 발사 수는 별개입니다. 근접무기는 최대 피해 공격의 수치이며 다른 공격은 추가 성능에 표시합니다.',
  '비교 소재가 있는 아이템은 표시된 소재로 계산한 값입니다. 소재·품질·장착 구성·사용 모드에 따라 실제 수치는 달라질 수 있습니다.',
  '수류탄은 인벤토리 투척의 기본값입니다. 추가 성능의 신관·억제 효과는 환경·누적 상태에 따라 달라지며, 장전 시간은 재사용 시간과 별도입니다.',
  '빈 셀은 해당 없음 또는 확정 불가입니다. 표시된 0은 실제 피해나 스탯이 0인 경우입니다. 가격은 기본 시장가치이며 실제 거래 가격은 달라집니다.',
  '자료: 제공된 Helodrace 전체 파일(2026-09-26) 및 설치된 RimWorld 1.6 기본 정의·계산 규칙. 포탑 내부 총기·투사체·추상 정의·명시적 테스트 장비 제외.'
 ]},
 'Apparel':{'headers':AHEAD,'rows':[[r[h] for h in AHEAD] for dn,r in apparel_rows],'ids':[dn for dn,r in apparel_rows], 'notes':[
  '비교 기준: 보통 품질·최대 내구도·기본 장착 구성. 비교 소재가 있는 아이템은 표시된 소재로 계산했습니다. 빈 셀은 해당 없음 또는 확정 불가입니다.',
  '방어/착용 부위는 착용 위치이며 방어 수치가 비어 있는 보조장비가 그 부위를 보호한다는 뜻은 아닙니다. 방탄판·모듈은 호환 장비에 장착해야 작동합니다.',
  '추가 방호는 부위별 별도 방어층이며 본체 방어력에 단순 합산되지 않습니다. 추가 방호의 순서는 날카로움/둔탁함/열입니다. 방패 날카로움은 전방 차단 수치입니다.',
  '방탄판은 2장 세트이며 내구도는 한 장당 수치입니다. 확정 방어는 해당 판 내구도 40% 이상이고 원거리 날카로움 공격의 관통력이 표시값 이하일 때 적용됩니다.',
  '인체공학·통풍·하중 분산은 기본 구성 합계이며 모듈 행은 장착 시 증감입니다. 판의 설치 위치에 따라 증감이 달라지는 경우 빈 셀입니다.',
  '이동속도 변화는 착용 효과의 합계입니다. 쾌적 최고온도 변화는 통풍 환산 효과를 포함하며 더위 단열과 별개입니다. 무게·가격은 아이템 자체 기준입니다.',
  '자료: 제공된 Helodrace 전체 파일(2026-09-26) 및 설치된 RimWorld 1.6 기본 정의·계산 규칙. 추상 정의·명시적 테스트 장비 제외.'
 ]}}
(OUT/'rows.json').write_text(json.dumps(payload,ensure_ascii=False,indent=2),encoding='utf-8')
(OUT/'audit.json').write_text(json.dumps({'rows':audit,'warnings':warnings,'excluded':data['excluded'],'patchlog':data['patchlog']},ensure_ascii=False,indent=2),encoding='utf-8')
print('Rows',len(weapon_rows),len(apparel_rows),'unique items',len(set([d for d,r in weapon_rows]+[d for d,r in apparel_rows])))
for dn,r in weapon_rows:print(dn,r['피해량'],r['관통력'],r['사거리 (칸)'],r['재사용 (초)'])
