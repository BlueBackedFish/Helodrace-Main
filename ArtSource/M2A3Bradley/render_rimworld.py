"""Render the saved simplified model in a fresh Blender process."""
import bpy,os,runpy,sys,tempfile
from pathlib import Path
ROOT=os.path.dirname(os.path.abspath(__file__));OUT=os.path.join(ROOT,'RimWorld')
SOURCE=os.path.join(OUT,'bradley_rimworld.blend')
sys.path.insert(0,ROOT)
from face_depth_tone import apply_face_depth_tone
# Render to fresh files before replacing published PNGs. Direct overwrites can
# fail on Windows when a preview holds a file while OpenImageIO opens it.
with tempfile.TemporaryDirectory(prefix='.render-',dir=OUT) as staging:
    bpy.ops.wm.open_mainfile(filepath=SOURCE)
    for camera in ['iso_front','iso_rear']:
        bpy.context.scene.camera=bpy.data.objects['Camera_'+camera]
        apply_face_depth_tone(bpy.context.scene)
        bpy.context.scene.render.filepath=os.path.join(staging,camera+'.png')
        bpy.ops.render.render(write_still=True)
    runpy.run_path(os.path.join(ROOT,'render_vf_east.py'),init_globals={'SOURCE_BLEND':SOURCE,'OUTPUT_DIR':os.path.join(staging,'VF_East'),'PREPARE_RENDER':apply_face_depth_tone})
    files=[p for p in Path(staging).rglob('*') if p.is_file()]
    assert len(files)==11
    for p in files:
        target=Path(OUT)/p.relative_to(staging);target.parent.mkdir(exist_ok=True,parents=True)
        os.replace(p,target)
print('ALL RIMWORLD RENDERS PUBLISHED')
