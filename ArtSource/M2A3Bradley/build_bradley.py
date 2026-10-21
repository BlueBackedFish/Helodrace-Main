"""Editable, shadow-free M2A3 Bradley art model. Run with Blender --background --python."""
import bpy
import math
import os
import json
from mathutils import Vector

OUT = os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.world = bpy.data.worlds.new('Unlit world')
scene.unit_settings.system = 'METRIC'

def linear(v):
    v /= 255
    return v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4

def material(name, rgb):
    m = bpy.data.materials.new(name)
    m.diffuse_color = tuple(linear(v) for v in rgb) + (1,)
    m.use_nodes = True
    nodes = m.node_tree.nodes
    nodes.clear()
    e = nodes.new('ShaderNodeEmission')
    e.inputs['Color'].default_value = m.diffuse_color
    out = nodes.new('ShaderNodeOutputMaterial')
    m.node_tree.links.new(e.outputs[0], out.inputs['Surface'])
    return m

# Reference: installed RimThunder - Desert Sabre - Continued (3724793908),
# M2A3 vehicle_east / vehicle_turret_full and M1A2 vehicle_south textures.
# Their neutral value maps use bright rims, gray panels and almost black tracks.
# Reinterpret that value separation with a desaturated TAN palette; no image reuse.
tan = [material('TAN | upper faces', (213,204,180)),
       material('TAN | fore and aft faces', (150,141,120)),
       material('TAN | side faces', (169,159,135)),
       material('TAN | underside', (101,96,83))]
edge = [material('Panel edges and recesses', (83,80,70))]
rubber = [material('Track rubber', (30,31,29))]
steel = [material('Track metal', (78,79,73))]
glass = [material('Optics blue black', (32,44,45))]
glasslight = [material('Optics reflection', (129,153,151))]
red = [material('Tail lamp red', (137,48,40))]
white = [material('Headlamp ivory', (239,234,215))]
rim = material('TAN | painted edge highlight', (237,230,209))
turret_rim = material('TAN | turret slope transition', (191,182,157))

def finish(obj, name, mats, parent=None):
    obj.name = name
    for m in mats:
        obj.data.materials.append(m)
    if len(mats) == 4:
        for p in obj.data.polygons:
            p.material_index = 0 if p.normal.z > .45 else 3 if p.normal.z < -.45 else 1 if abs(p.normal.y) > .5 else 2
    if parent:
        world = obj.matrix_world.copy()
        obj.parent = parent
        obj.matrix_world = world
    return obj

def empty(name, pos, parent=None):
    o = bpy.data.objects.new(name, None)
    scene.collection.objects.link(o)
    o.empty_display_type = 'PLAIN_AXES'
    o.empty_display_size = .45
    o.location = pos
    bpy.context.view_layer.update()
    if parent:
        world = o.matrix_world.copy()
        o.parent = parent
        o.matrix_world = world
    return o

root = empty('BRADLEY_ROOT', (0,0,0))
hull = empty('HULL', (0,0,0), root)
turret = empty('TURRET_YAW', (-.18,-.38,2.19), hull)
gun = empty('GUN_ELEVATION', (-.18,-1.27,2.68), turret)
ramp = empty('REAR_RAMP_HINGE', (0,2.93,.64), hull)
door = empty('PERSONNEL_DOOR_HINGE', (.86,3.04,.71), ramp)
turret['animation_axis'] = 'Local Z; independent turret yaw'
gun['animation_axis'] = 'Local X; positive elevation'
ramp['animation_axis'] = 'Local X; negative rotation lowers ramp outwards'
door['animation_axis'] = 'Local Z; negative rotation opens rear personnel door outwards'

def box(name, pos, size, mats=tan, parent=hull):
    bpy.ops.mesh.primitive_cube_add(size=1, location=pos)
    o = bpy.context.object
    o.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(o,name,mats,parent)

def mesh(name, verts, faces, mats=tan, parent=hull):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    o = bpy.data.objects.new(name, me)
    scene.collection.objects.link(o)
    # Correct winding even for procedural profiles.
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    o.select_set(False)
    return finish(o,name,mats,parent)

def profile(name, yz, x1, x2, mats=tan, parent=hull):
    n=len(yz)
    verts=[(x,y,z) for x in (x1,x2) for y,z in yz]
    faces=[tuple(range(n-1,-1,-1)),tuple(range(n,n*2))]
    faces += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    return mesh(name,verts,faces,mats,parent)

def loft(name, outline, bottom, top, inset=1, mats=tan, parent=hull):
    cx=sum(p[0] for p in outline)/len(outline)
    cy=sum(p[1] for p in outline)/len(outline)
    verts=[(x,y,bottom) for x,y in outline]+[(cx+(x-cx)*inset,cy+(y-cy)*inset,top) for x,y in outline]
    n=len(outline)
    faces=[tuple(range(n-1,-1,-1)),tuple(range(n,n*2))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    return mesh(name,verts,faces,mats,parent)

def cylinder(name, pos, radius, depth, mats=tan, parent=hull, axis='Z', vertices=24):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=radius,depth=depth,location=pos)
    o=bpy.context.object
    if axis=='X': o.rotation_euler.y=math.pi/2
    if axis=='Y': o.rotation_euler.x=math.pi/2
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    return finish(o,name,mats,parent)

def rod(name, a, b, radius=.025, mats=tan, parent=hull, vertices=10):
    a,b=Vector(a),Vector(b)
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=radius,depth=(b-a).length,location=(a+b)/2)
    o=bpy.context.object
    o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    return finish(o,name,mats,parent)

