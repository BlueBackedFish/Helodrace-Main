"""Package supersampled, cutout-ready sprites; keep the M2 barrel/body joint unoutlined."""
from pathlib import Path
import argparse,json,hashlib,sys,shutil,math,os,tempfile,io
import numpy as np
from PIL import Image,ImageDraw,ImageFont
from lxml import etree as E
ROOT=Path(__file__).resolve().parent;REPO=ROOT.parent.parent;OUT=ROOT/'output'
sys.path.insert(0,str(REPO/'ArtSource/VehiclePipeline'))
from images import resize_linear
parser=argparse.ArgumentParser();parser.add_argument('--apply',action='store_true');args=parser.parse_args()
RUNTIME=OUT/'Runtime';RUNTIME.mkdir(exist_ok=True)

def opened(path):
    with Image.open(path) as image:return image.convert('RGBA')
def atomic_write(path,data):
    with tempfile.NamedTemporaryFile(dir=path.parent,prefix=path.name+'.',suffix='.hmg-temp',delete=False) as temporary:
        temporary.write(data);name=temporary.name
    os.replace(name,path)

def save_png(image,path):
    buffer=io.BytesIO();image.save(buffer,format='PNG');atomic_write(path,buffer.getvalue())
grown_cache={}
def grown(image,radius):
    alpha=np.asarray(image.getchannel('A'))
    key=(hashlib.sha256(alpha.tobytes()).digest(),radius)
    if key not in grown_cache:
        padding=math.ceil(radius);padded=np.pad(alpha,padding);result=np.zeros_like(alpha)
        for dy in range(-padding,padding+1):
            for dx in range(-padding,padding+1):
                if dx*dx+dy*dy<=radius*radius:
                    np.maximum(result,padded[padding+dy:padding+dy+image.height,padding+dx:padding+dx+image.width],out=result)
        grown_cache[key]=result
    return grown_cache[key]
def outline(image,radius):
    back=Image.new('RGBA',image.size);back.putalpha(Image.fromarray(grown(image,radius)))
    return Image.alpha_composite(back,image)
def shared_outline(part,full,radius):
    p=np.asarray(part.getchannel('A'));a=np.asarray(full.getchannel('A'))
    stroke=(grown(full,radius).astype('int16')-a).clip(0,255).astype('uint8')
    stroke[grown(part,radius)==0]=0
    back=Image.new('RGBA',part.size);back.putalpha(Image.fromarray(stroke));return Image.alpha_composite(back,part)
def runtime(image):return resize_linear(image,1024)
def export(name,image):
    assert image.size==(1024,1024)
    alpha=image.getchannel('A');b=alpha.getbbox();assert b and min(b[0],b[1],1024-b[2],1024-b[3])>=8,(name,b)
    assert alpha.getextrema()==(0,255)
    save_png(image,RUNTIME/(name+'.png'));return b
