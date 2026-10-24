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

def export(out,stem,footprint,features):
    tex=os.path.abspath(os.path.join(out,'../../Textures/Buildings'))
    bpy.context.view_layer.update()
    meshes=[o for o in scene.objects if o.type=='MESH']
    points=[o.matrix_world@v.co for o in meshes for v in o.data.vertices]
    lo=Vector([min(p[i] for p in points) for i in range(3)])
    hi=Vector([max(p[i] for p in points) for i in range(3)])
    target=(lo+hi)/2;target.z*=.80
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
    scale=span*2*1.15
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
    scene.camera=cameras[0]
    reference=os.path.join(out,'Reference')
    if os.path.isdir(reference):
        for file in os.listdir(reference):
            if file.endswith('.png'):bpy.data.images.load(os.path.join(reference,file)).pack()
    bpy.context.preferences.filepaths.save_version=0
    blend=os.path.join(out,stem+'.blend');bpy.ops.wm.save_as_mainfile(filepath=blend)
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
                   'orthographic_scale':scale,'cameras':checks,'in_game_tested':False},f,indent=2)