def handle(name, x,y,z, parent=hull, width=.22):
    rod(name+' L',(x-width/2,y,z),(x-width/2,y+.045,z+.06),.018,parent=parent)
    rod(name+' grip',(x-width/2,y+.045,z+.06),(x+width/2,y+.045,z+.06),.018,parent=parent)
    rod(name+' R',(x+width/2,y+.045,z+.06),(x+width/2,y,z),.018,parent=parent)

# Front is -Y, right is +X. Approximate silhouette from the supplied five-view drawing.
profile('Lower armored tub',[(-3.15,.92),(-2.6,.45),(2.49,.45),(2.99,.91),(2.84,1.48),(-2.92,1.48)],-1.25,1.25)
profile('Main hull sloped glacis',[(-3.25,1.17),(-3.16,1.42),(-2.32,2.04),(2.68,2.04),(2.99,1.67),(2.92,.94),(-2.83,.94)],-1.48,1.48)
box('Deck rim',(0,.18,2.065),(2.86,5.0,.07))

# Continuous track bands and individually modeled shoes, six road wheels per side.
track_path=[(-2.55,.16),(2.47,.16),(2.95,.43),(3.03,.82),(2.71,1.13),(-2.57,1.13),(-3.04,.77),(-3.04,.47)]
inner=[(-2.51,.27),(2.44,.27),(2.84,.49),(2.9,.79),(2.66,1.02),(-2.53,1.02),(-2.91,.73),(-2.91,.51)]
wheel_centers=[-2.03,-1.28,-.53,.48,1.23,1.98]
wheel_radius=.37
assert min(b-a for a,b in zip(wheel_centers,wheel_centers[1:]))>2*wheel_radius
for side in (-1,1):
    x=side*1.39
    n=len(track_path)
    vs=[(xx,y,z) for xx in (x-.24,x+.24) for path in (track_path,inner) for y,z in path]
    fs=[]
    for i in range(n):
        j=(i+1)%n
        fs += [(i,j,16+j,16+i),(8+i,24+i,24+j,8+j),(i,8+i,8+j,j),(16+i,16+j,24+j,24+i)]
    mesh('Continuous track '+str(side),vs,fs,rubber)
    for i,(y,z) in enumerate(track_path):
        yy,zz=track_path[(i+1)%n]
        length=math.hypot(yy-y,zz-z)
        count=max(1,round(length/.18))
        for k in range(count):
            t=(k+.5)/count
            shoe=box('Track shoe',(x,y+(yy-y)*t,z+(zz-z)*t),(.53,length/count*.83,.065),steel)
            shoe.rotation_euler.x=math.atan2(zz-z,yy-y)
    for y in wheel_centers:
        cylinder('Road wheel tire',(x,y,.60),wheel_radius,.37,rubber,axis='X')
        cylinder('Road wheel TAN disc',(x+side*.205,y,.60),.30,.055,axis='X')
        cylinder('Road wheel hub',(x+side*.245,y,.60),.105,.075,edge,axis='X')
        for a in range(8):
            ang=a*math.tau/8
            cylinder('Hub bolt',(x+side*.29,y+.15*math.cos(ang),.60+.15*math.sin(ang)),.023,.012,tan,axis='X',vertices=8)
    for y in [-2.68,2.66]:
        cylinder('Idler sprocket',(x,y,.73),.31,.35,steel,axis='X',vertices=16)
        cylinder('Sprocket cap',(x+side*.21,y,.73),.21,.06,axis='X',vertices=16)
        if y<0:
            for k in range(12):
                a=k*math.tau/12
                tooth=box('Drive sprocket tooth',(x+side*.18,y+.30*math.cos(a),.73+.30*math.sin(a)),(.11,.085,.085),steel)
                tooth.rotation_euler.x=a
    # Skirt sections retain the characteristic low, exposed road wheels.
    for i in range(6):
        y=-2.36+i*.86
        profile('Side skirt panel',[(y-.415,.97),(y+.415,.97),(y+.415,1.48),(y-.415,1.48)],side*1.64-.055,side*1.64+.055)
        for yy in [y-.32,y+.32]:
            cylinder('Skirt fastener',(side*1.705,yy,1.39),.028,.015,edge,axis='X',vertices=8)
        rod('Skirt grab handle',(side*1.72,y-.13,1.17),(side*1.72,y+.13,1.17),.022)
    # Base configuration: continuous upper hull, no added rectangular tile row.
    box('Rear fender',(side*1.39,2.77,1.23),(.63,.5,.14))
    box('Front fender',(side*1.39,-2.85,1.18),(.62,.46,.13))

# Deck details: engine grilles, driver hatch, front service plate, rear roof hatch.
box('Engine grille recess',(.74,-1.66,2.11),(.95,1.26,.035),edge)
for i in range(16):
    box('Engine grille slat',(.74,-2.23+i*.076,2.138),(.89,.035,.028))
