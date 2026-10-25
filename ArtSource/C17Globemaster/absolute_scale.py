"""Measure actual game drawSize and raster density of the requested references."""
from pathlib import Path
import hashlib,json,xml.etree.ElementTree as ET
from PIL import Image


def reference_scale(main):
    wanted={'HD_Radio','HD_TreadleLathe','HD_LineShaftTurretLathe','HD_LineShaftMillingMachine'}
    records={}
    for path in (main/'Defs').rglob('*.xml'):
        try:root=ET.parse(path).getroot()
        except ET.ParseError:continue
        for thing in root.findall('ThingDef'):
            name=thing.findtext('defName')
            if name not in wanted:continue
            graphic=thing.find('graphicData')
            draw=[float(v) for v in graphic.findtext('drawSize').strip('()').split(',')]
            texture=main/'Textures'/(graphic.findtext('texPath')+'_south.png')
            image=Image.open(texture)
            density=[p/t for p,t in zip(image.size,draw)]
            assert abs(density[0]-512/3)<1e-6 and abs(density[1]-512/3)<1e-6
            records[name]=dict(definition=str(path),texture=str(texture),size=list(image.size),
                draw_size_tiles=draw,pixels_per_tile=density,
                sha256=hashlib.sha256(texture.read_bytes()).hexdigest())
    assert set(records)==wanted
    return records
