"""Original early/mid-century three-electrode arc furnace, 4x4 tiles."""
import os,sys
OUT=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,os.path.join(OUT,'../IndustrialMachines'))
import sprite_common as s
s.period_start();s.scene['design_period']='early/mid twentieth-century three-electrode arc furnace'
shell=s.pal('Dark_furnace_shell_',[119,96,83])
lining=s.pal('Refractory_',[195,172,151],False)
heat=s.pal('Tap_heat_',[(194,103,43),(158,70,32),(127,56,29)],False)
s.cube('01_Foundation',(0,0,.10),(3.76,3.76,.20),s.stone)
for x in [-1.02,.51]:s.cube('02_Tilting_cradle_foot',(x,-.20,.30),(.44,2.40,.40),s.bed)
s.vessel('03_Round_furnace_shell',(-.25,-.20,1.04),1.17,.92,.23,shell)
for z in [.67,1.40]:s.cyl('04_Shell_reinforcing_band',(-.25,-.20,z),1.205,.085,s.bed)
s.cyl('05_Refractory_roof_seat',(-.25,-.20,1.58),1.19,.11,lining)
s.vessel('06_Removable_roof',(-.25,-.20,1.70),1.15,.075,.12,s.cast)
for x in [-1.46,.97]:
    s.cyl('07_Tilt_trunnion',(x,-.20,1.02),.17,.25,s.steel,'X')
    s.cube('08_Trunnion_bearing',(x,-.20,.87),(.25,.43,.43),s.cast)
# Broad graphite electrodes; three separate copper arms meet rear lifting columns.
for i,(x,y) in enumerate([(-.78,.02),(.28,.02),(-.25,-.78)]):
    s.cyl('09_Roof_electrode_seal',(x,y,1.82),.205,.10,s.dark)
    s.cyl('10_Graphite_electrode',(x,y,2.35),.125,1.14,s.dark)
    s.cyl('11_Copper_electrode_clamp',(x,y,2.59),.178,.13,s.copper)
    s.cube('12_Electrode_arm',(x,(y+1.27)/2,2.59),(.17,1.27-y,.13),s.copper)
    s.cube('13_Insulator',(x,1.15,2.59),(.24,.23,.20),s.steel)
    s.cube('14_Lifting_column',(x,1.36,1.62),(.22,.25,2.84),s.bed)
    s.cube('15_Lifting_column_cap',(x,1.36,3.06),(.28,.31,.10),s.steel)
s.cube('16_Mast_foundation',(-.25,1.36,.29),(1.94,.66,.18),s.bed)
s.cube('17_Mast_cross_tie',(-.25,1.36,2.08),(1.60,.29,.16),s.cast)
# Slag door and shallow tapping runner face the operator (-Y).
s.cyl('18_Slag_door_frame',(-.25,-1.335,.98),.32,.15,s.bed,'Y')
s.cyl('19_Slag_door',(-.25,-1.425,.98),.245,.045,s.dark,'Y')
s.cube('20_Tapping_glow',(-.25,-1.455,.86),(.28,.018,.13),heat)
s.cube('21_Tapping_runner',(-.25,-1.61,.71),(.34,.52,.09),s.dark)
for x in [-.465,-.035]:s.cube('22_Runner_cheek',(x,-1.61,.77),(.09,.52,.15),lining)
s.cube('23_Control_foot',(1.43,-1.08,.28),(.48,.58,.16),s.bed)
s.cube('24_Control_pedestal',(1.43,-1.08,.73),(.34,.43,.79),s.cast)
s.cube('25_Control_panel',(1.43,-1.305,.82),(.29,.025,.43),s.dark)
s.gauge('26_Current_meter',(1.43,-1.34,.91),.082)
for x in [1.35,1.51]:s.cyl('27_Control_switch',(x,-1.34,.71),.026,.04,s.brass,'Y')
s.cube('28_Service_step',(1.44,.27,.40),(.58,1.55,.13),s.bed)
for y in [-.42,.96]:
    s.cube('29_Walkway_leg',(1.58,y,.29),(.12,.13,.37),s.bed)
    s.rod('30_Outer_guard_post',(1.72,y,.46),(1.72,y,.93),.033,s.cast)
s.rod('31_Outer_guard_rail',(1.72,-.42,.93),(1.72,.96,.93),.033,s.cast)
s.rivets('32_Cradle_anchors',[(x,y,.515) for x in [-1.02,.51] for y in [-1.21,.82]],r=.038)
s.export(OUT,'HD_SAFurnace',[4,4],['three large graphite electrodes and separate copper arms','round dark shell on a tilting cradle','refractory roof seat and removable roof','operator-side slag door and tapping runner','rear electrode lifting columns','small control pedestal and service walkway'])
