"""Check new machine tiers retain source recipes and resolve their game resources."""
from pathlib import Path
import xml.etree.ElementTree as ET
import json
ROOT=Path(__file__).resolve().parents[2]
CORE=Path('C:/Program Files (x86)/Steam/steamapps/common/RimWorld/Data/Core/Defs')
own=[n for f in (ROOT/'Defs').rglob('*.xml') for n in ET.parse(f).getroot()]
core=[n for f in CORE.rglob('*.xml') for n in ET.parse(f).getroot()]
def index(kind):return {n.findtext('defName'):n for n in core+own if n.tag==kind and n.findtext('defName')}
things=index('ThingDef');recipes=index('RecipeDef');research=index('ResearchProjectDef')
def locale(kind,key):return any(ET.parse(f).getroot().findtext(key) for f in (ROOT/'Languages/Korean (한국어)/DefInjected'/kind).glob('*.xml'))
def available(name):
    explicit={('recipe',x.text) for x in things[name].findall('recipes/li')}
    declared={('recipe',key) for key,r in recipes.items() if name in [x.text for x in r.findall('recipeUsers/li')]}
    implied={('maker',key) for key,n in things.items() if name in [x.text for x in n.findall('recipeMaker/recipeUsers/li')]}
    return explicit|declared|implied
assets=[('HD_MachiningCenter1970','MachiningCenter1970',['HD_CNCMillingMachine'],[3,3]),
 ('HD_MultiTaskMachine2000','MultiTaskMachine2000',['HD_CNCLathe','HD_CNCMillingMachine'],[4,3]),
 ('HD_HydraulicPress1970','HydraulicPress1970',['HD_LineShaftHydraulicPress'],[3,3]),
 ('HD_RollingMill1970','RollingMill1970',['HD_LineShaftRollingMachine'],[4,3])]
for name,folder,sources,size in assets:
    n=things[name];assert sum(x.findtext('defName')==name for x in own)==1
    assert n.findtext('thingClass')=='Building_WorkTable' and n.findtext('tickerType')=='Rare'
    assert locale('ThingDef',name+'.label') and locale('ThingDef',name+'.description')
    for cost in n.find('costList'):assert cost.tag in things,cost.tag
    for r in n.findall('researchPrerequisites/li'):assert r.text in research,r.text
    comps={c.get('Class'):c for c in n.find('comps')}
    assert comps['CompProperties_Power'].findtext('compClass')=='CompPowerTrader'
    assert float(comps['CompProperties_Power'].findtext('basePowerConsumption'))>0
    assert 'CompProperties_Flickable' in comps and 'Helodrace.CompProperties_OperatingHeatPusher' in comps
    assert 'Helodrace.CompProperties_MechanicalUser' not in comps
    expected=set().union(*(available(s) for s in sources))
    assert available(name)==expected,(name,'Lost or unexpected source recipes',expected^available(name))
    for r in n.findall('recipes/li'):assert r.text in recipes,r.text
    assert float(n.findtext('statBases/WorkTableWorkSpeedFactor'))>max(float(things[s].findtext('statBases/WorkTableWorkSpeedFactor')) for s in sources)
    workers=[w for w in own if w.tag=='WorkGiverDef' and name in [x.text for x in w.findall('fixedBillGiverDefs/li')]]
    assert len(workers)==1 and workers[0].findtext('giverClass')=='WorkGiver_DoBill'
    assert workers[0].findtext('workType')=='Crafting' and locale('WorkGiverDef',workers[0].findtext('defName')+'.label')
    if name in ['HD_MachiningCenter1970','HD_MultiTaskMachine2000']:
        assert 'CompProperties_Facility' in comps
        for bench in ['HD_BasicWorkbench','HD_GunSmithTable']:
            links=things[bench].find('comps/li[@Class="CompProperties_AffectedByFacilities"]/linkableFacilities')
            assert name in [x.text for x in links]
    meta=json.loads((ROOT/'ArtSource'/folder/'validation.json').read_text(encoding='utf8'))
    assert meta['footprint']==size and n.findtext('size')=='('+','.join(map(str,size))+')'
    assert meta['outline_pixels']==12 and meta['texture_resolution']==[256*max(size)]*2
    assert n.findtext('graphicData/graphicClass')=='Graphic_Multi' and n.findtext('rotatable')=='true'
    assert [float(x) for x in n.findtext('graphicData/drawSize').strip('()').split(',')]==meta['expected_draw_size']
for name in ['HelodIndustrialAutomation','HelodMultiTaskMachining']:
    assert locale('ResearchProjectDef',name+'.label')
    def ancestors(key,trail=()):
        assert key in research and key not in trail,(key,trail)
        for child in research[key].findall('prerequisites/li'):ancestors(child.text,trail+(key,))
    ancestors(name)
assert ('maker','HD_Apparel_GreatWarM1Helmet') in available('HD_HydraulicPress1970')
extension=things['HD_MultiTaskMachine2000'].find('modExtensions/li')
assert extension.findtext('latheTier')=='4' and extension.findtext('millingTier')=='4'
print('PASS: four electric machine tiers, direct/generated recipe parity, bill workers, facility links, research/resources/localization and presentation metadata.')
