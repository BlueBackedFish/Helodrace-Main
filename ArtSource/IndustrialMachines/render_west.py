"""Add an actual West view to saved industrial models, preserving their meshes/colors."""
import bpy,os,sys,json,hashlib,math
import numpy as np
from mathutils import Vector
OUT=os.path.dirname(os.path.abspath(__file__));ROOT=os.path.abspath(os.path.join(OUT,'../..'))
sys.path.insert(0,OUT)
import sprite_common as s
from sprite_common import geometry_hash
from integrate_asset import integrate
ASSETS=[('HD_SmallSteamEngine','SmallSteamEngine','small_steam_engine.blend',True),
 ('HD_LargeSteamEngine','LargeSteamEngine','HD_LargeSteamEngine.blend',True),
 ('HD_IndustrialDieselEngine','IndustrialDieselEngine','HD_IndustrialDieselEngine.blend',True),
 ('HD_EarlyIndustrialGenerator','EarlyIndustrialGenerator','HD_EarlyIndustrialGenerator.blend',True),
 ('HD_EarlyIndustrialLargeMotor','EarlyIndustrialMotor','HD_EarlyIndustrialLargeMotor.blend',True),
 ('HD_HaberBoschHighPressureReactor','HighPressureReactor','HD_HaberBoschHighPressureReactor.blend',True),
 ('HD_StirredTankReactor','StirredTankReactor','HD_StirredTankReactor.blend',True),
 ('HD_BasicPumpJack','BasicPumpJack','HD_BasicPumpJack.blend',True),
 ('HD_DistillationTower','DistillationTower','HD_DistillationTower.blend',True),
 ('HD_MetalPumpJack','MetalPumpJack','HD_MetalPumpJack.blend',False),
 ('HD_RotaryDrill','RotaryDrill','HD_RotaryDrill.blend',False)]

def read_pixels(path):
    im=bpy.data.images.load(path,check_existing=False);im.colorspace_settings.name='Non-Color'
    w,h=im.size
    p=np.empty(w*h*4,np.float32);im.pixels.foreach_get(p);return p.reshape(h,w,4)

if __name__=='__main__':
    import runpy
    runpy.run_path(os.path.join(OUT,'normalize_density.py'),run_name='__main__')
