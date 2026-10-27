"""Original 2000s enclosed mill-turn center, shown with its central service opening."""
import os,sys
OUT=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,os.path.join(OUT,'../LaterIndustry'))
import machine_common as m
s=m.s;m.start('circa 2000-2008 B-axis mill-turn machining center')
window=s.pal('Guard_glass_proxy_',[(91,117,124)]*3,False)
s.cube('01_Deep_slant_bed_base',(0,0,.19),(3.78,2.49,.38),s.bed)
s.cube('02_Dark_machining_pan',(0,-.09,.47),(2.30,1.54,.16),s.dark)
s.cube('03_Slant_bed',(-.08,.04,.69),(2.14,.94,.32),s.bed)
for y in [-.30,.31]:s.cube('04_Spindle_axis_guide',(0,y,.88),(2.13,.09,.08),s.steel)
for x in [-1.48,1.49]:
    s.cube('05_Full_height_end_enclosure',(x,.05,1.11),(.68,1.98,1.59),s.cast)
    s.cube('06_End_roof_cap',(x,.05,1.94),(.73,2.05,.10),s.steel)
s.cube('07_Rear_service_spine',(0,.89,1.24),(2.32,.32,1.38),s.cast)
for y in [-.92,.77]:s.cube('08_Opening_roof_rail',(0,y,1.96),(2.35,.15,.10),s.cast)
s.cube('09_Left_spindle_housing',(-.98,.03,1.14),(.47,.65,.58),s.bed)
m.chuck('10_Main_spindle_chuck',(-.65,.03,1.16),.23)
s.cube('11_Travelling_subspindle',(.89,.03,1.12),(.42,.59,.53),s.bed)
# The right chuck faces toward the main spindle.
before=set(s.scene.objects)
m.chuck('12_Subspindle_chuck',(.58,.03,1.16),.21)
for obj in set(s.scene.objects)-before:
    obj.location.x=1.16-obj.location.x
s.cyl('13_Turned_stock',(-.01,.03,1.16),.09,1.10,s.steel,'X')
s.cube('14_B_axis_head_carrier',(.13,.55,1.62),(.53,.48,.47),s.bed)
s.cyl('15_B_axis_trunnion',(.13,.27,1.66),.23,.20,s.dark,'Y')
s.rod('16_Tilting_milling_spindle',(.11,.19,1.71),(-.17,-.14,1.40),.175,s.cast)
s.rod('17_Angled_spindle_nose',(-.17,-.14,1.40),(-.25,-.23,1.31),.095,s.steel)
s.rod('18_Angled_milling_tool',(-.25,-.23,1.31),(-.31,-.30,1.24),.037,s.dark)
for x,width in [(-.88,.53),(.92,.48)]:
    s.cube('19_Part_open_sliding_guard',(x,-.95,1.17),(width,.095,.99),s.cast)
    s.cube('20_Large_guard_window',(x,-1.008,1.29),(width-.12,.018,.60),window)
    s.cube('21_Door_handle',(x+.17,-1.03,1.15),(.033,.035,.32),s.dark)
s.cube('22_Left_front_access_door',(-1.49,-.951,.74),(.48,.025,.54),s.bed)
s.cube('23_Right_front_panel',(1.49,-.951,1.21),(.52,.025,1.19),s.cast)
m.console('24_2000s_operator_panel',(1.49,-1.10,1.25),modern=True)
s.cube('25_Rear_electrical_door',(-.54,1.062,1.12),(1.02,.018,1.03),s.bed)
m.vents('26_Rear_cooling_vents',.64,1.062,1.15,.72)
s.cube('27_Rear_door_handle',(-.17,1.081,1.14),(.03,.025,.25),s.steel)
s.cube('28_Chip_conveyor', (1.86,.78,.28),(.30,.87,.30),s.dark)
s.cube('29_Coolant_service_tank',(1.46,.91,.29),(.55,.57,.27),s.bed)
m.finish(OUT,'HD_MultiTaskMachine2000',[4,3],['2000s wide enclosed mill-turn center','opposed main and travelling subspindle chucks','large angled B-axis milling head over common stock','partial sliding guards and broad screen control','central roof service opening is fixed geometry in every view'])
