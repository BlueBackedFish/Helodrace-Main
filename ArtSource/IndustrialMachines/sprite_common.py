"""Fixed-color building sprite primitives and per-asset export, Blender 5.1."""
import bpy, math, os, json
import numpy as np
from mathutils import Vector

def mat(name,rgb,tint=True):
    if isinstance(rgb,(int,float)):rgb=(rgb,)*3
    def lin(c):
        c/=255
        return c/12.92 if c<=.04045 else ((c+.055)/1.055)**2.4
    m=bpy.data.materials.new(name);m.use_nodes=True;m['tintable']=tint
    m.diffuse_color=tuple(lin(c) for c in rgb)+(1,)
    m.node_tree.nodes.clear();e=m.node_tree.nodes.new('ShaderNodeEmission')
    e.inputs[0].default_value=m.diffuse_color
    output=m.node_tree.nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(e.outputs[0],output.inputs[0])
    return m

def pal(name,values,tint=True):return [mat(name+str(i),c,tint) for i,c in enumerate(values)]

def start():
    global scene,bed,cast,steel,dark,brass,copper,rubber
    bpy.ops.wm.read_factory_settings(use_empty=True);scene=bpy.context.scene
    bed=pal('Foundation_', [184,171,163]);cast=pal('Cast_', [216,204,194])
    steel=pal('Steel_', [234,223,213]);dark=pal('Recess_', [137,128,121])
    brass=pal('Brass_',[(210,170,91),(190,151,74),(180,141,65)],False)
    copper=pal('Copper_',[(159,107,60),(143,91,48),(133,83,43)],False)
    rubber=pal('Insulator_', [55,49,45],False)

def period_start():
    """Neutral metal values matched to neighboring workshop sprites."""
    global bed,cast,steel,dark,brass,copper,stone,pit
    start()
    bed=pal('Cast_bed_', [167,141,126])
    cast=pal('Neutral_cast_iron_', [211,191,176])
    steel=pal('Machined_iron_', [238,217,199])
    dark=pal('Cast_recess_', [83,70,61])
    brass=pal('Brass_fitting_',[(202,161,83),(184,143,69),(175,135,62)],False)
    copper=pal('Copper_winding_',[(164,113,66),(147,96,52),(138,88,46)],False)
    stone=pal('Concrete_', [154,137,123],False)
    pit=pal('Pit_interior_', [49,47,41],False)
    scene['outline_pixels']=12
    scene['presentation_rule']='one_model_unit_per_game_tile'

def gauge(name,p,r=.09):
    """Large pressure dial facing the working side (-Y)."""
    x,y,z=p
    cyl(name+'_rim',p,r,.040,dark,'Y')
    cyl(name+'_face',(x,y-.025,z),r*.78,.012,steel,'Y')
    rod(name+'_needle',(x,y-.036,z),(x+r*.42,y-.036,z+r*.37),r*.095,dark)
    cyl(name+'_hub',(x,y-.040,z),r*.15,.016,dark,'Y')

def belt_drive(name,a,b,r1,r2):
    """Two pulleys and broad belt runs, all in one X/Z plane."""
    a,b=Vector(a),Vector(b)
    cyl(name+'_driver',a,r1,.07,dark,'Y')
    cyl(name+'_driven',b,r2,.07,dark,'Y')
    direction=(b-a).normalized();side=Vector((-direction.z,0,direction.x))
    for sign in [-1,1]:rod(name+'_belt',a+sign*side*r1,b+sign*side*r2,.018,rubber)

def outline_file(color_path,mask_path,radius):
    """Black alpha-dilation beneath the render; mask stroke stays unselected."""
    def read(path):
        im=bpy.data.images.load(path,check_existing=False);im.colorspace_settings.name='Non-Color'
        w,h=im.size;p=np.empty(w*h*4,np.float32);im.pixels.foreach_get(p)
        return p.reshape(h,w,4)
    color,mask=read(color_path),read(mask_path)
    assert np.max(abs(color[:,:,3]-mask[:,:,3]))<.001
    h,w=color.shape[:2];a=color[:,:,3];padded=np.pad(a,radius);grown=np.zeros_like(a)
    for dy in range(-radius,radius+1):
        for dx in range(-radius,radius+1):
            if dx*dx+dy*dy<=radius*radius:
                np.maximum(grown,padded[radius+dy:radius+dy+h,radius+dx:radius+dx+w],out=grown)
    ratio=np.divide(a,grown,out=np.zeros_like(a),where=grown>0)
    for pixels,path in [(color,color_path),(mask,mask_path)]:
        result=pixels.copy();result[:,:,:3]*=ratio[:,:,None];result[:,:,3]=grown
        im=bpy.data.images.new('Outlined_export',w,h,alpha=True);im.colorspace_settings.name='Non-Color'
        im.pixels.foreach_set(result.ravel());im.file_format='PNG';im.filepath_raw=path;im.save()

