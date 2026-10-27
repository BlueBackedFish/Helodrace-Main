"""A four-view computer desk with connected thick peripheral/power cables."""
from pathlib import Path
import bpy,sys,json,math
from mathutils import Vector
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output';OUT.mkdir(parents=True,exist_ok=True)
sys.path.insert(0,str(ROOT.parent/'PropAssets'));import modeling as m
scene=m.start()
wood=m.material('Desk wood top',(133,97,67));edge=m.material('Desk wood edge',(113,83,57));frame=m.material('Desk painted steel',(70,76,72))
black=m.material('Cable and rubber',(31,36,34));gray_cable=m.material('Heavy gray cable',(63,70,66));blue_cable=m.material('Heavy data cable',(55,72,77))
case=m.material('Computer graphite case',(62,68,64));top=m.material('Computer upper covers',(73,79,75));bezel=m.material('Screen bezel',(40,46,43));glass=m.material('Screen dark teal',(26,47,49));grid=m.material('Screen fixed graphics',(49,76,73));lit=m.material('Screen status graphics',(123,154,136));amber=m.material('Amber indicators',(166,130,65));key=m.material('Keycaps',(112,119,111));strip=m.material('Power strip',(111,114,105))
m.box('Desk tabletop',(0,0,.745),(1.9,.96,.09),wood,.018,side=edge)
for x in [-.84,.84]:
    for y in [-.35,.35]:
        m.box(f'Desk leg {x} {y}',(x,y,.365),(.085,.085,.70),frame,.009)
        m.box(f'Desk foot {x} {y}',(x,y,.025),(.13,.13,.05),black,.009)
m.box('Rear under-table brace',(0,.35,.26),(1.69,.055,.06),frame,.008)
m.box('Monitor foot',(-.22,.21,.81),(.32,.22,.035),bezel,.012);m.box('Monitor stem',(-.22,.23,.98),(.075,.07,.32),case,.012)
m.box('Monitor bezel',(-.22,.19,1.225),(.86,.09,.54),bezel,.012,side=case)
def screen_rect(name,x,z,w,h,mat,y=.142):return m.panel(name,[(x-w/2,y,z-h/2),(x+w/2,y,z-h/2),(x+w/2,y,z+h/2),(x-w/2,y,z+h/2)],mat)
screen_rect('Monitor display',-.22,1.225,.775,.455,glass,.142)
screen_rect('Monitor title bar',-.22,1.425,.75,.025,grid,.140)
for i in range(5):screen_rect('Screen map column '+str(i),-.54+i*.10,1.215,.006,.32,grid,.139)
for i in range(4):screen_rect('Screen map row '+str(i),-.34,1.075+i*.09,.46,.006,grid,.138)
for x,z in [(-.41,1.17),(-.23,1.31),(-.33,1.11)]:screen_rect('Screen marker',x,z,.026,.017,amber,.136)
for i in range(6):screen_rect('Screen status row '+str(i),.075,1.355-i*.055,.13 if i%2 else .17,.014,lit,.137)
m.cylinder('Monitor status light',(.13,.140,.989),(.13,.136,.989),.008,lit,16)
m.box('Desktop tower',(.63,.18,1.08),(.40,.47,.58),top,.018,side=case)
m.box('Desktop front face',(.63,-.060,1.08),(.35,.012,.53),bezel,.008)
m.box('Desktop drive slot',(.61,-.068,1.28),(.25,.007,.018),black)
for i in range(6):m.box('Desktop front grille '+str(i),(.52+i*.044,-.069,1.07),(.014,.008,.24),black)
m.cylinder('Desktop power button',(.75,-.071,1.30),(.75,-.079,1.30),.020,key,24)
m.cylinder('Desktop power LED',(.72,-.073,1.245),(.72,-.080,1.245),.007,lit,16)
for i in range(5):m.panel('Desktop side vent '+str(i),[(.834,.06+i*.052,.93),(.834,.076+i*.052,.93),(.834,.076+i*.052,1.22),(.834,.06+i*.052,1.22)],black)
for i in range(3):m.box('Rear cable plug '+str(i),(.55+i*.07,.424,.94+i*.08),(.055,.022,.035),black,.003)
m.box('Keyboard base',(-.26,-.28,.81),(.70,.25,.035),bezel,.012)
for row in range(4):
    for col in range(14):m.box(f'Keyboard key {row} {col}',(-.567+col*.047,-.205-row*.044,.834),(.037,.033,.012),key,.004)
