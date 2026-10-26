"""Verify seven machine scenes, active texture links and tile-calibrated presentation."""
import bpy,os,json,math,xml.etree.ElementTree as ET
import numpy as np
from mathutils import Vector
OUT=os.path.dirname(os.path.abspath(__file__))
ROOT=os.path.abspath(os.path.join(OUT,'../..'))
defs={}
for path in ['Defs/WildWest/Buildings/Industry_WildWest.xml','Defs/GreatWar/Buildings/Industry_GreatWar.xml']:
    defs.update({n.findtext('defName'):n for n in ET.parse(os.path.join(ROOT,path)).getroot().findall('ThingDef')})
assets=[('HD_SmallSteamEngine','SmallSteamEngine','(2,2)','small_steam_engine.blend'),
        ('HD_LargeSteamEngine','LargeSteamEngine','(4,3)','HD_LargeSteamEngine.blend'),
        ('HD_IndustrialDieselEngine','IndustrialDieselEngine','(4,3)','HD_IndustrialDieselEngine.blend'),
        ('HD_EarlyIndustrialGenerator','EarlyIndustrialGenerator','(3,3)','HD_EarlyIndustrialGenerator.blend'),
        ('HD_EarlyIndustrialLargeMotor','EarlyIndustrialMotor','(3,3)','HD_EarlyIndustrialLargeMotor.blend'),
        ('HD_HaberBoschHighPressureReactor','HighPressureReactor','(3,3)','HD_HaberBoschHighPressureReactor.blend'),
        ('HD_StirredTankReactor','StirredTankReactor','(3,3)','HD_StirredTankReactor.blend')]
def bounds(obj):
    points=[obj.matrix_world@v.co for v in obj.data.vertices]
    return [(min(v[i] for v in points),max(v[i] for v in points)) for i in range(3)]
prior_base_areas=dict(zip([a[0] for a in assets],[3.515,9.816,9.625,6.235,6.042]))
report={}
for stem,folder,size,blend in assets:
    node=defs[stem];reactor=folder in ['HighPressureReactor','StirredTankReactor']
    assert node.findtext('graphicData/texPath')=='Buildings/'+stem
    assert node.findtext('graphicData/graphicClass')=='Graphic_Multi'
    assert node.findtext('size')==size
    assert node.findtext('rotatable')=='true'
    for direction in ['north','east','south','west']:
        for suffix in ['', 'm']:
            assert os.path.isfile(os.path.join(ROOT,'Textures/Buildings',stem+'_'+direction+suffix+'.png'))
    with open(os.path.join(ROOT,'ArtSource',folder,'validation.json')) as f:checks=json.load(f)
    assert checks['design_period'] and checks['outline_pixels']==12
    assert checks['presentation_rule']=='one_model_unit_per_game_tile' and checks['ground_anchor']==[0,0,0]
    draw=[float(v) for v in node.findtext('graphicData/drawSize').strip('()').split(',')]
    assert draw==checks['expected_draw_size']==[checks['orthographic_scale']]*2
    assert max(abs(float(a)-b) for a,b in zip(node.findtext('graphicData/drawOffset').strip('()').split(','),checks['expected_draw_offset']))<1e-8
    if not reactor:assert checks['geometry_and_palette_unchanged']
    if reactor:
        active_mask=os.path.join(ROOT,'Textures',node.findtext('graphicData/texPath')+'_northm.png')
        assert os.path.isfile(active_mask)
        assert not os.path.exists(os.path.join(ROOT,'Textures/Buildings',stem+'_north_m.png'))
    assert all(c['mask_alpha_matches'] and abs(c['downward_degrees']-64)<.001 and c['minimum_margin']>=12 for c in checks['cameras'].values())
    bpy.ops.wm.open_mainfile(filepath=os.path.join(ROOT,'ArtSource',folder,blend))
    bpy.context.view_layer.update()
    cameras=[bpy.data.objects['Camera_'+d] for d in ['North','East','South','West']]
    assert all(c.data.type=='ORTHO' for c in cameras)
    assert max(c.data.ortho_scale for c in cameras)-min(c.data.ortho_scale for c in cameras)<1e-6
    for c in cameras:
        forward=c.rotation_euler.to_quaternion()@Vector((0,0,-1))
        assert abs(math.degrees(math.asin(-forward.z))-64)<.001
        assert (forward+(c.location-Vector(checks['camera_target'])).normalized()).length<1e-5,'Camera does not aim at ground origin'
    report[stem]={'definition_texture_resolves':True,'footprint_unchanged':size,'color_and_mask_files':8,'saved_scene_cameras_verified':4,'common_scale':cameras[0].data.ortho_scale,'design_period':checks['design_period']}
    base_bounds=[bounds(o) for o in bpy.context.scene.objects if o.type=='MESH' and o.name.startswith('01_')]
    area=sum((b[0][1]-b[0][0])*(b[1][1]-b[1][0]) for b in base_bounds)
    report[stem]['foundation_top_area']=area
    if stem in prior_base_areas:
        assert area<prior_base_areas[stem]
        report[stem]['foundation_area_reduction_percent']=100*(1-area/prior_base_areas[stem])
    report[stem].update({'draw_size_matches_orthographic_scale':draw,'ground_anchor_verified':True,'outline_pixels':12})
    painted={m for o in bpy.context.scene.objects if o.type=='MESH' for m in o.data.materials if m.name.startswith('Neutral_cast_iron_')}
    assert painted and all(max(m.diffuse_color[:3])-min(m.diffuse_color[:3])<1e-6 for m in painted)
    for direction in ['north','east','south','west']:
        pair=[]
        for suffix in ['', 'm']:
            im=bpy.data.images.load(os.path.join(ROOT,'Textures/Buildings',stem+'_'+direction+suffix+'.png'),check_existing=False)
            assert list(im.size)==checks['texture_resolution'];im.colorspace_settings.name='Non-Color'
            w,h=im.size;p=np.empty(w*h*4,np.float32);im.pixels.foreach_get(p);pair.append(p.reshape(h,w,4))
        color,mask=pair;assert np.max(abs(color[:,:,3]-mask[:,:,3]))<.001
        stroke=(color[:,:,:3].max(axis=2)<.001)&(color[:,:,3]>.95)
        # Dark antialiased source edges can round to RGB=0 while retaining 2/255 mask red.
        assert stroke.sum()>1000 and np.max(mask[:,:,:3][stroke])<=2.01/255
    report[stem]['neutral_body_and_unselected_black_outline_verified']=True
