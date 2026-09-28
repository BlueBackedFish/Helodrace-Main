from pathlib import Path
from lxml import etree as E
import json, math, re
OUT=Path(__file__).parent
data=json.loads((OUT/'resolved.json').read_text(encoding='utf-8'))
defs={tuple(k.split('|',1)):E.fromstring(v) for k,v in data['defs'].items()}
labels={tuple(k.split('|',1)):v for k,v in data['labels'].items()}
def node(t,d):return defs.get((t,d))
def txt(n,p,default=None):return n.findtext(p,default) if n is not None else default
def num(n,p,default=None):
 s=txt(n,p)
 return float(s) if s is not None and s.strip() else default
def lbl(t,d):return labels.get((t,d),d)
def comp(n,name):
 cs=n.xpath('comps/li[contains(@Class,"'+name+'") ]')
 return cs[0] if cs else None
def material(n):
 cats=n.xpath('stuffCategories/li/text()')
 for cat,dn in [('Woody','WoodLog'),('Metallic','Steel'),('Fabric','Cloth'),('Leathery','Leather_Plain')]:
  if cat in cats:return dn
 return None
def finalize(value,sd):
 if abs(value)>num(sd,'roundToFiveOver',math.inf):value=round(value/5)*5
 if txt(sd,'roundValue')=='true':value=round(value)
 return max(num(sd,'minValue',-9999999),min(num(sd,'maxValue',9999999),value))
warnings=[]
def market(n,stuff=None,stack=()):
 dn=txt(n,'defName')
 if dn in stack:raise ValueError(('price cycle',dn))
 direct=num(n,'statBases/MarketValue')
 sd=node('StatDef','MarketValue')
 if direct is not None:
  sn=node('ThingDef',stuff) if stuff else None
  val=direct*num(sn,'stuffProps/statFactors/MarketValue',1)+num(sn,'stuffProps/statOffsets/MarketValue',0)
  return finalize(val,sd)
 total=0;work=max(stat(n,'WorkToMake',stuff),stat(n,'WorkToBuild',stuff));count=1
 costs=n.find('costList');stuffcount=num(n,'costStuffCount',0)
 if (costs is None or len(costs)==0) and not stuffcount:
  for (t,d),r in defs.items():
   if t!='RecipeDef':continue
   prods=r.find('products')
   if prods is None or len(prods)!=1 or prods[0].tag!=dn:continue
   ingredients=r.findall('ingredients/li')
   fixed=[x.xpath('filter/thingDefs/li/text()') for x in ingredients]
   if any(len(x)!=1 for x in fixed):break
   work=num(r,'workAmount',0);count=float(prods[0].text)
   for ingredient,fd in zip(ingredients,fixed):
    amount=num(ingredient,'count',0);thing=node('ThingDef',fd[0])
    if thing is None:warnings.append('missing ingredient '+fd[0]);return None
    total+=math.ceil(amount)*market(thing,None,stack+(dn,))
   break
 elif costs is not None:
  for c in costs:
   thing=node('ThingDef',c.tag)
   if thing is None:warnings.append('missing ingredient '+c.tag);return None
   price=market(thing,None,stack+(dn,))
   if price is None:return None
   total+=float(c.text)*price
 if stuffcount:
  sn=node('ThingDef',stuff) if stuff else None
  if sn is None:warnings.append('unspecified stuff price '+dn);return None
  total+=stuffcount/num(sn,'volumePerUnit',1)*market(sn,None,stack+(dn,))
 if work>2:total+=work*.0036
 return finalize(total/count,sd)
def stat(n,name,stuff=None):
 sd=node('StatDef',name)
 if sd is None:warnings.append('missing stat '+name);return None
 if name=='MarketValue':return market(n,stuff)
 val=num(n,'statBases/'+name,num(sd,'defaultBaseValue',1))
 sn=node('ThingDef',stuff) if stuff else None
 if sn is not None:
  if val>0 or txt(sd,'applyFactorsIfNegative','true')=='true':val*=num(sn,'stuffProps/statFactors/'+name,1)
  val+=num(sn,'stuffProps/statOffsets/'+name,0)
 for part in sd.findall('parts/li'):
  cls=part.get('Class','')
  if cls=='StatPart_Stuff':
   power=txt(part,'stuffPowerStat');mult=txt(part,'multiplierStat')
   if sn is not None:val+=stat(n,mult,stuff)*stat(sn,power)
  elif cls=='StatPart_Quality':val*=num(part,'factorNormal',1)
 # Other parts concern pawn stats, traits or damaged/nonstandard items;
 # comparison assumes full durability and normal quality.
 return finalize(val,sd)
def projectile(dn,weapon=None):
 n=node('ThingDef',dn)
 if n is None:warnings.append('missing projectile '+str(dn));return {}
 p=n.find('projectile');damage=node('DamageDef',txt(p,'damageDef'))
 base=num(p,'damageAmountBase',-1)
 if base<0:base=num(damage,'defaultDamage')
 ap=num(p,'armorPenetrationBase',-1)
 if base is None:warnings.append('missing damage '+dn)
 if num(p,'damageAmountBase',-1)<0 and ap<0:ap=num(damage,'defaultArmorPenetration',-1)
 if not txt(damage,'armorCategory'):ap=0
 elif ap<0:ap=base*.015
 dmgmult=stat(weapon,'RangedWeapon_DamageMultiplier') if weapon is not None else 1
 apmult=stat(weapon,'RangedWeapon_ArmorPenetrationMultiplier') if weapon is not None else 1
 return {'damage':round(base*dmgmult),'ap':ap*apmult,'radius':num(p,'explosionRadius'),'stopping':num(p,'stoppingPower',.5),'damageType':txt(p,'damageDef'),'n':n}
def melee(n,stuff=None):
 attacks=[]
 for tool in n.findall('tools/li'):
  power=num(tool,'power',0);cool=num(tool,'cooldownTime',0)*stat(n,'MeleeWeapon_CooldownMultiplier',stuff)
  for capacity in tool.xpath('capacities/li/text()'):
   cap=node('ToolCapacityDef',capacity)
   if cap is None:continue
   # tool capacities link to ManeuverDefs rather than DamageDefs directly.
   for (t,d),m in defs.items():
    if t!='ManeuverDef' or capacity not in m.xpath('requiredCapacity/text()'):continue
    verb=m.find('verb');dt=txt(verb,'meleeDamageDef');dd=node('DamageDef',dt)
    armor=node('DamageArmorCategoryDef',txt(dd,'armorCategory'))
    multstat=txt(armor,'multStat');sn=node('ThingDef',stuff) if stuff else None
    value=power*stat(n,'MeleeWeapon_DamageMultiplier',stuff)
    if sn is not None and multstat:value*=stat(sn,multstat)
    ap=num(tool,'armorPenetration',-1)
    if ap<0:ap=value*.015
    else:ap*=stat(n,'MeleeWeapon_DamageMultiplier',stuff)
    attacks.append({'label':txt(tool,'label'),'damage':value,'cooldown':cool,'ap':ap,'type':lbl('DamageDef',dt)})
 return sorted(attacks,key=lambda x:(-x['damage'],-x['ap'],x['cooldown']))
if __name__=='__main__':
 for dn,i in data['items'].items():
  n=node('ThingDef',dn);stuff=material(n)
  vals={s:stat(n,s,stuff) for s in ['Mass','MarketValue','ArmorRating_Sharp','ArmorRating_Blunt','ArmorRating_Heat','Insulation_Cold','Insulation_Heat']}
  print(dn,stuff,vals,melee(n,stuff)[:2] if i['weapon'] else '')
 print('WARNINGS',warnings)
