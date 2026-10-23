"""Render the separate BUSK exterior with the established Bradley projections."""
from pathlib import Path
import sys, json, runpy
import bpy

HERE=Path(__file__).resolve().parent
OUT=HERE/'output'
SOURCE=OUT/'bradley_busk_iii.blend'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
scene=bpy.context.scene
scene.frame_set(1)
scene.render.resolution_x=scene.render.resolution_y=1024
scene.render.resolution_percentage=100
scene.render.image_settings.color_depth='8'
scene.render.film_transparent=True
for name in ['iso_front','iso_rear']:
    scene.camera=bpy.data.objects['Camera_'+name]
    scene.render.filepath=str(OUT/(name+'.png'))
    bpy.ops.render.render(write_still=True)
runpy.run_path(str(HERE.parent/'render_vf_east.py'),run_name='__main__',
              init_globals={'SOURCE_BLEND':str(SOURCE),'OUTPUT_DIR':str(OUT/'VF_East')})
print('BUSK split EAST and isometric renders saved')
