from pathlib import Path
import json,xml.etree.ElementTree as E
ROOT=Path(__file__).resolve().parents[3]
rows=json.loads((ROOT/'Docs/Data/ArmorMaterials/recipe-materials.json').read_text(encoding='utf-8'))
assert len(rows)==19
# Earlier before/after files are immutable records of those engine runs.
current = {r['defName']: r['after'] for r in json.loads((ROOT / 'Docs/Data/EquipmentMaterials/recipe-materials.json').read_text(encoding='utf-8')) if r['kind'] == 'ThingDef'}
for row in rows:
    row = dict(row, after=current.get(row['defName'], row['after']))
    d=next(x for x in E.parse(ROOT/row['file']).getroot() if x.findtext('defName')==row['defName'])
    assert {x.tag:int(x.text) for x in d.find('costList')}==row['after'],row['defName']
    assert {x.tag:x.text for x in d.findall('statBases/*')}==row['statBases'],row['defName']
    assert d.findtext('comps/li/material')==row['material'],row['defName']
    if row['material'] is not None:
        assert ('HD_Alumina' in row['after'])==(row['material']=='Ceramic'),row['defName']
    assert 'HD_Nylon' in row['after'],row['defName']
print('PASS: 19 armor input lists, unchanged base stats/material identities, nylon covers and ceramic-only alumina.')