loft('Driver hatch rim',[(-1.24,-2.05),(-.63,-2.12),(-.38,-1.89),(-.4,-1.13),(-1.24,-1.13)],2.09,2.15)
loft('Driver hatch',[(-1.16,-1.98),(-.69,-2.04),(-.47,-1.86),(-.49,-1.22),(-1.16,-1.22)],2.15,2.19)
box('Driver periscope housing',(-.87,-1.13,2.22),(.55,.16,.13))
box('Driver periscope lens',(-.87,-1.22,2.24),(.43,.012,.065),glass)
handle('Driver hatch handle',-.85,-1.72,2.21)
service_outline=[(-.58,-3.03),(.58,-3.03),(.63,-2.57),(-.63,-2.57)]
def glacis_z(y):return 1.42+(y+3.16)*.62/.84
service_verts=[(x,y,glacis_z(y)+dz) for dz in [.012,.038] for x,y in service_outline]
mesh('Glacis service panel',service_verts,[(3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)])
for x,y in service_outline:
    cylinder('Service panel bolt',(x*.9,y,glacis_z(y)+.047),.025,.016,edge,vertices=8)
for side in (-1,1):
    box('Headlight housing',(side*1.23,-2.69,1.77),(.28,.22,.19))
    box('Headlight lens',(side*1.23,-2.81,1.77),(.2,.018,.11),white)
    rod('Front tow eye',(side*1.05,-3.22,1.02),(side*1.05,-3.29,1.15),.055)
box('Rear roof hatch seam',(0,2.20,2.116),(1.88,.90,.027),edge)
loft('Rear roof hatch',[(-.90,1.79),(.90,1.79),(.90,2.61),(-.90,2.61)],2.13,2.22,.87)
for x in [-.62,0,.62]:
    cylinder('Roof hatch hinge',(x,2.58,2.23),.05,.23,axis='X')
handle('Roof hatch pull',0,1.91,2.23)
for x in [-1.13,1.13]:
    cy,length=(2.17,.82) if x>0 else (2.03,1.10)
    box('Rear deck stowage box',(x,cy,2.16),(.32,length,.16))
    for y in [cy-length*.32,cy+length*.32]:
        box('Stowage latch',(x,y,2.255),(.12,.08,.03),edge)

# Rear frame leaves a true opening when the ramp lowers. Opaque main hull rear
# is replaced by a recessed dark compartment backing, visible behind the ramp.
# Cut opening out of main hull so the demonstration actually opens into the body.
cutter=box('Temporary rear opening cutter',(0,2.75,1.39),(2.12,1.2,1.47),edge,parent=None)
for shell in ['Main hull sloped glacis','Lower armored tub']:
    main=bpy.data.objects[shell]
    bpy.context.view_layer.objects.active=main
    mod=main.modifiers.new('Rear access aperture','BOOLEAN');mod.operation='DIFFERENCE';mod.object=cutter
    bpy.ops.object.modifier_apply(modifier=mod.name)
bpy.data.objects.remove(cutter,do_unlink=True)
box('Compartment inner darkness',(0,2.19,1.36),(2.08,.04,1.39),rubber)
box('Compartment floor',(0,2.5,.65),(2.09,.86,.06),edge)
for x in [-1.13,1.13]:
    box('Rear door jamb',(x,2.91,1.35),(.16,.15,1.45))
