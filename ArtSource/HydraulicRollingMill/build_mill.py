"""Original compact 1970s four-high hydraulic plate rolling mill."""
import os,sys
OUT=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,os.path.join(OUT,'../LaterIndustry'))
import machine_common as m
s=m.s;m.start('circa 1972-1979 hydraulic four-high plate mill')
s.cube('01_Main_cast_skid',(0,.02,.12),(3.43,1.39,.24),s.bed)
for x in [-1.03,1.03]:
    s.cube('02_Mill_stand_foot',(x,0,.29),(.69,1.17,.32),s.bed)
    s.cube('03_Heavy_housing',(x,.04,1.09),(.43,.84,1.62),s.cast)
    for z in [.90,1.24]:s.cube('04_Roll_bearing_chock',(x,-.03,z),(.49,.94,.27),s.bed)
    s.cyl('05_Hydraulic_gap_actuator',(x,.04,2.07),.18,.38,s.bed)
    s.cyl('06_Actuator_endcap',(x,.04,2.29),.21,.075,s.steel)
for name,z,r in [('07_Lower_backup_roll',.49,.27),('08_Lower_work_roll',.90,.14),
                 ('09_Upper_work_roll',1.24,.14),('10_Upper_backup_roll',1.65,.27)]:
    s.cyl(name,(0,0,z),r,1.80,s.steel,'X',vertices=40,large=True)
s.cube('11_Rear_crown_tie',(0,.39,1.96),(2.53,.20,.21),s.cast)
for sign in [-1,1]:
    for x in [-.81,.81]:s.cube('12_Feed_table_rail',(x,sign*1.04,.76),(.10,.88,.30),s.bed)
    for y in [.87,1.26]:s.cyl('13_Large_feed_roller',(0,sign*y,.97),.095,1.63,s.steel,'X')
sheet=s.pal('Rolled_stock_',[(160,173,176),(137,150,154),(122,135,138)],False)
s.cube('14_Plate_in_work_gap',(0,-.16,1.073),(.90,1.44,.035),sheet)
s.cube('15_Cast_gear_reducer',(1.47,.05,.72),(.49,.80,.86),s.bed)
for z in [.90,1.24]:
    s.cyl('16_Roll_drive_coupling',(1.24,.01,z),.105,.33,s.dark,'X')
s.cyl('17_Electric_drive_motor',(1.48,.84,.60),.24,.62,s.cast,'Y')
s.cyl('18_Motor_end_cover',(1.48,1.18,.60),.21,.07,s.dark,'Y')
m.power_pack('19_Hydraulic_unit',(-1.51,.76,.43))
s.path('20_Broad_hydraulic_feed',[(-1.51,.76,.81),(-1.51,.42,2.08),(-1.03,.42,2.08),(-1.03,.04,2.08)],.042,s.dark)
s.path('21_Actuator_crossover',[(-1.03,.37,2.11),(1.03,.37,2.11),(1.03,.04,2.11)],.037,s.bed)
s.cube('22_Control_console_stand',(-1.51,-.71,.55),(.15,.17,.82),s.bed)
m.console('23_Roll_gap_control',(-1.51,-.71,1.12))
m.finish(OUT,'HD_HydraulicRollingMill',[4,3],['1970s four-high plate rolling mill','two work rolls and two large backup rolls','hydraulic roll-gap actuators and coarse crossover main','four prominent entry and exit feed rollers with plate stock','separate electric drive, hydraulic unit and gap-control console'])
