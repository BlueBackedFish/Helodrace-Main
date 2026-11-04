from pathlib import Path
import json
import xml.etree.ElementTree as E

ROOT = Path(__file__).resolve().parents[3]
rows = json.loads((ROOT / 'Docs/Data/HelmetShields/recipe-materials.json').read_text(encoding='utf-8'))
assert len(rows) == 5
# Earlier before/after files are immutable records of those engine runs.
current = {r['defName']: r['after'] for r in json.loads((ROOT / 'Docs/Data/EquipmentMaterials/recipe-materials.json').read_text(encoding='utf-8')) if r['kind'] == 'ThingDef'}
current.update(json.loads((ROOT / 'Docs/Data/ArmorInstant/current-costs.json').read_text(encoding='utf-8')))
for row in rows:
    row = dict(row, after=current.get(row['defName'], row['after']))
    d = next(x for x in E.parse(ROOT / row['file']).getroot() if x.findtext('defName') == row['defName'])
    assert {x.tag: int(x.text) for x in d.find('costList')} == row['after'], row['defName']
    assert {x.tag: x.text for x in d.findall('statBases/*')} == row['statBases'], row['defName']
    allowed = {x.text for x in d.findall('recipeMaker/defaultIngredientFilter/thingDefs/li')}
    assert allowed == set(row['after']), row['defName']
    if row['defName'] == 'HD_Apparel_GreatWarM1Helmet':
        assert row['before'] == row['after'], row['defName']

modern = E.parse(ROOT / 'Defs/ModernWar/Items/Apparel_ModernWar.xml').getroot()
ibtv = next(d for d in modern if d.findtext('defName') == 'HD_Apparel_IBTVAssault')
comp = next(c for c in ibtv.findall('comps/li') if c.get('Class') == 'Helodrace.ModernWar.CompProperties_ModularArmor')
assert comp.find('defaultParts') is None
assert len(comp.findall('slots/li')) == 6
print('PASS: five helmet/shield costs and default filters, unchanged base stats, empty IBTV with all six slots preserved.')