box('Rear lintel',(0,2.91,2.01),(2.2,.16,.15))
# Close the roof between the deck ending at Y2.68 and the rear lintel.
# This belongs to the fixed hull, so lowering the ramp leaves a roof overhead.
box('Rear roof closure',(0,2.805,2.065),(2.26,.43,.07))
# Rounded personnel hatch at blueprint rear-view left (+X), with a true opening.
def rear_panel(name,cx,cz,width,height,radius,y,depth,mats=tan,parent=ramp):
    outline=[]
    for dx,dz,start in [(1,1,0),(-1,1,90),(-1,-1,180),(1,-1,270)]:
        for k in range(7):
            a=math.radians(start+k*15)
            outline.append((cx+dx*(width/2-radius)+radius*math.cos(a),
                            cz+dz*(height/2-radius)+radius*math.sin(a)))
    n=len(outline)
    verts=[(x,yy,z) for yy in [y-depth/2,y+depth/2] for x,z in outline]
    faces=[tuple(range(n-1,-1,-1)),tuple(range(n,n*2))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    return mesh(name,verts,faces,mats,parent)
rear_armor=rear_panel('Ramp armor',0,1.34,2.08,1.36,.08,2.96,.12)
cut=rear_panel('Temporary personnel opening',.43,1.34,.88,1.10,.25,2.96,.5,parent=None)
bpy.context.view_layer.objects.active=rear_armor
mod=rear_armor.modifiers.new('Personnel access aperture','BOOLEAN');mod.operation='DIFFERENCE';mod.object=cut
bpy.ops.object.modifier_apply(modifier=mod.name)
bpy.data.objects.remove(cut,do_unlink=True)
rear_panel('Personnel door seal',.43,1.34,.86,1.08,.24,3.005,.075,edge,parent=door)
rear_panel('Personnel door',.43,1.34,.82,1.04,.23,3.045,.06,parent=door)
box('Personnel vision port',(.43,3.08,1.65),(.22,.02,.10),edge,parent=door)
handle('Personnel pull',.12,3.09,1.28,parent=door,width=.12)
for z in [1.04,1.65]:
    cylinder('Personnel hinge',(.86,3.09,z),.042,.16,parent=ramp)
# Large oval cover/retaining line alongside the hatch, prominent in the drawing.
for k in range(40):
    a=k*math.tau/40;b=(k+1)*math.tau/40
    rod('Rear oval cover rim',(-.52+.39*math.cos(a),3.038,1.34+.51*math.sin(a)),
        (-.52+.39*math.cos(b),3.038,1.34+.51*math.sin(b)),.018,edge,parent=ramp)
for a in [0,.8,1.6,2.4,3.2,4,4.8,5.6]:
    box('Oval cover retaining latch',(-.52+.40*math.cos(a),3.06,1.34+.52*math.sin(a)),(.065,.04,.09),parent=ramp)
box('Oval cover plate',(-.52,3.04,1.54),(.27,.025,.16),parent=ramp)
handle('Oval cover lower handle',-.52,3.07,.91,parent=ramp,width=.27)
for x in [-.77,.77]:
    cylinder('Ramp lower hinge',(x,2.97,.64),.075,.27,axis='X')
for x in [-1.27,1.27]:
    box('Tail lamp mount',(x,2.98,1.89),(.22,.13,.24))
    cylinder('Red tail lamp',(x,3.055,1.92),.075,.022,red,axis='Y',vertices=12)

# Offset two-man faceted turret, long cannon, left-side twin TOW launcher.
cylinder('Turret ring',(-.18,-.38,2.15),.95,.17,parent=turret,vertices=48)
outline=[(-1.21,-.94),(-.82,-1.38),(.42,-1.38),(.88,-.98),(.91,.61),(.59,.94),(-.96,.94),(-1.30,.49)]
# Blueprint: broad lower cheeks, inward-sloping upper front/side armor and
# relatively upright rear bustle. Explicit matched rings avoid a full-width
# roof lip hiding the slope when viewed from the high game camera.
roof_outline=[(-.99,-.79),(-.68,-1.08),(.32,-1.08),(.65,-.80),
              (.69,.61),(.56,.93),(-.92,.93),(-1.05,.46)]
lower_outline=[(-.18+(x+.18)*.88,-.38+(y+.38)*.90) for x,y in outline]
rings=[[(x,y,2.25) for x,y in lower_outline],
       [(x,y,2.40) for x,y in outline],
       [(x,y,2.91) for x,y in roof_outline]]
n=len(outline)
verts=[v for ring in rings for v in ring]
faces=[tuple(range(n-1,-1,-1))]
for level in range(2):
    faces.extend((level*n+i,level*n+(i+1)%n,(level+1)*n+(i+1)%n,(level+1)*n+i) for i in range(n))
faces.append(tuple(range(2*n,3*n)))
armor=mesh('Faceted turret armor',verts,faces,parent=turret)
# Keep sloped armor distinct from the roof even where normals tilt upward.
for p in armor.data.polygons:
    if len(p.vertices)==4:
        p.material_index=1 if abs(p.normal.y)>.5 else 2
loft('Turret roof',roof_outline,2.91,2.96,1,parent=turret)
armor['construction']='Narrow lower base / broad shoulder at Z2.40 / inset roof at Z2.91; matched roof perimeter'
slope_angles=[]
for i in [0,1,2,3,7]:
    j=(i+1)%n
    a,b=Vector(rings[1][i]),Vector(rings[1][j])
    c=Vector(rings[2][i])
    normal=(b-a).cross(c-a).normalized()
    angle=math.degrees(math.asin(abs(normal.z)))
    assert angle>15, (i,angle)
    slope_angles.append(round(angle,2))
armor['front_side_slope_degrees_from_vertical']=slope_angles
# Fasteners follow the actual sloped plates instead of floating at fixed X.
for i in [0,2,3,7]:
    j=(i+1)%n
    a,b=Vector(rings[1][i]),Vector(rings[1][j])
    c,d=Vector(rings[2][i]),Vector(rings[2][j])
    normal=(b-a).cross(c-a).normalized()
    midpoint=(a+b+c+d)/4
    if normal.dot(midpoint-Vector((-.18,-.38,2.6)))<0:
        normal=-normal
    for t in [.22,.75]:
        lower=a.lerp(b,t); upper=c.lerp(d,t)
        pos=lower.lerp(upper,.64)+normal*.012
        bolt=cylinder('Turret applique bolt',pos,.026,.018,edge,parent=None,vertices=8)
        bolt.rotation_euler=normal.to_track_quat('Z','Y').to_euler()
        bpy.context.view_layer.update()
        world=bolt.matrix_world.copy();bolt.parent=turret;bolt.matrix_world=world
box('Gun mantlet',(-.18,-1.34,2.69),(.53,.4,.37),parent=gun)
rod('Cannon base',(-.18,-1.4,2.69),(-.18,-1.93,2.69),.11,parent=gun,vertices=12)
rod('25mm cannon',(-.18,-1.88,2.69),(-.18,-3.04,2.69),.044,mats=edge,parent=gun,vertices=12)
rod('Muzzle brake',(-.18,-3.04,2.69),(-.18,-3.26,2.69),.064,parent=gun,vertices=12)
cylinder('Muzzle bore',(-.18,-3.267,2.69),.034,.008,rubber,parent=gun,axis='Y')
for y in [-3.08,-3.14,-3.20]:
    for x in [-.244,-.116]:
        box('Muzzle vent',(x,y,2.69),(.006,.026,.045),rubber,parent=gun)
rod('Coax barrel',(.18,-1.36,2.64),(.18,-1.74,2.64),.025,mats=steel,parent=gun)
for x,y,r in [(-.64,.03,.32),(.19,.17,.27)]:
    cylinder('Cupola rim',(x,y,3.0),r+.045,.1,parent=turret)
    cylinder('Crew hatch',(x,y,3.065),r,.045,parent=turret)
    handle('Crew hatch handle',x,y,3.1,parent=turret,width=.19)
    for a in [-2.7,-2,-1.3]:
        box('Cupola periscope',(x+r*math.cos(a),y+r*math.sin(a),3.06),(.12,.09,.08),glass,parent=turret)
box('Main sight pedestal',(.34,-.64,3.03),(.46,.46,.17),parent=turret)
box('Main sight head',(.34,-.63,3.27),(.43,.36,.34),parent=turret)
box('Main sight glass',(.34,-.818,3.28),(.32,.018,.22),glass,parent=turret)
box('Main sight glint',(.27,-.829,3.28),(.035,.005,.18),glasslight,parent=turret)
box('CITV pedestal',(.42,.53,3.11),(.31,.31,.31),parent=turret)
box('CITV head',(.42,.53,3.39),(.36,.39,.27),parent=turret)
cylinder('CITV lens',(.42,.325,3.39),.10,.018,glass,parent=turret,axis='Y')
box('TOW launcher bracket',(-1.06,-.05,2.79),(.80,.67,.17),parent=turret)
box('Twin TOW launcher',(-1.43,-.15,2.99),(.43,1.55,.56),parent=turret)
for z in [2.85,3.12]:
    cylinder('TOW tube front rim',(-1.43,-.935,z),.117,.04,edge,parent=turret,axis='Y',vertices=12)
    cylinder('TOW tube dark opening',(-1.43,-.96,z),.084,.01,glass,parent=turret,axis='Y',vertices=12)
box('Turret rear basket',(-.25,1.0,2.63),(1.55,.20,.37),parent=turret)
for x in [-.85,-.35,.15,.55]:
    box('Basket strap',(x,1.11,2.64),(.045,.025,.33),edge,parent=turret)
for x,y in [(-.84,.62),(.68,.42)]:
    cylinder('Antenna base',(x,y,3.01),.055,.14,parent=turret)
    rod('Whip antenna',(x,y,3.08),(x,y,4.15),.009,mats=steel,parent=turret,vertices=6)
for side in [-1,1]:
    edge_index=0 if side<0 else 2
    next_index=(edge_index+1)%n
    a,b=Vector(rings[1][edge_index]),Vector(rings[1][next_index])
    c,d=Vector(rings[2][edge_index]),Vector(rings[2][next_index])
    outward=(b-a).cross(c-a).normalized()
    if outward.dot((a+b+c+d)/4-Vector((-.18,-.38,2.6)))<0:outward=-outward
    for i in range(4):
        t=.16+i*.22
        start=a.lerp(b,t).lerp(c.lerp(d,t),.49+(i%2)*.13)
        rod('Smoke launcher mounting boss',start-outward*.025,start+outward*.04,.062,parent=turret)
        end=start+outward*.26+Vector((0,0,.11))
        direction=(end-start).normalized()
        rod('Smoke grenade tube',start+outward*.025,end,.059,parent=turret)
        rod('Smoke grenade black cap',end-direction*.018,end+direction*.027,.066,rubber,parent=turret)
        rod('Smoke cap inset',end+direction*.027,end+direction*.031,.046,steel,parent=turret)

# Blueprint fittings, before relocating the complete turret assembly.
box('TOW launcher side cover',(-1.66,-.15,2.99),(.035,1.34,.44),parent=turret)
for y in [-.65,.37]:
    cylinder('TOW cover hinge',(-1.69,y,3.0),.035,.18,parent=turret)
    box('TOW cover latch',(-1.69,y,2.85),(.04,.08,.07),edge,parent=turret)
box('TOW rear cap',(-1.43,.64,2.99),(.46,.06,.59),parent=turret)
box('Main sight center mullion',(.34,-.833,3.28),(.035,.025,.25),parent=turret)
# Museum M2A3 photographs: CIV rotation collar, antenna cage, lifting eyes,
# mantlet-side grenade stowage and the casing-ejection flap above the gun.
cylinder('CITV rotation collar',(.42,.53,3.22),.245,.12,parent=turret)
box('CITV rear access plate',(.42,.74,3.40),(.27,.035,.18),parent=turret)
for x in [.29,.55]:
    for z in [3.32,3.47]:
        cylinder('CITV cover fastener',(x,.765,z),.018,.012,edge,parent=turret,axis='Y',vertices=8)
for x,y in [(-.84,.62),(.68,.42)]:
    for dx in [-.11,.11]:
        rod('Antenna guard upright',(x+dx,y,3.02),(x+dx,y,3.26),.018,parent=turret)
    for k in range(12):
        a=math.pi*k/12;b=math.pi*(k+1)/12
        rod('Antenna guard arch',(x+.11*math.cos(a),y,3.26+.11*math.sin(a)),
            (x+.11*math.cos(b),y,3.26+.11*math.sin(b)),.018,parent=turret)
for x in [-.61,.25]:
    box('Smoke grenade stowage box',(x,-1.22,2.36),(.29,.26,.19),parent=turret)
    box('Smoke stowage lid',(x,-1.22,2.46),(.31,.28,.025),parent=turret)
    box('Smoke stowage latch',(x,-1.36,2.39),(.055,.025,.09),edge,parent=turret)
box('Casing ejection flap',(-.18,-1.21,2.91),(.24,.15,.045),parent=turret)
for y in [-2.88,-2.74,-2.60,-2.46,-2.32,-2.18]:
    rod('Cannon fluted rib',(-.137,y,2.69),(-.137,y+.10,2.69),.009,steel,parent=gun,vertices=6)
for x in [-.79,.53]:
    for k in range(12):
        a=k*math.tau/12;b=(k+1)*math.tau/12
        rod('Turret lifting eye',(x+.055*math.cos(a),.68,2.99+.065*math.sin(a)),
            (x+.055*math.cos(b),.68,2.99+.065*math.sin(b)),.018,parent=turret)
for i in [3,7]:
    j=(i+1)%n
    a=Vector(rings[1][i]).lerp(Vector(rings[1][j]),.48)
    b=Vector(rings[2][i]).lerp(Vector(rings[2][j]),.48)
    direction=Vector((1 if i==3 else -1,0,0))*.013
    rod('Turret armor panel joint',a+direction,b+direction,.012,edge,parent=turret)
for side in [-1,1]:
    # The reference's two sides differ; the large louver belongs to one side.
    if side==-1:
        box('Side louver surround',(side*1.58,.57,1.78),(.06,.89,.63))
        box('Side louver recess',(side*1.617,.57,1.78),(.017,.78,.54),edge)
        for k in range(9):
            box('Side louver slat',(side*1.635,.57,1.54+k*.058),(.045,.76,.026))
    # Raised light guards and proper loop-shaped front recovery fittings.
    for xx in [side*1.23-.16,side*1.23+.16]:
        rod('Headlight guard',(xx,-2.82,1.67),(xx,-2.82,1.91),.018)
    rod('Headlight guard top',(side*1.23-.16,-2.82,1.91),(side*1.23+.16,-2.82,1.91),.018)
    for k in range(16):
        a=k*math.tau/16;b=(k+1)*math.tau/16
        rod('Front recovery eye',(side*1.05+.07*math.cos(a),-3.28,1.07+.09*math.sin(a)),
            (side*1.05+.07*math.cos(b),-3.28,1.07+.09*math.sin(b)),.022)

# Mirror only blueprint-mismatched fittings, keeping the turret's lateral offset.
# Reflect mesh vertices in world X, then repair winding; animation pivots remain
# right-handed and their descendant meshes continue to rotate normally.
from mathutils import Matrix
import bmesh
reflect=Matrix.Scale(-1,4,Vector((1,0,0)))
for obj in list(bpy.data.objects):
    if obj.type=='MESH' and obj.name.startswith(('Engine grille','Driver ', 'TOW ', 'Twin TOW', 'CITV')):
        bpy.context.view_layer.update()
        obj.data.transform(obj.matrix_world.inverted()@reflect@obj.matrix_world)
        bm=bmesh.new();bm.from_mesh(obj.data)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        bm.to_mesh(obj.data);bm.free();obj.data.update()
turret.location.y+=.75
bpy.context.view_layer.update()
# Geometry checks from the audit: layout, barrel clearance, wheel spacing.
def world_bounds(obj):
    points=[obj.matrix_world@Vector(p) for p in obj.bound_box]
    return [min(p[i] for p in points) for i in range(3)],[max(p[i] for p in points) for i in range(3)]
assert world_bounds(bpy.data.objects['Engine grille recess'])[1][0]<0
assert world_bounds(bpy.data.objects['Driver hatch'])[0][0]>0
assert world_bounds(bpy.data.objects['Twin TOW launcher'])[0][0]>0
assert world_bounds(bpy.data.objects['Muzzle bore'])[0][1]>-3.25
assert world_bounds(armor)[0][1]>-.8

# Narrow painted highlights mimic the reference's crisp light panel rims.
# Applied geometry keeps the exact same emission appearance in Blender and GLB.
# Only substantial armor/hardware receives the treatment, not bolts or handles.
rim_parts = ('Main hull', 'Deck rim', 'Side skirt panel',
             'Rear fender', 'Front fender', 'Driver hatch', 'Rear roof hatch',
             'Rear deck stowage', 'Ramp ', 'Personnel door', 'Rear door jamb',
             'Rear lintel', 'Faceted turret', 'Turret roof', 'Gun mantlet',
             'Crew hatch', 'Cupola rim', 'Main sight', 'CITV head',
             'Twin TOW launcher', 'Turret rear basket', 'Road wheel TAN disc')
for obj in list(bpy.data.objects):
    if obj.type != 'MESH' or not obj.name.startswith(rim_parts):
        continue
    if len(obj.data.materials) != 4:
        continue
    obj.data.materials.append(turret_rim if obj.name.startswith(('Faceted turret','Turret roof')) else rim)
    bevel = obj.modifiers.new('Painted bright panel rim', 'BEVEL')
    bevel.width = .012
    bevel.segments = 1
    bevel.affect = 'EDGES'
    bevel.limit_method = 'ANGLE'
    bevel.angle_limit = math.radians(35)
    bevel.material = len(obj.data.materials)-1
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=bevel.name)

