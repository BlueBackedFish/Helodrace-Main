from extract import *
def simple(n):
 if n is None:return None
 return {c.tag: simple(c) if len(c) else (c.text or '').strip() for c in n}
for dn,i in data['items'].items():
 n=node('ThingDef',dn)
 if i['weapon']:
  print('W',dn,'verbs', [simple(x) for x in n.findall('verbs/li')], 'proj', txt(n,'projectileWhenLoaded'))
  for pid in n.xpath('verbs/li/defaultProjectile/text()')[:1]+n.xpath('projectileWhenLoaded/text()'):
   p=projectile(pid,n);print('P',pid,{k:v for k,v in p.items() if k!='n'})
   for ex in p['n'].findall('modExtensions/li'):print('E',ex.get('Class'),simple(ex))
 for c in n.findall('comps/li'):
  if c.get('Class','').startswith('Helodrace') and any(x in c.get('Class','') for x in ['ModularArmor','DirectionalBallisticShield','GasMask','Medibag','M79','Recoilless']):
   print('C',dn,E.tostring(c,encoding='unicode'))
print('ARMOR MODULES')
for (t,d),n in defs.items():
 if t.endswith('ModularArmorPartDef') and (txt(n,'plateThingDef') or txt(n,'partThingDef')):
  print(d,{k:txt(n,k) for k in ['plateThingDef','partThingDef','fixedPosition','armorRatingSharp','armorRatingBlunt','armorRatingHeat','guaranteedBlockPenetration','ergonomics','ventilation','loadDistribution']}, E.tostring(n.find('statModifiers'),encoding='unicode') if n.find('statModifiers') is not None else '')
