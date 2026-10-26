"""Record visible-color and edge-width evidence from neighboring building sprites."""
import bpy,os,json
import numpy as np
OUT=os.path.dirname(os.path.abspath(__file__));ROOT=os.path.abspath(os.path.join(OUT,'../..'))
names=['HD_Radio','HD_LineShaftPress','HD_RollingMC','HD_LineShaftMillingMachine','HD_LineShaftTurretLathe','HD_GunSmithTable','HD_ClothingBench','HD_SmallSteamEngine','HD_LargeSteamEngine','HD_IndustrialDieselEngine','HD_EarlyIndustrialGenerator','HD_EarlyIndustrialLargeMotor','HD_HaberBoschHighPressureReactor','HD_StirredTankReactor']
report={}
for name in names:
    im=bpy.data.images.load(os.path.join(ROOT,'Textures/Buildings',name+'_north.png'),check_existing=False)
    im.colorspace_settings.name='Non-Color';w,h=im.size
    p=np.empty(w*h*4,np.float32);im.pixels.foreach_get(p);p=p.reshape(h,w,4)
    rgb=np.rint(p[:,:,:3]*255).astype(np.uint8);opaque=p[:,:,3]>.95
    colors,counts=np.unique(rgb[opaque],axis=0,return_counts=True);order=np.argsort(counts)[-8:][::-1]
    runs=[]
    for row in range(h):
        xs=np.where(opaque[row])[0]
        if len(xs)==0:continue
        run=0
        for x in range(xs[0],min(w,xs[0]+30)):
            if opaque[row,x] and rgb[row,x].max()<35:run+=1
            else:break
        if 0<run<30:runs.append(run*512/w)
    report[name]={'top_opaque_rgb':[{'rgb':colors[i].tolist(),'pixels':int(counts[i])} for i in order],
                  'median_dark_left_edge_at_512px':float(np.median(runs)) if runs else 0.0}
with open(os.path.join(OUT,'family_palette_audit.json'),'w') as f:json.dump(report,f,indent=2)
print('Recorded dominant colors and visible dark edge widths for',len(report),'building sprites.')
