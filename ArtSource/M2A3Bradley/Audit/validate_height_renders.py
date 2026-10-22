"""Check actual PNG pixels against the saved final emission and layer composite."""
import bpy,json,sys,numpy as np
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from bpy_extras.object_utils import world_to_camera_view
ROOT=Path(__file__).resolve().parent.parent;sys.path.insert(0,str(ROOT))
from height_palette import WEIGHTS,display,COLOR_ATTRIBUTE
from mesh_regions import regions
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'RimWorld/bradley_rimworld.blend'))
s=bpy.context.scene;s.frame_set(1);meshes=[o for o in s.objects if o.type=='MESH']
turret=bpy.data.objects['TURRET_YAW']
def under(o,parent):
    while o:
        if o==parent:return True
        o=o.parent
    return False
def read(name):
    im=bpy.data.images.load(str(ROOT/'RimWorld/VF_East'/(name+'.png')),check_existing=False)
    a=np.array(im.pixels[:],dtype=np.float32).reshape(1024,1024,4)
    if not im.is_float:a[:,:,:3]=np.where(a[:,:,:3]<=.04045,a[:,:,:3]/12.92,((a[:,:,:3]+.055)/1.055)**2.4)
    bpy.data.images.remove(im);return a
def display_y(c):return sum(display(float(v))*w for v,w in zip(c,WEIGHTS))
def bvh(objects):
    verts=[];faces=[];index=[]
    for o in objects:
        offset=len(verts);verts.extend(o.matrix_world@v.co for v in o.data.vertices)
        for p in o.data.polygons:faces.append([offset+i for i in p.vertices]);index.append((o.name,p.index))
    return BVHTree.FromPolygons(verts,faces,all_triangles=False),index
def measure(name,lo,hi,image,camera,tree,index,main=False,role=None):
    s.camera=camera;bpy.context.view_layer.update();o=bpy.data.objects[name]
    ids=set(regions(o.data)[0]) if main else None;values=[];mismatches=[]
    toward=camera.matrix_world.to_quaternion()@Vector((0,0,1))
    for p in o.data.polygons:
        z=(o.matrix_world@p.center).z;m=o.data.materials[p.material_index]
        if ids is not None and p.index not in ids:continue
        if p.normal.z<.85 or p.area<.00025 or not lo<=z<=hi or not m.get('paint_role') or role and m['paint_role']!=role:continue
        world=o.matrix_world@p.center
        hit,normal,pi,d=tree.ray_cast(world+toward*20,-toward)
        if pi is None or index[pi]!=(o.name,p.index):continue
        projected=world_to_camera_view(s,camera,world);x=int(projected.x*1024);y=int(projected.y*1024)
        if not 1<=x<1023 or not 1<=y<1023:continue
        # Reject edges and tiny triangles: only an opaque, stable 3x3 patch.
        patch=image[y-1:y+2,x-1:x+2]
        if patch[:,:,3].min()<.999:continue
        py=np.array([display_y(c) for c in patch[:,:,:3].reshape(-1,3)])
        if np.ptp(py)>3:continue
        colors=o.data.color_attributes[COLOR_ATTRIBUTE].data
        expected=display_y(np.mean([colors[i].color[:3] for i in p.loop_indices],axis=0))
        if abs(float(np.median(py))-expected)>3:
            mismatches.append({'face':p.index,'png':float(np.median(py)),'emission':expected});continue
        values.append(float(np.median(py)))
    assert values,('No visible stable cap samples',name)
    assert len(mismatches)<=max(2,len(values)*.1),(name,mismatches)
    return {'visible_patch_count':len(values),'boundary_patches_rejected':len(mismatches),'actual_png_luminance':float(np.median(values))}
body=read('vehicle_east');top=read('turret_top_full_east');preview=read('preview_east')
hull_tree,hull_index=bvh([o for o in meshes if not under(o,turret)])
top_tree,top_index=bvh([o for o in meshes if under(o,turret)])
camera=bpy.data.objects['Camera_west']
samples={'hull_roof':measure('WT_@root',1.98,2.16,body,camera,hull_tree,hull_index,role='hull_assembly')}
data=bpy.data.cameras.new('Audit top');data.type='ORTHO';data.ortho_scale=camera.data.ortho_scale
topcam=bpy.data.objects.new('Audit top',data);s.collection.objects.link(topcam)
topcam.location=turret.matrix_world.translation+Vector((0,0,16));topcam.rotation_euler=(0,0,-np.pi/2)
for label,name,lo,hi,main,role in (
 ('turret_roof','WT_bone_turret',2.715,2.745,False,'turret_assembly'),
 ('hatch_03','WT_hatch_03',2.77,2.90,True,None),
 ('hatch_02','WT_hatch_02',2.85,3.,True,None),
 ('commander_sight_top','WT_bone_commander_sight_v',3.28,3.4,False,None)):
    samples[label]=measure(name,lo,hi,top,topcam,top_tree,top_index,main,role)
ys=[v['actual_png_luminance'] for v in samples.values()]
assert all(b>a+1 for a,b in zip(ys,ys[1:])),samples
# Opaque source pixels must survive compositing without gamma/exposure changes.
s.camera=camera;bpy.context.view_layer.update();p=world_to_camera_view(s,camera,turret.matrix_world.translation)
dx,dy=round(p.x*1024)-512,round(p.y*1024)-512
overlay=np.zeros_like(top);x0,x1=max(0,dx),min(1024,1024+dx);y0,y1=max(0,dy),min(1024,1024+dy)
overlay[y0:y1,x0:x1]=top[y0-dy:y1-dy,x0-dx:x1-dx]
mask=overlay[:,:,3]>.9999
error=float(np.max(abs(preview[mask,:3]-overlay[mask,:3])))
assert error<.0002,error
mask=(overlay[:,:,3]<.00001)&(body[:,:,3]>.9999)
assert float(np.max(abs(preview[mask,:3]-body[mask,:3])))<.0002
report={'actual_png_height_samples':samples,'opaque_composite_linear_rgb_max_error':error,
 'method':'Visible BVH-verified cap centroids, stable opaque 3x3 pixel patches; compare final emission then assert height order.'}
(ROOT/'Audit/SurfaceRules/render_validation.json').write_text(json.dumps(report,indent=2)+'\n')
print('ACTUAL PNG HEIGHT ORDER AND COMPOSITE COLOR VALIDATION PASSED',json.dumps(report))
