"""Semantic definition checks for electric NC machines and the small transistor fab."""
from pathlib import Path
import sys,json,xml.etree.ElementTree as ET
from PIL import Image
ROOT=Path(__file__).resolve().parents[2]
CORE=Path('C:/Program Files (x86)/Steam/steamapps/common/RimWorld/Data/Core/Defs')
own=[node for file in (ROOT/'Defs').rglob('*.xml') for node in ET.parse(file).getroot()]
core=[node for file in CORE.rglob('*.xml') for node in ET.parse(file).getroot()]
def index(kind):return {n.findtext('defName'):n for n in core+own if n.tag==kind and n.findtext('defName')}
things=index('ThingDef');recipes=index('RecipeDef');research=index('ResearchProjectDef')
cnc=[('HD_NCLathe','NCLathe','HD_LineShaftTurretLathe'),('HD_NCMillingMachine','NCMillingMachine','HD_LineShaftMillingMachine')]
mode=sys.argv[1] if len(sys.argv)>1 else 'all'
selected=[] if mode=='fab' else cnc.copy()
if mode!='cnc':selected.append(('HD_SemiconductorFab','SemiconductorFab',None))
def available(name):
    return set(n.text for n in things[name].findall('recipes/li')) | {name_r for name_r,r in recipes.items() if name in [n.text for n in r.findall('recipeUsers/li')]}
def locale(kind,key):
    return any(ET.parse(f).getroot().findtext(key) for f in (ROOT/'Languages/Korean (한국어)/DefInjected'/kind).glob('*.xml'))
for name,folder,old in selected:
    node=things[name]
    assert sum(n.findtext('defName')==name for n in own)==1
    assert node.findtext('thingClass')=='Building_WorkTable'
    assert node.findtext('hasInteractionCell')=='true' and node.findtext('rotatable')=='true'
    assert node.findtext('tickerType')=='Rare','Operating heat needs a tick callback'
    assert locale('ThingDef',name+'.label') and locale('ThingDef',name+'.description')
    for item in node.find('costList'):assert item.tag in things,item.tag
    for r in node.findall('researchPrerequisites/li'):assert r.text in research,r.text
    comps={c.get('Class'):c for c in node.find('comps')}
    assert comps['CompProperties_Power'].findtext('compClass')=='CompPowerTrader'
    assert float(comps['CompProperties_Power'].findtext('basePowerConsumption'))>0
    assert 'CompProperties_Flickable' in comps and 'Helodrace.CompProperties_OperatingHeatPusher' in comps
    assert 'Helodrace.CompProperties_MechanicalUser' not in comps
    workgivers=[n for n in own if n.tag=='WorkGiverDef' and name in [x.text for x in n.findall('fixedBillGiverDefs/li')]]
    assert len(workgivers)==1 and workgivers[0].findtext('giverClass')=='WorkGiver_DoBill'
    assert workgivers[0].findtext('workType')=='Crafting'
    assert locale('WorkGiverDef',workgivers[0].findtext('defName')+'.label')
    assert available(name),'A playable worktable must expose recipes'
    for r in available(name):assert r in recipes,r
    if old:
        assert available(name)==available(old),(name,'Recipe coverage differs from source machine')
        assert float(node.findtext('statBases/WorkTableWorkSpeedFactor'))>float(things[old].findtext('statBases/WorkTableWorkSpeedFactor'))
        assert 'CompProperties_Facility' in comps
        extension=node.find('modExtensions/li');assert extension.get('Class')=='Helodrace.MachineToolFacilityExtension'
        for bench in ['HD_BasicWorkbench','HD_GunSmithTable']:
            facilities=things[bench].find('comps/li[@Class="CompProperties_AffectedByFacilities"]/linkableFacilities')
            assert name in [x.text for x in facilities]
    meta=json.loads((ROOT/'ArtSource'/folder/'validation.json').read_text(encoding='utf8'))
    footprint=[int(v) for v in node.findtext('size').strip('()').split(',')]
    assert footprint==meta['footprint']==([3,3] if old is None else [3,2])
    assert meta['texture_resolution']==[768,768] and meta['outline_pixels']==12
    for direction in ['north','east','south','west']:
        prefix=ROOT/'Textures'/node.findtext('graphicData/texPath')
        color=Image.open(str(prefix)+'_'+direction+'.png');mask=Image.open(str(prefix)+'_'+direction+'m.png')
        assert color.size==mask.size==(768,768)
        assert color.getchannel('A').tobytes()==mask.getchannel('A').tobytes()
if mode!='fab':
    nc=research['HelodNumericalControl'];assert nc.findtext('techLevel')=='Industrial'
    assert locale('ResearchProjectDef','HelodNumericalControl.label')
    for r in nc.findall('prerequisites/li'):assert r.text in research
    # Check the new branch cannot cycle through its prerequisites.
    def ancestors(name,path=()):
        assert name not in path,('Research cycle',path,name)
        for child in research[name].findall('prerequisites/li'):ancestors(child.text,path+(name,))
    ancestors('HelodNumericalControl')
if mode!='cnc':
    recipe=recipes['HD_FabricateSolidStateComponents']
    assert recipe.findtext('products/ComponentIndustrial')=='1'
    assert recipe.findtext('researchPrerequisite')=='HelodTransistor'
    assert locale('RecipeDef','HD_FabricateSolidStateComponents.label')
    for ingredient in recipe.findall('ingredients/li'):
        for item in ingredient.findall('filter/thingDefs/li'):assert item.text in things,item.text
    # Building its first fab must not require its own product.
    assert things['HD_SemiconductorFab'].find('costList/ComponentIndustrial') is None
    assert recipe.findtext('skillRequirements/Crafting')=='10'
print('PASS:',mode,'electric worktables, bill workers, research/resources/localization, recipe parity, facility links and four-view texture contracts.')