def quad(name,points,material):
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(points,[],[tuple(range(len(points)))]);mesh.update()
    obj=bpy.data.objects.new(name,mesh);scene.collection.objects.link(obj);mesh.materials.append(material)
    return obj

def vessel(name,p,r,straight,cap,mats):
    """Closed upright vessel with broad dished heads and a straight jacket."""
    profile=[(0,-straight/2-cap),(.50*r,-straight/2-.866*cap),(.866*r,-straight/2-.50*cap),
             (r,-straight/2),(r,straight/2),(.866*r,straight/2+.50*cap),
             (.50*r,straight/2+.866*cap),(0,straight/2+cap)]
    n=40;verts=[(p[0]+radius*math.cos(i*math.tau/n),p[1]+radius*math.sin(i*math.tau/n),p[2]+z) for radius,z in profile for i in range(n)]
    faces=[];colors=[]
    for k in range(len(profile)-1):
        for i in range(n):
            j=(i+1)%n;faces.append((k*n+i,k*n+j,(k+1)*n+j,(k+1)*n+i))
            colors.append(0 if k>=4 else (2 if k<2 else 1))
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    obj=bpy.data.objects.new(name,mesh);scene.collection.objects.link(obj)
    for m in mats:mesh.materials.append(m)
    for f,c in zip(mesh.polygons,colors):f.material_index=c
    return obj

def handwheel(name,p,r=.12,axis='Z'):
    ring(name+'_rim',p,r,r*.74,.028,brass,axis)
    cyl(name+'_hub',p,r*.22,.045,brass,axis)
    for i in range(4):
        a=i*math.tau/4
        d=Vector((math.cos(a),math.sin(a),0)) if axis=='Z' else Vector((math.cos(a),0,math.sin(a)))
        rod(name+'_spoke',Vector(p),Vector(p)+d*r*.86,.015,brass)

def arch(name,p,outer,inner,depth,mats):
    """Upper half of a cast magnetic yoke, in the Y/Z plane."""
    n=20;verts=[];faces=[]
    for dx in [-depth/2,depth/2]:
        for radius in [outer,inner]:
            verts.extend([(p[0]+dx,p[1]+radius*math.cos(i*math.pi/n),p[2]+radius*math.sin(i*math.pi/n)) for i in range(n+1)])
    k=n+1
    for i in range(n):
        j=i+1
        faces.extend([(i,j,2*k+j,2*k+i),(k+i,3*k+i,3*k+j,k+j),
                      (i,k+i,k+j,j),(2*k+i,2*k+j,3*k+j,3*k+i)])
    faces.extend([(0,2*k,3*k,k),(n,k+n,3*k+n,2*k+n)])
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    obj=bpy.data.objects.new(name,mesh);scene.collection.objects.link(obj)
    for m in mats:mesh.materials.append(m)
    for f in mesh.polygons:f.material_index=0 if abs(f.normal.x)>.9 else 1
    return obj

def cube(name,p,size,mats):
    bpy.ops.mesh.primitive_cube_add(size=1,location=p);o=bpy.context.object;o.name=name;o.dimensions=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    for m in mats:o.data.materials.append(m)
    for f in o.data.polygons:f.material_index=0 if f.normal.z>.5 else (1 if abs(f.normal.y)>.5 else 2)
    return o

def cyl(name,p,r,length,mats,axis='Z',vertices=32,large=False):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=r,depth=length,location=p)
    o=bpy.context.object;o.name=name
    if axis=='X':o.rotation_euler.y=math.pi/2
    elif axis=='Y':o.rotation_euler.x=math.pi/2
    for m in mats:o.data.materials.append(m)
    q=o.rotation_euler.to_quaternion()
    for f in o.data.polygons:
        cap=abs(f.normal.z)>.9
        f.material_index=0 if cap else (0 if large and (q@f.normal).z>.4 else 1)
    return o

def rod(name,a,b,r,mats):
    a,b=Vector(a),Vector(b);o=cyl(name,(a+b)/2,r,(b-a).length,mats,vertices=12)
    o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler();return o

def bar(name,a,b,width,depth,mats):
    """Rectangular structural beam between two world-space joints."""
    a,b=Vector(a),Vector(b);o=cube(name,(a+b)/2,(width,depth,(b-a).length),mats)
    o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler();q=o.rotation_euler.to_quaternion()
    for f in o.data.polygons:
        n=q@f.normal;f.material_index=0 if n.z>.4 else (1 if abs(n.y)>.5 else 2)
    return o