# Camera-relative emission shading: facing surfaces and nearer depth are brighter,
# without lights, AO or cast shadows. Paint on the turret gets a separate boost.
def under(obj,parent):
    while obj:
        if obj==parent:return True
        obj=obj.parent
    return False
turret_materials={}
for obj in list(bpy.data.objects):
    if obj.type=='MESH' and under(obj,turret):
        for slot in obj.material_slots:
            base=slot.material
            if base.name.startswith('TAN |'):
                if base.name not in turret_materials:
                    m=base.copy();m.name='Turret '+base.name
                    m.diffuse_color=tuple(min(v*1.18,.92) for v in base.diffuse_color[:3])+(1,)
                    turret_materials[base.name]=m
                slot.material=turret_materials[base.name]
for m in bpy.data.materials:
    if not m.use_nodes:continue
    nodes=m.node_tree.nodes;links=m.node_tree.links
    emission=next((n for n in nodes if n.type=='EMISSION'),None)
    if not emission:continue
    facing=nodes.new('ShaderNodeLayerWeight')
    facing.inputs['Blend'].default_value=.5
    inverse=nodes.new('ShaderNodeMath');inverse.operation='SUBTRACT';inverse.inputs[0].default_value=1
    links.new(facing.outputs['Facing'],inverse.inputs[1])
    strength=nodes.new('ShaderNodeMath');strength.operation='MULTIPLY_ADD'
    strength.inputs[1].default_value=.24;strength.inputs[2].default_value=.82
    links.new(inverse.outputs[0],strength.inputs[0])
    camera=nodes.new('ShaderNodeCameraData')
    depth=nodes.new('ShaderNodeMapRange');depth.clamp=True
    depth.inputs['From Min'].default_value=12;depth.inputs['From Max'].default_value=20
    depth.inputs['To Min'].default_value=1.04;depth.inputs['To Max'].default_value=.94
    links.new(camera.outputs['View Z Depth'],depth.inputs['Value'])
    product=nodes.new('ShaderNodeMath');product.operation='MULTIPLY'
    links.new(strength.outputs[0],product.inputs[0]);links.new(depth.outputs['Result'],product.inputs[1])
    # Continuous root-space height tint: only darken, never brighten the palette.
    # A shared reference prevents each separate panel restarting the gradient.
    coordinates=nodes.new('ShaderNodeTexCoord');coordinates.object=bpy.data.objects['BRADLEY_ROOT']
    height_axis=nodes.new('ShaderNodeSeparateXYZ')
    links.new(coordinates.outputs['Object'],height_axis.inputs[0])
    height=nodes.new('ShaderNodeMapRange');height.name='Height darkening';height.clamp=True
    # Resolve centimetre-scale relief around each assembly's roof.
    # Keep the raised hatch colour; darken its supporting roof instead.
    painted='TAN |' in m.name
    z_low,z_high=(2.96,3.09) if m.name.startswith('Turret TAN |') else (2.10,2.23)
    if not painted:z_low,z_high=0,4.2
    height.inputs['From Min'].default_value=z_low; height.inputs['From Max'].default_value=z_high
    height.inputs['To Min'].default_value=.84 if painted else .90; height.inputs['To Max'].default_value=1
    links.new(height_axis.outputs['Z'],height.inputs['Value'])
    shaded=nodes.new('ShaderNodeMath');shaded.operation='MULTIPLY'
    links.new(product.outputs[0],shaded.inputs[0]);links.new(height.outputs['Result'],shaded.inputs[1])
    color=nodes.new('ShaderNodeMixRGB');color.name='Camera paint modulation';color.blend_type='MULTIPLY'
    color.inputs[0].default_value=1;color.inputs[1].default_value=m.diffuse_color
    links.new(shaded.outputs[0],color.inputs[2]);links.new(color.outputs[0],emission.inputs['Color'])
