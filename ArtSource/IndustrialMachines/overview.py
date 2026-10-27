"""Make a seven-building North review sheet; no model rebuilds or rerenders."""
import bpy,os,json,sys
import numpy as np
OUT=os.path.dirname(os.path.abspath(__file__))
ROOT=os.path.abspath(os.path.join(OUT,'../..'))
sys.path.insert(0,OUT)
from building_theme import STEEL
names=['HD_SmallSteamEngine','HD_LargeSteamEngine','HD_IndustrialDieselEngine','HD_EarlyIndustrialGenerator','HD_EarlyIndustrialLargeMotor','HD_HaberBoschHighPressureReactor','HD_StirredTankReactor']
images=[]
for name in names:
    im=bpy.data.images.load(os.path.join(ROOT,'Textures/Buildings',name+'_north.png'))
    im.colorspace_settings.name='Non-Color';w,h=im.size
    p=np.empty(w*h*4,np.float32);im.pixels.foreach_get(p);p=p.reshape(h,w,4)
    mask=bpy.data.images.load(os.path.join(ROOT,'Textures/Buildings',name+'_northm.png'))
    mask.colorspace_settings.name='Non-Color';m=np.empty(w*h*4,np.float32);mask.pixels.foreach_get(m)
    weights=m.reshape(h,w,4)[:,:,:2]
    p[:,:,:3]*=np.clip((1-weights.sum(axis=2))[:,:,None]+weights[:,:,0,None]+weights[:,:,1,None]*np.array(STEEL)/255,0,1)
    im.pixels.foreach_set(p.ravel());im.scale(512,512)
    p=np.empty(512*512*4,np.float32);im.pixels.foreach_get(p);images.append(p.reshape(512,512,4))
blank=np.zeros_like(images[0]);images.extend([blank,blank])
rows=[np.concatenate(images[i:i+3],axis=1) for i in [0,3,6]]
im=bpy.data.images.new('Seven_period_industrial_buildings',1536,1536,alpha=True)
im.colorspace_settings.name='Non-Color';im.pixels.foreach_set(np.concatenate(rows[::-1],axis=0).ravel())
im.file_format='PNG';im.filepath_raw=os.path.join(OUT,'overview_north.png');im.save()
with open(os.path.join(OUT,'overview_layout.json'),'w') as f:json.dump({'top_row':names[:3],'middle_row':names[3:6],'bottom_row':names[6:]+[None,None]},f,indent=2)