def side_profile(name,points,depth,mats,y=0):
    """Extrude a coarse X/Z silhouette across Y, for cast machine plates."""
    n=len(points);verts=[(x,y+dy,z) for dy in [-depth/2,depth/2] for x,z in points]
    faces=[tuple(range(n-1,-1,-1)),tuple(range(n,2*n))]
    faces.extend([(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)])
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    obj=bpy.data.objects.new(name,mesh);scene.collection.objects.link(obj)
    for m in mats:mesh.materials.append(m)
    for f in mesh.polygons:f.material_index=0 if f.normal.z>.4 else (1 if abs(f.normal.y)>.5 else 2)
    return obj

def path(name,points,r,mats):
    for a,b in zip(points,points[1:]):rod(name,a,b,r,mats)

def sphere(name,p,r,mats):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12,ring_count=6,radius=r,location=p)
    o=bpy.context.object;o.name=name;o.data.materials.append(mats[0]);return o

def ring(name,p,r,inner,depth,mats,axis='Y'):
    n=40;verts=[]
    for z in [-depth/2,depth/2]:
        for radius in [r,inner]:
            verts.extend([(radius*math.cos(i*math.tau/n),radius*math.sin(i*math.tau/n),z) for i in range(n)])
    faces=[];colors=[]
    for i in range(n):
        j=(i+1)%n
        faces.extend([(i,j,2*n+j,2*n+i),(n+i,3*n+i,3*n+j,n+j),
                      (i,n+i,n+j,j),(2*n+i,2*n+j,3*n+j,3*n+i)])
        colors.extend([1,2,0,0])
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    o=bpy.data.objects.new(name,mesh);scene.collection.objects.link(o);o.location=p
    if axis=='Y':o.rotation_euler.x=math.pi/2
    elif axis=='X':o.rotation_euler.y=math.pi/2
    for m in mats:mesh.materials.append(m)
    for f,c in zip(mesh.polygons,colors):f.material_index=c
    return o

def wheel(name,p,r,mats,axis='Y',spokes=8):
    ring(name+'_rim',p,r,r*.86,.13,mats,axis)
    cyl(name+'_hub',p,r*.14,.23,steel,axis)
    for i in range(spokes):
        a=i*math.tau/spokes
        if axis=='Y':direction=Vector((math.cos(a),0,math.sin(a)))
        else:direction=Vector((0,math.cos(a),math.sin(a)))
        rod(name+'_spoke',Vector(p)+direction*r*.12,Vector(p)+direction*r*.89,r*.043,mats)

def governor(name,p,height=.50):
    x,y,z=p
    cyl(name+'_base',(x,y,z+.035),.075,.07,brass)
    cyl(name+'_spindle',(x,y,z+height/2),.028,height,brass)
    for dz in [.14,height-.10]:cyl(name+'_collar',(x,y,z+dz),.052,.04,brass)
    for dx in [-.18,.18]:
        rod(name+'_arm',(x,y,z+height-.03),(x+dx,y,z+height*.56),.015,brass)
        sphere(name+'_ball',(x+dx,y,z+height*.56),.065,brass)
    sphere(name+'_cap',(x,y,z+height),.045,brass)

def rivets(name,points,axis='Z',r=.023):
    for p in points:cyl(name,p,r,.014,dark,axis,vertices=12)

def bounds(o):
    p=[o.matrix_world@v.co for v in o.data.vertices]
    return [[min(v[i] for v in p),max(v[i] for v in p)] for i in range(3)]

