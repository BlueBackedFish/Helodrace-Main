"""Verify five period machine scenes, definition links and geometric requirements."""
import bpy,os,json,math,xml.etree.ElementTree as ET
from mathutils import Vector
OUT=os.path.dirname(os.path.abspath(__file__))
ROOT=os.path.abspath(os.path.join(OUT,'../..'))
definitions=ET.parse(os.path.join(ROOT,'Defs/WildWest/Buildings/Industry_WildWest.xml')).getroot()
defs={node.findtext('defName'):node for node in definitions.findall('ThingDef')}
assets=[('HD_SmallSteamEngine','SmallSteamEngine','(2,2)','small_steam_engine.blend'),
        ('HD_LargeSteamEngine','LargeSteamEngine','(4,3)','HD_LargeSteamEngine.blend'),
        ('HD_IndustrialDieselEngine','IndustrialDieselEngine','(4,3)','HD_IndustrialDieselEngine.blend'),
        ('HD_EarlyIndustrialGenerator','EarlyIndustrialGenerator','(3,3)','HD_EarlyIndustrialGenerator.blend'),
        ('HD_EarlyIndustrialLargeMotor','EarlyIndustrialMotor','(3,3)','HD_EarlyIndustrialLargeMotor.blend')]
report={}
for stem,folder,size,blend in assets:
    node=defs[stem];assert node.findtext('graphicData/texPath')=='Buildings/'+stem
    assert node.findtext('size')==size
    for direction in ['north','east','south']:
        for suffix in ['', 'm']:
            assert os.path.isfile(os.path.join(ROOT,'Textures/Buildings',stem+'_'+direction+suffix+'.png'))
    with open(os.path.join(ROOT,'ArtSource',folder,'validation.json')) as f:checks=json.load(f)
    assert checks['design_period']
    assert all(c['mask_alpha_matches'] and abs(c['downward_degrees']-64)<.001 and c['minimum_margin']>=24 for c in checks['cameras'].values())
    bpy.ops.wm.open_mainfile(filepath=os.path.join(ROOT,'ArtSource',folder,blend))
    cameras=[bpy.data.objects['Camera_'+d] for d in ['North','East','South']]
    assert all(c.data.type=='ORTHO' for c in cameras)
    assert max(c.data.ortho_scale for c in cameras)-min(c.data.ortho_scale for c in cameras)<1e-6
    for c in cameras:
        forward=c.rotation_euler.to_quaternion()@Vector((0,0,-1))
        assert abs(math.degrees(math.asin(-forward.z))-64)<.001
    report[stem]={'definition_texture_resolves':True,'footprint_unchanged':size,'color_and_mask_files':6,'saved_scene_cameras_verified':3,'common_scale':cameras[0].data.ortho_scale,'design_period':checks['design_period']}
assert defs['HD_EarlyIndustrialGenerator'].findtext('graphicData/texPath')!=defs['HD_EarlyIndustrialLargeMotor'].findtext('graphicData/texPath')
# Test actual wheel geometry and the physical opening, not only scene metadata.
def bounds(obj):
    points=[obj.matrix_world@v.co for v in obj.data.vertices]
    return [(min(v[i] for v in points),max(v[i] for v in points)) for i in range(3)]
bpy.ops.wm.open_mainfile(filepath=os.path.join(ROOT,'ArtSource/LargeSteamEngine/HD_LargeSteamEngine.blend'))
bpy.context.view_layer.update()
rim=bounds(bpy.data.objects['05_Partly_buried_flywheel_rim'])
pit_bottom=bounds(bpy.data.objects['02_Pit_floor'])[2][1]
floor=bpy.context.scene['pit_floor_level']
assert pit_bottom<rim[2][0]<floor<rim[2][1]
assert rim[0][0]>-1.80 and rim[0][1]<.50 and rim[1][0]>.24 and rim[1][1]<.72
floor_blocks=[o for o in bpy.context.scene.objects if o.name.startswith('01_')]
assert len(floor_blocks)==4
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
print('Verified five scenes, five definition links, 30 render/mask files, recessed flywheel geometry and flush motor vents.')
