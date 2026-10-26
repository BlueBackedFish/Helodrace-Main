"""Original circa-1950 vacuum induction furnace; sealed vessel and vacuum plant."""
import os,sys,math
OUT=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,os.path.join(OUT,'../IndustrialMachines'))
import sprite_common as s
s.period_start();s.scene['design_period']='circa 1950 vacuum induction melting installation'
glass=s.pal('Inspection_glass_',[(43,66,76),(38,58,68),(32,50,59)],False)
s.cube('01_Cast_foundation',(0,0,.10),(2.76,3.72,.20),s.bed)
for x in [-.98,.28]:
    s.cube('02_Vessel_saddle_foot',(x,.30,.27),(.28,1.61,.24),s.cast)
    s.bar('03_Vessel_leg',(x,.30,.33),(x*.78,.30,.71),.18,.28,s.steel)
s.vessel('04_Sealed_vacuum_chamber',(-.35,.30,1.37),.70,1.33,.20,s.cast)
for z in [.72,2.03]:s.cyl('05_Chamber_flange',(-.35,.30,z),.746,.10,s.bed)
s.vessel('06_Clamped_top_lid',(-.35,.30,2.13),.72,.075,.13,s.steel)
s.cyl('07_Lid_charging_neck',(-.35,.30,2.36),.15,.20,s.cast)
s.cyl('08_Charging_cap',(-.35,.30,2.48),.19,.065,s.steel)
# Large sealed access door with one inspection window; no exposed induction coils.
s.cyl('09_Access_flange',(-.35,-.392,1.27),.49,.14,s.bed,'Y')
s.cyl('10_Access_door',(-.35,-.49,1.27),.414,.08,s.steel,'Y')
s.cyl('11_Window_rim',(-.35,-.545,1.27),.155,.04,s.dark,'Y')
s.cyl('12_Inspection_window',(-.35,-.571,1.27),.118,.015,glass,'Y')
for x in [-.86,.16]:s.cube('13_Door_clamp',(x,-.47,1.27),(.12,.19,.21),s.cast)
s.rivets('14_Large_flange_studs',[(-.35+.355*math.cos(a),-.54,1.27+.355*math.sin(a)) for a in [i*math.tau/8 for i in range(8)]],axis='Y',r=.029)
# Broad short vacuum line feeds a separate pump/motor on the right skid.
s.path('15_Vacuum_manifold',[(.30,.52,1.52),(.94,.52,1.52),(.94,.52,.83)],.105,s.cast)
s.cyl('16_Vacuum_isolation_valve',(.61,.52,1.52),.15,.19,s.bed,'X')
s.cyl('17_Valve_stem',(.61,.52,1.69),.029,.18,s.brass)
s.handwheel('18_Isolation_handwheel',(.61,.52,1.81),.13)
s.cube('19_Pump_skid',(.94,.30,.28),(.65,1.56,.16),s.bed)
s.cube('20_Vacuum_pump',(.94,.53,.63),(.49,.55,.53),s.dark)
s.cyl('21_Pump_end_cover',(.94,.22,.63),.235,.09,s.cast,'Y')
s.cyl('22_Pump_motor',(.94,-.24,.55),.22,.63,s.bed,'Y')
s.cyl('23_Motor_end',(.94,-.59,.55),.245,.075,s.steel,'Y')
# Older broad cabinet, two dials, and two cooling-water connections.
s.cube('24_Control_cabinet',(-.72,-1.22,.63),(.92,.60,.89),s.cast)
s.cube('25_Control_recess',(-.72,-1.534,.75),(.74,.026,.43),s.dark)
for x in [-.94,-.50]:s.gauge('26_Control_meter',(x,-1.57,.84),.105)
for x in [-.94,-.72,-.50]:s.cyl('27_Control_switch',(x,-1.57,.62),.029,.04,s.brass,'Y')
for x in [-.60,-.12]:
    s.cyl('28_Water_feedthrough',(x,-.35,.77),.095,.12,s.dark,'Y')
    s.path('29_Cooling_connection',[(x,-.43,.77),(x,-.73,.77),(x,-.73,.35)],.044,s.copper)
s.gauge('30_Vacuum_dial',(-.35,-.392,1.87),.105)
# A single lid-lifting post and offset swing arm give a readable rear silhouette.
s.cube('31_Lid_lifting_post',(-.97,1.13,1.43),(.18,.22,2.46),s.bed)
s.cube('32_Lid_lifting_arm',(-.66,.88,2.69),(.79,.18,.14),s.cast)
s.rod('33_Lid_lift_link',(-.35,.88,2.69),(-.35,.43,2.30),.043,s.steel)
s.cube('34_Rear_power_enclosure',(.32,1.29,.65),(.75,.60,.88),s.bed)
for x in [.09,.32,.55]:s.cube('35_Flush_enclosure_vent',(x,1.596,.70),(.052,.007,.40),s.dark)
s.export(OUT,'HD_VIMFurnace',[3,4],['sealed dished vacuum chamber and clamped lid','large circular access door and small inspection window','separate vacuum pump and motor','short vacuum manifold and isolation valve','operator cabinet with two large meters','cooling water connections and lid lifting post'])
