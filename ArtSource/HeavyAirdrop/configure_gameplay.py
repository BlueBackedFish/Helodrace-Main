"""Split reusable equipment, localize 30 t preparation and register animated cord anchors."""
from pathlib import Path
import argparse,copy,json,math
import xml.etree.ElementTree as E
from PIL import Image
ROOT=Path(__file__).resolve().parent
parser=argparse.ArgumentParser();parser.add_argument('--mod-root',type=Path,required=True);mod=parser.parse_args().mod_root.resolve();runtime=mod/'1.6'
def setv(p,tag,value):
    n=p.find(tag)
    if n is None:n=E.SubElement(p,tag)
    n.text=str(value);return n
def write(root,path):
    E.indent(root,space='  ');path.parent.mkdir(parents=True,exist_ok=True);E.ElementTree(root).write(path,encoding='utf-8',xml_declaration=True)
def vec(v):return '('+','.join(f'{float(x):.8f}' for x in v)+')'
def parse(s):return [float(x) for x in s.strip('()').split(',')]
path=runtime/'Defs/Vehicles/HeavyAirdrop.xml';defs=E.parse(path).getroot()
plane=next(n for n in defs if n.findtext('defName')=='HDA_C17')
flight=plane.find("modExtensions/li[@Class='Helodrace.Aircraft.AircraftDefExtension']")
setv(flight,'takeoffDistance',150);setv(flight,'landingDistance',150)
# Keep the vehicle and its construction placeholder consistent with the selected game balance.
bradley_path=runtime/'Defs/Vehicles/M3A3.xml';bradley=E.parse(bradley_path).getroot()
for mass in bradley.findall('.//vehicleStats/Mass')+bradley.findall('.//statBases/Mass'):mass.text='23000'
write(bradley,bradley_path)
template=next(n for n in defs if n.findtext('defName') in ['HDA_HELLVADS_Kit','HDA_HELLVADS_Pallet'])
recipe_template=next(n for n in defs if n.findtext('defName') in ['HDA_MakeHELLVADSKit','HDA_MakeHELLVADSPallet'])
for n in list(defs):
    if n.findtext('defName') in ['HDA_HELLVADS_Kit','HDA_HELLVADS_Pallet','HDA_HELLVADS_Parachutes','HDA_MakeHELLVADSKit','HDA_MakeHELLVADSPallet','HDA_MakeHELLVADSParachutes']:defs.remove(n)
