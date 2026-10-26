"""Bind existing vehicle art to a game-only heavy drop kit, without editing sources."""
from pathlib import Path
import hashlib,json
from PIL import Image

HERE=Path(__file__).resolve().parent;ART=HERE.parent
variants={
    'm8':dict(path='M8AGS/output/vehicle.blend',kind='m8',hull_layer='hull'),
    'bradley':dict(path='M2A3Bradley/SurfaceDetails/output/Standard/vehicle.blend',kind='bradley'),
    'bradley_busk':dict(path='M2A3Bradley/SurfaceDetails/output/BUSK/vehicle.blend',kind='bradley')}
limits=[]
for d in ['north','east','south','west']:
    image=Image.open(ART/'M8AGS/output/Textures/Outlined/512'/f'hull_{d}.png')
    b=image.getchannel('A').getbbox();w,h=b[2]-b[0],b[3]-b[1]
    bounds=(2.9,4.9) if d in ['north','south'] else (4.9,2.9)
    limits.extend([bounds[0]*512/w,bounds[1]*512/h])
draw=min(limits)
m8_report=json.loads((ART/'M8AGS/output/report.json').read_text())
for v in variants.values():
    source=ART/v['path'];v['sha256']=hashlib.sha256(source.read_bytes()).hexdigest()
    v['world_scale']=draw/m8_report['render']['ortho_scale'] if v['kind']=='m8' else 6.19354839/8.699999809265137
config=dict(schema=1,game_only=True,m8_draw_size=draw,m8_footprint=[3,5],
    max_rigged_mass_kg=30000,pallet_mass_kg=700,parachutes_mass_kg=300,m8_base_mass_kg=25000,bradley_base_mass_kg=23000,
    master=4096,draw_size=18,pixels_per_tile=512/3,outline_pixels=12,
    directions=dict(north=0,east=90,south=180,west=270),variants=variants,
    pallet=dict(width=3.5,length=5.8,deck_height=0),
    canopy=dict(count=4,radius=1.75,rim_height=13.0,apex_height=14.2),
    note='HELLVADS-inspired game art; canopy count and load limits are game balance, not engineering rigging instructions',
    references=['https://sam.gov/opp/c845b356f1684854a91c7525295840f6/view',
        'https://www.ustranscom.mil/cmd/associated/rdte/files/40%20Ft%20Aerial%20Delivery%20Platform.pdf'])
(HERE/'settings.json').write_text(json.dumps(config,indent=2)+'\n',encoding='utf-8')
print('Configured four-direction heavy-drop art, M8 drawSize',draw)
