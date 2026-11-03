"""Authored clay trap and clay target, Blender 5.1 CLI; no raster generation."""
from pathlib import Path
import sys,math,json
import bpy
ROOT=Path(__file__).resolve().parent;REPO=ROOT.parent.parent
sys.path.insert(0,str(ROOT.parent/'IndustrialMachines'));import sprite_common as s
s.period_start();scene=bpy.context.scene
scene['outline_pixels']=12;scene['presentation_rule']='one_model_unit_per_game_tile'
scene['design_period']='mechanical spring trap, 20th century outdoor recreation'
orange=s.pal('Porcelain_orange_',[(220,104,40),(187,76,27),(158,59,23)],False)
black=s.pal('Insulator_black_',[(48,48,45),(38,38,35),(29,29,27)],False)
# Ground frame, post, tilt pivot, broad throwing arm and magazine make the silhouette.
s.cube('01_Base_frame',(0,0,.09),(1.55,.72,.16),s.bed)
for x in [-.62,.62]:
 s.cube('02_Rubber_foot_'+str(x),(x,0,.025),(.25,.86,.05),black)
s.cube('03_Main_pedestal',(-.27,.07,.32),(.35,.31,.43),s.cast)
s.cyl('04_Tilt_axle',(-.27,.07,.57),.12,.50,s.dark,'X',vertices=16)
s.cyl('05_Spring_drum',(-.27,.04,.69),.24,.20,s.cast,'Z',vertices=24)
s.cube('06_Throwing_arm',(.15,-.06,.81),(.99,.12,.055),s.steel)
s.cube('07_Throwing_arm_end_stop',(.66,-.06,.85),(.07,.19,.13),black)
s.cyl('08_Launch_plate',(.51,-.04,.85),.22,.04,black,'Z',vertices=32)
s.cyl('09_Clay_on_throwing_plate',(.51,-.04,.885),.19,.033,orange,'Z',vertices=32)
s.cube('10_Magazine_cradle',(-.67,.12,.48),(.42,.47,.11),s.cast)
for i in range(5):s.cyl('11_Stacked_clay_'+str(i),(-.67,.12,.57+i*.044),.18,.036,orange,'Z',vertices=32)
for x in [-.86,-.48]:s.rod('12_Magazine_guide_'+str(x),(x,.12,.51),(x,.12,.85),.022,s.steel)
s.cube('13_Release_pedal',(.32,-.35,.06),(.38,.19,.065),black)
s.rod('14_Pedal_link',(.10,-.33,.08),(-.27,-.11,.64),.022,s.steel)
s.cube('15_Arm_guard',(-.24,.31,.72),(.65,.055,.25),s.cast)
# Preserve the existing shape and materials; halve geometry only. The exporter
# reapplies the same 12px exterior outline at the calibrated 1x1 density.
for obj in list(scene.objects):
 if obj.type=='MESH':obj.location*=.5;obj.scale*=.5
s.export(str(ROOT),'HD_ClayTrap',(1,1),['spring drum','throwing arm','orange clay magazine','foot release pedal'])
# Separate editable 3D target source and transparent item sprite.
sys.path.insert(0,str(ROOT.parent/'PropAssets'));import modeling as m
scene=m.start();scene['presentation_rule']='3D clay disc item proxy';scene['outline_pixels']=12
orange=m.material('Clay orange top',(220,104,40));rim=m.material('Clay orange rim',(181,72,27));base=m.material('Clay black underside',(37,36,33))
m.cylinder('01_Clay underside',(0,0,.0),(0,0,.045),.32,base,48)
m.cylinder('02_Clay rim',(0,0,.045),(0,0,.09),.32,rim,48)
m.cylinder('03_Raised orange crown',(0,0,.09),(0,0,.13),.245,orange,48)
m.cylinder('04_Central orange disc',(0,0,.13),(0,0,.15),.165,orange,48)
cam=m.camera('Target_64deg',-90,64,.9,(0,0,.06));m.render(ROOT/'clay_raw.png',cam,256);m.save(ROOT/'clay_pigeon.blend')
(ROOT/'clay_metadata.json').write_text(json.dumps(dict(source='clay_pigeon.blend',resolution=256,draw_size=.4,outline_pixels=12,orthographic_degrees=64,flat_emission=True,transparent=True,shadows=False,AO=False),indent=2))
print('CLAY_BUILD_PASS: existing trap at half geometry, 1x1 / 256px / 64deg / four views / unchanged 12px outline')
