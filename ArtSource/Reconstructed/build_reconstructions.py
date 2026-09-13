"""Primitive reconstruction of the five supplied designs, not sprite billboards.

Run with Blender --background --python build_reconstructions.py [-- AssetName].
Source coordinates below are measured on the filled NORTH image, not its stroke.
Masks are loaded, measured, packed for inspection and attached as provenance.
"""
import bpy
import math
import os
import sys
import json
import numpy as np
from mathutils import Vector

ROOT = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, ROOT)
from analyze_references import analyze, pixels, REF

S = math.sin(math.radians(64))
C = math.cos(math.radians(64))
report = analyze()
ASSET = ''
P = 160
N = 512
palette = {}
source = None
mask = None

def lin(v):
    v /= 255
    return v/12.92 if v <= .04045 else ((v+.055)/1.055)**2.4

def material(value):
    rgb = (value,)*3 if isinstance(value, (int,float)) else tuple(value)
    if rgb not in palette:
        mat = bpy.data.materials.new('Fill_'+'_'.join(str(int(v)) for v in rgb))
        mat.diffuse_color = tuple(lin(v) for v in rgb)+(1,)
        mat.use_nodes = True
        mat.node_tree.nodes.clear()
        emission = mat.node_tree.nodes.new('ShaderNodeEmission')
        emission.inputs['Color'].default_value = mat.diffuse_color
        output = mat.node_tree.nodes.new('ShaderNodeOutputMaterial')
        mat.node_tree.links.new(emission.outputs[0], output.inputs['Surface'])
        palette[rgb] = mat
    return palette[rgb]

def loc(u,v,z):
    return ((u-N/2)/P, ((N/2-v)/P-C*z)/S, z)

def provenance(obj, rect):
    x0,y0,x1,y1 = map(int,rect)
    patch = mask[max(0,y0):min(N,y1),max(0,x0):min(N,x1)]
    obj['source_north_region_px'] = list(rect)
    if patch.size:
        selection = (patch[:,:,0]>180)&(patch[:,:,1]<80)&(patch[:,:,3]>127)
        obj['material_mask_selected_fraction'] = round(float(selection.mean()),4)
    obj['reference_color'] = 'HD_'+ASSET+'_north.png'
    obj['reference_mask'] = 'HD_'+ASSET+'_northm.png'

def box(name, center, size, top=153, side=None, end=None):
    bpy.ops.mesh.primitive_cube_add(size=1, location=center)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    for val in [top, side if side is not None else top, end if end is not None else (side if side is not None else top)]:
        obj.data.materials.append(material(val))
    for face in obj.data.polygons:
        face.material_index = 0 if face.normal.z > .5 else (1 if abs(face.normal.y)>.5 else 2)
    return obj

def rect(name, coords, z, height, top=153, side=None):
    x0,y0,x1,y1 = coords
    center = loc((x0+x1)/2,(y0+y1)/2,z)
    obj = box(name,(center[0],center[1],z-height/2),((x1-x0)/P,(y1-y0)/(P*S),height),top,side)
    provenance(obj,coords)
    return obj

def cylinder(name, center, radius, length, axis='Z', color=153, cap=None, vertices=16, radius2=None):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=length, location=center)
    obj = bpy.context.object
    obj.name = name
    if axis=='X':
        obj.rotation_euler.y = math.pi/2
    elif axis=='Y':
        obj.rotation_euler.x = math.pi/2
    obj.data.materials.append(material(color))
    obj.data.materials.append(material(cap if cap is not None else color))
    for face in obj.data.polygons:
        face.material_index = 1 if len(face.vertices)>4 else 0
    return obj

def rod(name, start, end, radius=.014, color=153):
    start,end = Vector(start),Vector(end)
    obj=cylinder(name,(start+end)/2,radius,(end-start).length,color=color,vertices=8)
    obj.rotation_euler=(end-start).to_track_quat('Z','Y').to_euler()
    return obj