def export(out,stem,footprint,features,blend_name=None):
    tex=os.path.abspath(os.path.join(out,'../../Textures/Buildings'))
    bpy.context.view_layer.update()
    meshes=[o for o in scene.objects if o.type=='MESH']
    points=[o.matrix_world@v.co for o in meshes for v in o.data.vertices]
    lo=Vector([min(p[i] for p in points) for i in range(3)])
    hi=Vector([max(p[i] for p in points) for i in range(3)])
    calibrated=scene.get('presentation_rule')=='one_model_unit_per_game_tile'
    target=Vector((0,0,0)) if calibrated else (lo+hi)/2
    if not calibrated:target.z*=.80
    dirs=[('North',-90),('East',0),('South',90)];cameras=[]
    span=0
    for name,az in dirs:
        a=math.radians(az);e=math.radians(64)
        data=bpy.data.cameras.new('Camera_'+name);data.type='ORTHO'
        c=bpy.data.objects.new(data.name,data);scene.collection.objects.link(c)
        c.location=target+10*Vector((math.cos(a)*math.cos(e),math.sin(a)*math.cos(e),math.sin(e)))
        c.rotation_euler=(target-c.location).to_track_quat('-Z','Y').to_euler()
        q=c.rotation_euler.to_quaternion();right=q@Vector((1,0,0));up=q@Vector((0,1,0))
        span=max(span,max(max(abs((p-target).dot(right)),abs((p-target).dot(up))) for p in points))
        cameras.append(c)
    if calibrated:
        # Extra headroom changes canvas and drawSize together, never model-to-tile size.
        required=2*span/(1-2*(24+scene.get('outline_pixels',0))/512)
        scale=math.ceil(max(max(footprint)*1.5,required)*4)/4
    else:scale=span*2*1.15
    assert (scale/2-span)*512/scale>=24+scene.get('outline_pixels',0), 'Increase canvas size or reduce outlying geometry before rendering'
    for c in cameras:c.data.ortho_scale=scale
    scene.render.engine='BLENDER_EEVEE';scene.render.film_transparent=True
    scene.render.resolution_x=scene.render.resolution_y=512;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA'
    scene.view_settings.view_transform='Standard';scene.view_settings.look='None'
    scene.view_settings.exposure=0;scene.view_settings.gamma=1
    red=mat('MASK_red',(255,0,0));black=mat('MASK_unselected',(0,0,0))
    originals={o.name:list(o.data.materials) for o in meshes}
    for c in cameras:
        scene.camera=c;direction=c.name.removeprefix('Camera_').lower()
        scene.render.filepath=os.path.join(tex,stem+'_'+direction+'.png');bpy.ops.render.render(write_still=True)
        for o in meshes:
            for i,m in enumerate(originals[o.name]):o.data.materials[i]=red if m['tintable'] else black
        scene.render.filepath=os.path.join(tex,stem+'_'+direction+'m.png');bpy.ops.render.render(write_still=True)
        for o in meshes:
            for i,m in enumerate(originals[o.name]):o.data.materials[i]=m
        if scene.get('outline_pixels',0):
            outline_file(os.path.join(tex,stem+'_'+direction+'.png'),scene.render.filepath,int(scene['outline_pixels']))
    scene.camera=cameras[0]
    reference=os.path.join(out,'Reference')
    if os.path.isdir(reference):
        for file in os.listdir(reference):
            if file.endswith('.png'):bpy.data.images.load(os.path.join(reference,file)).pack()
    bpy.context.preferences.filepaths.save_version=0
    blend=os.path.join(out,blend_name or stem+'.blend');bpy.ops.wm.save_as_mainfile(filepath=blend)
    bpy.ops.wm.open_mainfile(filepath=blend)
    previews=[];checks={}
    for name,az in dirs:
        def pixels(suffix):
            im=bpy.data.images.load(os.path.join(tex,stem+'_'+name.lower()+suffix+'.png'),check_existing=False)
            assert tuple(im.size)==(512,512)
            im.colorspace_settings.name='Non-Color';p=np.empty(512*512*4,np.float32)
            im.pixels.foreach_get(p);return p.reshape(512,512,4)
        p=pixels('');m=pixels('m');assert np.max(abs(p[:,:,3]-m[:,:,3]))<.001
        y,x=np.where(p[:,:,3]>.5);margin=int(min(x.min(),y.min(),511-x.max(),511-y.max()));assert margin>=24
        c=bpy.data.objects['Camera_'+name];forward=c.rotation_euler.to_quaternion()@Vector((0,0,-1))
        angle=math.degrees(math.asin(-forward.z));assert abs(angle-64)<.001
        checks[name]={'downward_degrees':angle,'minimum_margin':margin,'mask_alpha_matches':True}
        previews.append(p)
    im=bpy.data.images.new('North_East_South',1536,512,alpha=True);im.colorspace_settings.name='Non-Color'
    im.pixels.foreach_set(np.concatenate(previews,axis=1).ravel());im.file_format='PNG'
    im.filepath_raw=os.path.join(out,'preview_north_east_south.png');im.save()
    im.scale(768,256);im.filepath_raw=os.path.join(out,'preview_256px.png');im.save()
    count=len([o for o in bpy.context.scene.objects if o.type=='MESH'])
    with open(os.path.join(out,'validation.json'),'w') as f:
        json.dump({'asset':stem,'footprint':footprint,'features':features,'mesh_objects':count,
                   'design_period':bpy.context.scene.get('design_period',''),
                   'orthographic_scale':scale,'outline_pixels':bpy.context.scene.get('outline_pixels',0),
                   'expected_draw_size':[scale,scale],
                   'presentation_rule':bpy.context.scene.get('presentation_rule','auto_fit'),
                   'ground_anchor':[float(v) for v in target],
                   'cameras':checks,'in_game_tested':False},f,indent=2)
