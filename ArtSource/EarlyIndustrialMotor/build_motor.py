"""New enclosed finned motor, independent of the open-field generator."""
import os,sys,math
OUT=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,os.path.join(OUT,'../IndustrialMachines'))
import sprite_common as s
s.start()
s.cube('01_Motor_bed',(0,0,.14),(2.75,2.60,.28),s.bed)
for x in [-.56,.56]:
    s.cube('02_Motor_saddle',(x,0,.46),(.25,1.25,.36),s.cast)
    for y in [-.55,.55]:s.rivets('03_Foot_bolt',[(x,y,.648)],r=.028)
s.cyl('04_Enclosed_stator',(0,0,1.04),.63,1.50,s.cast,'X',large=True)
for x in [-.82,.82]:s.cyl('05_End_shield',(x,0,1.04),.69,.14,s.steel,'X')
# Eight long ribs, not many miniature fins or a repeated texture pattern.
for i in range(8):
    a=i*math.tau/8
    rib=s.cube('06_Long_cooling_rib',(0,.646*math.cos(a),1.04+.646*math.sin(a)),(1.36,.12,.055),s.cast)
    rib.rotation_euler.x=a-math.pi/2
s.cube('07_Terminal_box',(0,.08,1.82),(.60,.51,.31),s.cast)
s.cube('08_Terminal_lid',(0,.08,1.995),(.65,.55,.065),s.steel)
s.rivets('09_Lid_fastener',[(x,y,2.035) for x in [-.23,.23] for y in [-.08,.24]],r=.020)
s.cyl('10_Cable_gland',(.10,.385,1.80),.066,.12,s.rubber,'Y')
s.cyl('11_Output_shaft',(-1.11,0,1.04),.095,.56,s.steel,'X')
s.cyl('12_Output_coupling',(-1.23,0,1.04),.19,.18,s.dark,'X')
# Fan cover is one solid cylinder whose end face is partitioned into flat vents.
n=32;cx=.97;z0=1.04;depth=.15;verts=[]
for x,r in [(cx-depth/2,.59),(cx+depth/2,.59),(cx+depth/2,.51),(cx+depth/2,.17)]:
    verts.extend([(x,r*math.cos(i*math.tau/n),z0+r*math.sin(i*math.tau/n)) for i in range(n)])
faces=[tuple(range(n-1,-1,-1)),tuple(range(3*n,4*n))];colors=[1,0]
for i in range(n):
    j=(i+1)%n
    faces.extend([(i,j,n+j,n+i),(n+i,n+j,2*n+j,2*n+i),(2*n+i,2*n+j,3*n+j,3*n+i)])
    colors.extend([1,0,2 if i%4<2 else 0])
mesh=s.bpy.data.meshes.new('Flat_vent_fan_cover');mesh.from_pydata(verts,[],faces);mesh.update()
fan=s.bpy.data.objects.new('13_Fan_cover_with_flush_vents',mesh);s.scene.collection.objects.link(fan)
for m in [s.steel[0],s.cast[1],s.dark[1]]:mesh.materials.append(m)
for f,c in zip(mesh.polygons,colors):f.material_index=c
s.cube('14_Side_identification_plate',(.05,-.645,1.13),(.43,.012,.16),s.dark)
s.rivets('15_Base_stud',[(x,y,.292) for x in [-1.16,1.16] for y in [-1.05,1.05]],r=.027)
s.export(OUT,'HD_EarlyIndustrialLargeMotor',[3,3],['enclosed stator','eight long cooling ribs','raised terminal box','flat-vent fan cover','shaft output coupling','two saddle feet'])