def wheel(name, center, radius, axis='Y', color=88):
    # The large source handwheels are recognition features; no fine hardware.
    bpy.ops.mesh.primitive_torus_add(major_segments=12, minor_segments=4,
                                   location=center, major_radius=radius*.83, minor_radius=radius*.17)
    obj=bpy.context.object
    obj.name=name
    obj.data.materials.append(material(color))
    if axis=='Y': obj.rotation_euler.x=math.pi/2
    if axis=='X': obj.rotation_euler.y=math.pi/2
    a,b,c=center
    if axis=='Y':
        rod(name+'_spoke',(a-radius,b,c),(a+radius,b,c),radius*.08,color)
        rod(name+'_spoke',(a,b,c-radius),(a,b,c+radius),radius*.08,color)
    else:
        rod(name+'_spoke',(a,b-radius,c),(a,b+radius,c),radius*.08,color)
        rod(name+'_spoke',(a,b,c-radius),(a,b,c+radius),radius*.08,color)
    return obj

def prism(name, xy, bottom, top, color, side=None):
    count=len(xy)
    verts=[(x,y,z) for z in [bottom,top] for x,y in xy]
    faces=[tuple(reversed(range(count))),tuple(range(count,2*count))]
    faces += [(i,(i+1)%count,(i+1)%count+count,i+count) for i in range(count)]
    mesh=bpy.data.meshes.new(name)
    mesh.from_pydata(verts,[],faces)
    obj=bpy.data.objects.new(name,mesh)
    bpy.context.collection.objects.link(obj)
    mesh.materials.append(material(color))
    mesh.materials.append(material(side if side is not None else color))
    for face in mesh.polygons: face.material_index=0 if face.index==1 else 1
    return obj

def lathe(turret=False):
    rect('White_cast_bed',(95,190,411,273),.80,.20,255,(213,211,211))
    # Existing source rail is an interior structural bar, not the black outline.
    rect('Front_bed_way',(96,272,411,280),.80,.035,129,100)
    bed_y=loc(256,231.5,.8)[1]
    for u in [106,398]:
        x=loc(u,0,0)[0]
        for y in [bed_y-.28,bed_y+.24]:
            box('Cast_support_leg',(x,y,.285),(.125,.08,.48),184,184)
        box('Foot_crossbar',(x,bed_y-.02,.03),(.125,.62,.06),184,184)
        box('Splayed_front_foot',(x,bed_y-.28,.025),(.245,.10,.05),184,184)
    gold=(175,132,69)
    goldshade=(127,96,50)
    rect('Headstock_saddle',(102,192,171,249),.89,.17,gold if turret else 160,127)
    rect('Headstock_rear_cheek',(103,178,114,241),1.10,.19,129,111)
    rect('Headstock_front_cheek',(153,177,170,241),1.10,.19,129,111)
    rect('Headstock_drive_housing',(114,191,153,233),1.12,.17,88,75)
    rect('Stepped_drive_top',(131,185,151,223),1.13,.04,100,88)
    c=loc(183,215,1.02)
    cylinder('Three_jaw_chuck',c,.13,.18,'X',100,110,vertices=8)
    # Three coarse projecting jaw blocks, visible in all provided lathe views.
    for angle in [0,120,240]:
        t=math.radians(angle)
        y=c[1]+math.cos(t)*.15
        z=c[2]+math.sin(t)*.15
        jaw=box('Chuck_jaw',(c[0]+.115,y,z),(.10,.11,.09),160,143)
        jaw.rotation_euler.x=t
    if turret:
        rect('Ochre_drive_cover',(104,178,169,192),1.10,.075,gold,goldshade)
        rect('Ochre_drive_apron',(104,249,169,270),.91,.30,gold,goldshade)
        rect('Turret_carriage',(348,171,385,252),1.08,.30,88,75)
        center=Vector(loc(366,213,1.19))
        xy=[]
        for i in range(16):
            a=math.pi/8+i*math.pi/8
            r=.205 if i%2==0 else .14
            xy.append((center.x+math.cos(a)*r,center.y+math.sin(a)*r))
        prism('Eight_lobed_tool_turret',xy,1.10,1.19,70,56)
        for dx,dy in [(-.29,0),(.29,0),(-.20,-.20),(.20,-.20)]:
            rod('Turret_tool',center+Vector((dx*.45,dy*.45,-.03)),center+Vector((dx,dy,-.03)),.017,184)
        rod('Turret_lock_lever',loc(358,258,1.08),loc(376,265,1.08),.022,48)
    else:
        rect('Tailstock_casting',(369,170,408,246),1.10,.38,88,75)
        rod('Tailstock_spindle',loc(333,212,1.14),loc(372,212,1.14),.016,184)
        cylinder('Tailstock_quill',loc(359,210,1.14),.050,.16,'X',184,170)
        cylinder('Tailstock_feed',loc(359,246,1.02),.042,.13,'X',160,153)
        rect('Ochre_rear_carriage',(207,179,272,194),1.06,.065,gold,goldshade)
        rect('Ochre_front_apron',(207,255,272,271),.91,.29,gold,goldshade)
        rect('Tool_slide',(232,237,247,260),1.12,.11,184,153)
        rod('Toolpost_lock',loc(234,258,1.18),loc(245,270,1.18),.016,75)
        wheel('Carriage_handwheel',loc(223,280,.86),.057,'Y',75)
        # Six broad segments form the actual unmasked brown drive belt.
        x=loc(135,0,0)[0]
        path=[(bed_y-.36,.18),(bed_y-.36,.92),(bed_y-.22,1.23),
              (bed_y+.22,1.23),(bed_y+.36,.92),(bed_y+.36,.18)]
        for a,b in zip(path,path[1:]+path[:1]):
            start=(x,a[0],a[1]); end=(x,b[0],b[1])
            belt=rod('Brown_drive_belt',start,end,.016,(102,74,48))
            belt['mask_interpretation']='Unmasked brown belt preserved; black surrounding contour omitted.'
        rect('Treadle_pedal',(290,302,348,324),.12,.025,153,129)
        for u in [298,307,316,325,334,343]:
            rect('Pedal_slot',(u,305,u+2,321),.1205,.001,100,100)
    # Longitudinal feedscrew + the large end wheel clearly visible in references.
    front_y=bed_y-.32
    rod('Longitudinal_feed_screw',(-.98,front_y,.72),(1.03,front_y,.72),.014,70)
    wheel('End_feed_handwheel',(1.035,front_y,.72),.075,'X',75)
    if not turret:
        wheel('Tailstock_handwheel',(1.035,bed_y,1.02),.080,'X',88)

