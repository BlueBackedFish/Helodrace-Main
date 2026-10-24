"""Original c.1900-1925 open-field belt-driven DC dynamo."""
import os,sys,math
OUT=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,os.path.join(OUT,'../IndustrialMachines'))
import sprite_common as s
s.period_start();s.scene['design_period']='1900-1925 open-field DC dynamo'
s.cube('01_Cast_dynamo_skid',(0,0,.10),(1.32,1.84,.20),s.bed)
for x in [-1.045,1.045]:s.cube('01_Bearing_wing',(x,0,.10),(.77,.88,.20),s.bed)
for y in [-.63,.63]:
    s.cube('02_Field_foot',(0,y,.28),(1.28,.42,.16),s.cast)
    s.cube('03_Vertical_field_iron',(0,y,.65),(1.03,.18,.74),s.cast)
    s.cube('04_Copper_field_winding',(0,y,.67),(.86,.32,.44),s.copper)
    s.cube('05_Inward_pole_shoe',(0,y*.68,.95),(.81,.19,.28),s.dark)
    for x in [-.50,.50]:s.rivets('06_Field_foot_stud',[(x,y,.371)],r=.025)
for x in [-.49,.49]:s.arch('07_Arched_magnetic_yoke',(x,0,.94),.74,.54,.13,s.cast)
s.cyl('08_Exposed_armature',(0,0,.94),.335,.85,s.dark,'X')
for x in [-.40,.40]:s.cyl('09_Armature_end_ring',(x,0,.94),.345,.06,s.steel,'X')
s.cyl('10_Main_shaft',(0,0,.94),.067,2.72,s.steel,'X')
s.cyl('11_Copper_commutator',(.71,0,.94),.26,.26,s.copper,'X')
for x in [.605,.815]:s.ring('12_Commutator_edge',(x,0,.94),.265,.225,.025,s.brass,'X')
for y in [-.27,.27]:
    s.cube('13_Carbon_brush',(.72,y,.94),(.16,.10,.16),s.rubber)
    s.path('14_Brush_holder',[(.72,y,.94),(.72,y*1.70,1.10),(.43,y*2.15,1.10)],.027,s.brass)
for x in [-1.24,1.24]:
    s.cube('15_Pedestal_foot',(x,0,.28),(.31,.68,.16),s.cast)
    s.cube('16_Outboard_bearing_pedestal',(x,0,.58),(.22,.34,.49),s.cast)
    s.cyl('17_Split_bearing',(x,0,.94),.115,.25,s.steel,'X')
    s.cyl('18_Bearing_oiler',(x,0,1.09),.036,.09,s.brass)
s.cyl('19_Flat_belt_pulley',(-.91,0,.94),.40,.28,s.dark,'X')
for x in [-1.06,-.76]:s.ring('20_Pulley_flange',(x,0,.94),.415,.375,.025,s.steel,'X')
s.cube('21_Insulated_terminal_bridge',(.13,.64,1.13),(.60,.24,.10),s.rubber)
for x in [-.04,.30]:s.cyl('22_Brass_terminal',(x,.64,1.24),.045,.14,s.brass)
s.rivets('23_Base_stud',[(x,y,.211) for x in [-1.25,1.25] for y in [-.36,.36]],r=.027)
for y in [-.63,.63]:
    for x in [-.26,0,.26]:s.cube('24_Coil_binding_strip',(x,y,.67),(.030,.335,.46),s.brass)
    for x in [-.51,.51]:
        s.cube('25_Field_pole_foot_gusset',(x,y,.40),(.10,.30,.25),s.cast)
        s.rivets('26_Yoke_anchor',[(x,y,.98)],r=.026)
# Large longitudinal armature conductors, not a fine winding texture.
for i,a in enumerate([math.pi/4,math.pi/2,math.pi*3/4]):
    o=s.cube('27_Armature_copper_conductor',(0,.338*math.cos(a),.94+.338*math.sin(a)),(.70,.095,.028),s.copper)
    o.rotation_euler.x=a-math.pi/2
s.cube('28_Brush_carrier_crossbar',(.72,0,1.255),(.15,.74,.065),s.cast)
for y in [-.31,.31]:s.cube('29_Brush_spring_block',(.72,y,1.17),(.14,.12,.16),s.dark)
for x in [-1.24,1.24]:
    s.cube('30_Split_bearing_cap',(x,0,1.015),(.28,.30,.07),s.cast)
    s.rivets('31_Bearing_cap_stud',[(x,y,1.06) for y in [-.105,.105]],r=.021)
s.export(OUT,'HD_EarlyIndustrialGenerator',[3,3],['1900-1925 open-field DC dynamo','arched magnetic yokes','two copper field windings','visible armature and commutator','carbon brushes and brass terminals','outboard bearing pedestals','flat-belt pulley'])
