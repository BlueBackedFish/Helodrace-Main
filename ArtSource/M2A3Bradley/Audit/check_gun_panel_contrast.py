"""Validate the exact 79-face edit and preservation of all other corner colors."""
import bpy,sys,json,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parent.parent;OUT=ROOT/'Audit/GunPanels';sys.path.insert(0,str(ROOT))
from local_panel_paint import panel_faces,LOCAL_ATTRIBUTE
from height_palette import COLOR_ATTRIBUTE,display,WEIGHTS
cfg=json.loads((ROOT/'part_contrast.json').read_text())['height_first_paint']
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'RimWorld/bradley_rimworld.blend'));s=bpy.context.scene;s.frame_set(1)
def snapshot():
    result={}
    for o in s.objects:
        if o.type!='MESH':continue
        ids=set()
        for t in cfg['local_raised_panels']['targets']:
            if t['object']==o.name:ids.update(panel_faces(o,t))
        colors=o.data.color_attributes[COLOR_ATTRIBUTE].data
        outside=[tuple(round(c,6) for c in colors[li].color[:3]) for p in o.data.polygons if p.index not in ids for li in p.loop_indices]
        inside=[tuple(round(c,6) for c in colors[li].color[:3]) for i in sorted(ids) for li in o.data.polygons[i].loop_indices]
        result[o.name]={'faces':sorted(ids),'outside_hash':hashlib.sha256(str(outside).encode()).hexdigest(),'inside_hash':hashlib.sha256(str(inside).encode()).hexdigest(),
          'luminance_range':[min(sum(display(c)*w for c,w in zip(rgb,WEIGHTS)) for rgb in inside),max(sum(display(c)*w for c,w in zip(rgb,WEIGHTS)) for rgb in inside)] if inside else None}
    return result
current=snapshot()
if '--capture' in sys.argv:
    (OUT/'before_colors.json').write_text(json.dumps(current,indent=2)+'\n');print('GUN PANEL BASELINE CAPTURED');sys.exit(0)
before=json.loads((OUT/'before_colors.json').read_text());assert current.keys()==before.keys()
for name,record in current.items():
    assert record['faces']==before[name]['faces']
    assert record['outside_hash']==before[name]['outside_hash'],('Outside target color changed',name)
    if record['faces']:assert record['inside_hash']!=before[name]['inside_hash']
assert sum(len(v['faces']) for v in current.values())==79
coax=current['WT_bone_gun']['luminance_range'];cover=current['WT_ex_decor_01']['luminance_range']
assert 29.99<coax[1]-coax[0]<30.01,'Keep the two-tone housing palette'
assert cover[0]>coax[1]+1 and cover[1]<211
assert coax[1]>before['WT_bone_gun']['luminance_range'][1]+4
report={'baseline_commit':'6a7ab6f6','target_faces':79,'all_other_corner_colors_identical':True,'coax_two_tone_gap_preserved':True,'panels':{n:v for n,v in current.items() if v['faces']}}
if '--pixels' in sys.argv:
    from mathutils import Vector
    from bpy_extras.object_utils import world_to_camera_view
    from statistics import median
    import math
    turret=bpy.data.objects['TURRET_YAW'];data=bpy.data.cameras.new('Panel PNG audit');data.type='ORTHO';data.ortho_scale=bpy.data.objects['Camera_west'].data.ortho_scale
    camera=bpy.data.objects.new('Panel PNG audit',data);s.collection.objects.link(camera)
    camera.location=turret.matrix_world.translation+Vector((0,0,16));camera.rotation_euler=(0,0,-math.pi/2);s.camera=camera;bpy.context.view_layer.update()
    im=bpy.data.images.load(str(ROOT/'RimWorld/VF_East/barrel_top_east.png'),check_existing=False)
    assert im.is_float and im.size[:]==(1024,1024)
    rgba=im.pixels[:];samples={}
    for name,face in (('WT_bone_gun',845),('WT_ex_decor_01',0)):
        o=bpy.data.objects[name];p=o.data.polygons[face];v=world_to_camera_view(s,camera,o.matrix_world@p.center);x,y=int(v.x*1024),int(v.y*1024)
        ys=[]
        for py in range(y-1,y+2):
            for px in range(x-1,x+2):
                i=4*(py*1024+px);assert rgba[i+3]>.999
                ys.append(sum(display(rgba[i+k])*w for k,w in enumerate(WEIGHTS)))
        expected=sum(sum(display(c)*w for c,w in zip(o.data.color_attributes[COLOR_ATTRIBUTE].data[li].color[:3],WEIGHTS)) for li in p.loop_indices)/len(p.loop_indices)
        assert abs(median(ys)-expected)<1.5,(name,median(ys),expected)
        samples[name]={'actual_png_luminance':median(ys),'expected':expected,'pixel':[x,1023-y]}
    assert samples['WT_ex_decor_01']['actual_png_luminance']>samples['WT_bone_gun']['actual_png_luminance']+4
    assert samples['WT_ex_decor_01']['actual_png_luminance']<211
    report['actual_png_samples']=samples
(OUT/'validation.json').write_text(json.dumps(report,indent=2)+'\n')
print('LOCAL GUN PANEL CONTRAST AND ALL OTHER COLORS VALIDATED',json.dumps(report))