def milling():
    rect('Rectangular_cast_base',(116,201,395,309),.66,.60,213,153)
    rect('Column_foot',(140,246,189,269),.87,.16,195,117)
    rect('White_column',(136,185,192,246),1.17,.50,234,195)
    rect('Overhanging_white_head',(191,185,292,200),1.17,.19,234,195)
    rect('Horizontal_head_arm',(191,207,291,244),1.02,.12,234,195)
    for y in [223,268]:
        rect('Brown_longitudinal_way',(223,y,395,y+9),.86,.055,(117,78,46),(92,63,41))
    rect('Dark_slide_bed',(224,233,394,267),.85,.09,70,56)
    rect('Cross_slide_saddle',(272,219,385,279),.94,.065,85,70)
    rect('Carriage_recess',(291,236,369,256),.951,.01,111,100)
    for y in [212,258]:
        rect('Raised_machined_rail',(276,y,381,y+10),1.015,.045,184,111)
        rect('Rail_top_face',(279,y+2,378,y+5),1.016,.002,213,153)
    rect('Broad_center_clamp',(323,212,335,268),1.05,.07,234,195)
    wheel('Slide_handwheel',loc(286,277,1.00),.080,'Y',100)
    wheel('Base_handwheel',loc(141,324,.49),.080,'Y',100)
    rod('Slide_feed_shaft',loc(384,263,.93),loc(400,263,.93),.013,85)

def press():
    rect('Broad_pale_base',(164,588,868,938),.15,.125,213,165)
    for x0,x1 in [(215,363),(663,811)]:
        rect('Forward_cast_foot',(x0,680,x1,894),.40,.25,184,153)
    # Split the existing solid pier mass into large source-color bands.
    for x0,x1 in [(206,371),(653,819)]:
        whole=rect('Tall_frame_pier',(x0,63,x1,298),3.70,3.40,117,96)
        ynear=whole.location.y-whole.dimensions.y/2
        xmid=whole.location.x
        z0=((512-528)/P-S*ynear)/C
        z1=((512-450)/P-S*ynear)/C
        box('Broad_light_pier_band',(xmid,ynear-.001,(z0+z1)/2),((x1-x0-18)/P,.002,z1-z0),184,184)
    for label,toprect,ztop,bottomv,darkrect in [
        ('Upper',(371,135,653,273),3.15,389,(383,298,642,361)),
        ('Lower',(371,423,653,563),1.45,680,(383,589,642,652))]:
        h=(bottomv-toprect[3])/(P*C)
        beam=rect(label+'_press_crossbeam',toprect,ztop,h,101,83)
        yn=beam.location.y-beam.dimensions.y/2
        x0,y0,x1,y1=darkrect
        zhi=((N/2-y0)/P-S*yn)/C
        zlo=((N/2-y1)/P-S*yn)/C
        box(label+'_recessed_dark_opening',((x0+x1-N)/2/P,yn-.001,(zhi+zlo)/2),((x1-x0)/P,.002,zhi-zlo),52,52)
    # White hydraulic rams are broad flattened proxy cylinders, matching the
    # source's deliberately shallow ellipses rather than imposing realism.
    for label,z0,z1,v in [('Upper',3.25,3.95,128),('Lower',1.60,2.65,380)]:
        p=loc(516,v,z1)
        ram=cylinder(label+'_white_ram',(p[0],p[1],(z0+z1)/2),104/(2*P),z1-z0,'Z',255,255,vertices=20)
        ram.scale.y=.20
    rect('Center_front_step',(410,721,616,780),.32,.17,165,132)
    rect('Center_step_top',(405,680,621,716),.58,.32,153,117)

