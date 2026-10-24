"""Verify the four building definitions resolve independent render/mask sets."""
import bpy,os,json,xml.etree.ElementTree as ET
OUT=os.path.dirname(os.path.abspath(__file__))
ROOT=os.path.abspath(os.path.join(OUT,'../..'))
definitions=ET.parse(os.path.join(ROOT,'Defs/WildWest/Buildings/Industry_WildWest.xml')).getroot()
defs={node.findtext('defName'):node for node in definitions.findall('ThingDef')}
assets=[('HD_LargeSteamEngine','LargeSteamEngine','(4,3)'),
        ('HD_IndustrialDieselEngine','IndustrialDieselEngine','(4,3)'),
        ('HD_EarlyIndustrialGenerator','EarlyIndustrialGenerator','(3,3)'),
        ('HD_EarlyIndustrialLargeMotor','EarlyIndustrialMotor','(3,3)')]
report={}
for stem,folder,size in assets:
    node=defs[stem];assert node.findtext('graphicData/texPath')=='Buildings/'+stem
    assert node.findtext('size')==size
    for direction in ['north','east','south']:
        for suffix in ['', 'm']:
            assert os.path.isfile(os.path.join(ROOT,'Textures/Buildings',stem+'_'+direction+suffix+'.png'))
    with open(os.path.join(ROOT,'ArtSource',folder,'validation.json')) as f:checks=json.load(f)
    assert all(c['mask_alpha_matches'] and abs(c['downward_degrees']-64)<.001 and c['minimum_margin']>=24 for c in checks['cameras'].values())
    report[stem]={'definition_texture_resolves':True,'footprint_unchanged':size,'color_and_mask_files':6,'validated_views':3}
assert defs['HD_EarlyIndustrialGenerator'].findtext('graphicData/texPath')!=defs['HD_EarlyIndustrialLargeMotor'].findtext('graphicData/texPath')
bpy.ops.wm.open_mainfile(filepath=os.path.join(ROOT,'ArtSource/EarlyIndustrialMotor/HD_EarlyIndustrialLargeMotor.blend'))
fan=bpy.data.objects['13_Fan_cover_with_flush_vents']
vents=[p for p in fan.data.polygons if p.material_index==2]
assert len(vents)==16
for face in vents:
    assert all(abs(fan.data.vertices[i].co.x-1.045)<1e-5 for i in face.vertices)
report['motor_fan_vent_faces_coplanar']=True
report['generator_and_motor_use_distinct_textures']=True
report['in_game_tested']=False
with open(os.path.join(OUT,'integration_validation.json'),'w') as f:json.dump(report,f,indent=2)
print('Verified four definition links, 24 render/mask files, and flat motor fan vents.')
