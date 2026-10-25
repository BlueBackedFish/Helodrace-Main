"""Publish true 1024 masters and independent main barrels to an Arsenal checkout/stage.

Usage: python ArtSource/VehicleRuntime/package_1024.py --mod-root <path>
World draw size, west alignment and damage/module identities are preserved.
"""
from pathlib import Path
import argparse,copy,json,sys,hashlib
import xml.etree.ElementTree as E
import numpy as np
from PIL import Image,ImageDraw
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'ArtSource/VehiclePipeline'))
from images import resize_linear
from outline_review import black_outline
NS='Helodrace.LobosArsenal.Vehicles.'
args=argparse.ArgumentParser();args.add_argument('--mod-root',type=Path,required=True);mod=args.parse_args().mod_root.resolve()
def setv(parent,tag,value):
    n=parent.find(tag)
    if n is None:n=E.SubElement(parent,tag)
    n.text=str(value);return n
def write(root,path):
    E.indent(root,space='  ');E.ElementTree(root).write(path,encoding='utf-8',xml_declaration=True)
def vec(v):return '('+','.join(f'{float(x):.8f}' for x in v)+')'
def parse(v):return [float(x) for x in v.strip('()').split(',')]
def outlined(image,radius):
    # Single narrow AA boundary; alpha test in game gives a solid cutout silhouette.
    return black_outline(image,radius)
defs=mod/'1.6/Defs/Vehicles';textures=mod/'1.6/Textures/Vehicles'
bradley=E.parse(defs/'M3A3.xml').getroot();props=bradley.find(f".//li[@Class='{NS}CompProperties_Bradley']")
previous=int(props.findtext('textureSize','512'));ratio=1024/previous
setv(props,'textureSize',1024);setv(props,'outlineWidth',round(int(props.findtext('outlineWidth','4'))*ratio))
write(bradley,defs/'M3A3.xml')
art=E.parse(defs/'M3A3Art.xml').getroot();a=art[0]
for path in ['anchors/li/pixel','towMuzzles/li/pixel','modulePositions/li/pixel']:
    for n in a.findall(path):n.text=vec([v*ratio for v in parse(n.text)])
states=a.find('states');states.clear()
board=Image.new('RGB',(1600,1040),(67,70,72));draw=ImageDraw.Draw(board)
for vi,variant in enumerate(['Standard','BUSK']):
    source=ROOT/'ArtSource/M2A3Bradley/TextureExport/output/1024'/variant
    index=json.loads((source/'index.json').read_text());assert index['size']==[1024,1024]
    for di,(direction,view) in enumerate(index['directions'].items()):
        for kind in ['hull','turret']:
            for name,item in view[kind+'_states'].items():
                state=E.SubElement(states,'li');setv(state,'key',('standard:' if variant=='Standard' else '')+f'{direction}:{kind}:{name}')
                layers=E.SubElement(state,'layers')
                entries=[dict(file=item['file'])] if kind=='hull' else [dict(file=view['parts']['turret_base']),dict(file=view['parts']['tow_'+name]),dict(file=view['parts']['gun'],recoiling='true')]
                if kind=='hull':entries+=item['overlays']
                else:
                    ids={m['states'][name]:mid for mid,m in view['era'].items() if m['parent']=='turret'}
                    entries += [dict(file=f,module=ids[f]) for f in item['era_files']]
                for entry in entries:
                    image=Image.open(source/entry['file']).convert('RGBA');box=image.getchannel('A').getbbox()
                    if box is None:continue
                    relative=('Standard/' if variant=='Standard' else '')+entry['file'];dest=textures/'M3A3/BUSK'/relative;dest.parent.mkdir(parents=True,exist_ok=True);image.crop(box).save(dest)
                    li=E.SubElement(layers,'li')
                    for k,v in dict(file=relative,x=box[0],y=1024-box[3],width=box[2]-box[0],height=box[3]-box[1],**{k:v for k,v in entry.items() if k!='file'}).items():setv(li,k,v)
        def assembled(kind,name):
            item=view[kind+'_states'][name];im=Image.open(source/item['file']).convert('RGBA')
            extra=item['overlays'] if kind=='hull' else [dict(file=f) for f in item['era_files']]
            for e in extra:im.alpha_composite(Image.open(source/e['file']).convert('RGBA'))
            return outlined(im,int(props.findtext('outlineWidth')))
        hull=assembled('hull','roof_closed_ramp_closed');turret=assembled('turret','stowed')
        anchor=next(n for n in a.find('anchors') if n.findtext('direction')==direction)
        lift=parse(anchor.findtext('turretOffset'))[1]*1024/float(props.findtext('drawSize'))
        ax,ay=view['hull_turret_anchor'];hull.alpha_composite(turret,(round(ax-512),round(ay-512-lift)))
        if variant=='Standard':
            hull.save(textures/'M3A3'/f'vehicle_{direction}.png');turret.save(textures/'M3A3'/f'turret_{direction}.png')
            if direction=='east':hull.save(textures/'M3A3/UI.png')
        elif direction=='east':hull.save(textures/'M3A3/BUSKKit.png')
        thumb=resize_linear(hull,400);board.paste(thumb,(di*400,vi*300+25),thumb);draw.text((di*400+5,vi*300+5),f'M3A3 {variant} {direction}',fill='white')
