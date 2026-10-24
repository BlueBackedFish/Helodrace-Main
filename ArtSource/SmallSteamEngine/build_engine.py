"""Reference-led 2x2 steam engine, fixed-color sprite geometry, Blender 5.1."""
import bpy, math, os, json
import numpy as np
from mathutils import Vector
OUT=os.path.dirname(os.path.abspath(__file__))
TEX=os.path.abspath(os.path.join(OUT,'../../Textures/Buildings'))
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene
def material(name,rgb,tint=True):
    if isinstance(rgb,int):rgb=(rgb,)*3
    def lin(c):
        c/=255
        return c/12.92 if c<=.04045 else ((c+.055)/1.055)**2.4
    m=bpy.data.materials.new(name);m.use_nodes=True;m['tintable']=tint
    m.diffuse_color=tuple(lin(c) for c in rgb)+(1,)
    m.node_tree.nodes.clear();e=m.node_tree.nodes.new('ShaderNodeEmission')
    e.inputs[0].default_value=m.diffuse_color
    o=m.node_tree.nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(e.outputs[0],o.inputs[0])
    return m
def palette(name,values,tint=True):return [material(name+str(i),v,tint) for i,v in enumerate(values)]
bed=palette('Foundation_', [192,178,169])
cast=palette('Cast_iron_', [219,207,197])
steel=palette('Shaft_metal_', [235,224,213])
recess=palette('Mechanical_recess_', [151,142,134])
brass=palette('Governor_brass_',[(214,177,95),(195,157,78),(184,146,70)],False)
copper=palette('Crank_brown_',[(138,94,55),(125,83,47),(116,76,43)],False)
def cube(name,p,size,mats):
    bpy.ops.mesh.primitive_cube_add(size=1,location=p)
    o=bpy.context.object;o.name=name;o.dimensions=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    for m in mats:o.data.materials.append(m)
    for f in o.data.polygons:f.material_index=0 if f.normal.z>.5 else (1 if abs(f.normal.y)>.5 else 2)
    return o
def cylinder(name,p,r,depth,mats,axis='Z',r2=None):
    if r2 is None:bpy.ops.mesh.primitive_cylinder_add(vertices=24,radius=r,depth=depth,location=p)
    else:bpy.ops.mesh.primitive_cone_add(vertices=24,radius1=r,radius2=r2,depth=depth,location=p)
    o=bpy.context.object;o.name=name
    for m in mats:o.data.materials.append(m)
    for f in o.data.polygons:f.material_index=0 if f.normal.z>.9 else (2 if f.normal.z<-.9 else 1)
    if axis=='Y':o.rotation_euler.x=math.pi/2
    elif axis=='X':o.rotation_euler.y=math.pi/2
    return o
def rod(name,a,b,r,mats):
    a,b=Vector(a),Vector(b);o=cylinder(name,(a+b)/2,r,(b-a).length,mats)
    o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler();return o
def wheel(name,y):
    x=-.43;z=.83;outer=.65;inner=.565;half=.045;n=40
    verts=[(x+r*math.cos(i*math.tau/n),y+dy,z+r*math.sin(i*math.tau/n))
           for dy in [-half,half] for r in [outer,inner] for i in range(n)]
    faces=[];colors=[]
    for i in range(n):
        j=(i+1)%n
        faces.extend([(i,j,2*n+j,2*n+i),(n+i,3*n+i,3*n+j,n+j),
                      (i,n+i,n+j,j),(2*n+i,2*n+j,3*n+j,3*n+i)])
        colors.extend([1,2,0,0])
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    o=bpy.data.objects.new(name,mesh);scene.collection.objects.link(o)
    for m in cast:mesh.materials.append(m)
    for f,c in zip(mesh.polygons,colors):f.material_index=c
    cylinder(name+'_hub',(x,y,z),.115,.145,steel,'Y')
    for i in range(6):
        a=i*math.tau/6+.17
        p=(x+.315*math.cos(a),y,z+.315*math.sin(a))
        spoke=cube(name+'_spoke',p,(.55,.065,.060),cast)
        spoke.rotation_euler.y=-a
    return o
