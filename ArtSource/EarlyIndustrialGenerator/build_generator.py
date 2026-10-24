"""Original c.1900-1925 open-field belt-driven DC dynamo."""
import os,sys
OUT=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,os.path.join(OUT,'../IndustrialMachines'))
import sprite_common as s
s.period_start();s.scene['design_period']='1900-1925 open-field DC dynamo'
s.cube('01_Cast_dynamo_skid',(0,0,.10),(2.90,2.15,.20),s.bed)
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
s.rivets('23_Base_stud',[(x,y,.211) for x in [-1.25,1.25] for y in [-.91,.91]],r=.027)
s.export(OUT,'HD_EarlyIndustrialGenerator',[3,3],['1900-1925 open-field DC dynamo','arched magnetic yokes','two copper field windings','visible armature and commutator','carbon brushes and brass terminals','outboard bearing pedestals','flat-belt pulley'])
