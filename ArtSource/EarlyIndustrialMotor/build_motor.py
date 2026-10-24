"""Original c.1920-1940 smooth cast-frame electric motor."""
import os,sys,math
OUT=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,os.path.join(OUT,'../IndustrialMachines'))
import sprite_common as s
s.period_start();s.scene['design_period']='1920-1940 cast-frame electric motor'
s.cube('01_Cast_motor_skid',(0,0,.10),(1.50,1.44,.20),s.bed)
for x in [-1.075,1.075]:s.cube('01_Bearing_wing',(x,0,.10),(.65,.76,.20),s.bed)
for x in [-.43,.43]:
    s.cube('02_Integral_motor_foot',(x,0,.34),(.26,1.16,.28),s.cast)
    for y in [-.48,.48]:s.rivets('03_Foot_bolt',[(x,y,.49)],r=.025)
s.cyl('04_Smooth_cast_stator',(0,0,.91),.56,1.18,s.cast,'X',large=True)
for x in [-.40,.40]:s.ring('05_Cast_strengthening_band',(x,0,.91),.585,.548,.055,s.cast,'X')
s.cyl('06_Drive_end_shield',(-.66,0,.91),.615,.15,s.cast,'X',large=True)
# Four broad flat vent regions partition the right shield's end plane.
n=32;back=.59;front=.745;z0=.91;verts=[]
for x,r in [(back,.615),(front,.615),(front,.49),(front,.20)]:
    verts.extend([(x,r*math.cos(i*math.tau/n),z0+r*math.sin(i*math.tau/n)) for i in range(n)])
faces=[tuple(range(n-1,-1,-1)),tuple(range(3*n,4*n))];colors=[1,0]
for i in range(n):
    j=(i+1)%n
    faces.extend([(i,j,n+j,n+i),(n+i,n+j,2*n+j,2*n+i),(2*n+i,2*n+j,3*n+j,3*n+i)])
    colors.extend([1,0,2 if i%8<3 else 0])
mesh=s.bpy.data.meshes.new('Flat_cast_end_shield');mesh.from_pydata(verts,[],faces);mesh.update()
shield=s.bpy.data.objects.new('07_End_shield_with_flush_vents',mesh);s.scene.collection.objects.link(shield)
for m in [s.cast[0],s.cast[1],s.dark[1]]:mesh.materials.append(m)
for f,c in zip(mesh.polygons,colors):f.material_index=c
s.scene['motor_vent_plane_x']=front;s.scene['motor_vent_face_count']=12
s.cyl('08_Through_shaft',(0,0,.91),.070,2.68,s.steel,'X')
for x in [-1.22,1.22]:
    s.cube('09_Bearing_foot',(x,0,.28),(.30,.61,.16),s.cast)
    s.cube('10_Outboard_pedestal',(x,0,.58),(.21,.31,.46),s.cast)
    s.cyl('11_Split_bearing',(x,0,.91),.11,.25,s.steel,'X')
    s.cyl('12_Grease_cup',(x,0,1.06),.034,.085,s.brass)
s.cyl('13_Flat_belt_pulley',(-.96,0,.91),.36,.22,s.dark,'X')
for x in [-1.08,-.84]:s.ring('14_Pulley_flange',(x,0,.91),.38,.34,.025,s.steel,'X')
s.cube('15_Side_terminal_box',(0,-.53,1.075),(.42,.24,.26),s.cast)
s.cube('16_Terminal_box_lid',(0,-.53,1.222),(.46,.27,.035),s.bed)
ceramic=s.pal('Porcelain_terminal_',[(211,204,184)]*3,False)
for x in [-.115,.115]:
    s.cyl('17_Porcelain_insulator',(x,-.53,1.285),.042,.095,ceramic)
    s.cyl('18_Terminal_nut',(x,-.53,1.35),.024,.045,s.brass)
s.cube('19_Nameplate',(0,-.565,.91),(.28,.012,.12),s.dark)
s.rivets('20_Base_stud',[(x,y,.211) for x in [-1.24,1.24] for y in [-.30,.30]],r=.027)
# Broad casting ribs, service lid and lifting lug make the rounded casing readable.
for a in [math.pi/4,math.pi*3/4]:
    o=s.cube('21_Long_casting_rib',(0,.565*math.cos(a),.91+.565*math.sin(a)),(.72,.065,.045),s.cast)
    o.rotation_euler.x=a-math.pi/2
s.cube('22_Stator_service_border',(0,-.12,1.465),(.48,.47,.030),s.dark)
s.cube('23_Stator_service_lid',(0,-.12,1.49),(.39,.38,.020),s.steel)
s.rivets('24_Service_lid_stud',[(x,y,1.513) for x in [-.14,.14] for y in [-.24,.0]],r=.020)
s.ring('25_Lifting_eye',(0,.29,1.495),.105,.062,.060,s.cast,'X')
s.cube('26_Terminal_ceramic_bridge',(0,-.53,1.25),(.35,.16,.025),s.dark)
for x in [-1.22,1.22]:
    s.cube('27_Bearing_split_cap',(x,0,.99),(.28,.29,.075),s.cast)
    s.rivets('28_Cap_fastener',[(x,y,1.04) for y in [-.105,.105]],r=.022)
for x in [-.55,.55]:
    for a in [math.pi/4,math.pi*3/4]:
        s.cyl('29_End_shield_tie_bolt',(x,.46*math.cos(a),.91+.46*math.sin(a)),.027,.045,s.dark,'X')
s.export(OUT,'HD_EarlyIndustrialLargeMotor',[3,3],['1920-1940 smooth cast-frame motor','two broad casting bands','four large flush end-shield vents','outboard split bearings','flat-belt output pulley','small side terminal box with porcelain studs'])
