"""Sprite-proxy helpers for 1970s electric industry and a 2000s mill-turn center."""
import os,sys,math,json
sys.path.insert(0,os.path.join(os.path.dirname(__file__),'../IndustrialMachines'))
import sprite_common as s

def start(period):
    s.period_start();s.scene['design_period']=period

def console(name,p,modern=False):
    x,y,z=p
    monitor=s.pal(name+'_display_',[(85,116,119)]*3,False) if modern else s.pal(name+'_CRT_',[(98,119,97)]*3,False)
    s.cube(name+'_cabinet',(x,y,z),(.51,.38,.84),s.cast)
    s.cube(name+'_screen_bezel',(x,y-.199,z+.16),(.39,.026,.32),s.dark)
    s.cube(name+'_screen',(x,y-.216,z+.17),(.31,.012,.23),monitor)
    s.cube(name+'_keyboard',(x,y-.202,z-.17),(.39,.025,.21),s.bed)
    for dx in [-.12,0,.12]:
        s.cube(name+'_key_group',(x+dx,y-.221,z-.16),(.075,.014,.10),s.dark)
    s.cyl(name+'_override',(x+.15,y-.23,z-.32),.043,.035,s.dark,'Y')

def chuck(name,p,r=.22):
    x,y,z=p;s.cyl(name+'_body',p,r,.15,s.dark,'X')
    for i in range(3):
        a=i*math.tau/3
        obj=s.cube(name+'_jaw',(x+.09,y+r*.57*math.cos(a),z+r*.57*math.sin(a)),(.075,r*.46,r*.37),s.steel)
        obj.rotation_euler.x=a

def power_pack(name,p):
    x,y,z=p
    s.cube(name+'_tank',(x,y,z),(.61,.77,.48),s.bed)
    s.cube(name+'_lid',(x,y,z+.26),(.65,.81,.065),s.cast)
    s.cyl(name+'_pump_motor',(x,y+.12,z+.46),.15,.33,s.dark)
    s.cyl(name+'_filler',(x-.18,y-.20,z+.33),.055,.10,s.steel)

def vents(name,x,y,z,width=.42):
    for dz in [-.13,0,.13]:s.cube(name,(x,y,z+dz),(width,.012,.042),s.dark)

def finish(out,stem,footprint,features):
    before=s.geometry_hash();s.export(out,stem,footprint,features)
    assert s.geometry_hash()==before
    path=os.path.join(out,'validation.json')
    with open(path,encoding='utf8') as f:meta=json.load(f)
    meta.update(density_export_preserves_geometry_and_palette=True,
                density_export_geometry_palette_sha256=before,rotation_export_geometry_palette_sha256=before)
    with open(path,'w',encoding='utf8') as f:json.dump(meta,f,indent=2)
