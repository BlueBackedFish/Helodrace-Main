"""1900-1920 horizontal compound mill engine with a genuine sunken wheel pit."""
import os,sys,math
OUT=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,os.path.join(OUT,'../IndustrialMachines'))
import sprite_common as s
s.period_start();s.scene['design_period']='1900-1920 horizontal compound mill engine'
FLOOR=.22;WX=-.65;WY=.48;WZ=.70;R=1.04
# Opening X [-1.80,.50], Y [.24,.72]. No foundation slab crosses this slot.
s.cube('01_Left_floor',(-1.855,.575,.11),(.11,1.05,.22),s.stone)
s.cube('01_Right_floor',(1.19,-.06,.11),(1.38,1.84,.22),s.stone)
s.cube('01_Front_floor',(-.65,.145,.11),(2.30,.19,.22),s.stone)
s.cube('01_Front_bearing_tongue',(-.225,-.425,.11),(1.45,.95,.22),s.stone)
s.cube('01_Back_floor',(-.65,.91,.11),(2.30,.38,.22),s.stone)
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
# Connected guide bed, service pump and belt drive replace the former empty apron.
s.cube('26_Extended_cast_guide_bed',(-.08,-.50,.335),(1.21,.71,.23),s.bed)
for y in [-.70,-.30]:s.cube('27_Machined_guide_rail',(-.02,y,.55),(1.05,.075,.10),s.steel)
s.cube('28_Sliding_crosshead',(.04,-.50,.64),(.22,.31,.10),s.dark)
s.cube('29_Pump_plinth',(1.03,.43,.32),(.64,.61,.20),s.bed)
s.cyl('30_Condensate_feed_pump',(1.03,.43,.64),.23,.52,s.cast,large=True)
s.cyl('31_Pump_cover',(1.03,.43,.925),.25,.06,s.steel)
s.cyl('32_Pump_oiler',(1.03,.43,1.01),.055,.10,s.brass)
s.path('33_Pump_delivery',[(1.03,.43,.68),(1.47,.43,.68),(1.47,-.23,.68),(1.47,-.23,1.05)],.038,s.copper)
s.belt_drive('34_Governor_drive',(-.65,-.04,.70),(1.06,-.04,1.08),.13,.11)
s.cyl('35_Rear_power_pulley',(WX,1.035,WZ),.38,.15,s.dark,'Y')
for y in [.95,1.12]:s.ring('36_Power_pulley_flange',(WX,y,WZ),.395,.35,.025,s.steel,'Y')
for x,r in [(.72,.28),(1.40,.35)]:
    s.cube('37_Valve_chest_lid_border',(x,-.50,.87+r+.142),(.32,.29,.025),s.dark)
    s.cube('38_Valve_chest_lid',(x,-.50,.87+r+.161),(.25,.22,.018),s.steel)
    s.rivets('39_Lid_fastener',[(x+dx,-.50,.87+r+.18) for dx in [-.085,.085]],r=.018)
s.path('40_Header_gauge_takeoff',[(1.70,.45,1.55),(1.70,.17,1.55)],.028,s.brass)
s.gauge('41_Steam_header_dial',(1.70,.17,1.62),.095)
for x in [-1.63,-1.36,-1.09,-.82,-.55]:s.cube('42_Rear_bearing_bed_rib',(x,.92,.242),(.06,.23,.044),s.bed)
s.scene['framing_floor']=FLOOR
s.scene['pit_floor_level']=FLOOR;s.scene['wheel_bottom_level']=WZ-R
s.export(OUT,'HD_LargeSteamEngine',[4,3],['1900-1920 tandem compound cylinders','low crankshaft','flywheel bottom enters a real below-floor pit','dark recessed slot and concrete edges','brass governor','exposed crosshead'])