m.box('Keyboard space bar',(-.30,-.382,.835),(.27,.027,.012),key,.004)
for x in [-.55,-.49,-.09,-.03,.03]:m.box('Keyboard bottom key',(x,-.382,.835),(.037,.027,.012),key,.004)
m.box('Mouse mat',(.30,-.28,.796),(.27,.30,.008),bezel,.014)
bpy.ops.mesh.primitive_uv_sphere_add(segments=32,ring_count=16,location=(.30,-.29,.835));mouse=bpy.context.object;mouse.name='Wired mouse';mouse.scale=(.05,.080,.033);mouse.data.materials.append(case);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
m.cable('Mouse split seam',[(.30,-.355,.853),(.30,-.30,.867),(.30,-.245,.858)],.0027,black)
m.cylinder('Mouse wheel',(.30,-.275,.869),(.30,-.247,.869),.008,key,16)
m.box('Rear power strip',(-.12,.422,.817),(.58,.08,.045),strip,.008)
for i in range(4):m.box('Power plug '+str(i),(-.32+i*.14,.42,.849),(.070,.052,.028),black,.007)
cords=[
 ('Thick monitor power cable',[(-.22,.25,1.07),(-.48,.31,.91),(-.79,.28,.827),(-.82,.04,.826),(-.66,-.055,.826),(-.55,.11,.826),(-.67,.32,.826),(-.33,.42,.865)],.022,black),
 ('Thick desktop power cable',[(.56,.43,.94),(.66,.47,.87),(.38,.48,.833),(.12,.46,.834),(-.17,.42,.865)],.021,gray_cable),
 ('Thick monitor data cable',[(-.09,.25,1.09),(-.35,.36,.89),(-.73,.38,.827),(-.91,.20,.827),(-.76,.065,.828),(-.58,.25,.828),(-.15,.34,.828),(.30,.36,.829),(.63,.43,1.02)],.018,blue_cable),
 ('Thick incoming power cable',[(-.04,.42,.865),(-.42,.45,.835),(-.79,.46,.829),(-.99,.44,.67),(-.995,.39,.30)],.024,black),
 ('Keyboard USB cable',[(-.33,-.15,.825),(-.40,-.035,.825),(-.50,.16,.829),(-.24,.36,.831),(.69,.43,1.1)],.0085,black),
 ('Mouse USB cable',[(.30,-.215,.84),(.27,-.09,.825),(.42,.015,.825),(.45,.31,.829),(.69,.43,1.10)],.007,black)]
for name,points,r,mat in cords:m.cable(name,points,r,mat)
cams={d:m.camera('Camera_'+d,a,64,3) for d,a in [('north',-90),('east',0),('south',90),('west',180)]}
for direction,cam in cams.items():m.render(OUT/(direction+'_raw.png'),cam,1024)
scene.camera=cams['north'];scene['presentation_rule']='one model unit per game tile';scene['footprint']=[2,1];scene['outline_pixels']=12
m.save(OUT/'computer_desk.blend')
meshes=[o for o in scene.objects if o.type=='MESH'];assert all(o.data.vertices and o.data.polygons for o in meshes)
assert len([o for o in meshes if o.name.startswith('Keyboard key')])==56
assert len([o for o in meshes if o.name.startswith('Thick ')])==4
metadata=dict(footprint=[2,1],native_resolution=512,master_resolution=1024,draw_size=[3,3],draw_offset=[0,0,0],outline_pixels=12,downward_camera_degrees=64,projection='orthographic',working_side='-Y; north image shows operating faces',meshes=len(meshes),features=['monitor','desktop tower','keyboard','wired mouse','four thick cables','peripheral cables','rear power strip','table'],mesh_names=[o.name for o in meshes],gameplay_definition_added=False)
(OUT/'metadata.json').write_text(json.dumps(metadata,indent=2)+'\n')
print('COMPUTER_DESK_MODEL_PASS',flush=True)