assert sum(o.name.startswith('Smoke grenade tube') for o in bpy.data.objects)==8
assert not any(o.name.startswith('Upper side applique') for o in bpy.data.objects)

# One reusable demonstration action per moving pivot; frame 1 is clean rest pose.
scene.render.fps=24
scene.frame_start=1;scene.frame_end=120
for obj,axis,keys in [(turret,2,[(1,0),(40,45),(80,-35),(120,0)]),
                      (gun,0,[(1,0),(40,18),(80,0),(120,0)]),
                      (ramp,0,[(1,0),(40,0),(80,-100),(120,0)]),
                      (door,2,[(1,0),(25,-100),(40,0),(80,0),(120,0)])]:
    for f,degrees in keys:
        obj.rotation_euler[axis]=math.radians(degrees)
        obj.keyframe_insert(data_path='rotation_euler',frame=f)
    obj.animation_data.action.name=obj.name+'_DEMO'
scene.frame_set(1)

scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=scene.render.resolution_y=1024
scene.render.resolution_percentage=100
scene.render.film_transparent=True
scene.render.image_settings.file_format='PNG'
scene.render.image_settings.color_mode='RGBA'
scene.view_settings.view_transform='Standard'
scene.view_settings.look='None'
scene.render.image_settings.color_depth='8'
target=Vector((0,0,1.55))
for name,az,elevation in [('iso_front',-55,35.264),('iso_rear',125,35.264),
                          ('north',90,64),('east',0,64),('south',270,64),('west',180,64)]:
    data=bpy.data.cameras.new(name);data.type='ORTHO';data.ortho_scale=8.7
    cam=bpy.data.objects.new('Camera_'+name,data);scene.collection.objects.link(cam)
    a,e=math.radians(az),math.radians(elevation)
    cam.location=target+16*Vector((math.cos(a)*math.cos(e),math.sin(a)*math.cos(e),math.sin(e)))
    cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.camera=cam
    scene.render.filepath=os.path.join(OUT,'bradley_'+name+'.png')
    bpy.ops.render.render(write_still=True)