def rolling():
    # Five horizontal rollers; no invented machine housing or extra supports.
    # Each ellipse ratio and shaft extent comes from the unoutlined source.
    spec=[('Small_entry',122,447,714,39,.75),
          ('Small_guide',368,378,645,39,.91),
          ('Large_left',513,289,603,117,1.20),
          ('Lower_middle',684,375,688,115,.80),
          ('Large_right',850,285,601,117,1.20)]
    for name,u,vfar,vnear,rpx,z in spec:
        radius=rpx/P
        far=loc(u,vfar,z); near=loc(u,vnear,z)
        obj=cylinder(name+'_roller',((u-512)/P,(far[1]+near[1])/2,z),radius,abs(far[1]-near[1]),'Y',171,183,vertices=32)
        # Local Y becomes world Z after axis rotation. Ratio matches source cap.
        obj.scale.y=1.21
        end=loc(u,800,z)
        rod(name+'_shaft',(near[0],near[1]-.005,z),end,.046 if rpx>100 else .021,83)
        if rpx<100:
            collar_v=752 if name=='Small_entry' else 685
            collar_center=loc(u,collar_v-3,z)
            collar=cylinder(name+'_front_collar',collar_center,radius,.025,'Y',171,183,vertices=20)
            collar.scale.y=1.21
        obj['mask_interpretation']='Red mask selects roller/shaft group; exposed sheet is unmasked.'
    # Real ribbon mesh: finite thickness, a curved X/Z profile, width along Y.
    # The mask excludes it, but the gray color image explicitly shows this mass.
    profile=[(52,510),(64,492),(90,471),(160,444),(300,403),(440,365),
             (570,327),(682,309),(790,320),(910,344),(969,373)]
    yfar=.15
    ynear=-.86
    outer=[]
    for u,v in profile:
        x=(u-512)/P
        z=((512-v)/P-S*yfar)/C
        outer.append((x,z))
    verts=[]
    for dz in [0,-.015]:
        for y in [yfar,ynear]:
            verts.extend((x,y,z+dz) for x,z in outer)
    k=len(outer)
    faces=[]
    for i in range(k-1):
        faces.extend([(i,i+1,k+i+1,k+i),
                      (2*k+i,3*k+i,3*k+i+1,2*k+i+1),
                      (i,2*k+i,2*k+i+1,i+1),
                      (k+i,k+i+1,3*k+i+1,3*k+i)])
    faces.extend([(0,k,3*k,2*k),(k-1,2*k-1,4*k-1,3*k-1)])
    mesh=bpy.data.meshes.new('Bent_sheet_mesh');mesh.from_pydata(verts,[],faces)
    obj=bpy.data.objects.new('Exposed_bent_sheet',mesh);bpy.context.collection.objects.link(obj)
    mesh.materials.append(material(149))
    obj['mask_interpretation']='Visible unmasked #959595 sheet preserved; no outline extrusion.'

