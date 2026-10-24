"""1900-1920 horizontal compound mill engine with a genuine sunken wheel pit."""
import os,sys,math
OUT=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,os.path.join(OUT,'../IndustrialMachines'))
import sprite_common as s
s.period_start();s.scene['design_period']='1900-1920 horizontal compound mill engine'
FLOOR=.22;WX=-.65;WY=.48;WZ=.70;R=1.04
# Opening X [-1.80,.50], Y [.24,.72]. No foundation slab crosses this slot.
s.cube('01_Left_floor',(-1.875,0,.11),(.15,2.80,.22),s.stone)
s.cube('01_Right_floor',(1.225,0,.11),(1.45,2.80,.22),s.stone)
s.cube('01_Front_floor',(-.65,-.58,.11),(2.30,1.64,.22),s.stone)
s.cube('01_Back_floor',(-.65,1.06,.11),(2.30,.68,.22),s.stone)
s.cube('02_Pit_floor',(-.65,WY,-.52),(2.30,.48,.06),s.pit)
for y in [.22,.74]:s.cube('03_Pit_inner_wall',(-.65,y,-.14),(2.30,.04,.72),s.dark)
for x in [-1.82,.52]:s.cube('03_Pit_end_wall',(x,WY,-.14),(.04,.56,.72),s.dark)
for y in [.215,.745]:s.cube('04_Recessed_slot_lip',(-.65,y,.233),(2.38,.05,.026),s.bed)
for x in [-1.825,.525]:s.cube('04_Recessed_slot_end',(x,WY,.233),(.05,.58,.026),s.bed)
s.wheel('05_Partly_buried_flywheel',(WX,WY,WZ),R,s.cast,spokes=10)
s.cyl('06_Low_crankshaft',(WX,.03,WZ),.075,1.70,s.steel,'Y')
for y in [-.65,.94]:
    s.cube('07_Bearing_plinth',(WX,y,.40),(.39,.34,.36),s.cast)
    s.cyl('08_Main_bearing',(WX,y,WZ),.13,.27,s.dark,'Y')
s.cube('09_Cylinder_cast_bed',(1.02,-.50,.34),(1.50,.88,.24),s.bed)
for x,r in [(.72,.28),(1.40,.35)]:
    s.cube('10_Cylinder_saddle',(x,-.50,.53),(.42,.57,.20),s.cast)
    s.cyl('11_Compound_cylinder',(x,-.50,.87),r,.56,s.cast,'X',large=True)
    for dx in [-.30,.30]:s.cyl('12_Cylinder_flange',(x+dx,-.50,.87),r+.028,.050,s.steel,'X')
    s.cube('13_Valve_chest',(x,-.50,.87+r+.065),(.38,.36,.13),s.cast)
    s.cyl('14_Valve_oiler',(x,-.50,.87+r+.17),.042,.08,s.brass)
s.cube('15_Crosshead_guide',(.10,-.50,.57),(.65,.27,.12),s.bed)
s.cube('16_Crosshead',(.04,-.50,.76),(.16,.18,.23),s.steel)
s.rod('17_Piston_rod',(.42,-.50,.87),(.04,-.50,.87),.030,s.steel)
s.rod('18_Connecting_rod',(.04,-.50,.87),(WX+.15,-.50,WZ+.13),.042,s.steel)
s.rod('19_Crank_web',(WX,-.50,WZ),(WX+.15,-.50,WZ+.13),.075,s.cast)
s.cube('20_Governor_stand',(1.06,-.02,.79),(.21,.22,1.14),s.cast)
s.governor('21_Belt_driven_governor',(1.06,-.02,1.36),.45)
s.path('22_Steam_supply',[(1.70,.45,.28),(1.70,.45,1.55),(1.40,.45,1.55),(1.40,-.32,1.55),(1.40,-.32,1.25)],.055,s.cast)
s.path('23_Copper_drain',[(1.42,-.87,.88),(1.42,-.87,.48),(.85,-.87,.48)],.030,s.copper)
s.rivets('24_Bed_fastener',[(x,y,.465) for x in [.40,1.60] for y in [-.83,-.17]],r=.023)
s.rivets('25_Bearing_fastener',[(WX+dx,y,.588) for dx in [-.13,.13] for y in [-.65,.94]],r=.023)
s.scene['framing_floor']=FLOOR
s.scene['pit_floor_level']=FLOOR;s.scene['wheel_bottom_level']=WZ-R
s.export(OUT,'HD_LargeSteamEngine',[4,3],['1900-1920 tandem compound cylinders','low crankshaft','flywheel bottom enters a real below-floor pit','dark recessed slot and concrete edges','brass governor','exposed crosshead'])
