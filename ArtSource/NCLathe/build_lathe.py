"""Open-bed servo lathe with an early tape/relay numerical-control cabinet."""
import os,sys,math
OUT=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,os.path.join(OUT,'../EarlyAutomation'))
import machine_common as m
s=m.s;m.start()
for x in [-1.02,.44]:
    s.cube('01_Bed_foot',(x,-.13,.09),(.68,1.06,.18),s.bed)
    s.cube('02_Cast_pedestal',(x,-.13,.40),(.56,.82,.57),s.bed)
s.cube('03_Deep_lathe_bed',(-.29,-.13,.71),(2.18,.81,.22),s.cast)
s.cube('04_Recessed_chip_tray',(-.10,-.13,.836),(1.71,.66,.034),s.dark)
for y in [-.37,.11]:s.cube('05_Longitudinal_guideway',(-.11,y,.88),(1.83,.10,.095),s.steel)
s.cube('06_Cast_headstock',(-1.04,-.13,1.08),(.51,.67,.54),s.cast)
s.cyl('07_Headstock_motor',(-1.03,.37,.89),.21,.47,s.bed,'X')
s.cyl('08_Spindle_chuck',(-.715,-.13,1.16),.23,.16,s.dark,'X')
for i in range(3):
    a=i*math.tau/3
    o=s.cube('09_Three_chuck_jaws',(-.613,-.13+.14*math.cos(a),1.16+.14*math.sin(a)),(.07,.115,.10),s.steel)
    o.rotation_euler.x=a
s.cyl('10_Workpiece',(-.11,-.13,1.16),.072,.94,s.steel,'X')
s.cube('11_Tailstock_slide',(.53,-.13,.96),(.39,.50,.12),s.bed)
s.cube('12_Tailstock_body',(.59,-.13,1.12),(.32,.29,.28),s.cast)
s.cyl('13_Tailstock_quill',(.37,-.13,1.16),.075,.24,s.steel,'X')
s.cube('14_Servo_carriage',(-.07,-.34,.97),(.49,.49,.16),s.bed)
s.cube('15_Cross_slide',(-.07,-.38,1.08),(.31,.34,.09),s.steel)
s.cube('16_Square_tool_post',(-.07,-.32,1.18),(.16,.16,.16),s.dark)
s.cube('17_Turning_tool',(-.07,-.215,1.21),(.07,.19,.055),s.steel)
s.cyl('18_Feed_servo',(.54,-.60,.77),.11,.31,s.bed,'X')
s.rod('19_Enclosed_feed_screw',(-1.14,-.57,.76),(.44,-.57,.76),.036,s.dark)
s.cube('20_Rear_splash_guard',(-.25,.32,1.00),(1.95,.055,.34),s.bed)
s.cube('21_Headstock_access_panel',(-1.04,-.474,1.09),(.33,.014,.26),s.bed)
m.controller()
s.path('22_Control_conduit',[(1.02,.79,.23),(.44,.79,.23),(.44,.47,.23),(.44,.30,.56)],.04,s.dark)
m.finish(OUT,'HD_NCLathe',['1950s open-bed numerical-control lathe','large chuck, workpiece, tailstock and servo carriage','separate analog and punched-tape controller','fixed industrial palette and flush cabinet vents'])
