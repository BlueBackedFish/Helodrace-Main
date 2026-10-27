"""Cross-definition and presentation contracts for the newly playable oil machinery."""
from pathlib import Path
import json,xml.etree.ElementTree as ET
from PIL import Image
ROOT=Path(__file__).resolve().parents[3]
nodes=[n for file in (ROOT/'Defs').rglob('*.xml') for n in ET.parse(file).getroot()]
def index(kind):
    return {n.findtext('defName'):n for n in nodes if n.tag==kind and n.findtext('defName')}
things=index('ThingDef');research=index('ResearchProjectDef')
new=ET.parse(ROOT/'Defs/WildWest/Buildings/AdvancedOilExtraction.xml').getroot()
locale=ET.parse(ROOT/'Languages/Korean (한국어)/DefInjected/ThingDef/AdvancedOilExtraction.xml').getroot()
assert len(new)==2
for d in new:
    name=d.findtext('defName');assert sum(n.findtext('defName')==name for n in nodes)==1
    assert locale.findtext(name+'.label') and locale.findtext(name+'.description')
    for item in d.find('costList'):assert item.tag=='Steel' or item.tag in things,item.tag
    for r in d.findall('researchPrerequisites/li'):assert r.text in research,r.text
    comps={c.get('Class'):c for c in d.find('comps')}
    assert 'CompProperties_Flickable' in comps and 'Helodrace.CompProperties_OperatingHeatPusher' in comps
    assert 'Helodrace.CompProperties_MechanicalUser' not in comps
    assert d.findtext('rotatable')=='true' and d.findtext('graphicData/graphicClass')=='Graphic_Multi'
    folder='RotaryDrill' if name=='HD_RotaryDrill' else 'MetalPumpJack'
    meta=json.loads((ROOT/'ArtSource'/folder/'validation.json').read_text(encoding='utf8'))
    assert [int(v) for v in d.findtext('size').strip('()').split(',')]==meta['footprint']
    for direction in ['north','east','south','west']:
        color=Image.open(ROOT/'Textures'/f'{d.findtext("graphicData/texPath")}_{direction}.png')
        mask=Image.open(ROOT/'Textures'/f'{d.findtext("graphicData/texPath")}_{direction}m.png')
        assert color.size==mask.size==tuple(meta['texture_resolution'])
        assert color.getchannel('A').tobytes()==mask.getchannel('A').tobytes()
rig=things['HD_RotaryDrill'];comps={c.get('Class'):c for c in rig.find('comps')}
assert rig.findtext('tickerType')=='Normal' and rig.findtext('hasInteractionCell')=='true'
fuel=comps['CompProperties_Refuelable']
assert fuel.findtext('consumeFuelOnlyWhenUsed')=='true'
assert fuel.findtext('fuelFilter/thingDefs/li')=='HD_Diesel' and 'HD_Diesel' in things
assert float(fuel.findtext('fuelConsumptionRate'))>0 and float(fuel.findtext('fuelCapacity'))>0
drill=comps['Helodrace.CompProperties_OilFieldDevelopment']
assert 0<float(drill.findtext('totalDaysNeeded'))<7
assert float(drill.findtext('minimumQuality'))<=float(drill.findtext('maximumQuality'))
jobs=index('JobDef');assert jobs['HD_DevelopOilField'].findtext('driverClass')=='Helodrace.JobDriver_DevelopOilField'
assert any(n.tag=='WorkGiverDef' and n.findtext('giverClass')=='Helodrace.WorkGiver_DevelopOilField' for n in nodes)
pump=things['HD_MetalPumpJack'];comps={c.get('Class'):c for c in pump.find('comps')}
power=comps['CompProperties_Power'];assert power.findtext('compClass')=='CompPowerTrader'
assert float(power.findtext('basePowerConsumption'))>0
producer=comps['Helodrace.CompProperties_OilProducer'];assert producer.findtext('product') in things
assert pump.findtext('placeWorkers/li')=='Helodrace.PlaceWorker_OnOilFloor'
assert pump.findtext('tickerType')=='Rare'
print('PASS: two unique playable defs, resolved resources/research/jobs, Korean text, fuel/power contracts, four-view textures and footprint metadata.')
