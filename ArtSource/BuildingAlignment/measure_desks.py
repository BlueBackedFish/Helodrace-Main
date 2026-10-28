from pathlib import Path
import bpy,json
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view
root=Path(__file__).resolve().parents[2]
items=[('computer','ComputerDesk/output/computer_desk.blend','Desk tabletop',3),('controller','CROWSController/output/controller_desk.blend','Desk tabletop',3),('radio','CollinsRadioDesk/collins_radio_desk.blend','Desk_top',1.5),('gunsmith','GunBench/gun_bench.blend','01_Worktop',4.5),('clothing','ClothingBench/clothing_bench.blend','01_Worktable',4.5)]
result={}
for key,path,name,size in items:
    bpy.ops.wm.open_mainfile(filepath=str(root/'ArtSource'/path))
    scene=bpy.context.scene;bpy.context.view_layer.update()
    obj=scene.objects[name];points=[obj.matrix_world@v.co for v in obj.data.vertices]
    anchor=Vector(((min(v.x for v in points)+max(v.x for v in points))/2,(min(v.y for v in points)+max(v.y for v in points))/2,max(v.z for v in points)))
    views={}
    for cam in scene.objects:
        if cam.type!='CAMERA' or cam.name=='Controller_UI':continue
        assert abs(cam.data.ortho_scale-size)<1e-5,(key,cam.name,'draw size / camera scale mismatch')
        uv=world_to_camera_view(scene,cam,anchor)
        views[cam.name]=dict(anchor_uv=[uv.x,uv.y],draw_offset=[(.5-uv.x)*size,0,(.5-uv.y)*size],camera_scale=cam.data.ortho_scale)
    assert len(views)==4,(key,list(views))
    result[key]=dict(source=path,tabletop=name,anchor=list(anchor),draw_size=size,views=views)
(root/'ArtSource/BuildingAlignment/measurements.json').write_text(json.dumps(result,indent=2)+'\n')
print('DESK_MEASUREMENTS_PASS')
