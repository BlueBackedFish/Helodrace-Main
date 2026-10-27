"""Small authored QCB visual kit in the existing M2HB's leveled coordinates."""
from pathlib import Path
import json
import modeling as m

DESIGN=json.loads((Path(__file__).resolve().parent.parent/'HeavyMachineGuns/qcb_design.json').read_text())


def add(materials):
    d=DESIGN;objects=[]
    def tag(obj,layer):
        obj['art_addition']='QCB visual kit';obj['source_object']='authored_qcb';obj['pose']='gun';obj['layer']=layer;objects.append(obj)
    tag(m.cylinder('QCB barrel coupling',(0,d['coupling_y'][0],d['axis_z']),(0,d['coupling_y'][1],d['axis_z']),d['coupling_radius'],materials['m2_barrel_shoulder'],40),'barrel')
    tag(m.cylinder('QCB folded handle arm',d['arm_start'],d['arm_end'],d['arm_radius'],materials['m2_handle'],20),'barrel')
    a,b=d['grip_start'],d['grip_end'];r=d['grip_radius']
    tag(m.cylinder('QCB carry grip',a,b,r,materials['dark_top'],32),'barrel')
    for name,y in [('rear',a[1]),('front',b[1])]:
        tag(m.cylinder('QCB grip '+name+' ferrule',(a[0],y-.003,a[2]),(a[0],y+.003,a[2]),r+.001,materials['m2_handle'],24),'barrel')
    tag(m.cylinder('QCB handle pivot pin',(a[0],.343,a[2]),(a[0],.359,a[2]),.011,materials['m2_fastener'],24),'barrel')
    tag(m.box('QCB latch body',d['latch_center'],d['latch_size'],materials['m2_sleeve_rim'],bevel=.002),'body')
    tag(m.box('QCB latch tab',(.052,.304,.175),(.018,.021,.008),materials['m2_fastener'],bevel=.0015),'body')
    return objects
