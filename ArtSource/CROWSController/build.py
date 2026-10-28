"""Modular rugged display/control grip beside the existing desk keyboard."""
from pathlib import Path
import bpy,sys,json,math,hashlib
from mathutils import Vector
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';OUT.mkdir(parents=True,exist_ok=True)
sys.path.insert(0,str(ROOT.parent/'PropAssets'));import modeling as m;import surface_paint
scene=m.start();SOURCE=ROOT.parent/'ComputerDesk/output/computer_desk.blend'
with bpy.data.libraries.load(str(SOURCE),link=False) as (available,loaded):loaded.objects=list(available.objects)
original=[]
for o in loaded.objects:scene.collection.objects.link(o);original.append(o)
bpy.context.view_layer.update();before={o.name:[o.matrix_world@v.co for v in o.data.vertices] for o in original if o.type=='MESH'}
palette=surface_paint.palette();fde=m.material('Controller FDE shell',palette['equipment_top']);edge=m.material('Controller dark FDE',palette['mount_top']);light=m.material('Controller cover FDE',palette['lid_top']);rubber=m.material('Controller rubber',(31,36,34));case=m.material('Controller graphite grip',(62,68,64));steel=m.material('Controller metal',(101,108,101));screen=m.material('Controller display',(24,42,43));green=m.material('Controller screen marks',(112,150,125));amber=m.material('Controller amber',(172,132,62));red=m.material('Controller red switch',(145,65,49))
addon=[]
def tag(o,part='device'):o['addon_part']=part;addon.append(o);return o
def box(name,p,size,mat,bevel=0,side=None,part='device'):return tag(m.box('CROWS '+name,p,size,mat,bevel,side),part)
def cylinder(name,a,b,r,mat,segments=24,part='device'):return tag(m.cylinder('CROWS '+name,a,b,r,mat,segments),part)
box('control pedestal',(-.79,-.30,.817),(.25,.26,.045),edge,.012,side=edge)
box('control deck top',(-.79,-.30,.844),(.23,.24,.018),fde,.006)
for x in [-.886,-.694]:
    for y in [-.394,-.206]:
        cylinder('deck screw',(x,y,.854),(x,y,.862),.006,steel,16)
        cylinder('rubber desk foot',(x,y,.790),(x,y,.799),.013,rubber,24)
for i,r in enumerate([.045,.039,.032]):cylinder('rubber bellows '+str(i),(-.79,-.315,.853+i*.016),(-.79,-.315,.868+i*.016),r,rubber,32)
cylinder('grip stem',(-.79,-.315,.895),(-.79,-.329,.994),.018,case)
box('hand grip',(-.79,-.334,1.035),(.060,.072,.115),case,.014)
box('grip front trigger',(-.79,-.374,1.025),(.026,.012,.048),red,.005)
cylinder('thumb hat',(-.79,-.337,1.092),(-.79,-.337,1.106),.014,steel,8)
cylinder('thumb button',(-.767,-.315,1.085),(-.767,-.315,1.098),.009,amber,16)
for i,mat in enumerate([green,amber,red]):cylinder('mode button '+str(i),(-.702,-.354+i*.049,.854),(-.702,-.354+i*.049,.866),.008,mat,20)
display_start=len(addon)
box('display support foot',(-.79,-.184,.871),(.066,.060,.045),edge,.005)
cylinder('display angled support',(-.79,-.184,.890),(-.79,-.130,1.095),.017,case)
box('rugged display case',(-.79,-.130,1.170),(.27,.075,.25),fde,.009,side=edge)
for x in [-.915,-.665]:
    for z in [1.055,1.285]:box('display corner bumper',(x,-.13,z),(.028,.081,.025),rubber,.004)
