"""Bind one tile-calibrated four-view model without changing gameplay fields."""
import copy,hashlib,json,re,sys,xml.etree.ElementTree as ET
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]

def gameplay_hash(node):
    node=copy.deepcopy(node)
    for key in ['graphicData','rotatable']:
        child=node.find(key)
        if child is not None:node.remove(child)
    # Smoke origin is presentation data, not production/fuel/heat behavior.
    comps=node.find('comps')
    if comps is not None:
        for child in list(comps):
            if child.get('Class')=='Helodrace.CompProperties_SteamEngineSmoke':comps.remove(child)
    return hashlib.sha256(ET.tostring(node)).hexdigest()

def integrate(stem,folder,def_name=None):
    def_name=def_name or stem
    report=json.loads((ROOT/'ArtSource'/folder/'validation.json').read_text())
    assert set(report['cameras'])=={'North','East','South','West'}
    for direction in ['north','east','south','west']:
        for suffix in ['', 'm']:
            assert (ROOT/'Textures/Buildings'/f'{stem}_{direction}{suffix}.png').is_file()
    for path in (ROOT/'Defs').rglob('*.xml'):
        try:tree=ET.parse(path)
        except ET.ParseError:continue
        node=next((n for n in tree.getroot().findall('ThingDef') if n.findtext('defName')==def_name),None)
        if node is None:continue
        original=gameplay_hash(node);scale=report['orthographic_scale']
        offset='('+','.join(f'{v:.9g}' for v in report.get('expected_draw_offset',[0,0,0]))+')'
        def revise(match):
            block=match.group()
            if f'<defName>{def_name}</defName>' not in block:return block
            changes={'texPath':'Buildings/'+stem,'graphicClass':'Graphic_Multi','shaderType':'CutoutComplex',
                     'drawSize':f'({scale:g},{scale:g})','drawOffset':offset,'rotatable':'true'}
            for key,value in changes.items():
                if key=='drawOffset' and '<drawOffset>' not in block:
                    block=block.replace('</drawSize>','</drawSize>\n      <drawOffset>'+value+'</drawOffset>',1)
                assert re.search(fr'<{key}>[^<]*</{key}>',block),key
                block=re.sub(fr'<{key}>[^<]*</{key}>',f'<{key}>{value}</{key}>',block,count=1)
            return block
        with path.open(encoding='utf8',newline='') as f:text=f.read()
        revised=re.sub(r'<ThingDef\b[^>]*>.*?</ThingDef>',revise,text,flags=re.S)
        changed=next(n for n in ET.fromstring(revised).findall('ThingDef') if n.findtext('defName')==def_name)
        assert gameplay_hash(changed)==original,'Nonvisual gameplay definition changed'
        with path.open('w',encoding='utf8',newline='') as f:f.write(revised)
        report['definition']=str(path.relative_to(ROOT)).replace('\\','/')
        report['gameplay_fields_preserved_sha256']=original
        (ROOT/'ArtSource'/folder/'validation.json').write_text(json.dumps(report,indent=2))
        print(f'{stem}: four directions, rotatable, drawSize {scale:g}, gameplay fields preserved')
        return
    raise ValueError('Definition not found: '+stem)

if __name__=='__main__':integrate(sys.argv[1],sys.argv[2])
