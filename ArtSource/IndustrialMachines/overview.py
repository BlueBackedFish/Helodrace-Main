"""Make a seven-building North review sheet; no model rebuilds or rerenders."""
import bpy,os,json
import numpy as np
OUT=os.path.dirname(os.path.abspath(__file__))
ROOT=os.path.abspath(os.path.join(OUT,'../..'))
names=['HD_SmallSteamEngine','HD_LargeSteamEngine','HD_IndustrialDieselEngine','HD_EarlyIndustrialGenerator','HD_EarlyIndustrialLargeMotor','HD_HaberBoschHighPressureReactor','HD_StirredTankReactor']
images=[]
for name in names:
    im=bpy.data.images.load(os.path.join(ROOT,'Textures/Buildings',name+'_north.png'))
    assert tuple(im.size)==(512,512)
    im.colorspace_settings.name='Non-Color';p=np.empty(512*512*4,np.float32)
    im.pixels.foreach_get(p);images.append(p.reshape(512,512,4))
blank=np.zeros_like(images[0]);images.extend([blank,blank])
rows=[np.concatenate(images[i:i+3],axis=1) for i in [0,3,6]]
im=bpy.data.images.new('Seven_period_industrial_buildings',1536,1536,alpha=True)
im.colorspace_settings.name='Non-Color';im.pixels.foreach_set(np.concatenate(rows[::-1],axis=0).ravel())
im.file_format='PNG';im.filepath_raw=os.path.join(OUT,'overview_north.png');im.save()
with open(os.path.join(OUT,'overview_layout.json'),'w') as f:json.dump({'top_row':names[:3],'middle_row':names[3:6],'bottom_row':names[6:]+[None,None]},f,indent=2)
