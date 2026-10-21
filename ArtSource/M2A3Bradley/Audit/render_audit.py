"""Read-only orthographic audit of the saved model against the supplied blueprint."""
import bpy, os, json
from mathutils import Vector
OUT=os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT,'..','m2a3_bradley.blend'))
s=bpy.context.scene;s.frame_set(1)
s.render.resolution_x=1200;s.render.resolution_y=1200;s.render.resolution_percentage=100
s.render.film_transparent=True
target=Vector((0,0,1.8))
for name,direction in [('top',(0,0,1)),('side_right',(1,0,0)),('front',(0,-1,0)),('rear',(0,1,0))]:
    data=bpy.data.cameras.new(name);data.type='ORTHO';data.ortho_scale=8.3
    cam=bpy.data.objects.new(name,data);s.collection.objects.link(cam)
    cam.location=target+Vector(direction)*15
    cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
    s.camera=cam;s.render.filepath=os.path.join(OUT,name+'.png')
    bpy.ops.render.render(write_still=True)
report={}
for name in ['Main hull sloped glacis','Faceted turret armor','Turret roof','Twin TOW launcher','Gun mantlet','Rear roof hatch','Personnel door']:
    o=bpy.data.objects[name]
    points=[o.matrix_world@Vector(p) for p in o.bound_box]
    report[name]={'min':[round(min(p[i] for p in points),3) for i in range(3)],'max':[round(max(p[i] for p in points),3) for i in range(3)]}
with open(os.path.join(OUT,'bounds.json'),'w') as f:json.dump(report,f,indent=2)
