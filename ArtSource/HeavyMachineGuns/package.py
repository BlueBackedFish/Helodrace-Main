"""Package supersampled, cutout-ready sprites; keep the M2 barrel/body joint unoutlined."""
from pathlib import Path
import argparse,json,hashlib,sys,shutil,math,os,tempfile,io
import numpy as np
from PIL import Image,ImageDraw,ImageFont
from lxml import etree as E
ROOT=Path(__file__).resolve().parent;REPO=ROOT.parent.parent;OUT=ROOT/'output'
STYLE=json.loads((ROOT/'art_style.json').read_text())
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
def horizontal_max(alpha,radius):
    """Exact sliding maximum using block prefix/suffix scans, independent of radius."""
    if radius==0:return alpha
    height,width=alpha.shape;window=2*radius+1
    extra=(-width-2*radius)%window
    padded=np.pad(alpha,((0,0),(radius,radius+extra)))
    blocks=padded.reshape(height,-1,window)
    left=np.maximum.accumulate(blocks,axis=2).reshape(height,-1)
    right=np.maximum.accumulate(blocks[:,:,::-1],axis=2)[:,:,::-1].reshape(height,-1)
    return np.maximum(right[:,:width],left[:,window-1:window-1+width])

def grown(image,radius):
    alpha=np.asarray(image.getchannel('A'))
    key=(hashlib.sha256(alpha.tobytes()).digest(),radius)
    if key not in grown_cache:
        result=np.zeros_like(alpha);horizontal={}
        for dy in range(math.floor(radius)+1):
            span=math.floor(math.sqrt(max(0,radius*radius-dy*dy)))
            if span not in horizontal:horizontal[span]=horizontal_max(alpha,span)
            row=horizontal[span]
            if dy==0:np.maximum(result,row,out=result)
            else:
                np.maximum(result[dy:],row[:-dy],out=result[dy:])
                np.maximum(result[:-dy],row[dy:],out=result[:-dy])
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
def placed_at_world_scale(image,draw,world_size):
    width=image.width;size=round(width*draw/world_size);layer=resize_linear(image,size)
    canvas=Image.new('RGBA',image.size);canvas.alpha_composite(layer,((width-size)//2,(width-size)//2));return canvas

def composite_at_world_scale(gun,base,gun_draw,base_draw):
    world_size=max(gun_draw,base_draw)
    return Image.alpha_composite(placed_at_world_scale(base,base_draw,world_size),placed_at_world_scale(gun,gun_draw,world_size))

metadata={};bounds={};preview=[];comparison=[];world_views=[];stroke_pixels={}
for kind in ['M2HB','M1917']:
    folder=OUT/kind;meta=json.loads((folder/'metadata.json').read_text());metadata[kind]=meta
    radius=meta['outline_world_tiles']/meta['gun_draw_tiles']*meta['master']
    base_radius=meta['outline_world_tiles']/meta['mount_draw_tiles']*meta['master']
    full=opened(folder/'gun_raw.png');raw_base=opened(folder/'mount_raw.png');stand_master=outline(raw_base,base_radius);stand=runtime(stand_master)
    if kind=='M2HB':
        body_master=shared_outline(opened(folder/'body_raw.png'),full,radius);barrel_master=shared_outline(opened(folder/'barrel_raw.png'),full,radius)
        body=runtime(body_master);barrel=runtime(barrel_master);gun_master=Image.alpha_composite(barrel_master,body_master)
        gun=Image.alpha_composite(barrel,body)
        for name,img in [('HD_M2HB_Body',body),('HD_M2HB_Barrel',barrel)]:bounds[name]=export(name,img);stroke_pixels[name]=radius/2
        # Check the recoil interface over the original 0.125 world-tile travel.
        recoil_px=round(.125/meta['gun_draw_tiles']*1024)
        shifted=Image.new('RGBA',gun.size);shifted.alpha_composite(barrel,(0,recoil_px));shifted=Image.alpha_composite(shifted,body)
        comparison.extend([(kind+' / REST',gun),(kind+' / RECOIL',shifted)])
        ui_name='HD_M2HB_UI'
    else:
        gun_master=outline(full,radius);gun=runtime(gun_master)
        # Vanilla TurretTop adds ArtworkRotation=-90 degrees. Its input art
        # must face +X; previews and the M2 custom renderer use north-facing art.
        bounds['HD_m1917HMG_Top']=export('HD_m1917HMG_Top',gun.transpose(Image.Transpose.ROTATE_270))
        stroke_pixels['HD_m1917HMG_Top']=radius/2;ui_name='HD_m1917HMGUI'
    bounds['HD_'+kind+'_Tripod']=export('HD_'+kind+'_Tripod',stand);stroke_pixels['HD_'+kind+'_Tripod']=base_radius/2
    registered=composite_at_world_scale(gun_master,stand_master,meta['gun_draw_tiles'],meta['mount_draw_tiles'])
    world_views.append((kind,registered,max(meta['gun_draw_tiles'],meta['mount_draw_tiles'])))
    # Menu icons are independently fitted. Give both fitted layers the same
    # final-pixel outline instead of magnifying the world outline differently.
    world_size=max(meta['gun_draw_tiles'],meta['mount_draw_tiles'])
    ui_layers=[placed_at_world_scale(raw_base,meta['mount_draw_tiles'],world_size),placed_at_world_scale(full,meta['gun_draw_tiles'],world_size)]
    raw=Image.alpha_composite(*ui_layers);bbox=raw.getchannel('A').getbbox();crop=raw.crop(bbox);side=math.ceil(max(crop.size)*1024/928)
    icon=Image.new('RGBA',(2048,2048))
    for layer in ui_layers:
        square=Image.new('RGBA',(side,side));square.alpha_composite(layer.crop(bbox),((side-crop.width)//2,(side-crop.height)//2))
        icon=Image.alpha_composite(icon,outline(resize_linear(square,2048),meta['icon_outline_pixels']*2))
    ui=runtime(icon);bounds[ui_name]=export(ui_name,ui);stroke_pixels[ui_name]=meta['icon_outline_pixels']
    preview.append((kind,ui));comparison.append((kind+' / NEW',ui))

def sheet(entries,path,columns=2):
    cell=480;rows=(len(entries)+columns-1)//columns;canvas=Image.new('RGB',(cell*columns,rows*(cell+38)),(65,70,73));draw=ImageDraw.Draw(canvas)
    font_path=Path('C:/Windows/Fonts/arial.ttf');font=ImageFont.truetype(str(font_path),20)
    for i,(label,im) in enumerate(entries):
        x=(i%columns)*cell;y=(i//columns)*(cell+38);draw.text((x+15,y+12),label,font=font,fill='white')
        image=resize_linear(im,cell);canvas.paste(image,(x,y+38),image)
    save_png(canvas,path)
common_world_size=max(draw for _,_,draw in world_views)
world_preview=[]
for kind,registered,draw in world_views:
    size=round(registered.width*draw/common_world_size);layer=resize_linear(registered,size);canvas=Image.new('RGBA',registered.size)
    canvas.alpha_composite(layer,((canvas.width-size)//2,(canvas.height-size)//2));world_preview.append((kind+' / SAME WORLD SCALE',canvas))
sheet(world_preview,OUT/'preview.png')
sheet(world_preview,OUT/'fde_dark_preview.png')
sheet(preview,OUT/'icon_preview.png')
def previous(name):
    return opened(ROOT/'Reference/Previous'/name)
old_m2=previous('HD_M2HB_UI.png');old_17=previous('HD_m1917HMGUI.png')
sheet([('M2HB / ORIGINAL GAME',old_m2),('M2HB / EXT MODEL',preview[0][1]),('M1917 / ORIGINAL GAME',old_17),('M1917 / EXT MODEL',preview[1][1])],OUT/'before_after.png')
rejected=opened(ROOT/'Reference/RejectedProceduralPreview.png');revised=opened(OUT/'preview.png')
review=Image.new('RGBA',(960,1036));review.alpha_composite(rejected,(0,0));review.alpha_composite(revised,(0,518));save_png(review,OUT/'revision_comparison.png')
sheet(comparison[:2],OUT/'m2_recoil_review.png')
manifest=dict(passed=True,projection='90 degree top-down orthographic',master=2048,runtime=1024,outline='Uniform world-space black circular outline; independently uniform icon-pixel outline; 2x supersampling; no internal barrel/body joint stroke',
    texture_directory=STYLE['texture_directory'],outline_world_tiles=STYLE['outline_world_tiles'],icon_outline_pixels=STYLE['icon_outline_pixels'],outline_texture_pixels=stroke_pixels,
    bounds=bounds,files={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(RUNTIME.glob('*.png'))},
    gun_draw_tiles={kind:meta['gun_draw_tiles'] for kind,meta in metadata.items()},
    mount_draw_tiles={kind:meta['mount_draw_tiles'] for kind,meta in metadata.items()},
    visible_gun_length_tiles={kind:meta['model_length']*meta['model_to_world'] for kind,meta in metadata.items()},
    runtime_artwork_axes={'M2HB':'+Y / north; custom renderer','M1917':'+X / east; vanilla TurretTop ArtworkRotation=-90'},
    note='M2 barrel/body use the same registration and existing 0.125-tile recoil; tripod layers and UI composites are separate.')
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
if args.apply:
    target=REPO/'Textures'/STYLE['texture_directory'];target.mkdir(parents=True,exist_ok=True)
    backup=ROOT/'Reference/Previous';backup.mkdir(exist_ok=True)
    for p in RUNTIME.glob('*.png'):
        old=target/p.name
        if not old.exists() or old.read_bytes()!=p.read_bytes():atomic_write(old,p.read_bytes())
    path=REPO/'Defs/GreatWar/Buildings/Defense_GreatWar.xml'
    if not (backup/path.name).exists():shutil.copy2(path,backup/path.name)
    tree=E.parse(str(path));root=tree.getroot()
    for kind,defname in [('M2HB','HD_Building_M2HB'),('M1917','HD_Building_M1917HMG')]:
        building=root.xpath("ThingDef[defName='%s']"%defname)[0]
        building.find('graphicData/texPath').text=STYLE['texture_directory']+'/HD_'+kind+'_Tripod'
        building.find('uiIconPath').text=STYLE['texture_directory']+('/HD_M2HB_UI' if kind=='M2HB' else '/HD_m1917HMGUI')
        meta=metadata[kind]
        building.find('graphicData/drawSize').text=f"({meta['mount_draw_tiles']:.8f},{meta['mount_draw_tiles']:.8f})"
        if kind=='M2HB':
            comp=building.find("comps/li[@Class='Helodrace.CompProperties_ReciprocatingTurretBarrel']")
            comp.find('barrelTexPath').text=STYLE['texture_directory']+'/HD_M2HB_Barrel';comp.find('bodyTexPath').text=STYLE['texture_directory']+'/HD_M2HB_Body'
            comp.find('drawSize').text=f"{meta['gun_draw_tiles']*10:.8f}";comp.find('drawOffset').text='0'
        else:
            node=building.find('building/turretTopDrawSize')
            if node is None:
                container=building.find('building');container[-1].tail='\n      ';node=E.SubElement(container,'turretTopDrawSize');node.tail='\n    '
            node.text=f"{meta['gun_draw_tiles']:.8f}"
    content=b'<?xml version="1.0" encoding="utf-8"?>\n'+E.tostring(root,encoding='utf-8').rstrip()+b'\n'
    if path.read_bytes()!=content:atomic_write(path,content)
    path=REPO/'Defs/GreatWar/Items/Weapons_GreatWar.xml'
    if not (backup/path.name).exists():shutil.copy2(path,backup/path.name)
    tree=E.parse(str(path));root=tree.getroot();gun=root.xpath("ThingDef[defName='HD_Gun_M1917HMG_TurretGun']")[0]
    old_path=gun.findtext('graphicData/texPath');new_path=STYLE['texture_directory']+'/HD_m1917HMG_Top'
    content=path.read_bytes();needle=('<texPath>'+old_path+'</texPath>').encode('utf-8');assert content.count(needle)==1
    content=content.replace(needle,('<texPath>'+new_path+'</texPath>').encode('utf-8'),1)
    if path.read_bytes()!=content:atomic_write(path,content)
print('HMG_SPRITES_PASS: '+str(len(bounds))+' RGBA textures; registered mounts and separate M2 recoil layers'+('; applied to existing defs' if args.apply else '; preview only'))
