"""Timber walking-beam oil pump, with restrained unpainted iron mechanisms."""
import os,sys
OUT=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,os.path.join(OUT,'../IndustrialMachines'))
import sprite_common as s
s.period_start();s.scene['design_period']='early timber walking-beam oilfield pumping unit'
wood=s.pal('Vanilla_timber_',[(133,97,67),(113,83,57),(96,70,48)],False)
endgrain=s.pal('Timber_end_',[(153,116,80),(130,96,65),(113,83,57)],False)
iron=s.pal('Untinted_iron_', [157,133,119],False)
polished=s.pal('Polished_rod_', [225,202,187],False)
black=s.pal('Drive_belt_', [55,51,46],False)
for y in [-.83,.83]:s.cube('01_Long_timber_sill',(0,y,.12),(5.65,.26,.24),wood)
for x in [-2.35,-.25,1.73,2.55]:s.cube('01_Timber_cross_sleeper',(x,0,.12),(.26,1.94,.24),wood)
# Two trestles with broad bracing, rather than a painted metal A-frame.
for y in [-.40,.40]:
    for x in [-.68,.65]:s.bar('02_Sampson_trestle_leg',(x,y,.24),(.0,y,2.30),.25,.24,wood)
    s.bar('03_Trestle_cross_brace',(-.65,y,.40),(.40,y,1.72),.13,.14,endgrain)
    s.cube('04_Trestle_tie',(0,y,.64),(1.20,.20,.18),wood)
s.cube('05_Pivot_transom',(0,0,2.28),(.55,1.18,.25),endgrain)
s.cyl('06_Iron_fulcrum',(0,0,2.48),.13,1.15,iron,'Y')
s.cube('07_Timber_walking_beam',(-.10,0,2.54),(4.55,.30,.36),wood)
for x in [-2.25,0,2.05]:
    s.cube('08_Beam_iron_strap',(x,0,2.54),(.13,.33,.38),iron)
s.cube('09_Pump_end_timber_head',(-2.30,0,2.35),(.25,.37,.57),endgrain)
s.cyl('10_Hanger_pin',(-2.30,0,2.56),.075,.49,polished,'Y')
s.rod('11_Polished_pump_rod',(-2.30,-.20,2.55),(-2.30,-.20,.49),.045,polished)
s.cube('12_Wellhead_foot',(-2.30,-.20,.31),(.58,.60,.14),iron)
s.cyl('13_Stuffing_box',(-2.30,-.20,.53),.14,.30,iron)
s.cyl('14_Gland_cap',(-2.30,-.20,.70),.17,.055,polished)
s.cube('15_Crank_pedestal',(1.55,0,.53),(.49,.67,.58),wood)
s.cyl('16_Crank_axle',(1.55,0,.94),.09,.91,polished,'Y')
# Six broad wooden wheel spokes and iron tire remain readable at sprite scale.
s.ring('17_Crank_wheel_tire',(1.55,-.29,.94),.60,.51,.17,iron,'Y')
s.cyl('18_Crank_wheel_hub',(1.55,-.29,.94),.13,.23,iron,'Y')
for dx,dz in [(.52,0),(-.52,0),(0,.52),(0,-.52),(.37,.37),(-.37,-.37)]:
    s.bar('19_Wooden_wheel_spoke',(1.55,-.29,.94),(1.55+dx,-.29,.94+dz),.085,.10,endgrain)
s.cyl('20_Crank_pin',(1.22,-.41,1.30),.07,.14,polished,'Y')
s.bar('21_Wooden_pitman',(1.22,-.44,1.30),(2.05,-.44,2.50),.14,.12,endgrain)
s.rod('22_Beam_pitman_pin',(2.05,-.44,2.50),(2.05,0,2.50),.055,polished)
s.cube('23_Line_shaft_bearing_stand',(2.51,.36,.49),(.32,.40,.50),wood)
s.cyl('24_Input_pulley',(2.51,.36,.85),.24,.15,iron,'Y')
s.cyl('25_Wheel_drive_pulley',(1.55,.36,.94),.36,.15,iron,'Y')
for dz in [-.23,.23]:s.rod('26_Transmission_belt',(2.51,.36,.85+dz),(1.55,.36,.94+dz*1.5),.025,black)
s.cyl('27_Timber_receiving_barrel',(-1.95,.65,.65),.31,.82,wood)
for z in [.35,.86]:s.ring('28_Barrel_iron_hoop',(-1.95,.65,z),.32,.297,.050,iron,'Z')
s.cyl('29_Barrel_lid',(-1.95,.65,1.08),.32,.045,endgrain)
s.path('30_Wellhead_outlet',[(-2.30,-.20,.48),(-2.65,-.20,.48),(-2.65,.65,.48),(-2.20,.65,.80)],.050,iron)
for y in [-.41,.41]:s.cube('31_Joinery_plate',(0,y,2.15),(.36,.035,.30),iron)
# Wood and iron keep their own baked colors; wood stuff must not brown the ironwork.
s.export(OUT,'HD_BasicPumpJack',[6,3],['timber sills and braced Sampson trestles','thick wooden walking beam and pitman','wooden spoked wheel with iron tire','external line-shaft pulley drive','polished rod and stuffing box','small wooden receiving barrel','baked vanilla wood and untinted iron'])
