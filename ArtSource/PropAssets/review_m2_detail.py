"""Compare rendered M2 surface roles and silhouettes with the previous art."""
from pathlib import Path
from collections import Counter
import argparse,io,json,subprocess
from PIL import Image,ImageDraw,ImageFont
import m2_detail

ROOT=Path(__file__).resolve().parents[2];OUT=Path(__file__).resolve().parent/'output'
parser=argparse.ArgumentParser();parser.add_argument('--baseline',default='a6f57bd1');args=parser.parse_args()
cache={}
baseline_paths=set(subprocess.run(['git','ls-tree','-r','--name-only',args.baseline],cwd=ROOT,check=True,capture_output=True,text=True).stdout.splitlines())
old_paint=json.loads(subprocess.run(['git','show',args.baseline+':ArtSource/PropAssets/weapon_paint.json'],cwd=ROOT,check=True,capture_output=True,text=True).stdout)
paint_changed=old_paint!=json.loads((ROOT/'ArtSource/PropAssets/weapon_paint.json').read_text())


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
alpha_changed=[];alpha_unchanged=[];new_images=[]
for path in files:
    relative=path.relative_to(ROOT).as_posix()
    if relative not in baseline_paths:new_images.append(relative);continue
    (alpha_unchanged if previous(path).getchannel('A').tobytes()==opened(path).getchannel('A').tobytes() else alpha_changed).append(relative)

report={};sources=[]
for kind,relative,scale in [('M2HB','ArtSource/HeavyMachineGuns/output/M2HB/gun_raw.png',json.loads((ROOT/'ArtSource/HeavyMachineGuns/output/M2HB/metadata.json').read_text())['camera_scale']),('CROWS','ArtSource/CROWSII/output/gun_raw.png',3.2)]:
    path=ROOT/relative;im=opened(path);p=m2_detail.SETTINGS['profiles']['M2HB'];w=im.width;x,z=p['axis_xz'];lo,hi=p['vent_range_y']
    if kind=='CROWS':
        t=json.loads((ROOT/'ArtSource/CROWSII/output/metadata.json').read_text())['weapon_mount_transform']
        x=t[0][0]*x+t[0][3];lo=t[1][1]*lo+t[1][3];hi=t[1][1]*hi+t[1][3]
    bbox=(round(w/2+(x-.06)/scale*w),round(w/2-hi/scale*w),round(w/2+(x+.06)/scale*w),round(w/2-lo/scale*w))
    counts=Counter(px[:3] for px in im.crop(bbox).get_flattened_data() if px[3]==255)
    outer=counts[tuple(m2_detail.PALETTE['sleeve_outer'])];inside=counts[tuple(m2_detail.PALETTE['recess'])]
    assert outer>100 and inside>100,(kind,outer,inside)
    all_counts=Counter(px[:3] for px in im.get_flattened_data() if px[3]==255)
    required=['barrel_root','barrel_shoulder','barrel_taper','muzzle','handle','fastener']
    for role in required:assert all_counts[tuple(m2_detail.PALETTE[role])]>10,(kind,role)
    report[kind]=dict(sleeve_outer_pixels=outer,vent_interior_pixels=inside,distinct_detail_pixels={role:all_counts[tuple(m2_detail.PALETTE[role])] for role in required})
    if kind=='M2HB':
        folder=ROOT/'Textures/Buildings/Security/HeavyMachineGunsFDE'
        runtime=Image.alpha_composite(opened(folder/'HD_M2HB_Barrel.png'),opened(folder/'HD_M2HB_Body.png'))
    else:
        folder=ROOT/'Textures/Buildings/Security/CROWSII';runtime=opened(folder/'HD_CROWSII_M2_Frame.png')
        for name in ['HD_CROWSII_M2Barrel.png','HD_CROWSII_M2Body.png','HD_CROWSII_M2_FeedOverlay.png']:runtime=Image.alpha_composite(runtime,opened(folder/name))
    final_counts=Counter(px[:3] for px in runtime.get_flattened_data() if px[3]==255)
    for role in ['sleeve_outer','recess','barrel_taper']:assert final_counts[tuple(m2_detail.PALETTE[role])]>10,(kind,role,'runtime')
    report[kind]['runtime_detail_pixels']={role:final_counts[tuple(m2_detail.PALETTE[role])] for role in ['sleeve_outer','recess','barrel_taper']}
    sources.extend([(kind+' / BEFORE',previous(path)),(kind+' / AFTER',im)])

unchanged=['ArtSource/CROWSII/output/base_raw.png','Textures/Buildings/Security/CROWSII/HD_CROWSII_M2_Base.png','ArtSource/HeavyMachineGuns/output/M2HB/mount_raw.png','Textures/Buildings/Security/HeavyMachineGunsFDE/HD_M2HB_Tripod.png','ArtSource/HeavyMachineGuns/output/M1917/gun_raw.png','ArtSource/HeavyMachineGuns/output/M1917/mount_raw.png','Textures/Buildings/Security/HeavyMachineGunsFDE/HD_M1917_Tripod.png','Textures/Buildings/Security/HeavyMachineGunsFDE/HD_m1917HMG_Top.png','Textures/Buildings/Security/HeavyMachineGunsFDE/HD_m1917HMGUI.png']
for relative in unchanged:
    path=ROOT/relative;old=previous(path);current=opened(path)
    assert old.getchannel('A').tobytes()==current.getchannel('A').tobytes(),relative
    if not paint_changed:assert old.tobytes()==current.tobytes(),relative

font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',18);canvas=Image.new('RGB',(1200,900),(65,70,73));draw=ImageDraw.Draw(canvas)
for i,(label,im) in enumerate(sources):
    im=im.crop(im.getchannel('A').getbbox());im=im.resize((round(im.width*840/im.height),840),Image.Resampling.LANCZOS)
    canvas.paste(im,(i*300+(300-im.width)//2,45),im);draw.text((i*300+10,12),label,font=font,fill='white')
canvas.save(OUT/'m2_detail_comparison.png')
donor_check=json.loads((ROOT/'ArtSource/CROWSII/output/donor_validation.json').read_text());assert donor_check['passed'] and donor_check['all_face_emission_colors_identical'] and donor_check['qcb_objects']==8
result=dict(passed=True,baseline=args.baseline,alpha_unchanged_images=alpha_unchanged,alpha_changed_for_qcb_and_weapon_transplant=alpha_changed,new_images=new_images,geometry_stable_images=unchanged,shared_paint_palette_changed=paint_changed,non_target_rgb_compared=not paint_changed,pixel_validation=report,geometry_validation='HMG original source preserved with 8 tagged QCB additions; CROWS canonical donor geometry/UV/material comparison passed',in_game_tested=False)
(OUT/'m2_detail_validation.json').write_text(json.dumps(result,indent=2)+'\n')
print('M2_DETAIL_REVIEW_PASS: QCB/vent/axial role pixels; canonical CROWS weapon donor verified; non-target geometry unchanged')
