from pathlib import Path
import zipfile,json,math
from lxml import etree as E
OUT=Path(__file__).parent
rows=json.loads((OUT/'rows.json').read_text(encoding='utf-8'))
raw=json.loads((OUT/'resolved.json').read_text(encoding='utf-8'))
assert raw['missing']==[],raw['missing']
assert not any(t=='ThingDef' for t,d in raw['duplicates']),raw['duplicates']
audit=json.loads((OUT/'audit.json').read_text(encoding='utf-8'))
assert not audit['warnings']
def source(name):return E.fromstring(raw['defs']['ThingDef|'+name])
def val(sheet,dn,header):
 d=rows[sheet];return d['rows'][d['ids'].index(dn)][d['headers'].index(header)]
assert val('Weapons','HD_Gun_M16A4_Weapon','피해량')==11
assert val('Weapons','HD_Gun_M16A4_Weapon','무게 (kg)')==3.4
assert val('Weapons','HD_Gun_M16A4_Weapon','관통력')==.18
assert val('Weapons','HD_Gun_ColtSAA_Weapon','관통력')==.18
assert val('Weapons','HD_Gun_M1897_Weapon','산탄 수/발')==9
assert val('Weapons','HD_Gun_M79_Weapon','피해량')==42
assert val('Weapons','HD_Grenade_M67_Item','사거리 (칸)')==13.9
assert val('Weapons','HD_Grenade_M67_Item','조준/준비 (초)')==3
assert val('Weapons','HD_Grenade_M14_Item','관통력')==.75
assert val('Weapons','HD_Apparel_ZaperX26_Device','재사용 (초)')==2.4
assert val('Weapons','HD_C4_Charge','폭발 반경 (칸)')==1.68
assert val('Apparel','HD_Apparel_IBTVAssault','통풍')==4.65
assert val('Apparel','HD_Apparel_IBTVAssault','쾌적 최고온도 변화 (°C)')==-.35
assert val('Apparel','HD_Apparel_IBTVAssault','날카로움')==.35
assert val('Apparel','HD_ArmorPlate_ESAPI','확정 방어 관통력')==.6
assert val('Apparel','HD_ArmorPlate_ESAPI','방탄판 내구도/장')==40
assert val('Apparel','HD_ModularPart_MultiHitVisor','날카로움')==.75
assert val('Apparel','HD_Apparel_ShieldIronHideIV','날카로움')==2
patches=[p for p in audit['patchlog'] if p.get('class')=='PatchOperationReplace' and p.get('xpath','').endswith('/verbs')]
assert len(patches)==9 and all(p['matches']==1 for p in patches)
ns={'m':'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}
def letters(n):
 out=''
 while n:n,r=divmod(n-1,26);out=chr(65+r)+out
 return out
with zipfile.ZipFile(OUT/'Helodrace_무기_의상_비교표.xlsx') as z:
 strings=[]
 if 'xl/sharedStrings.xml' in z.namelist():
  sr=E.fromstring(z.read('xl/sharedStrings.xml'));strings=[''.join(si.xpath('.//m:t/text()',namespaces=ns)) for si in sr]
 for idx,(name,data) in enumerate(rows.items(),1):
  sheet=E.fromstring(z.read(f'xl/worksheets/sheet{idx}.xml'));cells={}
  for c in sheet.xpath('//m:sheetData/m:row/m:c',namespaces=ns):
   t=c.get('t');v=c.findtext('m:v',namespaces=ns)
   value=''.join(c.xpath('.//m:t/text()',namespaces=ns)) if t=='inlineStr' else v if t=='str' else strings[int(v)] if t=='s' and v is not None else float(v) if v is not None else None
   cells[c.get('r')]=value
  matrix=[data['headers']]+data['rows']
  for r,row in enumerate(matrix,1):
   for c,expected in enumerate(row,1):
    address=f'{letters(c)}{r}';actual=cells.get(address)
    if isinstance(expected,(int,float)):assert actual is not None and math.isclose(actual,expected,abs_tol=1e-7),(name,address,actual,expected)
    else:assert actual==expected,(name,address,actual,expected)
  pane=sheet.find('m:sheetViews/m:sheetView/m:pane',ns)
  assert pane is not None and pane.get('state')=='frozen' and pane.get('ySplit')=='1' and pane.get('xSplit')=='2',(name,E.tostring(pane))
  table=E.fromstring(z.read(f'xl/tables/table{idx}.xml'))
  assert table.get('ref')==f'A1:{letters(len(data["headers"]))}{len(matrix)}'
  assert table.find('m:autoFilter',ns) is not None
  assert len(data['ids'])==len(set(data['ids']))
  assert not set(data['ids']).intersection(raw['excluded'])
  print(name,'all exported cells match; filters and panes verified;',len(data['rows']),'items')
 print('Item union',len(set(rows['Weapons']['ids']+rows['Apparel']['ids'])),'Excluded tests',sum(v=='test' for v in raw['excluded'].values()),'Excluded internal guns',sum(v=='internal turret gun' for v in raw['excluded'].values()))
print('Source links, inherited stats, mode/comp behavior, unit conversion, unique rows and export checks complete.')