def rect(name,x,z,w,h,mat,y=-.172):return tag(m.panel('CROWS '+name,[(x-w/2,y,z-h/2),(x+w/2,y,z-h/2),(x+w/2,y,z+h/2),(x-w/2,y,z+h/2)],mat))
rect('display screen',-.79,1.174,.208,.168,screen)
rect('crosshair horizontal',-.79,1.177,.062,.006,green,-.174)
rect('crosshair vertical',-.79,1.177,.006,.052,green,-.175)
for x in [-.842,-.738]:rect('target vertical',x,1.177,.005,.080,amber,-.175)
for z in [1.137,1.217]:rect('target horizontal',-.79,z,.109,.005,amber,-.176)
rect('display status',-.79,1.240,.13,.007,green,-.177)
for i in range(4):cylinder('display function key '+str(i),(-.868+i*.052,-.170,1.062),(-.868+i*.052,-.180,1.062),.007,rubber,20)
for z in [1.122,1.177,1.232]:cylinder('display side key',(-.910,-.170,z),(-.910,-.180,z),.006,steel,16)
for o in addon[display_start:]:o['controller_subpart']='display'
box('tower controller interface',(.76,.430,.990),(.050,.035,.060),case,.005,part='interface')
cylinder('tower controller plug',(.76,.441,.990),(.76,.457,.990),.012,rubber,20,part='interface')
tag(m.cable('CROWS controller data lead',[(-.705,-.183,.831),(-.699,-.06,.829),(-.58,.14,.829),(-.34,.33,.831),(.28,.39,.835),(.67,.46,.89),(.76,.457,.990)],.009,rubber),'cable')
lead=tag(m.cable('CROWS display local lead',[(-.854,-.126,1.098),(-.875,-.15,.982),(-.86,-.19,.899),(-.856,-.212,.851)],.007,rubber),'device');lead['controller_subpart']='display'
# Offset the small display toward the keyboard's rear-left corner. Its case
# clears the keyboard vertically and fits the original 3-tile camera in E/W.
for o in addon:
    if o.get('controller_subpart')=='display':o.location.x+=.09
module=bpy.data.objects.new('CROWS_CONTROLLER_MODULE',None);scene.collection.objects.link(module);module.location=(-.79,-.30,.79);bpy.context.view_layer.update()
for o in addon:
    world=o.matrix_world.copy();o.parent=module;o.matrix_parent_inverse=module.matrix_world.inverted();o.matrix_world=world
bpy.context.view_layer.update()
for o in original:
    if o.type=='MESH':
        for v,p in zip(o.data.vertices,before[o.name]):assert (o.matrix_world@v.co-p).length<1e-6,(o.name,v.index)
device=[o for o in addon if o['addon_part']=='device'];device_points=[o.matrix_world@v.co for o in device for v in o.data.vertices]
assert max(v.x for v in device_points)<-.54 and min(v.x for v in device_points)>-.95
pedestal=next(o for o in addon if o.name=='CROWS control pedestal');assert abs(min((pedestal.matrix_world@v.co).z for v in pedestal.data.vertices)-.7945)<1e-6
desk=[o for o in original if o.type=='MESH'];cameras={d:next(o for o in original if o.name=='Camera_'+d) for d in ['north','east','south','west']}
holdout=bpy.data.materials.new('Existing desk holdout');holdout.use_nodes=True;holdout.node_tree.nodes.clear();h=holdout.node_tree.nodes.new('ShaderNodeHoldout');output=holdout.node_tree.nodes.new('ShaderNodeOutputMaterial');holdout.node_tree.links.new(h.outputs[0],output.inputs['Surface'])
for direction,cam in cameras.items():
    for o in addon:o.hide_render=False
    m.render(OUT/(direction+'_full_raw.png'),cam,1024)
    backup={o:list(o.data.materials) for o in desk}
    for o,mats in backup.items():
        for i in range(len(mats)):o.data.materials[i]=holdout
    m.render(OUT/(direction+'_addon_raw.png'),cam,1024)
    for o,mats in backup.items():
        for i,mat in enumerate(mats):o.data.materials[i]=mat
for o in desk:o.hide_render=True
for o in addon:o.hide_render=o not in device
ui=m.camera('Controller_UI',-90,64,.75,target=(-.79,-.22,1.00));m.render(OUT/'controller_ui_raw.png',ui,1024)
for o in desk+addon:o.hide_render=False
scene.camera=cameras['north'];m.save(OUT/'controller_desk.blend')
metadata=dict(source_desk=str(SOURCE),source_desk_sha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest(),desk_meshes=len(before),controller_meshes=len(addon),controller_anchor=[-.79,-.30,.79],device_bounds=[list(map(min,zip(*[tuple(v) for v in device_points]))),list(map(max,zip(*[tuple(v) for v in device_points])))],source_desk_geometry_preserved=True,source_desk_materials_preserved=True,keyboard_mouse_and_cables_preserved=True,master_resolution=1024,native_resolution=512,draw_size=[3,3],footprint=[2,1],projection=64,outline_pixels=12,parts=[dict(name=o.name,part=o['addon_part']) for o in addon],reference='https://www.army.mil/article/181552/weapon_system_moves_reserve_gunners_to_the_safety_of_the_backseat',gameplay_definition_added=False)
(OUT/'metadata.json').write_text(json.dumps(metadata,indent=2)+'\n')
print('CROWS_CONTROLLER_MODEL_PASS: modular display, control grip and data cable; original desk geometry, keyboard/mouse and camera registration retained')
