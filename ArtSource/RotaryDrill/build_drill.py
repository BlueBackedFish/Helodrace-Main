"""Circa-1950 rotary-table land rig; no modern hydraulic mast or top drive."""
import os,sys
OUT=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,os.path.join(OUT,'../IndustrialMachines'))
import sprite_common as s
s.period_start();s.scene['design_period']='circa 1950 steel derrick rotary-table land drilling rig'
for y in [-1.62,1.62]:s.cube('01_Skid_long_rail',(0,y,.10),(3.75,.19,.20),s.bed)
for x in [-1.53,1.55]:s.cube('01_Skid_crossmember',(x,0,.10),(.20,3.60,.20),s.bed)
# The raised drilling floor leaves a genuine central table opening.
s.cube('02_Left_drill_deck',(-1.14,.35,.50),(.92,2.40,.12),s.bed)
s.cube('03_Right_drill_deck',(.34,.35,.50),(.92,2.40,.12),s.bed)
s.cube('04_Front_drill_deck',(-.40,-.39,.50),(.56,.92,.12),s.bed)
s.cube('05_Rear_drill_deck',(-.40,1.09,.50),(.56,.92,.12),s.bed)
for x in [-1.32,.52]:
    for y in [-.57,1.27]:s.cube('06_Deck_leg',(x,y,.32),(.24,.25,.48),s.cast)
s.cyl('07_Dark_well_below_table',(-.40,.35,.28),.27,.045,s.dark)
s.ring('08_Rotary_table',(-.40,.35,.66),.38,.25,.16,s.steel,'Z')
s.ring('09_Rotary_drive_housing',(-.40,.35,.59),.45,.27,.12,s.dark,'Z')
s.cube('10_Square_Kelly',(-.40,.35,1.87),(.085,.085,2.15),s.steel)
s.cyl('11_Drill_string',(-.40,.35,.54),.045,.56,s.steel)
# Four tapering legs, three large braced bays. No miniature lattice clutter.
levels=[.56,1.95,3.35,4.70];corners=[(-1,-1),(1,-1),(1,1),(-1,1)]
def corner(z,c):
    half=.92-(z-.56)*(.62/4.14)
    return (-.40+c[0]*half,.35+c[1]*half,z)
for c in corners:s.bar('12_Derrick_corner_leg',corner(.56,c),corner(4.70,c),.095,.095,s.cast)
for z in levels:
    for i in range(4):s.bar('13_Derrick_tier',corner(z,corners[i]),corner(z,corners[(i+1)%4]),.070,.070,s.steel)
for bay,(low,high) in enumerate(zip(levels,levels[1:])):
    for i in range(4):
        c0,c1=corners[i],corners[(i+1)%4]
        if bay%2:c0,c1=c1,c0
        s.bar('14_Large_diagonal_brace',corner(low,c0),corner(high,c1),.050,.055,s.cast)