def cameras_and_export(asset, footprint):
    scene=bpy.context.scene
    meshes=[o for o in scene.objects if o.type=='MESH']
    points=[o.matrix_world@Vector(corner) for o in meshes for corner in o.bound_box]
    low=Vector(tuple(min(p[i] for p in points) for i in range(3)))
    high=Vector(tuple(max(p[i] for p in points) for i in range(3)))
    target=(low+high)/2
    # One common target and scale for all rotations, with 9% minimum margins.
    directions=[('North',-90),('South',90),('East',0)]
    extent=0
    for name,deg in directions:
        a=math.radians(deg)
        forward=Vector((math.cos(a)*C,math.sin(a)*C,S))
        right=Vector((-math.sin(a),math.cos(a),0))
        up=forward.cross(right)
        extent=max(extent,max(abs((p-target).dot(right)) for p in points)*2,
                   max(abs((p-target).dot(up)) for p in points)*2)
    scale=extent/.82
    for name,deg in directions:
        a=math.radians(deg)
        data=bpy.data.cameras.new('Camera_'+name);data.type='ORTHO';data.ortho_scale=scale
        camera=bpy.data.objects.new('Camera_'+name,data);scene.collection.objects.link(camera)
        camera.location=target+10*Vector((math.cos(a)*C,math.sin(a)*C,S))
        camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
        camera['downward_angle_degrees']=64
        camera['target_center']=list(target)
    scene.render.engine='BLENDER_EEVEE'
    scene.render.resolution_x=scene.render.resolution_y=512
    scene.render.resolution_percentage=100
    scene.render.film_transparent=True
    scene.render.image_settings.file_format='PNG'
    scene.render.image_settings.color_mode='RGBA'
    scene.view_settings.view_transform='Standard'
    scene.view_settings.look='None'
    scene.view_settings.exposure=0
    scene.view_settings.gamma=1
    scene.world.color=(0,0,0)
    out=os.path.join(ROOT,asset);os.makedirs(out,exist_ok=True)
    for name,_ in directions:
        scene.camera=bpy.data.objects['Camera_'+name]
        scene.render.filepath=os.path.join(out,'HD_'+asset+'_'+name.lower()+'.png')
        bpy.ops.render.render(write_still=True)
    # Pack source/mask pairs as unrendered reference data, never as mesh textures.
    for row in report[asset]:
        for kind in ['color','mask']:
            image=bpy.data.images.load(os.path.join(REF,asset,row[kind]),check_existing=True)
            image.pack()
            image.use_fake_user=True
    scene.camera=bpy.data.objects['Camera_North']
    scene['reference_asset']=asset
    scene['mask_usage']='Material/component selection; unmasked visible fills retained; black outer stroke omitted.'
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                area.spaces.active.shading.color_type='MATERIAL'
                area.spaces.active.region_3d.view_perspective='CAMERA'
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out,'HD_'+asset+'.blend'))
    manifest={'asset':asset,'source_pairs':report[asset], 'game_footprint':footprint,
              'mesh_objects':len(meshes),'vertices':sum(len(o.data.vertices) for o in meshes),
              'world_bounds':[list(low),list(high)],'ortho_scale':scale,'target_center':list(target),
              'downward_degrees':64,'resolution':[512,512],
              'camera_positions':'North -Y, South +Y, East +X; chosen to match supplied sprite orientation.',
              'unseen_views_inferred':asset in ['RollingMC','LineShaftPress'],
              'no_outline_geometry':True,'no_image_textures_on_model':True}
    with open(os.path.join(out,'manifest.json'),'w') as f:json.dump(manifest,f,indent=2)
    print('FINISHED',asset,'vertices',manifest['vertices'],'scale',scale,flush=True)

def build(asset):
    global ASSET,P,N,palette,source,mask
    ASSET=asset
    N=1024 if asset in ['RollingMC','LineShaftPress'] else 512
    P={'RollingMC':1024/3,'LineShaftPress':256}.get(asset,160)
    palette={}
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.world=bpy.data.worlds.new('UnlitWorld')
    source=pixels(os.path.join(REF,asset,'HD_'+asset+'_north.png'))
    mask=pixels(os.path.join(REF,asset,'HD_'+asset+'_northm.png'))
    if asset=='TreadleLathe':lathe(False)
    elif asset=='LineShaftTurretLathe':lathe(True)
    elif asset=='LineShaftMillingMachine':milling()
    elif asset=='LineShaftPress':press()
    elif asset=='RollingMC':rolling()
    bpy.context.view_layer.update()
    cameras_and_export(asset,[3,3] if N==1024 else [2,1])

selected=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else list(report)
for asset in selected:
    assert asset in report and asset!='GunBench'
    build(asset)
