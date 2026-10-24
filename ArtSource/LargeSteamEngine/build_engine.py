"""Rebuild the reference twin-cylinder large line-shaft steam engine."""
import os,sys
OUT=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,os.path.join(OUT,'../IndustrialMachines'))
import sprite_common as s
s.start()
s.cube('01_Cast_bed',(0,0,.16),(3.90,2.75,.32),s.bed)
for x in [-1.60,-.55,.55,1.60]:s.cube('02_Foundation_lug',(x,-1.33,.16),(.24,.36,.32),s.cast)
s.cyl('03_Common_crankshaft',(0,.12,1.48),.085,1.85,s.steel,'Y')
for y in [-.65,.65]:
    s.cube('04_Main_bearing_pedestal',(0,y,.825),(.35,.28,1.01),s.cast)
    s.cyl('05_Bearing_cap',(0,y,1.48),.15,.24,s.dark,'Y')
s.wheel('06_Large_rear_flywheel',(0,.92,1.48),1.10,s.cast)
for x,sign in [(-1.22,-1),(1.22,1)]:
    s.cube('07_Cylinder_saddle',(x,-.40,.65),(.65,.73,.66),s.cast)
    s.cyl('08_Working_cylinder',(x,-.40,1.17),.39,.82,s.cast,'X',large=True)
    for dx in [-.44,.44]:s.cyl('09_Cylinder_head',(x+dx,-.40,1.17),.425,.075,s.steel,'X')
    s.cube('10_Valve_chest',(x,-.40,1.635),(.68,.61,.18),s.steel)
    s.cyl('11_Brass_valve',(x,-.40,1.78),.065,.12,s.brass)
    s.cyl('12_Piston_gland',(x-sign*.49,-.40,1.17),.09,.10,s.dark,'X')
    s.rod('13_Piston_rod',(x-sign*.5,-.40,1.17),(sign*.32,-.40,1.17),.033,s.steel)
    s.cube('14_Crosshead_guide',(sign*.56,-.40,.99),(.54,.23,.09),s.dark)
    s.cube('15_Crosshead',(sign*.35,-.40,1.14),(.13,.17,.19),s.steel)
    s.rod('16_Crank_link',(sign*.35,-.40,1.17),(sign*.13,-.40,1.39),.045,s.steel)
    s.path('17_Copper_drain',[(x,-.82,.78),(x,-.82,.64),(x-sign*.30,-.82,.64)],.038,s.copper)
    points=[(x+sign*.485,-.40+.29*s.math.cos(i*s.math.tau/6),1.17+.29*s.math.sin(i*s.math.tau/6)) for i in range(6)]
    s.rivets('18_Head_stud',points,'X',.021)
s.cube('19_Governor_tower',(0,.04,1.55),(.27,.32,.30),s.cast)
s.governor('20_Centrifugal_governor',(0,.04,1.70),.56)
s.path('21_Upper_steam_arch',[(1.28,-.18,1.73),(1.28,-.18,2.06),(1.80,-.18,2.06),(1.80,-.18,1.20)],.065,s.cast)
s.cyl('22_Steam_separator',(1.80,-.18,1.01),.14,.40,s.dark)
s.rivets('23_Bed_rivet',[(x,y,.327) for x in [-1.72,-.70,.70,1.72] for y in [-1.07,1.07]],r=.025)
s.export(OUT,'HD_LargeSteamEngine',[4,3],['twin working cylinders','large rear spoked flywheel','central governor','upper steam arch','copper drains'])
