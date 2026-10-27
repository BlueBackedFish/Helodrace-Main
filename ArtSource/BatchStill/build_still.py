"""Fired copper batch kettle and separate water-cooled condenser."""
import os,sys,math
OUT=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,os.path.join(OUT,'../IndustrialMachines'))
import sprite_common as s
s.period_start();s.scene['design_period']='early twentieth-century fired batch still'
for mats in [s.bed,s.cast,s.steel,s.dark]:
    for m in mats:m['tintable']=False
for m in s.stone:m['tintable']=True
s.cube('01_Kettle_hearth',(-.58,.12,.33),(1.46,1.60,.66),s.stone)
s.cube('02_Hearth_iron_cap',(-.58,.12,.69),(1.54,1.67,.10),s.bed)
s.cube('03_Fire_door_recess',(-.58,-.697,.34),(.67,.045,.39),s.dark)
s.cube('04_Fire_door',(-.58,-.733,.34),(.54,.045,.29),s.bed)
s.bar('05_Fire_door_latch',(-.72,-.768,.36),(-.44,-.768,.36),.045,.04,s.steel)
s.vessel('06_Copper_batch_kettle',(-.58,.12,1.06),.63,.55,.10,s.copper)
s.ring('07_Kettle_belly_band',(-.58,.12,.87),.654,.61,.075,s.bed,'Z')
# Broad conical head and short neck distinguish the still from a storage tank.
s.bpy.ops.mesh.primitive_cone_add(vertices=40,radius1=.64,radius2=.16,depth=.48,location=(-.58,.12,1.635))
o=s.bpy.context.object;o.name='08_Conical_copper_head'
for m in s.copper:o.data.materials.append(m)
for p in o.data.polygons:p.material_index=0 if p.normal.z>.2 else 1
s.cyl('09_Vapor_neck',(-.58,.12,1.96),.14,.20,s.copper)
s.path('10_Overhead_vapor_pipe',[(-.58,.12,2.04),(-.58,.12,2.21),(.85,.12,2.21),(.85,.12,1.93)],.075,s.copper)
s.ring('11_Kettle_head_seam',(-.58,.12,1.40),.655,.60,.055,s.copper,'Z')
s.cube('12_Condenser_footing',(.85,.17,.09),(.77,1.07,.18),s.stone)
for y in [-.19,.53]:s.cube('13_Condenser_leg',(.85,y,.31),(.15,.14,.35),s.bed)
s.vessel('14_Water_cooled_condenser',(.85,.17,1.15),.34,1.16,.10,s.cast)
for z in [.60,1.68]:s.ring('15_Condenser_band',(.85,.17,z),.359,.32,.075,s.bed,'Z')
s.cyl('16_Vapor_inlet',(.85,.17,1.86),.11,.14,s.copper)
s.path('17_Condenser_product_outlet',[(.85,.17,.56),(.85,-.45,.56),(.85,-.45,.39),(.85,-.84,.39)],.047,s.copper)
s.cyl('18_Product_valve',(.85,-.49,.53),.085,.17,s.brass,'Y')
s.handwheel('19_Product_handwheel',(.85,-.51,.66),.105)
s.vessel('20_Receiver_can',(.85,-1.01,.23),.25,.32,.055,s.bed)
s.cyl('21_Receiver_neck',(.85,-1.01,.44),.085,.12,s.dark)
s.path('22_Cooling_water_lower',[(1.14,.17,.78),(1.33,.17,.78),(1.33,.62,.78)],.045,s.steel)
s.path('23_Cooling_water_upper',[(1.14,.17,1.48),(1.33,.17,1.48),(1.33,.62,1.48)],.045,s.steel)
s.cyl('24_Kettle_fill_cap',(-.91,-.13,1.68),.115,.09,s.brass)
s.gauge('25_Kettle_thermometer',(-.57,-.52,1.27),.105)
s.rivets('26_Fire_door_hinges',[(x,-.766,z) for x in [-.81,-.35] for z in [.25,.43]],r=.025,axis='Y')
s.export(OUT,'HD_BatchStill',[3,3],['fired copper batch kettle with conical head','stone firebox and broad fire door','overhead vapor line to separate water condenser','coarse cooling ports and product receiver','limited copper and neutral iron palette'])
