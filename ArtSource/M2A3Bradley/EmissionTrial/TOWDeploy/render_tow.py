"""Render isolated deployment motion and aligned independent TOW sprite layers."""
import bpy,json,sys,os,tempfile,numpy as np
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent;TRIAL=HERE.parent
cfg=json.loads((HERE/'settings.json').read_text(encoding='utf-8-sig'))
def load_full():
    bpy.ops.wm.open_mainfile(filepath=str(HERE/'bradley_tow_deploy.blend'))
    s=bpy.context.scene;s.frame_set(1)
    for o in s.objects:
        if o.name!=cfg['control'] and o.animation_data:o.animation_data.action=None
    s.eevee.taa_render_samples=128;s.render.resolution_percentage=100;s.render.film_transparent=True
    return s
s=load_full();meshes=[o for o in s.objects if o.type=='MESH']
def under(obj):
    while obj:
        if obj.name=='TURRET_YAW':return True
        obj=obj.parent
    return False
turret=[o for o in meshes if under(o) and not o.name.startswith('WT_antenna')]
moving=[bpy.data.objects[n] for n in cfg['moving_objects']];fixed=[o for o in turret if o not in moving]
report={}
with tempfile.TemporaryDirectory(prefix='.render-',dir=HERE) as staging:
    stage=Path(staging)
    cam=bpy.data.objects['Camera_iso_front'];center=Vector((-.1,.35,2.8));cam.location=center+Vector((6,-7,5));cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=4.1;s.camera=cam
    s.render.resolution_x=s.render.resolution_y=768
    for o in meshes:o.hide_render=o not in turret
    for name,frame in [('stowed',20),('half',40),('deployed',60)]:
        s.frame_set(frame);s.render.filepath=str(stage/(name+'.png'));bpy.ops.render.render(write_still=True)
    data=bpy.data.cameras.new('TOW VF top');data.type='ORTHO';data.ortho_scale=bpy.data.objects['Camera_west'].data.ortho_scale
    top=bpy.data.objects.new('TOW VF top',data);s.collection.objects.link(top);top.location=bpy.data.objects['TURRET_YAW'].matrix_world.translation+Vector((0,0,16));top.rotation_euler=(0,0,-np.pi/2);s.camera=top
    s.render.resolution_x=s.render.resolution_y=1024
    hold=bpy.data.materials.new('Layer occlusion holdout');hold.use_nodes=True;nodes=hold.node_tree.nodes;nodes.clear();h=nodes.new('ShaderNodeHoldout');out=nodes.new('ShaderNodeOutputMaterial');hold.node_tree.links.new(h.outputs[0],out.inputs['Surface'])
    def render(name):
        s.render.filepath=str(stage/(name+'.png'));bpy.ops.render.render(write_still=True)
    s.frame_set(20)
    for o in moving:o.hide_render=True
    render('turret_base_top_east')
    slots={o.name:list(o.data.materials) for o in fixed}
    for name,frame in [('stowed',20),('deployed',60)]:
        s.frame_set(frame)
        for o in moving:o.hide_render=False
        render('turret_'+name+'_top_east')
        # Base stays present as a holdout so the independent moving sprite has
        # correct occlusion at the hinge, rather than drawing hidden surfaces.
        for o in fixed:
            for i in range(len(o.data.materials)):o.data.materials[i]=hold
        render('tow_'+name+'_top_east')
        for o in fixed:
            for i,m in enumerate(slots[o.name]):o.data.materials[i]=m
    def read(path):
        im=bpy.data.images.load(str(path),check_existing=False);a=np.array(im.pixels[:],dtype=np.float32).reshape(1024,1024,4)
        if not im.is_float:a[:,:,:3]=np.where(a[:,:,:3]<=.04045,a[:,:,:3]/12.92,((a[:,:,:3]+.055)/1.055)**2.4)
        bpy.data.images.remove(im);return a
    base=read(stage/'turret_base_top_east.png');body=read(TRIAL/'VF_East/vehicle_east.png')
    anchor=json.loads((TRIAL/'VF_East/render_report.json').read_text())['settings']['turret_pivot_pixels_top_origin']
    dx=round(anchor[0])-512;dy=round(1024-anchor[1])-512
    for state in ['stowed','deployed']:
        layer=read(stage/('tow_'+state+'_top_east.png'));ref=read(stage/('turret_'+state+'_top_east.png'))
        mask=(layer[:,:,3]>.9999)|(layer[:,:,3]<.00001)
        alpha=layer[:,:,3:4]+base[:,:,3:4]*(1-layer[:,:,3:4])
        rgb=np.divide(layer[:,:,:3]*layer[:,:,3:4]+base[:,:,:3]*base[:,:,3:4]*(1-layer[:,:,3:4]),alpha,out=np.zeros_like(base[:,:,:3]),where=alpha>0)
        reconstructed=np.concatenate((rgb,alpha),axis=2)
        opaque=mask&(ref[:,:,3]>.9999)&(alpha[:,:,0]>.9999)
        edge_error=float(np.max(abs(reconstructed[opaque,:3]-ref[opaque,:3])))
        stable=opaque.copy();lo=ref[:,:,:3].copy();hi=lo.copy()
        for dy0 in [-1,0,1]:
            for dx0 in [-1,0,1]:
                stable&=np.roll(opaque,(dy0,dx0),axis=(0,1))
                sample=np.roll(ref[:,:,:3],(dy0,dx0),axis=(0,1));lo=np.minimum(lo,sample);hi=np.maximum(hi,sample)
        stable&=np.max(hi-lo,axis=2)<.0001
        stable[:2]=False;stable[-2:]=False;stable[:,:2]=False;stable[:,-2:]=False
        assert stable.sum()>100
        error=float(np.max(abs(reconstructed[stable,:3]-ref[stable,:3])))
        def display_rgb(rgb):return 255*np.where(rgb<=.0031308,12.92*rgb,1.055*np.maximum(0,rgb)**(1/2.4)-.055)
        display_error=float(np.max(abs(display_rgb(reconstructed[stable,:3])-display_rgb(ref[stable,:3]))))
        assert display_error<1,(state,display_error)
        ys,xs=np.where(ref[:,:,3]>.01);assert min(xs)>8 and max(xs)<1015 and min(ys)>8 and max(ys)<1015
        report[state]={'stable_opaque_layer_reconstruction_max_linear_rgb_error':error,'max_stable_display_channel_error_0_to_255':display_error,'stable_pixels':int(stable.sum()),
            'including_antialias_boundary_max_error':edge_error,'bounds':[int(min(xs)),int(min(ys)),int(max(xs)),int(max(ys))]}
        overlay=np.zeros_like(ref);x0,x1=max(0,dx),min(1024,1024+dx);y0,y1=max(0,dy),min(1024,1024+dy)
        overlay[y0:y1,x0:x1]=ref[y0-dy:y1-dy,x0-dx:x1-dx]
        a=overlay[:,:,3:4];b=body[:,:,3:4];aa=a+b*(1-a)
        rr=np.divide(overlay[:,:,:3]*a+body[:,:,:3]*b*(1-a),aa,out=np.zeros_like(body[:,:,:3]),where=aa>0)
        result=np.concatenate((rr*aa,aa),axis=2)
        im=bpy.data.images.new('TOW mixed preview',width=1024,height=1024,alpha=True,float_buffer=True);im.alpha_mode='PREMUL';im.pixels.foreach_set(result.ravel());im.save_render(str(stage/('preview_'+state+'_east.png')),scene=s);bpy.data.images.remove(im)
    report['pivot']={'size':[1024,1024],'top_layer_pivot':[512,512],'layer_origin':'turret yaw pivot, identical for base and both TOW states','hull_anchor':anchor,'hull_elevation':64,'turret_elevation':90}
    (stage/'render_validation.json').write_text(json.dumps(report,indent=2)+'\n')
    bpy.ops.wm.open_mainfile(filepath=str(HERE/'tow_launcher.blend'));s=bpy.context.scene;s.render.resolution_x=s.render.resolution_y=384;s.eevee.taa_render_samples=128
    motion=stage/'Motion';motion.mkdir()
    for i,frame in enumerate([20,28,36,44,52,60,90,98,106,114,120]):
        s.frame_set(frame);s.render.filepath=str(motion/('%02d.png'%i));bpy.ops.render.render(write_still=True)
    for p in stage.rglob('*'):
        if p.is_file():
            dst=HERE/p.relative_to(stage);dst.parent.mkdir(parents=True,exist_ok=True);os.replace(p,dst)
print('TOW MOTION AND INDEPENDENT LAYERS RENDERED',json.dumps(report))
