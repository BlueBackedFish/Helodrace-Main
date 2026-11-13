"""Render in a fresh process after the source-heavy interchange export."""
import bpy,os,runpy
HERE=os.path.dirname(os.path.abspath(__file__))
OUT=os.path.join(HERE,'ERA');blend=os.path.join(OUT,'m3a3_bradley_era.blend')
bpy.ops.wm.open_mainfile(filepath=blend)
scene=bpy.context.scene;scene.frame_set(1)
for camera in ['iso_front','iso_rear','east']:
    scene.camera=bpy.data.objects['Camera_'+camera]
    scene.render.filepath=os.path.join(OUT,'audit_'+camera+'.png')
    bpy.ops.render.render(write_still=True)
runpy.run_path(os.path.join(HERE,'..','render_vf_east.py'),init_globals={'SOURCE_BLEND':blend,'OUTPUT_DIR':os.path.join(OUT,'VF_East')})
runpy.run_path(os.path.join(HERE,'..','ERA','validate_era.py'),init_globals={'ASSET_DIR':OUT,'BLEND_NAME':'m3a3_bradley_era.blend'})