# Broad bed, two flywheels and exposed shaft retain the original arrangement.
cube('01_Foundation',(0,0,.10),(1.92,1.90,.20),bed)
for y in [-.72,.72]:cube('02_Raised_bed_rail',(0,y,.235),(1.78,.11,.070),recess)
for y in [-.43,.43]:cube('03_Bearing_pedestal',(-.43,y,.405),(.28,.25,.46),cast)
cylinder('04_Common_crankshaft',(-.43,0,.83),.065,1.48,steel,'Y')
wheel('05_Front_flywheel',-.61);wheel('06_Rear_flywheel',.61)
# Upright engine casing and simple steam chest at the right of the flywheels.
cube('07_Right_engine_plinth',(.55,0,.26),(.76,1.57,.12),cast)
# The fuel casing is a deep solid body, close to the flywheel pair's overall span.
cube('08_Engine_block',(.55,.035,.635),(.64,1.20,.63),cast)
cube('09_Steam_chest',(.55,.035,1.015),(.60,.73,.17),steel)
cube('10_Front_inspection_border',(.55,-.580,.645),(.51,.030,.43),recess)
cube('11_Front_inspection_plate',(.55,-.601,.645),(.45,.015,.36),cast)
cylinder('12_Piston_gland',(.55,-.626,.70),.062,.035,steel,'Y')
cube('13_Crosshead_slide',(.53,-.73,.36),(.61,.13,.085),recess)
cube('14_Crosshead',(.56,-.73,.43),(.11,.11,.13),steel)
rod('15_Piston_rod',(.55,-.645,.70),(.56,-.73,.43),.022,steel)
rod('16_Connecting_link',(-.28,-.70,.96),(.56,-.73,.43),.026,steel)
cylinder('17_Crank_pin',(-.28,-.70,.96),.052,.035,steel,'Y')
rod('18_Brown_crank_arm',(-.43,-.70,.83),(-.28,-.70,.96),.044,copper)
# Small boiler between the wheels, below the axle, connected to the fuel casing.
cylinder('33_Small_boiler',(-.43,.02,.52),.25,.94,cast,'Y')
for y in [-.43,.02,.47]:
    cylinder('34_Boiler_band',(-.43,y,.52),.265,.040,steel,'Y')
cylinder('35_Boiler_front_cap',(-.43,-.465,.52),.235,.026,cast,'Y')
rod('36_Boiler_to_firebox',(-.24,.02,.52),(.32,.02,.52),.105,cast)
cylinder('37_Firebox_connection_flange',(.235,.02,.52),.135,.032,steel,'X')
# Short stack neck visibly enters the boiler shell instead of reaching the bed.
cylinder('19_Stack_base',(-.68,.25,.72),.15,.34,cast)
cylinder('20_Stack_tube',(-.68,.25,1.13),.135,.66,cast)
cylinder('21_Stack_flare',(-.68,.25,1.53),.135,.22,cast,r2=.195)
for z,r in [(1.40,.153),(1.64,.202)]:cylinder('22_Stack_band',(-.68,.25,z),r,.045,steel)
cylinder('23_Stack_dark_mouth',(-.68,.25,1.664),.168,.003,recess)
# Governor's paired brass balls are the characteristic colored accents.
cylinder('24_Governor_mount',(.55,.035,1.135),.075,.09,brass)
cylinder('25_Governor_spindle',(.55,.035,1.295),.025,.27,brass)
for z in [1.18,1.34]:cylinder('26_Governor_collar',(.55,.035,z),.047,.035,brass)
for x in [.41,.69]:
    rod('27_Governor_arm',(.55,.035,1.37),(x,.035,1.25),.012,brass)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12,ring_count=6,radius=.053,location=(x,.035,1.25))
    ball=bpy.context.object;ball.name='28_Governor_ball';ball.data.materials.append(brass[0])
cylinder('29_Governor_cap',(.55,.035,1.435),.042,.035,brass)
rod('30_Steam_pipe',(.82,.33,.42),(.82,.33,1.17),.037,cast)
rod('31_Pipe_elbow',(.82,.33,1.17),(.66,.33,1.17),.037,cast)
cylinder('32_Pipe_valve',(.82,.33,1.22),.054,.045,brass)
# Sprite-sized rivet heads on meaningful plates; no dense field of tiny screws.
for x in [.325,.775]:
    for z in [.475,.645,.815]:
        cylinder('38_Fuel_door_rivet',(x,-.617,z),.014,.012,recess,'Y')
for z in [.54,.77]:cube('39_Fuel_door_hinge',(.338,-.625,z),(.035,.026,.068),steel)
rod('40_Fuel_door_handle',(.715,-.637,.57),(.715,-.637,.70),.014,recess)
for x in [.25,.85]:
    for y in [-.68,.68]:
        cylinder('41_Plinth_rivet',(x,y,.326),.018,.012,recess)
for i in range(6):
    a=i*math.tau/6
    cylinder('42_Boiler_cap_rivet',(-.43+.190*math.cos(a),-.489,.52+.190*math.sin(a)),.012,.012,recess,'Y')