for key,label,mass,value,work,ingredients in [
 ('Pallet','HELLVADS vehicle pallet',700,3500,10000,{'Steel':300,'Plasteel':100,'ComponentIndustrial':6}),
 ('Parachutes','HELLVADS cargo parachute pack',300,2000,8000,{'Cloth':600,'ComponentIndustrial':4})]:
    item=copy.deepcopy(template);defs.insert(1,item)
    for k,v in dict(defName='HDA_HELLVADS_'+key,label=label,description=('Reusable vehicle pallet, honeycomb cushioning and restraints.' if key=='Pallet' else 'Four reusable cargo parachutes with suspension lines.')+' Carry one pallet and one parachute pack in the vehicle to prepare a load of at most 30,000 kg. Prepared vehicles cannot be boarded.').items():setv(item,k,v)
    setv(item.find('statBases'),'Mass',mass);setv(item.find('statBases'),'MarketValue',value)
    setv(item.find('graphicData'),'texPath','Vehicles/HeavyDrop/'+key)
    recipe=copy.deepcopy(recipe_template);defs.insert(3,recipe)
    for k,v in dict(defName='HDA_MakeHELLVADS'+key,label='make '+label,description='Manufacture a reusable '+label+'.',jobString='Making '+label+'.',workAmount=work).items():setv(recipe,k,v)
    ing=recipe.find('ingredients');ing.clear();fixed=recipe.find('fixedIngredientFilter/thingDefs');fixed.clear();products=recipe.find('products');products.clear();E.SubElement(products,'HDA_HELLVADS_'+key).text='1'
    for resource,count in ingredients.items():
        li=E.SubElement(ing,'li');E.SubElement(E.SubElement(E.SubElement(li,'filter'),'thingDefs'),'li').text=resource;E.SubElement(li,'count').text=str(count);E.SubElement(fixed,'li').text=resource
    icon=Image.open(ROOT/f'output/m8/Textures/{"pallet" if key=="Pallet" else "canopy"}_east.png').convert('RGBA');icon.thumbnail((480,480),Image.Resampling.LANCZOS)
    canvas=Image.new('RGBA',(512,512));canvas.alpha_composite(icon,((512-icon.width)//2,(512-icon.height)//2))
    canvas.save(runtime/f'Textures/Vehicles/HeavyDrop/{key}.png')
    dest=ROOT/'output/Icons'/f'{key}.png';dest.parent.mkdir(exist_ok=True);canvas.save(dest)
write(defs,path)
# New equipment has no obsolete combined-kit definition or translations.
lang=runtime/'Languages/Korean (한국어)/DefInjected'
for kind in ['ThingDef','RecipeDef']:
    p=lang/kind/'Airborne.xml';r=E.parse(p).getroot()
    for n in list(r):
        if n.tag.startswith(('HDA_HELLVADS_Kit.','HDA_MakeHELLVADSKit.')):r.remove(n)
    for key,name,desc in [('Pallet','HELLVADS 차량 팔레트','팔레트, 허니컴 완충재와 고정끈입니다.'),('Parachutes','HELLVADS 화물 낙하산 묶음','낙하산 4개와 현수줄입니다.')]:
        if kind=='ThingDef':
            setv(r,'HDA_HELLVADS_'+key+'.label',name);setv(r,'HDA_HELLVADS_'+key+'.description',desc+' 차량 인벤토리에 팔레트와 낙하산 묶음을 하나씩 넣어 준비하십시오. 장비·연료·탄약·화물을 포함한 총중량 한도는 30,000kg입니다. 준비 중이거나 준비된 차량에는 탑승할 수 없습니다.')
        else:
            for suffix,text in [('label',name+' 제작'),('description',desc+' 재사용 가능한 강하 장비를 제작합니다.'),('jobString',name+' 제작 중.')]:setv(r,'HDA_MakeHELLVADS'+key+'.'+suffix,text)
    write(r,p)
english=dict(
 HDA_Drop_NeedEquipment='Place one HELLVADS vehicle pallet and one cargo parachute pack in the vehicle inventory first.',
 HDA_Drop_Overweight='The complete rigged load exceeds 30,000 kg.',
 HDA_Drop_RigDesc='Install one pallet and one parachute pack, stow weapons facing forward, then load the vehicle. Boarding, driving and firing are blocked during preparation and while rigged. Release the rig after landing to recover both reusable items.',
 HDA_Drop_LoadDesc='Load one nearby prepared Bradley or M8. Complete mass limit: 30,000 kg. Loading takes 5 seconds.',
 HDA_Drop_NoBoarding='This vehicle is being prepared for airdrop or is already rigged. Release the rig before boarding.',
 HDA_Drop_WeaponsStowed='Weapons and ammunition feeds are locked for airdrop preparation.',
 HDA_Drop_Preparing='Weapons are returning to their transport positions. Wait until preparation is complete.',
 HDA_Drop_PreparingMass='HELLVADS preparing: {0} / {1} kg')
korean=dict(
 HDA_Drop_NeedEquipment='차량 인벤토리에 HELLVADS 차량 팔레트와 화물 낙하산 묶음을 하나씩 넣으십시오.',
 HDA_Drop_Overweight='강하 준비를 마친 총중량이 30,000kg을 초과합니다.',
 HDA_Drop_RigDesc='팔레트와 낙하산 묶음을 하나씩 설치하고 무장을 전방 수송 자세로 정렬합니다. 준비 중이거나 준비된 차량은 탑승·이동·발사가 금지됩니다. 착륙 후 장비를 해제하면 두 장비가 인벤토리로 돌아옵니다.',
 HDA_Drop_LoadDesc='근처의 준비가 끝난 브래들리 또는 M8 한 대를 싣습니다. 총중량 한도는 30,000kg이며 적재에 5초가 걸립니다.',
 HDA_Drop_NoBoarding='강하 준비 중이거나 준비가 끝난 차량입니다. 장비를 해제한 뒤 탑승하십시오.',
 HDA_Drop_WeaponsStowed='강하 준비로 무장과 탄약 공급이 잠겼습니다.',
 HDA_Drop_Preparing='포와 기관총을 수송 자세로 정렬하는 중입니다. 준비가 끝날 때까지 기다리십시오.',
 HDA_Drop_PreparingMass='HELLVADS 준비 중: {0} / {1}kg')
for folder,values in [('English',english),('Korean (한국어)',korean)]:
    p=runtime/'Languages'/folder/'Keyed/HeavyAirdrop.xml';r=E.parse(p).getroot()
    for n in list(r):
        if n.tag=='HDA_Drop_NeedKit':r.remove(n)
    for k,v in values.items():setv(r,k,v)
    write(r,p)
# Derive exact attachment pixels from the four separate canopy silhouettes in each source render.
artpath=runtime/'Defs/Vehicles/HeavyDropArt.xml';art=E.parse(artpath).getroot();layers=art.findall('.//layers/li');records={}
for layer in layers:
    variant,direction,part=layer.findtext('key').split(':')
    if part!='canopy':continue
    previous=layer.find('clusters')
    if previous is not None:layer.remove(previous)
    clusters=E.SubElement(layer,'clusters')
    image=Image.open(runtime/'Textures'/(layer.findtext('path')+'.png')).convert('RGBA');w,h=image.size
    size=parse(layer.findtext('size'));offset=parse(layer.findtext('offset'))
    pallet=next(l for l in layers if l.findtext('key')==variant+':'+direction+':pallet');ps=parse(pallet.findtext('size'));po=parse(pallet.findtext('offset'))
    rows=[]
    for sy in [0,1]:
        for sx in [0,1]:
            box=(sx*w//2,sy*h//2,(sx+1)*w//2,(sy+1)*h//2);mask=image.crop(box).getchannel('A').point(lambda a:255 if a>=128 else 0);b=mask.getbbox();assert b
            x0,y0,x1,y1=b;x0+=box[0];x1+=box[0];y0+=box[1];y1+=box[1]
            cluster=E.SubElement(clusters,'li');anchor=(po[0]+(-1 if sx==0 else 1)*ps[0]*.43,po[1]+(1 if sy==0 else -1)*ps[1]*.43);setv(cluster,'baseAnchor',vec(anchor));rim=E.SubElement(cluster,'rim')
            points=[]
            for i in range(9):
                angle=math.pi*i/8;px=(x0+x1)/2+math.cos(angle)*(x1-x0)*.42;py=(y0+y1)/2+math.sin(angle)*(y1-y0)*.42
                point=(offset[0]+(px/w-.5)*size[0],offset[1]+(.5-py/h)*size[1]);E.SubElement(rim,'li').text=vec(point);points.append(point)
            rows.append(dict(base=anchor,rim=points))
    records[variant+':'+direction]=rows
write(art,artpath)
(ROOT/'output/cord_anchors.json').write_text(json.dumps(records,indent=2)+'\n')
print('AIRDROP_EQUIPMENT_PASS: separate 700 kg pallet + 300 kg parachutes; 30 t; boarding/preparation text; 12 x 4 cord clusters')
