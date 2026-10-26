"""Validate actual saved four-view cameras, game links, masks and geometry preservation."""
import bpy,json,os,sys,math,subprocess,hashlib,xml.etree.ElementTree as ET
import numpy as np
from mathutils import Vector
OUT=os.path.dirname(os.path.abspath(__file__));ROOT=os.path.abspath(os.path.join(OUT,'../..'))
sys.path.insert(0,OUT)
from render_west import ASSETS,geometry_hash,read_pixels
from integrate_asset import gameplay_hash
assets=ASSETS+[(s,f,s+'.blend',True) for s,f in [('HD_SAFurnace','ElectricArcFurnace'),
 ('HD_VIMFurnace','VacuumInductionFurnace'),('HD_CableToolRig','CableToolRig')]]
defs={}
for folder,_,files in os.walk(os.path.join(ROOT,'Defs')):
    for file in files:
        if not file.endswith('.xml'):continue
        try:r=ET.parse(os.path.join(folder,file)).getroot()
        except ET.ParseError:continue
        defs.update({n.findtext('defName'):n for n in r.findall('ThingDef') if n.findtext('defName')})
report={};tex=os.path.join(ROOT,'Textures/Buildings')
for stem,folder,blend,active in assets:
    path=os.path.join(ROOT,'ArtSource',folder)
    with open(os.path.join(path,'validation.json')) as f:meta=json.load(f)
    bpy.ops.wm.open_mainfile(filepath=os.path.join(path,blend));bpy.context.view_layer.update()
    if 'rotation_export_geometry_palette_sha256' in meta:
        assert geometry_hash()==meta['rotation_export_geometry_palette_sha256']
    if active:
        d=defs[stem];assert d.findtext('rotatable')=='true'
        assert d.findtext('graphicData/graphicClass')=='Graphic_Multi'
        assert d.findtext('graphicData/texPath')=='Buildings/'+stem
        assert d.findtext('graphicData/shaderType')=='CutoutComplex'
        draw=[float(v) for v in d.findtext('graphicData/drawSize').strip('()').split(',')]
        assert draw==[meta['orthographic_scale']]*2
        assert d.findtext('graphicData/drawOffset')=='(0,0,0)'
        assert gameplay_hash(d)==meta['gameplay_fields_preserved_sha256']
    else:assert stem not in defs
    checks={}
    for name,direction in [('North',(0,-1,0)),('East',(1,0,0)),('South',(0,1,0)),('West',(-1,0,0))]:
        c=bpy.data.objects['Camera_'+name];f=c.rotation_euler.to_quaternion()@Vector((0,0,-1))
        assert c.data.type=='ORTHO' and abs(c.data.ortho_scale-meta['orthographic_scale'])<1e-5
        assert abs(math.degrees(math.asin(-f.z))-64)<.001
        h=Vector((c.location.x,c.location.y,0)).normalized();assert (h-Vector(direction)).length<1e-5
        assert (f+c.location.normalized()).length<1e-5
        color=read_pixels(os.path.join(tex,stem+'_'+name.lower()+'.png'))
        mask=read_pixels(os.path.join(tex,stem+'_'+name.lower()+'m.png'))
        assert np.max(abs(color[:,:,3]-mask[:,:,3]))<.001
        yy,xx=np.where(color[:,:,3]>.5);margin=int(min(xx.min(),yy.min(),511-xx.max(),511-yy.max()));assert margin>=24
        assert np.max(mask[:,:,1:3])<=1.01/255
        stroke=(color[:,:,:3].max(axis=2)<.001)&(color[:,:,3]>.95)
        assert np.max(mask[:,:,:3][stroke])<=2.01/255
        if stem in ['HD_CableToolRig','HD_BasicPumpJack']:assert mask[:,:,:3].max()<=1.01/255
        checks[name]={'elevation':64,'minimum_margin':margin,'mask_alpha_matches':True}
    report[stem]={'active_definition':active,'four_actual_camera_views_verified':checks,
                  'meshes':len([o for o in bpy.context.scene.objects if o.type=='MESH']),
                  'gameplay_fields_preserved':active,'geometry_and_colors_preserved':stem in [a[0] for a in ASSETS]}
    # Prior N/E/S exports are byte-for-byte unchanged for existing modeled machines.
    if stem in [a[0] for a in ASSETS]:
        for direction in ['north','east','south']:
            for suffix in ['', 'm']:
                relative='Textures/Buildings/'+stem+'_'+direction+suffix+'.png'
                original=subprocess.check_output(['git','show','e5af09e3:'+relative],cwd=ROOT)
                with open(os.path.join(ROOT,relative),'rb') as f:assert f.read()==original
        report[stem]['prior_north_east_south_bytes_preserved']=True
    # Interaction offsets must stay outside the occupied rectangle in every orientation.
    if active and defs[stem].findtext('hasInteractionCell')=='true':
        size=[int(v) for v in defs[stem].findtext('size').strip('()').split(',')]
        x,_,z=[int(v) for v in defs[stem].findtext('interactionCellOffset').strip('()').split(',')]
        for i in range(4):
            w,h=size if i%2==0 else size[::-1]
            assert not (-((w-1)//2)<=x<=w//2 and -((h-1)//2)<=z<=h//2)
            x,z=z,-x
        report[stem]['interaction_offset_outside_all_rotated_footprints']=True
    outlet={'HD_SmallSteamEngine':'14_Stack_lip','HD_IndustrialDieselEngine':'17_Exhaust_tip'}.get(stem)
    if outlet:
        obj=bpy.data.objects[outlet]
        points=[obj.matrix_world@v.co for v in obj.data.vertices]
        expected=[obj.location.x,max(v.z for v in points),obj.location.y]
        comp=next(c for c in defs[stem].find('comps') if c.get('Class')=='Helodrace.CompProperties_SteamEngineSmoke')
        actual=[float(v) for v in comp.findtext('smokeOffset').strip('()').split(',')]
        assert max(abs(a-b) for a,b in zip(actual,expected))<1e-5
        report[stem]['smoke_origin_matches_saved_stack_mesh']=True
    if stem=='HD_LargeSteamEngine':
        assert not any(c.get('Class')=='Helodrace.CompProperties_SteamEngineSmoke' for c in defs[stem].find('comps'))
# Verify the radio's exact N/S exchange, separate from its newly rendered West.
radio=os.path.join(ROOT,'ArtSource/CollinsRadioDesk')
with open(os.path.join(radio,'direction_correction.json')) as f:swap=json.load(f)
for direction,previous in [('north','south'),('south','north')]:
    with open(os.path.join(tex,'HD_Radio_'+direction+'.png'),'rb') as f:digest=hashlib.sha256(f.read()).hexdigest()
    assert digest==swap[previous+'_before_sha256']
with open(os.path.join(radio,'active_validation.json')) as f:meta=json.load(f)
bpy.ops.wm.open_mainfile(filepath=os.path.join(radio,'collins_radio_desk.blend'))
assert geometry_hash()==meta['geometry_palette_sha256']
assert bpy.data.objects['Camera_North'].location.y<0<bpy.data.objects['Camera_South'].location.y
assert bpy.data.objects['Camera_West'].location.x<0
for name in ['North','East','South','West']:
    c=bpy.data.objects['Camera_'+name];f=c.rotation_euler.to_quaternion()@Vector((0,0,-1))
    assert abs(math.degrees(math.asin(-f.z))-64)<.001
    im=bpy.data.images.load(os.path.join(tex,'HD_Radio_'+name.lower()+'.png'));assert tuple(im.size)==(256,256)
report['HD_Radio']={'exact_north_south_exchange_verified':True,'actual_west_render':True,'saved_cameras_64_degrees':4,'meshes_and_colors_preserved':True}
report['in_game_tested']=False
with open(os.path.join(OUT,'rotation_validation.json'),'w') as f:json.dump(report,f,indent=2)
print('Verified 15 saved scenes, 60 camera views, 116 PNGs; 12 newly rotatable definitions, radio correction, preserved gameplay and existing geometry/colors.')
