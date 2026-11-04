from pathlib import Path
import json
import xml.etree.ElementTree as E

ROOT = Path(__file__).resolve().parents[3]
BANNED = {'ComponentIndustrial', 'ComponentSpacer'}
rows = json.loads((ROOT / 'Docs/Data/EquipmentMaterials/recipe-materials.json').read_text(encoding='utf-8'))
assert len(rows) == 70
for row in rows:
    d = next(d for d in E.parse(ROOT / row['file']).getroot()
             if d.findtext('defName', d.get('Name', '')) == row['defName'])
    if row['kind'] == 'ThingDef':
        actual = {c.tag: int(c.text) for c in d.find('costList')}
        assert {s.tag: s.text for s in d.findall('statBases/*')} == row['statBases'], row['defName']
        assert (d.find('recipeMaker') is not None and d.find('recipeMaker').get('IsNull') == 'True') == row['recipeDisabled'], row['defName']
    else:
        actual = {i.findtext('filter/thingDefs/li'): int(i.findtext('count')) for i in d.findall('ingredients/li')}
        assert len(actual) == len(d.findall('ingredients/li')), row['defName']
        for tag in ['fixedIngredientFilter', 'defaultIngredientFilter']:
            assert {e.text for e in d.findall(f'{tag}/thingDefs/li')} == set(actual), row['defName']
    assert actual == row['after'] and not (BANNED & actual.keys()), row['defName']

for file in (ROOT / 'Defs').rglob('*.xml'):
    for d in E.parse(file).getroot():
        name = d.findtext('defName', d.get('Name', ''))
        assert not any(c.tag in BANNED for c in d.findall('costList/*')), (file, name)
        if 'Shield' in name:
            assert d.find('costList/Plasteel') is None, (file, name)
        if d.tag == 'RecipeDef':
            inputs = {i.text for i in d.findall('ingredients/li/filter/thingDefs/li')}
            if inputs & BANNED:
                assert name.startswith('HD_ConvertIndustrialComponentToGeneralMachinePart') and d.find('products/HD_GeneralMachinePart') is not None, (file, name)
print('PASS: 70 equipment definitions, merged conversion inputs, unchanged base stats/assembly policy, no vanilla components in any mod costList, and no shield plasteel.')
