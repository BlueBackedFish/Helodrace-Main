"""New open-field dynamo design, independent of the enclosed motor."""
import os,sys
OUT=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,os.path.join(OUT,'../IndustrialMachines'))
import sprite_common as s
s.start()
s.cube('01_Dynamo_bed',(0,0,.14),(2.80,2.60,.28),s.bed)
for x in [-.96,.96]:
    s.cube('02_Shaft_bearing_pedestal',(x,0,.62),(.27,.45,.68),s.cast)
    s.cyl('03_Shaft_bearing',(x,0,1.02),.155,.24,s.steel,'X')
s.cyl('04_Armature_shaft',(0,0,1.02),.085,2.50,s.steel,'X')
s.cyl('05_Exposed_armature',(-.08,0,1.02),.35,1.10,s.dark,'X',large=True)
for x in [-.47,-.26,-.05,.16,.37]:s.cyl('06_Armature_band',(x,0,1.02),.36,.025,s.steel,'X')
# Copper field windings form two large readable color regions around the rotor.
for y in [-.57,.57]:
    s.cube('07_Field_pole',(0,y,1.05),(1.48,.25,.86),s.cast)
    s.cube('08_Copper_field_coil',(0,y,1.05),(.84,.34,.49),s.copper)
    for x in [-.24,0,.24]:s.cube('09_Coil_strap',(x,y,1.05),(.045,.36,.51),s.dark)
for x in [-.72,.72]:
    s.cube('10_Field_yoke_foot',(x,0,.39),(.19,1.49,.22),s.cast)
    s.cube('11_Field_yoke_bridge',(x,0,1.55),(.18,1.49,.16),s.cast)
s.wheel('12_Belt_input_pulley',(-1.16,0,1.02),.43,s.cast,'X',6)
for x in [.59,.65,.71,.77]:s.cyl('13_Copper_commutator',(x,0,1.02),.285,.035,s.copper,'X')
s.cube('14_Top_carbon_brush',(.69,0,1.33),(.16,.20,.12),s.rubber)
s.cube('15_Side_carbon_brush',(.69,-.27,1.02),(.16,.10,.17),s.rubber)
s.cube('16_Terminal_pedestal',(.70,.98,.50),(.51,.38,.43),s.cast)
s.cube('17_Terminal_board',(.70,.98,.74),(.48,.35,.06),s.rubber)
for x in [.58,.82]:
    s.cyl('18_Porcelain_terminal',(x,.98,.84),.046,.14,s.steel)
    s.cyl('19_Terminal_cap',(x,.98,.925),.025,.04,s.brass)
s.path('20_Output_bus',[(.69,.06,1.37),(.69,.92,1.37),(.69,.92,.88)],.023,s.rubber)
s.rivets('21_Base_stud',[(x,y,.292) for x in [-1.18,1.18] for y in [-1.06,1.06]],r=.027)
s.rivets('22_Yoke_stud',[(x,y,1.638) for x in [-.72,.72] for y in [-.57,.57]],r=.024)
s.export(OUT,'HD_EarlyIndustrialGenerator',[3,3],['open field frame','two exposed copper field coils','exposed armature','copper commutator and carbon brushes','large belt input pulley','two output terminals'])
