"""Fixed-color building sprite primitives and per-asset export, Blender 5.1."""
import bpy, math, os, json, hashlib, tempfile
import numpy as np
from mathutils import Vector

def apply_building_theme(stem):
    from building_theme import apply
    return apply(bpy.context.scene,stem)

def save_preview(im,path):
    """Replace a complete PNG without truncating an image held by a viewer."""
    descriptor,temp=tempfile.mkstemp(prefix='.preview-',suffix='.png',dir=os.path.dirname(path))
    os.close(descriptor)
    try:
        im.file_format='PNG';im.filepath_raw=temp;im.save();os.replace(temp,path)
    finally:
        im.filepath_raw=path
        if os.path.exists(temp):os.remove(temp)

def geometry_hash():
    """Saved mesh transforms, topology, face assignments and fixed material colors."""
    bpy.context.view_layer.update();data=[]
    for o in sorted((o for o in bpy.context.scene.objects if o.type=='MESH'),key=lambda o:o.name):
        data.append([o.name,[list(row) for row in o.matrix_world],[list(v.co) for v in o.data.vertices],
                     [[list(p.vertices),p.material_index] for p in o.data.polygons],
                     [[m.name,list(m.diffuse_color),bool(m.get('tintable',False))] for m in o.data.materials]])
    return hashlib.sha256(json.dumps(data,separators=(',',':')).encode()).hexdigest()

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
    # Color-managed antialiasing can make mixed red/green weights sum above 1.
    # Normalize their overlap before the shader uses them as tint weights.
    mask[:,:,:2]/=np.maximum(1,mask[:,:,:2].sum(axis=2))[:,:,None]
    h,w=color.shape[:2];a=color[:,:,3];padded=np.pad(a,radius);grown=np.zeros_like(a)
    for dy in range(-radius,radius+1):
        for dx in range(-radius,radius+1):
            if dx*dx+dy*dy<=radius*radius:
                np.maximum(grown,padded[radius+dy:radius+dy+h,radius+dx:radius+dx+w],out=grown)
    ratio=np.divide(a,grown,out=np.zeros_like(a),where=grown>0)
    for pixels,path in [(color,color_path),(mask,mask_path)]:
        result=pixels.copy();result[:,:,:3]*=ratio[:,:,None];result[:,:,3]=grown
        im=bpy.data.images.new('Outlined_export',w,h,alpha=True);im.colorspace_settings.name='Non-Color'
        im.pixels.foreach_set(result.ravel());im.file_format='PNG'
        temporary=path+'.outlined.png';im.filepath_raw=temporary;im.save();os.replace(temporary,path)

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
    apply_building_theme(stem)
    tex=os.path.abspath(os.path.join(os.path.dirname(__file__),'../../Textures/Buildings'))
    bpy.context.view_layer.update()
    meshes=[o for o in scene.objects if o.type=='MESH']
    points=[o.matrix_world@v.co for o in meshes for v in o.data.vertices]
    lo=Vector([min(p[i] for p in points) for i in range(3)])
    hi=Vector([max(p[i] for p in points) for i in range(3)])
    calibrated=scene.get('presentation_rule')=='one_model_unit_per_game_tile'
    target=Vector((0,0,0)) if calibrated else (lo+hi)/2
    if not calibrated:target.z*=.80
    north=scene.get('north_azimuth',-90)
    dirs=[('North',north),('East',0),('South',north+180),('West',180)];cameras=[]
    span=0;vertical=[];horizontal=0
    for name,az in dirs:
        a=math.radians(az);e=math.radians(64)
        data=bpy.data.cameras.new('Camera_'+name);data.type='ORTHO'
        c=bpy.data.objects.new('Camera_'+name,data);scene.collection.objects.link(c)
        c.location=target+10*Vector((math.cos(a)*math.cos(e),math.sin(a)*math.cos(e),math.sin(e)))
        c.rotation_euler=(target-c.location).to_track_quat('-Z','Y').to_euler()
        q=c.rotation_euler.to_quaternion();right=q@Vector((1,0,0));up=q@Vector((0,1,0))
        span=max(span,max(max(abs((p-target).dot(right)),abs((p-target).dot(up))) for p in points))
        vertical.extend((p-target).dot(up) for p in points)
        horizontal=max(horizontal,max(abs((p-target).dot(right)) for p in points))
        cameras.append(c)
    if calibrated:
        # Canvas and drawSize grow by footprint; headroom uses a compensated camera shift.
        scale=max(footprint)*1.5
    else:scale=span*2*1.15
    resolution=256*max(footprint) if calibrated else 512
    draw_offset=0.
    if calibrated:
        limit=scale/2-(12+scene.get('outline_pixels',0))*scale/resolution
        lower=max(vertical)-limit;upper=min(vertical)+limit
        assert horizontal<=limit and lower<=upper,'Model exceeds the fixed-density canvas'
        draw_offset=max(lower,min(0.,upper))
        dz=draw_offset/math.cos(math.radians(64));target.z+=dz
        for c in cameras:c.location.z+=dz
    for c in cameras:c.data.ortho_scale=scale
    graphic_offset=draw_offset;tabletop_anchor=None
    anchor_mesh=scene.objects.get(scene.get('tabletop_anchor_mesh',''))
    if anchor_mesh is not None:
        anchor_points=[anchor_mesh.matrix_world@v.co for v in anchor_mesh.data.vertices]
        tabletop_anchor=Vector(((min(p.x for p in anchor_points)+max(p.x for p in anchor_points))/2,
                               (min(p.y for p in anchor_points)+max(p.y for p in anchor_points))/2,
                               max(p.z for p in anchor_points)))
        offsets=[-(tabletop_anchor-target).dot(c.rotation_euler.to_quaternion()@Vector((0,1,0))) for c in cameras]
        assert max(offsets)-min(offsets)<1e-5,'Tabletop requires directional registration'
        graphic_offset=offsets[0]
    scene.render.engine='BLENDER_EEVEE';scene.render.film_transparent=True
    scene.render.resolution_x=scene.render.resolution_y=resolution;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA'
    scene.view_settings.view_transform='Standard';scene.view_settings.look='None'
    scene.view_settings.exposure=0;scene.view_settings.gamma=1
    red=mat('MASK_red',(255,0,0));green=mat('MASK_metal_green',(0,255,0));black=mat('MASK_unselected',(0,0,0))
    originals={o.name:list(o.data.materials) for o in meshes}
    # Saved materials show the theme. Game albedo stays untinted so the shader
    # applies primary white/secondary Steel exactly once through the mask.
    raw={}
    for materials in originals.values():
        for m in materials:
            if m.name in raw:continue
            rgb=m.get('theme_base_rgb',None)
            raw[m.name]=mat('EXPORT_albedo_'+m.name,rgb,False) if rgb is not None else m
    selection=json.loads(scene.get('density_mask_policy','{}'))
    staging=tempfile.mkdtemp(prefix='.density-render-',dir=out)
    for c in cameras:
        scene.camera=c;direction=c.name.removeprefix('Camera_').lower()
        color_path=os.path.join(staging,stem+'_'+direction+'.png')
        mask_path=os.path.join(staging,stem+'_'+direction+'m.png')
        for o in meshes:
            for i,m in enumerate(originals[o.name]):o.data.materials[i]=raw[m.name]
        scene.render.filepath=color_path;bpy.ops.render.render(write_still=True)
        for o in meshes:
            for i,m in enumerate(originals[o.name]):
                channel=m.get('theme_channel',None)
                o.data.materials[i]=green if channel=='metal' else red if channel=='primary' else black if channel=='fixed' else red if m.get('tintable',selection.get(m.name,False)) else black
        scene.render.filepath=mask_path;bpy.ops.render.render(write_still=True)
        for o in meshes:
            for i,m in enumerate(originals[o.name]):o.data.materials[i]=m
        if scene.get('outline_pixels',0):
            outline_file(color_path,mask_path,int(scene['outline_pixels']))
        for path in [color_path,mask_path]:os.replace(path,os.path.join(tex,os.path.basename(path)))
    os.rmdir(staging)
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
            assert tuple(im.size)==(resolution,resolution)
            im.colorspace_settings.name='Non-Color';p=np.empty(resolution*resolution*4,np.float32)
            im.pixels.foreach_get(p);return p.reshape(resolution,resolution,4)
        p=pixels('');m=pixels('m');assert np.max(abs(p[:,:,3]-m[:,:,3]))<.001
        y,x=np.where(p[:,:,3]>.5);margin=int(min(x.min(),y.min(),resolution-1-x.max(),resolution-1-y.max()));assert margin>=12
        c=bpy.data.objects['Camera_'+name];forward=c.rotation_euler.to_quaternion()@Vector((0,0,-1))
        angle=math.degrees(math.asin(-forward.z));assert abs(angle-64)<.001
        checks[name]={'downward_degrees':angle,'minimum_margin':margin,'mask_alpha_matches':True}
        im=bpy.data.images.load(os.path.join(tex,stem+'_'+name.lower()+'.png'),check_existing=False)
        im.colorspace_settings.name='Non-Color';im.scale(512,512)
        if bpy.context.scene.get('building_theme_version',0):
            # Preview the shader's white primary / Steel secondary colors;
            # exported game albedo above remains unmultiplied.
            themed=p.copy();weights=m[:,:,:2]
            factor=(1-weights.sum(axis=2))[:,:,None]+weights[:,:,0,None]+weights[:,:,1,None]*np.array(bpy.context.scene['building_theme_secondary_color'])/255
            themed[:,:,:3]*=np.clip(factor,0,1)
            preview_image=bpy.data.images.new('Theme_preview',resolution,resolution,alpha=True)
            preview_image.colorspace_settings.name='Non-Color';preview_image.pixels.foreach_set(themed.ravel())
            preview_image.scale(512,512);im=preview_image
        preview=np.empty(512*512*4,np.float32);im.pixels.foreach_get(preview);previews.append(preview.reshape(512,512,4))
    im=bpy.data.images.new('North_East_South',1536,512,alpha=True);im.colorspace_settings.name='Non-Color'
    im.pixels.foreach_set(np.concatenate(previews[:3],axis=1).ravel());im.file_format='PNG'
    save_preview(im,os.path.join(out,'preview_north_east_south.png'))
    im.scale(768,256);save_preview(im,os.path.join(out,'preview_256px.png'))
    im=bpy.data.images.new('North_East_South_West',2048,512,alpha=True);im.colorspace_settings.name='Non-Color'
    im.pixels.foreach_set(np.concatenate(previews,axis=1).ravel());im.file_format='PNG'
    save_preview(im,os.path.join(out,'preview_four_views.png'))
    count=len([o for o in bpy.context.scene.objects if o.type=='MESH'])
    with open(os.path.join(out,'validation.json'),'w') as f:
        json.dump({'asset':stem,'footprint':footprint,'features':features,'mesh_objects':count,
                   'design_period':bpy.context.scene.get('design_period',''),
                   'texture_resolution':[resolution,resolution],
                   'nominal_pixels_per_footprint_tile':256,'pixels_per_game_unit':resolution/scale,
                   'outline_width_in_game_units':bpy.context.scene.get('outline_pixels',0)*scale/resolution,
                   'orthographic_scale':scale,'outline_pixels':bpy.context.scene.get('outline_pixels',0),
                   'expected_draw_size':[scale,scale],
                   'presentation_rule':bpy.context.scene.get('presentation_rule','auto_fit'),
                   'ground_anchor':[0.,0.,0.] if calibrated else [float(v) for v in target],
                   'camera_target':[float(v) for v in target],
                   'expected_draw_offset':[0.,0.,graphic_offset],
                   'tabletop_anchor':list(tabletop_anchor) if tabletop_anchor is not None else None,
                   'cameras':checks,'in_game_tested':False,
                   'building_theme_version':bpy.context.scene.get('building_theme_version',0),
                   'building_theme_family':bpy.context.scene.get('building_theme_family','authored'),
                   'building_theme_roles':json.loads(bpy.context.scene.get('building_theme_roles','{}')),
                   'theme_secondary_color':list(bpy.context.scene.get('building_theme_secondary_color',[255,255,255]))},f,indent=2)
