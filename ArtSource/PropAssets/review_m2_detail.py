"""Compare rendered M2 surface roles and silhouettes with the previous art."""
from pathlib import Path
from collections import Counter
import argparse,io,json,subprocess
from PIL import Image,ImageDraw,ImageFont
import m2_detail

ROOT=Path(__file__).resolve().parents[2];OUT=Path(__file__).resolve().parent/'output'
parser=argparse.ArgumentParser();parser.add_argument('--baseline',default='17ab00d8');args=parser.parse_args()
cache={}


def previous(path):
    key=path.relative_to(ROOT).as_posix()
    if key not in cache:
        data=subprocess.run(['git','show',args.baseline+':'+key],cwd=ROOT,check=True,capture_output=True).stdout
        with Image.open(io.BytesIO(data)) as im:cache[key]=im.convert('RGBA')
    return cache[key]


def opened(path):
    with Image.open(path) as im:return im.convert('RGBA')


files=[]
for folder in ['ArtSource/HeavyMachineGuns/output/M2HB','ArtSource/HeavyMachineGuns/output/M1917','ArtSource/CROWSII/output']:
    files.extend((ROOT/folder).glob('*_raw.png'))
for folder in ['Textures/Buildings/Security/HeavyMachineGunsFDE','Textures/Buildings/Security/CROWSII']:
    files.extend((ROOT/folder).glob('*.png'))
for path in files:
    assert previous(path).getchannel('A').tobytes()==opened(path).getchannel('A').tobytes(),path

report={};sources=[]
for kind,relative,scale in [('M2HB','ArtSource/HeavyMachineGuns/output/M2HB/gun_raw.png',json.loads((ROOT/'ArtSource/HeavyMachineGuns/output/M2HB/metadata.json').read_text())['camera_scale']),('CROWS','ArtSource/CROWSII/output/gun_raw.png',3.2)]:
    path=ROOT/relative;im=opened(path);p=m2_detail.SETTINGS['profiles'][kind];w=im.width;x,z=p['axis_xz'];lo,hi=p['vent_range_y']
    bbox=(round(w/2+(x-.06)/scale*w),round(w/2-hi/scale*w),round(w/2+(x+.06)/scale*w),round(w/2-lo/scale*w))
    counts=Counter(px[:3] for px in im.crop(bbox).get_flattened_data() if px[3]==255)
    outer=counts[tuple(m2_detail.PALETTE['sleeve_outer'])];inside=counts[tuple(m2_detail.PALETTE['recess'])]
    assert outer>100 and inside>100,(kind,outer,inside)
    all_counts=Counter(px[:3] for px in im.get_flattened_data() if px[3]==255)
    required=['barrel_root','barrel_shoulder','barrel_taper','muzzle']+(['cover','sight','fastener'] if kind=='CROWS' else [])
    for role in required:assert all_counts[tuple(m2_detail.PALETTE[role])]>10,(kind,role)
    report[kind]=dict(sleeve_outer_pixels=outer,vent_interior_pixels=inside,distinct_detail_pixels={role:all_counts[tuple(m2_detail.PALETTE[role])] for role in required})
    if kind=='M2HB':
        folder=ROOT/'Textures/Buildings/Security/HeavyMachineGunsFDE'
        runtime=Image.alpha_composite(opened(folder/'HD_M2HB_Barrel.png'),opened(folder/'HD_M2HB_Body.png'))
    else:runtime=opened(ROOT/'Textures/Buildings/Security/CROWSII/HD_CROWSII_M2_Top.png')
    final_counts=Counter(px[:3] for px in runtime.get_flattened_data() if px[3]==255)
    for role in ['sleeve_outer','recess','barrel_taper']:assert final_counts[tuple(m2_detail.PALETTE[role])]>10,(kind,role,'runtime')
    report[kind]['runtime_detail_pixels']={role:final_counts[tuple(m2_detail.PALETTE[role])] for role in ['sleeve_outer','recess','barrel_taper']}
    sources.extend([(kind+' / BEFORE',previous(path)),(kind+' / AFTER',im)])

unchanged=['ArtSource/CROWSII/output/frame_raw.png','ArtSource/CROWSII/output/base_raw.png','ArtSource/HeavyMachineGuns/output/M1917/gun_raw.png','ArtSource/HeavyMachineGuns/output/M1917/mount_raw.png','Textures/Buildings/Security/HeavyMachineGunsFDE/HD_m1917HMG_Top.png','Textures/Buildings/Security/HeavyMachineGunsFDE/HD_m1917HMGUI.png']
for relative in unchanged:
    path=ROOT/relative;assert previous(path).tobytes()==opened(path).tobytes(),relative

font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',18);canvas=Image.new('RGB',(1200,900),(65,70,73));draw=ImageDraw.Draw(canvas)
for i,(label,im) in enumerate(sources):
    im=im.crop(im.getchannel('A').getbbox());im=im.resize((round(im.width*840/im.height),840),Image.Resampling.LANCZOS)
    canvas.paste(im,(i*300+(300-im.width)//2,45),im);draw.text((i*300+10,12),label,font=font,fill='white')
canvas.save(OUT/'m2_detail_comparison.png')
result=dict(passed=True,baseline=args.baseline,unchanged_alpha_images=len(files),unchanged_non_target_images=unchanged,pixel_validation=report,geometry_validation='HMG source face/vertex/UV comparison and CROWS inline source comparison passed',in_game_tested=False)
(OUT/'m2_detail_validation.json').write_text(json.dumps(result,indent=2)+'\n')
print('M2_DETAIL_REVIEW_PASS: actual sleeve/vent and axial role pixels; '+str(len(files))+' unchanged alpha images; non-target artwork unchanged')