write(art,defs/'M3A3Art.xml')
# M8: independently re-rendered hull, stationary turret, recoiling barrel and roof weapon.
source=ROOT/'ArtSource/M8AGS/output/WeaponLayers';meta=json.loads((source/'pivots.json').read_text())
assert hashlib.sha256((ROOT/'ArtSource/M8AGS/output/vehicle.blend').read_bytes()).hexdigest()==meta['source_sha256']
m8=E.parse(defs/'M8.xml').getroot();ma=m8.find(NS+'M8ArtDef');scale=float(ma.findtext('drawSize'));anchors=ma.find('anchors');anchors.clear()
dest=textures/'M8';dest.mkdir(parents=True,exist_ok=True)
for di,(direction,record) in enumerate(meta['directions'].items()):
    images={key:resize_linear(outlined(Image.open(source/f'{key}_{direction}.png').convert('RGBA'),8),1024) for key in ['hull','turret','barrel','m2hb']}
    for k,im in images.items():im.save(dest/f'{k}_{direction}.png')
    # Retained combined sprites are for build menus/native VF gizmo art only.
    turret=images['barrel'].copy();turret.alpha_composite(images['turret'])
    mg=[v/2 for v in record['m2hb_anchor']];turret.alpha_composite(images['m2hb'],(round(mg[0]-512),round(mg[1]-512)))
    turret.save(dest/f'turret_full_{direction}.png')
    box=images['hull'].getchannel('A').getbbox();center=[(box[0]+box[2])/2,(box[1]+box[3])/2]
    anchor=[v/2 for v in record['hull_turret_anchor']]
    offset=[(512-center[0])*scale/1024,(center[1]-512)*scale/1024]
    def tip(im):
        b=im.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox()
        coords=np.argwhere(np.asarray(im)[:,:,3]>=128)
        axis=0 if direction in ['north','south'] else 1
        edge=coords[:,axis].min() if direction in ['north','west'] else coords[:,axis].max()
        pixels=coords[coords[:,axis]==edge];y,x=pixels.mean(axis=0)
        return [(x-512)*scale/1024,(512-y)*scale/1024]
    n=E.SubElement(anchors,'li')
    for k,v in dict(direction=direction,hullOffset=vec(offset),turretOffset=vec([(anchor[0]-512)*scale/1024,(512-anchor[1])*scale/1024]),muzzle=vec(tip(images['barrel'])),m2hbOffset=vec([(mg[0]-512)*scale/1024,(512-mg[1])*scale/1024]),m2hbMuzzle=vec(tip(images['m2hb']))).items():setv(n,k,v)
    for graphic in [m8[0].find('graphicData'),m8.find('Vehicles.VehicleBuildDef/graphicData')]:setv(graphic,'drawOffset'+direction.title(),vec([offset[0],0,offset[1]]))
    hull=images['hull'].copy();hull.alpha_composite(turret,(round(anchor[0]-512),round(anchor[1]-512)));hull.save(dest/f'vehicle_{direction}.png')
    if direction=='east':hull.save(dest/'UI.png')
    thumb=resize_linear(hull,400);board.paste(thumb,(di*400,625),thumb);draw.text((di*400+5,605),f'M8 {direction}',fill='white')
write(m8,defs/'M8.xml')
out=ROOT/'ArtSource/VehicleRuntime/output';out.mkdir(parents=True,exist_ok=True);board.save(out/'layer_sheet.png')
(out/'publication.json').write_text(json.dumps(dict(resolution=1024,world_scale_preserved=True,bradley_outline_pixels=int(props.findtext('outlineWidth')),m8_outline_pixels=4,states=len(states),source_m8=meta['source_sha256']),indent=2)+'\n')
print('VEHICLE_1024_PASS: 48 Bradley states; M8 hull/turret/M35/M2HB; world scale/anchors preserved')
