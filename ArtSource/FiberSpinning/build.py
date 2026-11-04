"""Original compact spinneret/drawing/take-up module."""
from pathlib import Path
import sys,json
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT.parent/'IndustrialMachines'));import sprite_common as s
s.period_start();s.scene['design_period']='mid-century compact synthetic fiber spinning attachment'
# Distinctive, readable one-tile machinery: rear polymer feed, spinneret,
# vertical fiber draw, front bath/godet, side take-up spool and analog controls.
s.cube('01_Open_skid',(0,0,.07),(.82,.72,.12),s.bed)
for x in [-.30,.30]:s.cube('02_Insulated_foot',(x,0,.02),(.16,.78,.04),s.rubber)
s.cube('03_Left_spinning_column',(-.24,.17,.44),(.19,.22,.67),s.cast)
s.cube('04_Feed_pump_housing',(-.24,.17,.78),(.29,.29,.19),s.cast)
s.cyl('05_Filter_head',(-.24,-.04,.70),.115,.13,s.steel,'Z',vertices=24)
s.cyl('06_Spinneret_disc',(-.24,-.04,.625),.13,.034,s.dark,'Z',vertices=24)
s.path('07_Reactor_feed_port',[(-.24,.17,.81),(-.39,.17,.81),(-.45,.17,.81)],.047,s.steel)
s.cyl('08_Reactor_pipe_coupling',(-.43,.17,.81),.075,.055,s.dark,'X',vertices=16)
s.cube('09_Quench_trough',(-.22,-.065,.18),(.36,.35,.13),s.steel)
s.cube('10_Recessed_liquid',(-.22,-.065,.252),(.26,.25,.014),s.dark)
fiber=s.pal('Unbleached_fiber_',[(225,219,196),(203,197,174),(185,179,158)],False)
for x in [-.30,-.24,-.18]:s.rod('11_Three_drawn_fiber_bands',(x,-.04,.61),(x,-.04,.27),.009,fiber)
for z in [.30,.46]:
 s.cyl('12_Drawing_godet',(.035,-.06,z),.065,.20,s.steel,'Y',vertices=24)
 s.cyl('13_Godet_dark_end',(.035,-.17,z),.036,.025,s.dark,'Y',vertices=16)
s.rod('14_Fiber_to_takeup',(.02,-.18,.43),(.25,-.18,.40),.013,fiber)
s.cube('15_Takeup_motor',(.26,.15,.24),(.24,.27,.25),s.dark)
s.cube('16_Takeup_bracket',(.26,-.025,.24),(.10,.17,.26),s.cast)
s.cyl('17_Takeup_reel',(.26,-.13,.39),.125,.22,fiber,'Y',vertices=32)
for y in [-.25,-.01]:s.cyl('18_Reel_flange',(.26,y,.39),.145,.025,s.dark,'Y',vertices=24)
s.cyl('19_Reel_hub',(.26,-.27,.39),.045,.032,s.steel,'Y',vertices=16)
s.cube('20_Control_box',(.22,.235,.64),(.28,.16,.22),s.cast)
s.gauge('21_Draw_speed_dial',(.22,.145,.67),.065)
s.cyl('22_Stop_button',(.30,.139,.58),.023,.023,s.rubber,'Y',vertices=12)
s.apply_building_theme('HD_FiberSpinningModule');before=s.geometry_hash()
s.export(str(ROOT),'HD_FiberSpinningModule',[1,1],['one-tile open skid','polymer feed coupling and filter spinneret','three drawn fiber bands and quench trough','two drawing godets','take-up reel and analog control dial'])
assert s.geometry_hash()==before
meta=json.loads((ROOT/'validation.json').read_text());meta['geometry_palette_sha256']=before
(ROOT/'validation.json').write_text(json.dumps(meta,indent=2)+'\n')
print('Fiber module exported; fabric items reuse the vanilla Cloth textures.')
