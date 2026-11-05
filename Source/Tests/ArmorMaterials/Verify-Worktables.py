from pathlib import Path
import json
import xml.etree.ElementTree as E

ROOT = Path(__file__).resolve().parents[3]
rows = json.loads((ROOT / 'Docs/Data/Worktables/routing.json').read_text(encoding='utf-8'))
assert len(rows) == 89
source = {}
for file in (ROOT / 'Defs').rglob('*.xml'):
    for d in E.parse(file).getroot():
        source[d.tag, d.findtext('defName', d.get('Name', ''))] = d
for row in rows:
    generated = row['recipe'].startswith('Make_')
    d = source['ThingDef' if generated else 'RecipeDef', row['product'] if generated else row['recipe']]
    users = d.find('recipeMaker/recipeUsers') if generated else d.find('recipeUsers')
    assert users is not None and users.get('Inherit') == 'False', row['recipe']
    assert [u.text for u in users] == row['after'], row['recipe']
    assert ('HD_BasicWorkbench' in row['after']) == ('HD_BasicWorkbench' in row['users']), row['recipe']
    if row['requiresPress']:
        assert d.findtext('modExtensions/li[@Class="Helodrace.FacilityRequirementExtension"]/requirements/HD_Pressing') == '1', row['product']

for name in ['HD_ClothingBench', 'HD_BasicWorkbench']:
    links = {x.text for x in source['ThingDef', name].findall('comps/li[@Class="CompProperties_AffectedByFacilities"]/linkableFacilities/li')}
    assert {'HD_LineShaftHydraulicPress', 'HD_ElectricHydraulicPress'} <= links
for name, tier in [('HD_LineShaftHydraulicPress', 1), ('HD_ElectricHydraulicPress', 2)]:
    d = source['ThingDef', name]
    assert d.find('comps/li[@Class="CompProperties_Facility"]') is not None
    assert d.findtext('modExtensions/li[@Class="Helodrace.FacilityCapabilityExtension"]/capabilities/HD_Pressing') == str(tier)

en = E.parse(ROOT / 'Languages/English/Keyed/ProductionFacilities.xml').getroot()
ko = E.parse(ROOT / 'Languages/Korean (한국어)/Keyed/ProductionFacilities.xml').getroot()
assert {x.tag for x in en} == {x.tag for x in ko}
assert all(x.text for x in en) and all(x.text for x in ko)
print('PASS: 89 authored routes, 59 retained basic recipes, 17 press requirements, both linkable pressing levels and matching English/Korean information keys.')