scene.frame_set(80)
scene.camera=bpy.data.objects['Camera_iso_rear']
scene.render.filepath=os.path.join(OUT,'bradley_ramp_open.png')
bpy.ops.render.render(write_still=True)
scene.frame_set(25)
scene.render.filepath=os.path.join(OUT,'bradley_personnel_open.png')
bpy.ops.render.render(write_still=True)
scene.frame_set(1)
scene.camera=bpy.data.objects['Camera_iso_front']
for o in bpy.context.selected_objects: o.select_set(False)
root.select_set(True);bpy.context.view_layer.objects.active=root
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_perspective='CAMERA'
            area.spaces.active.shading.type='MATERIAL'
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'m2a3_bradley.blend'))
# glTF has no camera-relative shader equivalent. Export a constant-emission
# fallback; the saved blend above retains the complete sprite shading setup.
for m in bpy.data.materials:
    if not m.use_nodes:continue
    emission=next((n for n in m.node_tree.nodes if n.type=='EMISSION'),None)
    if emission:
        for link in list(emission.inputs['Color'].links):m.node_tree.links.remove(link)
        emission.inputs['Color'].default_value=m.diffuse_color
bpy.ops.export_scene.gltf(filepath=os.path.join(OUT,'m2a3_bradley.glb'),export_format='GLB',export_animations=True,export_cameras=False,export_lights=False)

