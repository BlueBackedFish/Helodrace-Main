"""Shared coarse 1950s numerical-control cabinet; no modern screen or enclosure."""
import os,sys
sys.path.insert(0,os.path.join(os.path.dirname(__file__),'../IndustrialMachines'))
import sprite_common as s

def start():
    s.period_start()
    s.scene['design_period']='circa 1955-1959 early numerical control'

def controller(x=1.06,y=.43):
    s.cube('NC01_Control_cabinet',(x,y,.80),(.64,.78,1.45),s.cast)
    s.cube('NC02_Cabinet_plinth',(x,y,.105),(.72,.86,.21),s.bed)
    s.cube('NC03_Analog_instrument_panel',(x,y-.401,1.24),(.51,.032,.39),s.bed)
    for dx in [-.13,.13]:s.gauge('NC04_Large_analog_dial',(x+dx,y-.43,1.30),.085)
    for dx in [-.17,0,.17]:s.cyl('NC05_Control_switch',(x+dx,y-.432,1.12),.035,.035,s.dark,'Y',vertices=12)
    s.cube('NC06_Tape_reader_inset',(x,y-.404,.83),(.50,.035,.29),s.dark)
    for dx in [-.15,.15]:
        s.cyl('NC07_Punched_tape_reel',(x+dx,y-.44,.83),.105,.045,s.bed,'Y')
        s.cyl('NC08_Reel_hub',(x+dx,y-.47,.83),.031,.025,s.steel,'Y')
    s.cube('NC09_Exposed_paper_tape',(x,y-.47,.83),(.24,.012,.055),s.steel)
    s.cube('NC10_Lower_relay_door',(x,y-.405,.42),(.49,.025,.35),s.bed)
    s.cube('NC11_Door_handle',(x+.15,y-.43,.45),(.025,.03,.12),s.steel)
    for z in [.51,.67,.83]:
        s.cube('NC12_Flush_side_vent',(x+.324,y+.05,z),(.008,.44,.04),s.dark)
        s.cube('NC13_Flush_rear_vent',(x,y+.394,z),(.44,.008,.04),s.dark)

def finish(out,stem,features):
    s.apply_building_theme(stem)
    before=s.geometry_hash()
    s.export(out,stem,[3,2],features)
    import json
    path=os.path.join(out,'validation.json')
    with open(path,encoding='utf8') as f:meta=json.load(f)
    assert s.geometry_hash()==before
    meta.update(density_export_preserves_geometry_and_palette=True,
                density_export_geometry_palette_sha256=before,rotation_export_geometry_palette_sha256=before)
    with open(path,'w',encoding='utf8') as f:json.dump(meta,f,indent=2)
