from pathlib import Path
import json
import xml.etree.ElementTree as E

ROOT = Path(__file__).resolve().parents[3]
rows = json.loads((ROOT / 'Docs/Data/ArmorInstant/parts.json').read_text(encoding='utf-8'))
assert len(rows) == 18
assert sum(r['mode'] == 'instant' for r in rows) == 12
assert sum(r['mode'] == 'independent' for r in rows) == 6
items = {d.findtext('defName'): d for p in (ROOT / 'Defs').rglob('*.xml')
         for d in E.parse(p).getroot().findall('ThingDef') if d.findtext('defName')}
for row in rows:
    part = next(d for d in E.parse(ROOT / row['file']).getroot()
                if d.findtext('defName') == row['part'])
    assert (part.findtext('plateThingDef') or part.findtext('partThingDef')) == row['item']
    separate = part.find('plateThingDef') is not None or row['part'] == 'HD_FASTMTPart_GPNVG18'
    assert separate == (row['mode'] == 'independent'), row['part']
    assert (part.findtext('canInstantCraft', 'true') == 'false') == (row['part'] == 'HD_FASTMTPart_GPNVG18'), row['part']
    costs = {c.tag: int(c.text) for c in items[row['item']].findall('costList/*')}
    assert costs == row['cost'] and costs and all(n > 0 for n in costs.values()), row['part']

en = E.parse(ROOT / 'Languages/English/Keyed/ArmorInstantCrafting.xml').getroot()
ko = E.parse(ROOT / 'Languages/Korean (한국어)/Keyed/ArmorInstantCrafting.xml').getroot()
assert {e.tag for e in en} == {e.tag for e in ko}
assert all(e.text for e in en) and all(e.text for e in ko)
print('PASS: 12 instant accessories, five separate plate sets plus night vision, existing exact input costs and matching English/Korean editor keys.')