# Verify saved scene, pivot hierarchy, neutral rest pose and actual rendered alpha.
bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT,'m2a3_bradley.blend'))
assert bpy.data.objects['TURRET_YAW'].parent.name=='HULL'
assert bpy.data.objects['GUN_ELEVATION'].parent.name=='TURRET_YAW'
assert bpy.data.objects['PERSONNEL_DOOR_HINGE'].parent.name=='REAR_RAMP_HINGE'
assert not [o for o in bpy.data.objects if o.type=='LIGHT']
roof=bpy.data.objects['Rear roof closure']
assert roof.parent.name=='HULL'
for x in [-1.04,0,1.04]:
    for y in [2.66,2.80,2.98]:
        local=roof.matrix_world.inverted()@Vector((x,y,3))
        hit,point,normal,index=roof.ray_cast(local,Vector((0,0,-1)))
        assert hit and abs((roof.matrix_world@point).z-2.10)<.001
for m in bpy.data.materials:
    if not m.use_nodes or not any(n.type=='EMISSION' for n in m.node_tree.nodes):continue
    height=m.node_tree.nodes['Height darkening']
    assert height.clamp and height.interpolation_type=='LINEAR'
    assert 0 < height.inputs['To Min'].default_value < height.inputs['To Max'].default_value <= 1
    assert height.inputs['From Min'].default_value < height.inputs['From Max'].default_value
    coordinates=next(n for n in m.node_tree.nodes if n.type=='TEX_COORD')
    assert coordinates.object==bpy.data.objects['BRADLEY_ROOT']
import numpy as np
renders={}
for name in ['iso_front','iso_rear','north','east','south','west','ramp_open','personnel_open']:
    im=bpy.data.images.load(os.path.join(OUT,'bradley_'+name+'.png'),check_existing=False)
    assert tuple(im.size)==(1024,1024)
    pixels=np.array(im.pixels[:]).reshape(1024,1024,4)
    ys,xs=np.where(pixels[:,:,3]>.01)
    assert len(xs)>0 and min(xs)>8 and max(xs)<1015 and min(ys)>8 and max(ys)<1015
    renders[name]={'size':[1024,1024],'alpha_bounds':[int(min(xs)),int(min(ys)),int(max(xs)),int(max(ys))]}
    bpy.data.images.remove(im)
manifest={'front_axis':'-Y','up_axis':'Z','material':'Camera-facing, depth and continuous height-modulated TAN emission; turret paint 1.18 linear boost; no lights or cast shadows',
          'height_shading':{'coordinate_space':'BRADLEY_ROOT','hull_paint_z_range':[2.10,2.23],'turret_paint_z_range':[2.96,3.09],'paint_linear_rgb_multiplier':[.84,1.0],'nonpaint_z_range':[0,4.2],'nonpaint_multiplier':[.90,1.0],'interpolation':'continuous linear, clamped; roof darkening to distinguish raised hatches'},
          'smoke_launcher_tubes':8,'upper_side_addon_panels':0,'glb_material':'Constant emission fallback; camera shader retained in blend only',
          'blueprint_corrections':{'turret_rearward_shift':.75,'turret_armor_length':2.32,'barrel_shortened':.40,
                                   'road_wheel_diameter':.74,'road_wheel_centers':wheel_centers,
                                   'minimum_wheel_clearance':round(min(b-a for a,b in zip(wheel_centers,wheel_centers[1:]))-.74,3),
                                   'top_view_front_down':'engine left; driver hatch and TOW right'},
          'turret_front_side_slope_degrees_from_vertical':slope_angles,
          'style_reference':'Installed RimThunder - Desert Sabre - Continued 3724793908; M2A3 and M1A2 grayscale textures inspected, not copied',
          'mesh_objects':sum(o.type=='MESH' for o in bpy.data.objects),'renders':renders,
          'animation':'120 frames at 24fps; rest frame 1, door demo 25, turret/elevation 40, ramp 80',
          'validation':'Reopened blend, hierarchy, no lights, continuous darkening-only root-space height shader, PNG RGBA bounds passed'}
with open(os.path.join(OUT,'manifest.json'),'w',encoding='utf-8') as f:json.dump(manifest,f,indent=2)
print('BRADLEY VALIDATION PASSED')
