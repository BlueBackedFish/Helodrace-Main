"""Reframe one saved power model; preserve its geometry and painted materials."""
import bpy,os,sys,json,hashlib,re,xml.etree.ElementTree as ET
OUT=os.path.dirname(os.path.abspath(__file__));ROOT=os.path.abspath(os.path.join(OUT,'../..'))
sys.path.insert(0,OUT)
import sprite_common as s
assets={'SmallSteamEngine':('HD_SmallSteamEngine','small_steam_engine.blend'),
        'LargeSteamEngine':('HD_LargeSteamEngine','HD_LargeSteamEngine.blend'),
        'IndustrialDieselEngine':('HD_IndustrialDieselEngine','HD_IndustrialDieselEngine.blend'),
        'EarlyIndustrialGenerator':('HD_EarlyIndustrialGenerator','HD_EarlyIndustrialGenerator.blend'),
        'EarlyIndustrialMotor':('HD_EarlyIndustrialLargeMotor','HD_EarlyIndustrialLargeMotor.blend')}
name=sys.argv[sys.argv.index('--')+1];stem,blend=assets[name]
folder=os.path.join(ROOT,'ArtSource',name)
xml_path=os.path.join(ROOT,'Defs/WildWest/Buildings/Industry_WildWest.xml')
with open(xml_path,encoding='utf-8') as f:xml=f.read()
node=next(n for n in ET.fromstring(xml).findall('ThingDef') if n.findtext('defName')==stem)
footprint=[int(v) for v in node.findtext('size').strip('()').split(',')]
prior_draw=node.findtext('graphicData/drawSize');prior_offset=node.findtext('graphicData/drawOffset')
with open(os.path.join(folder,'validation.json')) as f:previous=json.load(f)
bpy.ops.wm.open_mainfile(filepath=os.path.join(folder,blend))
def model_hash():
    bpy.context.view_layer.update();data=[]
    for obj in sorted((o for o in bpy.context.scene.objects if o.type=='MESH'),key=lambda o:o.name):
        data.append([obj.name,[list(row) for row in obj.matrix_world],
                     [list(v.co) for v in obj.data.vertices],
                     [[list(p.vertices),p.material_index] for p in obj.data.polygons],
                     [[m.name,list(m.diffuse_color),m['tintable']] for m in obj.data.materials]])
    return hashlib.sha256(json.dumps(data,separators=(',',':')).encode()).hexdigest()
before=model_hash()
for direction in ['North','East','South']:
    camera=bpy.data.objects['Camera_'+direction];data=camera.data
    bpy.data.objects.remove(camera,do_unlink=True)
    if data.users==0:bpy.data.cameras.remove(data)
s.scene=bpy.context.scene;s.scene['outline_pixels']=12
s.scene['presentation_rule']='one_model_unit_per_game_tile'
s.export(folder,stem,footprint,previous['features'],blend_name=blend)
assert model_hash()==before,'Reframing changed model geometry or painted materials'
with open(os.path.join(folder,'validation.json')) as f:validation=json.load(f)
validation.update({'geometry_and_palette_unchanged':True,'model_geometry_palette_sha256':before,
                   'previous_draw_size':prior_draw,'previous_draw_offset':prior_offset})
with open(os.path.join(folder,'validation.json'),'w') as f:json.dump(validation,f,indent=2)
scale=validation['expected_draw_size'][0];value=f'({scale:g},{scale:g})'
def update(block):
    text=block.group()
    if '<defName>'+stem+'</defName>' not in text:return text
    text=re.sub(r'<drawSize>[^<]+</drawSize>','<drawSize>'+value+'</drawSize>',text,count=1)
    return re.sub(r'<drawOffset>[^<]+</drawOffset>','<drawOffset>(0,0,0)</drawOffset>',text,count=1)
with open(xml_path,encoding='utf-8') as f:current_xml=f.read()
revised=re.sub(r'<ThingDef\b[^>]*>.*?</ThingDef>',update,current_xml,flags=re.S)
with open(xml_path,'w',encoding='utf-8',newline='') as f:f.write(revised.replace('\r\n','\n').replace('\n','\r\n'))
readme=os.path.join(folder,'README.md')
with open(readme,encoding='utf-8') as f:doc=f.read()
doc=doc.replace('6px','12px')
doc=re.sub(r'\nTile calibration, 2026-10-26:.*(?:\n|$)','\n',doc)
doc+='\nTile calibration, 2026-10-26: orthographic scale and game drawSize are both '+value+'; the ground target and drawOffset are zero. One model unit is one game tile, following the radio 1.5 / 2×1 machine 3 presentation rule. The saved model was reframed without changing geometry or painted materials; the hash check is recorded in validation.json.\n'
with open(readme,'w',encoding='utf-8',newline='') as f:f.write(doc)
print(stem+': geometry/palette preserved; 12px outline, zero ground offset, drawSize '+value)
