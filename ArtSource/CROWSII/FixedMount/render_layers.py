"""Render the mounted station independently, using the plate's exact cameras."""
from pathlib import Path
import bpy,sys
ROOT=Path(__file__).resolve().parent;OUT=ROOT/'output'
sys.path.insert(0,str(ROOT.parents[1]/'PropAssets'));import modeling as m
if __name__=='__main__':
    bpy.ops.wm.open_mainfile(filepath=str(OUT/'fixed_mount.blend'))
    scene=bpy.context.scene;scene.frame_set(1)
    for o in scene.objects:
        if o.type=='MESH':o.hide_render=o.get('layer')=='foundation'
    for direction in ['top','north','east','south','west']:
        cam=scene.objects['Foundation_'+('Top' if direction=='top' else direction)]
        m.render(OUT/('station_'+direction+'_raw.png'),cam,2048)
    print('FIXED_MOUNT_LAYER_RENDER_PASS: five station-only views registered to the foundation')
