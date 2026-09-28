import json
from pathlib import Path
from lxml import etree as E
p=Path(__file__).parent
d=json.loads((p/'resolved.json').read_text(encoding='utf-8'))
defs={k:E.fromstring(v) for k,v in d['defs'].items()}
for name in ['Mass','MarketValue','WorkToMake','WorkToBuild','ArmorRating_Sharp','ArmorRating_Blunt','ArmorRating_Heat','Insulation_Cold','Insulation_Heat','AccuracyTouch','AccuracyShort','AccuracyMedium','AccuracyLong','MeleeWeapon_DamageMultiplier','MeleeWeapon_CooldownMultiplier','RangedWeapon_DamageMultiplier','RangedWeapon_ArmorPenetrationMultiplier','RangedWeapon_WarmupMultiplier','RangedWeapon_Cooldown']:
 print('STAT',name,E.tostring(defs['StatDef|'+name],encoding='unicode'))
for dn in ['Steel','WoodLog','Cloth','Leather_Plain']:
 print('STUFF',dn,E.tostring(defs['ThingDef|'+dn],encoding='unicode'))
for dn,i in d['items'].items():
 n=defs['ThingDef|'+dn]
 print('\nITEM',dn,i['label'])
 for t in ['statBases','equippedStatOffsets','apparel','verbs','tools']:
  sub=n.find(t)
  if sub is not None: print(t,E.tostring(sub,encoding='unicode'))
 if i['weapon']:
  for proj in n.xpath('verbs/li/defaultProjectile/text()')[:1]+n.xpath('projectileWhenLoaded/text()'):
   pn=defs['ThingDef|'+proj]; print('PROJECTILE',E.tostring(pn.find('projectile'),encoding='unicode'),'CLASS',pn.findtext('thingClass'));ext=pn.find('modExtensions')
   if ext is not None:print('EXT',E.tostring(ext,encoding='unicode'))
(p/'summary_ready.txt').write_text('complete',encoding='utf-8')