def composite_at_world_scale(gun,base,gun_draw,base_draw):
    width=gun.width;world_size=max(gun_draw,base_draw);canvas=Image.new('RGBA',(width,width))
    for image,draw in [(base,base_draw),(gun,gun_draw)]:
        size=round(width*draw/world_size);layer=resize_linear(image,size);canvas.alpha_composite(layer,((width-size)//2,(width-size)//2))
    return canvas

metadata={};bounds={};preview=[];comparison=[]
for kind in ['M2HB','M1917']:
    folder=OUT/kind;meta=json.loads((folder/'metadata.json').read_text());metadata[kind]=meta
    radius=meta['outline_runtime_pixels']*2
    full=opened(folder/'gun_raw.png');stand_master=outline(opened(folder/'mount_raw.png'),radius);stand=runtime(stand_master)
    if kind=='M2HB':
        body_master=shared_outline(opened(folder/'body_raw.png'),full,radius);barrel_master=shared_outline(opened(folder/'barrel_raw.png'),full,radius)
        body=runtime(body_master);barrel=runtime(barrel_master);gun_master=Image.alpha_composite(barrel_master,body_master)
        gun=Image.alpha_composite(barrel,body)
        for name,img in [('HD_M2HB_Body',body),('HD_M2HB_Barrel',barrel)]:bounds[name]=export(name,img)
        # Check the recoil interface over the original 0.125 world-tile travel.
        recoil_px=round(.125/meta['gun_draw_tiles']*1024)
        shifted=Image.new('RGBA',gun.size);shifted.alpha_composite(barrel,(0,recoil_px));shifted=Image.alpha_composite(shifted,body)
        comparison.extend([(kind+' / REST',gun),(kind+' / RECOIL',shifted)])
        ui_name='HD_M2HB_UI'
    else:
        gun_master=outline(full,radius);gun=runtime(gun_master);bounds['HD_m1917HMG_Top']=export('HD_m1917HMG_Top',gun);ui_name='HD_m1917HMGUI'
    bounds['HD_'+kind+'_Tripod']=export('HD_'+kind+'_Tripod',stand)
    registered=composite_at_world_scale(gun_master,stand_master,meta['gun_draw_tiles'],meta['mount_draw_tiles'])
    crop=registered.crop(registered.getchannel('A').getbbox());side=math.ceil(max(crop.size)*1024/928)
    square=Image.new('RGBA',(side,side));square.alpha_composite(crop,((side-crop.width)//2,(side-crop.height)//2))
    ui=runtime(square);bounds[ui_name]=export(ui_name,ui)
    preview.append((kind,ui));comparison.append((kind+' / NEW',ui))

def sheet(entries,path,columns=2):
    cell=480;rows=(len(entries)+columns-1)//columns;canvas=Image.new('RGB',(cell*columns,rows*(cell+38)),(65,70,73));draw=ImageDraw.Draw(canvas)
    font_path=Path('C:/Windows/Fonts/arial.ttf');font=ImageFont.truetype(str(font_path),20)
    for i,(label,im) in enumerate(entries):
        x=(i%columns)*cell;y=(i//columns)*(cell+38);draw.text((x+15,y+12),label,font=font,fill='white')
        image=resize_linear(im,cell);canvas.paste(image,(x,y+38),image)
    save_png(canvas,path)
sheet(preview,OUT/'preview.png')
def previous(name):
    backup=ROOT/'Reference/Previous'/name
    return opened(backup if backup.exists() else REPO/'Textures/Buildings/Security'/name)
old_m2=previous('HD_M2HB_UI.png');old_17=previous('HD_m1917HMGUI.png')
sheet([('M2HB / ORIGINAL GAME',old_m2),('M2HB / EXT MODEL',preview[0][1]),('M1917 / ORIGINAL GAME',old_17),('M1917 / EXT MODEL',preview[1][1])],OUT/'before_after.png')
rejected=opened(ROOT/'Reference/RejectedProceduralPreview.png');revised=opened(OUT/'preview.png')
review=Image.new('RGBA',(960,1036));review.alpha_composite(rejected,(0,0));review.alpha_composite(revised,(0,518));save_png(review,OUT/'revision_comparison.png')
sheet(comparison[:2],OUT/'m2_recoil_review.png')
manifest=dict(passed=True,projection='90 degree top-down orthographic',master=2048,runtime=1024,outline='6px black circular alpha dilation; 2x supersampling; no internal barrel/body joint stroke',
    bounds=bounds,files={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(RUNTIME.glob('*.png'))},
    gun_draw_tiles={kind:meta['gun_draw_tiles'] for kind,meta in metadata.items()},
    mount_draw_tiles={kind:meta['mount_draw_tiles'] for kind,meta in metadata.items()},
    note='M2 barrel/body use the same registration and existing 0.125-tile recoil; tripod layers and UI composites are separate.')
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
if args.apply:
    target=REPO/'Textures/Buildings/Security'
    backup=ROOT/'Reference/Previous';backup.mkdir(exist_ok=True)
    for p in RUNTIME.glob('*.png'):
        old=target/p.name
        if p.name in ['HD_M2HB_Body.png','HD_M2HB_Barrel.png','HD_M2HB_UI.png','HD_m1917HMG_Top.png','HD_m1917HMGUI.png'] and old.exists() and not (backup/p.name).exists():shutil.copy2(old,backup/p.name)
        if not old.exists() or old.read_bytes()!=p.read_bytes():atomic_write(old,p.read_bytes())
    path=REPO/'Defs/GreatWar/Buildings/Defense_GreatWar.xml'
    if not (backup/path.name).exists():shutil.copy2(path,backup/path.name)
    tree=E.parse(str(path));root=tree.getroot()
    for kind,defname in [('M2HB','HD_Building_M2HB'),('M1917','HD_Building_M1917HMG')]:
        building=root.xpath("ThingDef[defName='%s']"%defname)[0]
        building.find('graphicData/texPath').text='Buildings/Security/HD_'+kind+'_Tripod'
        meta=metadata[kind]
        building.find('graphicData/drawSize').text=f"({meta['mount_draw_tiles']:.8f},{meta['mount_draw_tiles']:.8f})"
        if kind=='M2HB':
            comp=building.find("comps/li[@Class='Helodrace.CompProperties_ReciprocatingTurretBarrel']")
            comp.find('drawSize').text=f"{meta['gun_draw_tiles']*10:.8f}";comp.find('drawOffset').text='0'
        else:
            node=building.find('building/turretTopDrawSize')
            if node is None:
                container=building.find('building');container[-1].tail='\n      ';node=E.SubElement(container,'turretTopDrawSize');node.tail='\n    '
            node.text=f"{meta['gun_draw_tiles']:.8f}"
    content=b'<?xml version="1.0" encoding="utf-8"?>\n'+E.tostring(root,encoding='utf-8').rstrip()+b'\n'
    if path.read_bytes()!=content:atomic_write(path,content)
print('HMG_SPRITES_PASS: '+str(len(bounds))+' RGBA textures; registered mounts and separate M2 recoil layers'+('; applied to existing defs' if args.apply else '; preview only'))