s.cube('15_Crown_crossbeam',(-.40,.35,4.73),(.77,.22,.14),s.steel)
for x in [-.49,-.31]:s.cyl('16_Crown_sheave',(x,.35,4.84),.13,.105,s.dark,'X')
s.cube('17_Travelling_block',(-.40,.35,3.33),(.24,.21,.34),s.dark)
for x in [-.48,-.32]:s.rod('18_Hoisting_line',(x,.35,4.82),(x,.35,3.49),.027,s.rubber)
s.rod('19_Hook_link',(-.40,.35,3.17),(-.40,.35,3.02),.040,s.steel)
s.cube('20_Drilling_swivel',(-.40,.35,2.98),(.21,.23,.24),s.cast)
s.cube('21_Fingerboard',(-.40,-.22,3.35),(1.15,.26,.065),s.bed)
for x in [-.92,.12]:s.rod('22_Fingerboard_post',(x,-.32,3.35),(x,-.32,3.64),.027,s.dark)
s.rod('23_Fingerboard_rail',(-.92,-.32,3.64),(.12,-.32,3.64),.027,s.dark)
# Separate drawworks and engine, tied to the hoisting equipment.
s.cube('24_Drawworks_bed',(1.13,-.31,.25),(.92,1.19,.14),s.bed)
s.cube('25_Drawworks_frame',(1.13,-.31,.59),(.72,.91,.55),s.cast)
s.cyl('26_Winch_drum',(1.13,-.31,.96),.28,.77,s.dark,'Y')
for y in [-.72,.10]:s.cyl('27_Drum_cheek',(1.13,y,.96),.32,.06,s.steel,'Y')
s.rod('28_Winch_fast_line',(-.08,.35,4.78),(1.13,-.31,1.18),.028,s.rubber)
s.rod('29_Brake_lever',(1.52,-.58,.74),(1.52,-.78,1.23),.030,s.steel)
s.sphere('30_Brake_grip',(1.52,-.78,1.23),.058,s.rubber)
s.cube('31_Engine_bed',(1.10,.87,.25),(.75,.98,.14),s.bed)
s.cube('32_Engine_enclosure',(1.10,.87,.64),(.65,.89,.64),s.dark)
s.cube('33_Engine_top',(1.10,.87,.985),(.71,.93,.055),s.cast)
for y in [.61,.86,1.11]:s.cube('34_Engine_side_vent',(1.431,y,.69),(.012,.13,.24),s.bed)
s.cyl('35_Exhaust_silencer',(.91,1.18,1.22),.070,.45,s.dark)
s.rod('36_Engine_drive_shaft',(1.10,.46,.68),(1.10,.10,.68),.045,s.steel)
# Mud system is deliberately coarse: one tank, one pump, one standpipe and hose.
s.cube('37_Mud_tank',(.25,-1.20,.43),(1.63,.57,.50),s.bed)
s.cube('38_Mud_tank_open_surface',(.25,-1.20,.687),(1.45,.40,.018),s.dark)
for x in [-.38,.25,.88]:s.cube('39_Tank_cross_stiffener',(x,-1.20,.72),(.055,.58,.065),s.cast)
s.cube('40_Mud_pump_bed',(-1.10,-1.20,.27),(.72,.64,.14),s.bed)
s.cube('41_Reciprocating_mud_pump',(-1.10,-1.20,.58),(.56,.48,.47),s.cast)
s.cyl('42_Pump_cylinder',(-1.10,-1.47,.58),.12,.13,s.dark,'Y')
s.path('43_Mud_suction',[(-.57,-1.20,.45),(-.82,-1.20,.45)],.060,s.cast)
s.path('44_Mud_standpipe',[(-1.37,-1.20,.59),(-1.37,.52,.59),(-1.27,.52,3.31)],.050,s.steel)
s.path('45_Kelly_hose',[(-1.27,.52,3.31),(-1.02,.52,3.58),(-.66,.52,3.58),(-.40,.35,3.02)],.045,s.rubber)
for y in [.20,1.40]:s.cube('46_Pipe_rack_saddle',(1.63,y,.25),(.48,.14,.17),s.cast)
for x in [1.48,1.63,1.78]:s.cyl('47_Racked_drill_pipe',(x,.80,.39),.055,1.56,s.steel,'Y')
s.cube('48_Front_access_step',(-.40,-.97,.30),(.75,.29,.13),s.steel)
s.rivets('49_Skid_anchor',[(x,y,.215) for x in [-1.53,1.55] for y in [-1.62,1.62]],r=.033)
s.export(OUT,'HD_RotaryDrill',[4,4],['circa-1950 land rotary-table rig','four-leg steel derrick with three coarse braced bays','rotary table and square Kelly through a real floor opening','crown sheaves, travelling block and drawworks','separate power enclosure','mud tank, pump, standpipe and Kelly hose','three-pipe rack','proposed 4x4 footprint; artwork only, no gameplay definition'])
