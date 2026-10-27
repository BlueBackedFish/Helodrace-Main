"""Use the installed native rotation and the exported pixels to check eight headings."""
from pathlib import Path
import subprocess,json,math,hashlib,io,os,tempfile
import numpy as np
from PIL import Image,ImageDraw,ImageFont
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output'
native_process=subprocess.run(['powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',str(ROOT/'Read-NativeTurretRotation.ps1')],capture_output=True,text=True)
if native_process.returncode:raise RuntimeError(native_process.stderr.strip())
native=json.loads(native_process.stdout)
assert native['artwork_rotation_degrees']==-90,native
manifest=json.loads((OUT/'manifest.json').read_text());meta=json.loads((OUT/'M1917/metadata.json').read_text())
path=OUT/'Runtime/HD_m1917HMG_Top.png'
with Image.open(path) as image:gun=image.convert('RGBA')
with Image.open(OUT/'Runtime/HD_M1917_Tripod.png') as image:stand=image.convert('RGBA')

def muzzle_heading(image):
    y,x=np.nonzero(np.asarray(image.getchannel('A'))>=128);dx=x-(image.width-1)/2;dy=y-(image.height-1)/2
    distance=np.hypot(dx,dy);tip=distance>=distance.max()-.75
    return math.degrees(math.atan2(float(dx[tip].mean()),float(-dy[tip].mean())))%360

assert abs(muzzle_heading(gun)-90)<1,'Runtime M1917 art must face east before the native -90 degree correction'
cell=320;label_height=32;canvas=Image.new('RGB',(cell*4,(cell+label_height)*2),(65,70,73));draw=ImageDraw.Draw(canvas)
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',18)
world_size=max(meta['gun_draw_tiles'],meta['mount_draw_tiles'])*1.12
results=[]
for index,(label,angle) in enumerate([('N',0),('NE',45),('E',90),('SE',135),('S',180),('SW',225),('W',270),('NW',315)]):
    rotated=gun.rotate(-(angle+native['artwork_rotation_degrees']),resample=Image.Resampling.BICUBIC)
    measured=muzzle_heading(rotated);error=abs((measured-angle+180)%360-180);assert error<1,(label,measured,error)
    results.append(dict(direction=label,aim_degrees=angle,measured_muzzle_degrees=measured,error_degrees=error))
    frame=Image.new('RGBA',(cell,cell))
    for layer,size_tiles in [(stand,meta['mount_draw_tiles']),(rotated,meta['gun_draw_tiles'])]:
        size=round(cell*size_tiles/world_size);scaled=layer.resize((size,size),Image.Resampling.LANCZOS);frame.alpha_composite(scaled,((cell-size)//2,(cell-size)//2))
    x=(index%4)*cell;y=(index//4)*(cell+label_height);draw.text((x+12,y+6),f'{label} / {angle} deg',font=font,fill='white');canvas.paste(frame,(x,y+label_height),frame)
    cx=x+cell/2;cy=y+label_height+cell/2;dx=math.sin(math.radians(angle));dy=-math.cos(math.radians(angle))
    draw.line((cx+dx*126,cy+dy*126,cx+dx*145,cy+dy*145),fill=(244,181,77),width=3)
    draw.line((cx+dx*145,cy+dy*145,cx+dx*136+dy*5,cy+dy*136-dx*5),fill=(244,181,77),width=3)
    draw.line((cx+dx*145,cy+dy*145,cx+dx*136-dy*5,cy+dy*136+dx*5),fill=(244,181,77),width=3)
buffer=io.BytesIO();canvas.save(buffer,format='PNG')
with tempfile.NamedTemporaryFile(dir=OUT,suffix='.png',delete=False) as temporary:temporary.write(buffer.getvalue());temporary_name=temporary.name
os.replace(temporary_name,OUT/'m1917_rotation_review.png')
result=dict(passed=True,native=native,runtime_top_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),runtime_artwork_muzzle_axis='+X / east',preview_artwork_muzzle_axis='+Y / north',headings=results,tripod_stays_fixed=True,in_game_render_test=False)
(OUT/'rotation_validation.json').write_text(json.dumps(result,indent=2)+'\n')
print('M1917_ROTATION_PASS: native ArtworkRotation=-90; exported pixel muzzle matches all eight aim headings; fixed tripod and original pivot')
