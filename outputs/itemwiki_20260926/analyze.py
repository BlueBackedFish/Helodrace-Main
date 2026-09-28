from pathlib import Path
from copy import deepcopy
from lxml import etree as E
import json, re, tarfile

ROOT=Path(__file__).resolve().parents[2]
OUT=Path(__file__).parent
GAME=Path('C:/Program Files (x86)/Steam/steamapps/common/RimWorld')
parser=E.XMLParser(remove_comments=True)
root=E.Element('Defs'); origins={}; files=[]
for base in [GAME/'Data/Core/Defs',GAME/'Data/Biotech/Defs',ROOT/'Defs']:
 for f in sorted(base.rglob('*.xml')):
  try: doc=E.parse(str(f),parser)
  except Exception as ex: print('XML ERROR',f,ex);continue
  if doc.getroot().tag!='Defs':continue
  files.append(str(f))
  for n in doc.getroot():
   root.append(n);origins[n]=str(f)

patchlog=[]
def patch(op):
 cls=op.get('Class',''); xp=op.findtext('xpath')
 if xp and xp.startswith('Defs/'):xp='/'+xp
 if cls=='PatchOperationSequence':
  for c in op.findall('operations/li'):patch(c)
  return
 if cls=='PatchOperationFindMod':
  patchlog.append({'class':cls,'mods':op.findtext('mods'),'skipped':'optional external mod not supplied'})
  return
 if cls=='PatchOperationConditional':
  target=op.find('match' if E.ElementTree(root).xpath(xp) else 'nomatch')
  if target is not None:patch(target)
  return
 if not xp: patchlog.append({'class':cls,'unhandled':True});return
 nodes=E.ElementTree(root).xpath(xp);patchlog.append({'class':cls,'xpath':xp,'matches':len(nodes)})
 for n in nodes:
  p=n.getparent()
  if cls=='PatchOperationReplace':
   idx=p.index(n);p.remove(n)
   for v in op.find('value'):p.insert(idx,deepcopy(v));idx+=1
  elif cls=='PatchOperationAdd':
   for v in op.find('value'):n.append(deepcopy(v))
  elif cls=='PatchOperationRemove':p.remove(n)
  else: raise ValueError(cls)
for f in sorted((ROOT/'Patches').rglob('*.xml')):
 for op in E.parse(str(f),parser).getroot().findall('Operation'):patch(op)
OUT.mkdir(exist_ok=True,parents=True)
E.ElementTree(root).write(str(OUT/'patched.xml'),encoding='utf-8',xml_declaration=True)

named={n.get('Name'):n for n in root if n.get('Name')}
def merge(parent,child):
 if child.get('Inherit','').lower()=='false':return deepcopy(child)
 result=deepcopy(parent)
 result.attrib.update(child.attrib)
 if len(child)==0:
  if (child.text or '').strip():result.text=child.text
  elif len(parent)==0:result.text=None
  return result
 if len(parent)==0:result.text=None
 for c in child:
  # XML list entries append; dictionaries/object fields merge by element name.
  matches=[x for x in result if x.tag==c.tag] if c.tag!='li' else []
  if matches:
   old=matches[0];idx=result.index(old);result.remove(old);result.insert(idx,merge(old,c))
  else:result.append(deepcopy(c))
 return result
cache={};missing=set()
def resolve(n,stack=()):
 if n in cache:return cache[n]
 pn=n.get('ParentName')
 if pn:
  if pn not in named:missing.add(pn);res=deepcopy(n)
  else:
   assert pn not in stack,(pn,stack)
   res=merge(resolve(named[pn],stack+(pn,)),n)
   for attr in ['Abstract','Name','ParentName']:
    if attr in n.attrib:res.set(attr,n.get(attr))
    elif attr in res.attrib:del res.attrib[attr]
 else:res=deepcopy(n)
 cache[n]=res;return res
defs={}; source={}; duplicates=[]
for n in root:
 dn=n.findtext('defName')
 if dn:
  key=(n.tag,dn)
  if key in defs:duplicates.append(key)
  defs[key]=resolve(n);source[key]=origins.get(n,'patch')
translations={}
for archive in [GAME/'Data/Core/Languages/Korean (한국어).tar',GAME/'Data/Biotech/Languages/Korean (한국어).tar']:
 if not archive.exists():continue
 with tarfile.open(archive) as tar:
  for member in tar.getmembers():
   if 'DefInjected/' not in member.name or not member.name.endswith('.xml'):continue
   try:d=E.fromstring(tar.extractfile(member).read(),parser)
   except:continue
   for n in d:translations[(Path(member.name).parent.name,n.tag)]=n.text or ''
for base in [GAME/'Data/Core/Languages/Korean/DefInjected',ROOT/'Languages/Korean (한국어)/DefInjected']:
 if not base.exists():continue
 for f in base.rglob('*.xml'):
  try:d=E.parse(str(f),parser)
  except:continue
  for n in d.getroot():translations[(f.parent.name,n.tag)]=n.text or ''
def label(tag,dn):
 return translations.get((tag,dn+'.label')) or defs.get((tag,dn),E.Element('x')).findtext('label') or dn
modthings={dn:n for (t,dn),n in defs.items() if t=='ThingDef' and source[(t,dn)].startswith(str(ROOT/'Defs'))}
items={};excluded={}
for dn,n in modthings.items():
 if n.get('Abstract','').lower()=='true':excluded[dn]='abstract';continue
 isapp=n.find('apparel') is not None and n.findtext('category')=='Item'
 cats=n.xpath('thingCategories/li/text()');cl=n.findtext('thingClass','')
 comps=n.xpath('comps/li/@Class')
 isarmorpart=any('CompProperties_ArmorPlateSet' in x for x in comps) or 'HD_ModularArmorParts' in cats
 isinv='HD_InventoryGrenades' in cats
 isweapon=n.findtext('equipmentType')=='Primary' or isinv or any(('WornMeleeWeapon' in c or 'CompProperties_ZaperX26' in c) for c in comps) or dn=='HD_C4_Charge'
 if re.search(r'(^|_)Test(_|$)',dn,re.I) or 'test harness' in n.findtext('label','').lower():
  excluded[dn]='test';continue
 if dn.endswith('_TurretGun'):
  excluded[dn]='internal turret gun';continue
 if isapp or isarmorpart or isweapon:
  items[dn]={'label':label('ThingDef',dn),'weapon':isweapon,'apparel':isapp,'armorpart':isarmorpart,'inventorygrenade':isinv,'file':str(Path(source[('ThingDef',dn)]).relative_to(ROOT)),'xml':E.tostring(n,encoding='unicode')}
OUT.mkdir(exist_ok=True,parents=True)
(OUT/'resolved.json').write_text(json.dumps({'items':items,'defs':{t+'|'+dn:E.tostring(n,encoding='unicode') for (t,dn),n in defs.items()},'labels':{t+'|'+dn:label(t,dn) for (t,dn) in defs},'patchlog':patchlog,'missing':sorted(missing),'duplicates':duplicates,'excluded':excluded},ensure_ascii=False,indent=2),encoding='utf-8')
print('XML files',len(files),'mod ThingDefs',len(modthings),'items',len(items),'weapons',sum(i['weapon'] for i in items.values()),'apparel/parts',sum(i['apparel'] or i['armorpart'] for i in items.values()),'missing parents',missing,'duplicates',duplicates)
for dn,i in items.items():print(dn,i['label'], 'W' if i['weapon'] else 'A', i['file'])
print('EXCLUDED TESTS',[(k,v) for k,v in excluded.items() if v=='test'])