assert defs['HD_EarlyIndustrialGenerator'].findtext('graphicData/texPath')!=defs['HD_EarlyIndustrialLargeMotor'].findtext('graphicData/texPath')
# Test actual wheel geometry and the physical opening, not only scene metadata.
bpy.ops.wm.open_mainfile(filepath=os.path.join(ROOT,'ArtSource/LargeSteamEngine/HD_LargeSteamEngine.blend'))
bpy.context.view_layer.update()
rim=bounds(bpy.data.objects['05_Partly_buried_flywheel_rim'])
pit_bottom=bounds(bpy.data.objects['02_Pit_floor'])[2][1]
floor=bpy.context.scene['pit_floor_level']
assert pit_bottom<rim[2][0]<floor<rim[2][1]
assert rim[0][0]>-1.80 and rim[0][1]<.50 and rim[1][0]>.24 and rim[1][1]<.72
floor_blocks=[o for o in bpy.context.scene.objects if o.name.startswith('01_')]
assert len(floor_blocks)==5
for block in floor_blocks:
    b=bounds(block)
    overlap_x=max(0,min(b[0][1],.50)-max(b[0][0],-1.80))
    overlap_y=max(0,min(b[1][1],.72)-max(b[1][0],.24))
    assert overlap_x*overlap_y<1e-6
report['large_flywheel_pit']={'physical_floor_opening_verified':True,'wheel_bottom':rim[2][0],'floor_level':floor,'buried_depth':floor-rim[2][0],'pit_bottom_clearance':rim[2][0]-pit_bottom}
bpy.ops.wm.open_mainfile(filepath=os.path.join(ROOT,'ArtSource/EarlyIndustrialMotor/HD_EarlyIndustrialLargeMotor.blend'))
shield=bpy.data.objects['07_End_shield_with_flush_vents']
vents=[p for p in shield.data.polygons if p.material_index==2]
assert len(vents)==bpy.context.scene['motor_vent_face_count']==12
for face in vents:
    assert all(abs(shield.data.vertices[i].co.x-bpy.context.scene['motor_vent_plane_x'])<1e-5 for i in face.vertices)
report['motor_vent_faces_coplanar']=True
report['generator_and_motor_use_distinct_textures']=True
report['in_game_tested']=False
with open(os.path.join(OUT,'integration_validation.json'),'w') as f:json.dump(report,f,indent=2)
print('Verified seven scenes, 56 render/mask files, 12px outlines, tile scale/drawSize, ground anchors, recessed flywheel and flush motor vents.')