target=Vector((0,0,.70));scale=2.75
for name,az in [('North',-90),('East',0),('South',90)]:
    a=math.radians(az);e=math.radians(64)
    data=bpy.data.cameras.new('Camera_'+name);data.type='ORTHO';data.ortho_scale=scale
    c=bpy.data.objects.new(data.name,data);scene.collection.objects.link(c)
    c.location=target+8*Vector((math.cos(a)*math.cos(e),math.sin(a)*math.cos(e),math.sin(e)))
    c.rotation_euler=(target-c.location).to_track_quat('-Z','Y').to_euler()
scene.render.engine='BLENDER_EEVEE';scene.render.film_transparent=True
scene.render.resolution_x=scene.render.resolution_y=512;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA'
scene.view_settings.view_transform='Standard';scene.view_settings.look='None'
scene.view_settings.exposure=0;scene.view_settings.gamma=1
mask_red=material('MASK_red',(255,0,0));mask_black=material('MASK_unselected',(0,0,0))
objects=[o for o in scene.objects if o.type=='MESH']
originals={o.name:list(o.data.materials) for o in objects}
for name in ['North','East','South']:
    scene.camera=bpy.data.objects['Camera_'+name]
    scene.render.filepath=os.path.join(TEX,'HD_SmallSteamEngine_'+name.lower()+'.png')
    bpy.ops.render.render(write_still=True)
    for o in objects:
        for i,m in enumerate(originals[o.name]):o.data.materials[i]=mask_red if m['tintable'] else mask_black
    scene.render.filepath=os.path.join(TEX,'HD_SmallSteamEngine_'+name.lower()+'m.png')
    bpy.ops.render.render(write_still=True)
    for o in objects:
        for i,m in enumerate(originals[o.name]):o.data.materials[i]=m
scene.camera=bpy.data.objects['Camera_North']
for name in ['HD_SmallSteamEngine_north.png','HD_SmallSteamEngine_northm.png']:
    image=bpy.data.images.load(os.path.join(OUT,'Reference',name));image.pack()
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'small_steam_engine.blend'))
bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT,'small_steam_engine.blend'))
scene=bpy.context.scene
bpy.context.view_layer.update()
objects=[o for o in scene.objects if o.type=='MESH']
checks={};images=[]
def pixels(path):
    im=bpy.data.images.load(path,check_existing=False);im.colorspace_settings.name='Non-Color'
    p=np.empty(512*512*4,np.float32);im.pixels.foreach_get(p);return p.reshape(512,512,4)
for name in ['North','East','South']:
    c=bpy.data.objects['Camera_'+name];f=c.rotation_euler.to_quaternion()@Vector((0,0,-1))
    angle=math.degrees(math.asin(-f.z));assert abs(angle-64)<.001
    p=pixels(os.path.join(TEX,'HD_SmallSteamEngine_'+name.lower()+'.png'))
    m=pixels(os.path.join(TEX,'HD_SmallSteamEngine_'+name.lower()+'m.png'))
    assert np.max(np.abs(p[:,:,3]-m[:,:,3]))<.001
    y,x=np.where(p[:,:,3]>.5);margin=min(x.min(),y.min(),511-x.max(),511-y.max());assert margin>=20
    checks[name]={'downward_degrees':angle,'margin_px':int(margin),'mask_alpha_matches':True}
    images.append(p)
combined=np.concatenate(images,axis=1)
im=bpy.data.images.new('North_East_South',1536,512,alpha=True);im.colorspace_settings.name='Non-Color'
im.pixels.foreach_set(combined.ravel());im.file_format='PNG';im.filepath_raw=os.path.join(OUT,'preview_north_east_south.png');im.save()
im.scale(768,256);im.filepath_raw=os.path.join(OUT,'preview_256px.png');im.save()
def world_size(obj):
    points=[obj.matrix_world@v.co for v in obj.data.vertices]
    return [max(v[i] for v in points)-min(v[i] for v in points) for i in range(3)]
with open(os.path.join(OUT,'validation.json'),'w') as f:
    json.dump({'footprint':[2,2],'mesh_objects':len(objects),'vertices':sum(len(o.data.vertices) for o in objects),
               'small_boiler_size':world_size(bpy.data.objects['33_Small_boiler']),
               'fuel_casing_size':world_size(bpy.data.objects['08_Engine_block']),
               'rivet_heads':sum('rivet' in o.name for o in objects),
               'cameras':checks,'game_tested':False},f,indent=2)
